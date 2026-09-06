using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.UI;

/// <summary>
/// Reusable dialogue surface for short authored game text. It lets Godot shape and wrap the full
/// line once, then reveals complete text elements through RichTextLabel.visible_characters.
/// Semantic authoring tags are parsed before rendering and mapped to a deliberately small set of
/// built-in BBCode treatments.
/// </summary>
public partial class ExpressiveTextPresenter : RichTextLabel
{
    private readonly List<RevealUnit> _units = [];
    private ExpressiveRevealTiming _timing = ExpressiveRevealTiming.Default;
    private string _identity = string.Empty;
    private string _source = string.Empty;
    private int _unitIndex;
    private int _speakableOrdinal;
    private double _untilNext;
    private double _speakingHold;
    private bool _revealing;
    private bool _speaking;

    private readonly record struct RevealUnit(string TextElement, int VisibleCodepointsAfter);

    public event Action? RevealStarted;
    public event Action<bool>? SpeakingChanged;
    public event Action? RevealCompleted;

    public bool IsRevealing => _revealing;
    public bool IsSpeaking => _speaking;
    public string Identity => _identity;
    public string PlainText { get; private set; } = string.Empty;

    public override void _Ready()
    {
        BbcodeEnabled = true;
        FitContent = true;
        ScrollActive = false;
        SelectionEnabled = false;
        MouseFilter = MouseFilterEnum.Ignore;
        AutowrapMode = TextServer.AutowrapMode.WordSmart;
        VisibleCharactersBehavior = TextServer.VisibleCharactersBehavior.CharsAfterShaping;
        ApplyWin98TextStyle();
        SetProcess(false);
    }

    /// <summary>
    /// Shows one semantic line. Re-presenting the same identity and exact source is idempotent and
    /// does not restart its reveal. A changed variant/source intentionally starts as a new line.
    /// </summary>
    public bool Present(
        string identity,
        string? semanticMarkup,
        LocalSettingsSave settings,
        ExpressiveRevealTiming? timing = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        identity ??= string.Empty;
        semanticMarkup ??= string.Empty;
        if (string.Equals(identity, _identity, StringComparison.Ordinal) &&
            string.Equals(semanticMarkup, _source, StringComparison.Ordinal))
        {
            return false;
        }

        StopSpeaking();
        _identity = identity;
        _source = semanticMarkup;
        _timing = timing ?? ExpressiveRevealTiming.Default;

        ExpressiveTextDocument document = ExpressiveSemanticMarkup.Parse(semanticMarkup);
        PlainText = document.PlainText;
        bool animate = Win98MotionPolicy.Allows(settings) && PlainText.Length > 0;
        Text = BuildBbCode(document.Runs, animate);
        BuildRevealUnits(PlainText);
        ApplyWin98TextStyle();

        _unitIndex = 0;
        _speakableOrdinal = 0;
        _untilNext = 0.0;
        _speakingHold = 0.0;

        if (!animate || _units.Count == 0)
        {
            _revealing = false;
            VisibleCharacters = -1;
            SetProcess(false);
            return true;
        }

        _revealing = true;
        VisibleCharacters = 0;
        SetProcess(true);
        RevealStarted?.Invoke();
        return true;
    }

    /// <summary>
    /// First-click behavior for the containing tutorial: finish the current typewriter reveal but
    /// do not advance tutorial progression. Returns true when it consumed an active reveal.
    /// </summary>
    public bool CompleteReveal()
    {
        if (!_revealing)
            return false;

        _revealing = false;
        VisibleCharacters = -1;
        _unitIndex = _units.Count;
        StopSpeaking();
        SetProcess(false);
        RevealCompleted?.Invoke();
        return true;
    }

    public void ClearPresentation()
    {
        _revealing = false;
        _identity = string.Empty;
        _source = string.Empty;
        PlainText = string.Empty;
        _units.Clear();
        Text = string.Empty;
        VisibleCharacters = -1;
        StopSpeaking();
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (!_revealing)
        {
            SetProcess(false);
            return;
        }

        delta = Math.Max(0.0, delta);
        if (_speakingHold > 0.0)
        {
            _speakingHold = Math.Max(0.0, _speakingHold - delta);
            if (_speakingHold <= 0.0)
                StopSpeaking();
        }

        _untilNext -= delta;
        while (_revealing && _untilNext <= 0.0 && _unitIndex < _units.Count)
        {
            RevealUnit unit = _units[_unitIndex++];
            VisibleCharacters = unit.VisibleCodepointsAfter;

            bool speakable = ExpressiveVoiceCadence.IsSpeakable(unit.TextElement);
            if (speakable)
            {
                _speakableOrdinal++;
                SetSpeaking(true);
                _speakingHold = 0.055;
                if (ExpressiveVoiceCadence.ShouldChirp(_speakableOrdinal, unit.TextElement))
                    UiFeedbackAudioBootstrap.TryPlayTutorialTextVoice(this);
            }
            else
            {
                StopSpeaking();
            }

            _untilNext += _timing.DelayAfter(unit.TextElement);
        }

        if (_unitIndex < _units.Count)
            return;

        _revealing = false;
        VisibleCharacters = -1;
        StopSpeaking();
        SetProcess(false);
        RevealCompleted?.Invoke();
    }

    private void BuildRevealUnits(string plainText)
    {
        _units.Clear();
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(plainText);
        int codepoints = 0;
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            foreach (Rune _ in element.EnumerateRunes())
                codepoints++;
            _units.Add(new RevealUnit(element, codepoints));
        }
    }

    private static string BuildBbCode(IReadOnlyList<ExpressiveTextRun> runs, bool animate)
    {
        var builder = new StringBuilder();
        foreach (ExpressiveTextRun run in runs)
        {
            string escaped = EscapeBbCode(run.Text);
            builder.Append(run.Role switch
            {
                ExpressiveSemanticRole.Input => $"[color=#000080][b]{escaped}[/b][/color]",
                ExpressiveSemanticRole.Action => $"[b]{escaped}[/b]",
                ExpressiveSemanticRole.Money => $"[color=#007000][b]{escaped}[/b][/color]",
                ExpressiveSemanticRole.Impact when animate => $"[wave amp=5 freq=3 connected=1][b]{escaped}[/b][/wave]",
                ExpressiveSemanticRole.Impact => $"[b]{escaped}[/b]",
                ExpressiveSemanticRole.Playful when animate => $"[wave amp=3 freq=2 connected=1]{escaped}[/wave]",
                _ => escaped,
            });
        }
        return builder.ToString();
    }

    private void ApplyWin98TextStyle()
    {
        AddThemeFontSizeOverride("normal_font_size", Win98ThemeFactory.Px(Win98ThemeFactory.BaseFontSize));
        AddThemeFontSizeOverride("bold_font_size", Win98ThemeFactory.Px(Win98ThemeFactory.BaseFontSize));
        AddThemeColorOverride("default_color", Win98ThemeFactory.Dark);
        AddThemeConstantOverride("line_separation", Win98ThemeFactory.Px(1));
    }

    private void SetSpeaking(bool speaking)
    {
        if (_speaking == speaking)
            return;
        _speaking = speaking;
        SpeakingChanged?.Invoke(speaking);
    }

    private void StopSpeaking()
    {
        _speakingHold = 0.0;
        SetSpeaking(false);
    }

    private static string EscapeBbCode(string text) =>
        (text ?? string.Empty).Replace("[", "[lb]", StringComparison.Ordinal);
}
