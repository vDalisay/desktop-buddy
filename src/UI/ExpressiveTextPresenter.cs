using System;
using System.Collections.Generic;
using System.Text;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.UI;

/// <summary>
/// Reusable dialogue surface for short authored game text. Godot shapes and wraps the full line
/// once, then <see cref="VisibleRatio"/> reveals its post-shaped glyph clusters. Semantic authoring
/// tags are parsed before rendering and mapped to a deliberately small set of built-in BBCode
/// treatments.
/// </summary>
public partial class ExpressiveTextPresenter : RichTextLabel
{
    private readonly List<string> _cadenceUnits = [];
    private ExpressiveRevealTiming _timing = ExpressiveRevealTiming.Default;
    private string _identity = string.Empty;
    private string _source = string.Empty;
    private int _unitIndex;
    private int _speakableOrdinal;
    private double _untilNext;
    private double _speakingHold;
    private bool _revealing;
    private bool _speaking;

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
        // Let TextServer keep combining sequences/ligatures together. The cadence model below
        // observes Unicode scalars only to decide timing and chirps; it does not shape glyphs.
        VisibleCharactersBehavior = TextServer.VisibleCharactersBehavior.GlyphsAuto;
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
        BuildCadenceUnits(PlainText);
        ApplyWin98TextStyle();

        _unitIndex = 0;
        _speakableOrdinal = 0;
        _untilNext = 0.0;
        _speakingHold = 0.0;

        if (!animate || _cadenceUnits.Count == 0)
        {
            _revealing = false;
            VisibleRatio = 1.0f;
            SetProcess(false);
            return true;
        }

        _revealing = true;
        VisibleRatio = 0.0f;
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
        VisibleRatio = 1.0f;
        _unitIndex = _cadenceUnits.Count;
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
        _cadenceUnits.Clear();
        Text = string.Empty;
        VisibleRatio = 1.0f;
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
        while (_revealing && _untilNext <= 0.0 && _unitIndex < _cadenceUnits.Count)
        {
            string unit = _cadenceUnits[_unitIndex++];
            // VisibleRatio remains a Godot-owned post-shaping reveal. We advance it from a pure
            // timing cadence, but never convert source text to per-glyph nodes or codepoint spans.
            VisibleRatio = _unitIndex / (float)_cadenceUnits.Count;

            bool speakable = ExpressiveVoiceCadence.IsSpeakable(unit);
            if (speakable)
            {
                _speakableOrdinal++;
                SetSpeaking(true);
                _speakingHold = 0.055;
                if (ExpressiveVoiceCadence.ShouldChirp(_speakableOrdinal, unit))
                    UiFeedbackAudioBootstrap.TryPlayTutorialTextVoice(this);
            }
            else
            {
                StopSpeaking();
            }

            _untilNext += _timing.DelayAfter(unit);
        }

        if (_unitIndex < _cadenceUnits.Count)
            return;

        _revealing = false;
        VisibleRatio = 1.0f;
        StopSpeaking();
        SetProcess(false);
        RevealCompleted?.Invoke();
    }

    private void BuildCadenceUnits(string plainText)
    {
        _cadenceUnits.Clear();
        foreach (Rune rune in plainText.EnumerateRunes())
            _cadenceUnits.Add(rune.ToString());
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
