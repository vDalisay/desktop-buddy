using System;
using System.Collections.Generic;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Ui;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.UI;

/// <summary>
/// Semantic reason a reward is being presented. Callers describe what happened; the popup owns
/// how that meaning maps to late-90s WordArt-inspired presentation.
/// </summary>
public enum RewardPresentationKind
{
    Generic = 0,
    ToolPurchase = 1,
    WorkSessionMilestone = 2,
    WorkLifetimeMilestone = 3,
}

/// <summary>
/// The one reward popup: a centred Win98 dialog carrying the icon of whatever just arrived,
/// its name, and the credit amount in the shell's money green. Purchases, Work milestones and
/// lifetime milestones all come through <see cref="Show"/>; there is deliberately no second
/// queue, no notification framework and no per-source visual configuration.
///
/// <para>Semantic reward kinds may use an original late-90s WordArt-inspired title treatment.
/// The queue remains authoritative: WordArt is only a child view inside this popup.</para>
///
/// <para>Timings, easings and the accessibility rules are documented in
/// <c>docs/REWARD_FEEL_PLAN.md</c>. The breathing pulse reuses the tutorial spotlight's
/// smoothstepped ping-pong rather than a sine, for the same sub-pixel reason.</para>
///
/// <para>The popup plays no audio of its own: each caller already owns its sound
/// (<see cref="UiSfx.Money"/> for a purchase, <see cref="UiFeedbackCue.Reward"/> for a
/// milestone), the same rule the Buy/Equip buttons follow.</para>
/// </summary>
public partial class RewardPopup : CanvasLayer
{
    private const double InSeconds = 0.18;
    private const double DwellSeconds = 2.40;
    private const double OutSeconds = 0.14;
    private const double BreathSeconds = 1.5;
    private const double WordArtSettleSeconds = 0.48;
    private const float BreathScale = 0.12f;
    private const float EntryScale = 0.72f;
    private const float ExitScale = 0.94f;
    private const float GlowAlphaCenter = 0.72f;
    private const float GlowAlphaSwing = 0.18f;

    private static readonly Color MoneyGreen = Color.Color8(0, 112, 0);
    private static readonly Vector2 PanelSize = new(304, 280);
    private const int HaloHeight = 142;
    private const int MoneyLineHeight = 42;
    private const int WordArtHeight = 52;

    private readonly Queue<Request> _queue = new();
    private readonly LocalSettingsSave _fallbackSettings = new();

    private PanelContainer? _panel;
    private GlowIcon _glow = null!;
    private Label _title = null!;
    private Control _wordArt = null!;
    private RichTextLabel[] _wordArtLayers = [];
    private Label _amount = null!;
    private SandboxRoot? _sandbox;

    private double _elapsed;
    private Phase _phase = Phase.Idle;
    private float _exitScale = 1.0f;
    private string _currentTitle = string.Empty;
    private RewardPresentationKind _currentKind;
    private WordArtPreset _wordArtPreset;
    private bool _wordArtMotionApplied;

    /// <summary>Every reward that has been shown, for scenarios and the demo.</summary>
    public int ShownCount { get; private set; }

    public bool IsShowing => _phase != Phase.Idle;

    public string CurrentTitle => _currentTitle;

    public RewardPresentationKind CurrentPresentationKind => _currentKind;

    public bool CurrentUsesWordArt => _currentKind != RewardPresentationKind.Generic;

    private enum Phase { Idle, In, Dwell, Out }

    private readonly record struct Request(
        Texture2D Icon,
        string Title,
        long AmountMilliCredits,
        RewardPresentationKind Kind);

    private readonly record struct WordArtPreset(
        Color Fill,
        Color Outline,
        Color Extrusion,
        int FontSize,
        float WaveAmplitude,
        float WaveFrequency,
        float EntryTiltDegrees);

    /// <summary>
    /// Queues one reward. <paramref name="amountMilliCredits"/> of zero hides the money line.
    /// <paramref name="kind"/> is semantic; callers never choose raw WordArt parameters.
    /// Safe to call from any node in the tree; does nothing if the autoload is absent.
    /// </summary>
    public static void Show(
        Node context,
        Texture2D icon,
        string title,
        long amountMilliCredits,
        RewardPresentationKind kind = RewardPresentationKind.Generic)
    {
        if (!GodotObject.IsInstanceValid(context) || !context.IsInsideTree())
            return;
        if (context.GetTree().Root.GetNodeOrNull<RewardPopup>(nameof(RewardPopup)) is { } popup)
            popup.Enqueue(icon, title, amountMilliCredits, kind);
    }

    public void Enqueue(
        Texture2D icon,
        string title,
        long amountMilliCredits,
        RewardPresentationKind kind = RewardPresentationKind.Generic)
    {
        // Two milestones can cross on one Work drain, so rewards queue and play in order.
        _queue.Enqueue(new Request(icon, title ?? string.Empty, amountMilliCredits, kind));
        if (_phase == Phase.Idle)
            Begin();
    }

    public override void _Ready()
    {
        // The character editor pauses the tree; a reward earned there still has to be shown.
        ProcessMode = ProcessModeEnum.Always;
        Layer = 300;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_phase == Phase.Idle || !GodotObject.IsInstanceValid(_panel))
            return;

        _elapsed += Math.Max(0.0, delta);
        LocalSettingsSave settings = ResolveSettings();
        bool animate = Win98MotionPolicy.Allows(settings);
        EffectsSettings effects = EffectsSettings.FromSave(settings);
        bool wordArt = _currentKind != RewardPresentationKind.Generic;

        float scale = 1.0f;
        float alpha = 1.0f;
        switch (_phase)
        {
            case Phase.In:
            {
                double duration = animate ? InSeconds : 0.0;
                if (_elapsed >= duration)
                {
                    _phase = Phase.Dwell;
                    _elapsed = 0.0;
                    break;
                }
                float t = (float)(_elapsed / duration);
                scale = Mathf.Lerp(EntryScale, 1.0f, EaseOutBack(t));
                alpha = t;
                break;
            }

            case Phase.Dwell:
                // WordArt already supplies the personality motion. Keeping the old 12% whole-dialog
                // breathing on top made the title feel seasick, so semantic WordArt rewards settle.
                scale = animate && !wordArt ? 1.0f + (BreathScale * PingPong(_elapsed)) : 1.0f;
                if (_elapsed >= DwellSeconds)
                {
                    _exitScale = scale;
                    _phase = Phase.Out;
                    _elapsed = 0.0;
                }
                break;

            case Phase.Out:
            {
                double duration = animate ? OutSeconds : 0.0;
                if (_elapsed >= duration)
                {
                    Finish();
                    return;
                }
                float t = (float)(_elapsed / duration);
                scale = Mathf.Lerp(_exitScale, ExitScale, t * t);
                alpha = 1.0f - t;
                break;
            }
        }

        UpdateWordArtMotion(animate);

        // Photosensitivity Safe holds the halo flat; it is the one thing here that brightens
        // and dims, which is exactly what the setting exists to tame.
        _glow.GlowAlpha = effects.PhotosensitivitySafe || !animate
            ? GlowAlphaCenter
            : GlowAlphaCenter + (GlowAlphaSwing * ((PingPong(_elapsed) * 2.0f) - 1.0f));
        _glow.QueueRedraw();

        _panel!.PivotOffset = _panel.Size * 0.5f;
        _panel.Scale = new Vector2(scale, scale);
        _panel.Modulate = new Color(1, 1, 1, alpha);
    }

    /// <summary>
    /// Smoothstep over a ping-pong ramp, the tutorial spotlight's easing. A sine crosses the
    /// middle fastest, which is where a moving sub-pixel edge shows up most.
    /// </summary>
    private static float PingPong(double seconds)
    {
        float phase = (float)Mathf.PosMod(seconds / BreathSeconds, 1.0);
        float ramp = phase < 0.5f ? phase * 2.0f : (1.0f - phase) * 2.0f;
        return ramp * ramp * (3.0f - (2.0f * ramp));
    }

    /// <summary>Overshoot of roughly 6%: the whole punch of the reveal lives in this curve.</summary>
    private static float EaseOutBack(float t)
    {
        const float c1 = 1.1f;
        const float c3 = c1 + 1.0f;
        float p = t - 1.0f;
        return 1.0f + (c3 * p * p * p) + (c1 * p * p);
    }

    private LocalSettingsSave ResolveSettings()
    {
        if (!GodotObject.IsInstanceValid(_sandbox))
            _sandbox = GetTree().Root.FindChild(nameof(SandboxRoot), true, false) as SandboxRoot;
        return GodotObject.IsInstanceValid(_sandbox) && _sandbox!.Settings is { } settings
            ? settings
            : _fallbackSettings;
    }

    private void Begin()
    {
        if (_queue.Count == 0)
            return;

        EnsurePanel();
        Request request = _queue.Dequeue();
        _currentTitle = request.Title;
        _currentKind = request.Kind;
        _glow.Icon = request.Icon;

        bool wordArt = request.Kind != RewardPresentationKind.Generic;
        _title.Text = request.Title;
        _title.Visible = !wordArt;
        _wordArt.Visible = wordArt;
        if (wordArt)
        {
            _wordArtPreset = ResolveWordArtPreset(request.Kind);
            ApplyWordArtPreset(request.Title);
            _wordArtMotionApplied = false;
        }

        _amount.Text = "+" + ContentDisplayName.Credits(request.AmountMilliCredits);
        _amount.Visible = request.AmountMilliCredits > 0;

        // A purchase has no money line, so the frame closes up rather than leaving a dead band
        // where the amount would have been. Win98Dialog centres by offset, so both move together.
        Vector2 size = _amount.Visible ? PanelSize : PanelSize - new Vector2(0, MoneyLineHeight);
        _panel!.CustomMinimumSize = size;
        _panel.OffsetLeft = -size.X / 2f;
        _panel.OffsetTop = -size.Y / 2f;
        _panel.OffsetRight = size.X / 2f;
        _panel.OffsetBottom = size.Y / 2f;
        _panel.Visible = true;
        _panel.Modulate = new Color(1, 1, 1, 0);
        _phase = Phase.In;
        _elapsed = 0.0;
        ShownCount++;
    }

    private void Finish()
    {
        _phase = Phase.Idle;
        _elapsed = 0.0;
        _currentTitle = string.Empty;
        _currentKind = RewardPresentationKind.Generic;
        if (GodotObject.IsInstanceValid(_panel))
        {
            _panel!.Visible = false;
            _panel.Scale = Vector2.One;
        }
        if (GodotObject.IsInstanceValid(_wordArt))
        {
            _wordArt.Scale = Vector2.One;
            _wordArt.Rotation = 0.0f;
        }
        Begin();
    }

    private void EnsurePanel()
    {
        if (GodotObject.IsInstanceValid(_panel))
            return;

        var root = new Control { Name = "RewardPopupRoot", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // The shell's own dialog chrome: raised frame, chunky bevels, flush blue title bar.
        _panel = Win98Dialog.Create(
            "RewardPopupPanel", "Reward", PanelSize, out VBoxContainer body, onClose: null, draggable: false);
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(_panel);

        _glow = new GlowIcon
        {
            Name = "RewardGlow",
            CustomMinimumSize = new Vector2(0, HaloHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        body.AddChild(_glow);

        _title = CenteredLabel("RewardTitle", 16, Win98ThemeFactory.Dark);
        body.AddChild(_title);

        _wordArt = BuildWordArtTitle();
        _wordArt.Visible = false;
        body.AddChild(_wordArt);

        _amount = CenteredLabel("RewardAmount", 22, MoneyGreen);
        body.AddChild(_amount);
    }

    private Control BuildWordArtTitle()
    {
        var root = new Control
        {
            Name = "RewardWordArtTitle",
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.Px(WordArtHeight)),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipContents = false,
        };

        _wordArtLayers = new RichTextLabel[4];
        for (int index = 0; index < _wordArtLayers.Length; index++)
        {
            var layer = new RichTextLabel
            {
                Name = index == _wordArtLayers.Length - 1 ? "WordArtFront" : $"WordArtDepth{index + 1}",
                BbcodeEnabled = true,
                FitContent = false,
                ScrollActive = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            layer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            int depth = _wordArtLayers.Length - 1 - index;
            layer.OffsetLeft = Win98ThemeFactory.Px(depth);
            layer.OffsetTop = Win98ThemeFactory.Px(depth);
            layer.OffsetRight = Win98ThemeFactory.Px(depth);
            layer.OffsetBottom = Win98ThemeFactory.Px(depth);
            root.AddChild(layer);
            _wordArtLayers[index] = layer;
        }

        return root;
    }

    private void ApplyWordArtPreset(string title)
    {
        string escaped = EscapeBbCode(title);
        for (int index = 0; index < _wordArtLayers.Length; index++)
        {
            RichTextLabel layer = _wordArtLayers[index];
            bool front = index == _wordArtLayers.Length - 1;
            layer.AddThemeFontSizeOverride("normal_font_size", Win98ThemeFactory.Px(_wordArtPreset.FontSize));
            layer.AddThemeColorOverride("default_color", front ? _wordArtPreset.Fill : _wordArtPreset.Extrusion);
            layer.AddThemeColorOverride("font_outline_color", _wordArtPreset.Outline);
            layer.AddThemeConstantOverride("outline_size", front ? Win98ThemeFactory.Px(2) : Win98ThemeFactory.Px(1));
            layer.Text = $"[center]{escaped}[/center]";
        }
    }

    private void UpdateWordArtMotion(bool animate)
    {
        if (_currentKind == RewardPresentationKind.Generic || !GodotObject.IsInstanceValid(_wordArt))
            return;

        bool active = animate &&
            (_phase == Phase.In || (_phase == Phase.Dwell && _elapsed < WordArtSettleSeconds));
        if (active != _wordArtMotionApplied)
        {
            string escaped = EscapeBbCode(_currentTitle);
            string body = active
                ? $"[wave amp={_wordArtPreset.WaveAmplitude:0.##} freq={_wordArtPreset.WaveFrequency:0.##} connected=1]{escaped}[/wave]"
                : escaped;
            foreach (RichTextLabel layer in _wordArtLayers)
                layer.Text = $"[center]{body}[/center]";
            _wordArtMotionApplied = active;
        }

        if (!active)
        {
            _wordArt.Scale = Vector2.One;
            _wordArt.Rotation = 0.0f;
            return;
        }

        float progress = _phase == Phase.In
            ? Mathf.Clamp((float)(_elapsed / Math.Max(InSeconds, 0.001)), 0.0f, 1.0f)
            : Mathf.Clamp((float)(_elapsed / WordArtSettleSeconds), 0.0f, 1.0f);
        float remaining = 1.0f - progress;
        float wobble = Mathf.Sin(progress * Mathf.Tau * 2.0f) * remaining;
        _wordArt.PivotOffset = _wordArt.Size * 0.5f;
        _wordArt.RotationDegrees = _wordArtPreset.EntryTiltDegrees * wobble;
        float punch = 1.0f + (0.08f * remaining * Mathf.Abs(wobble));
        _wordArt.Scale = new Vector2(punch, 1.0f + ((punch - 1.0f) * 0.55f));
    }

    private static WordArtPreset ResolveWordArtPreset(RewardPresentationKind kind) => kind switch
    {
        RewardPresentationKind.ToolPurchase => new WordArtPreset(
            Color.Color8(255, 210, 32),
            Color.Color8(20, 38, 126),
            Color.Color8(92, 72, 0),
            FontSize: 21,
            WaveAmplitude: 9.0f,
            WaveFrequency: 3.5f,
            EntryTiltDegrees: -4.0f),
        RewardPresentationKind.WorkSessionMilestone => new WordArtPreset(
            Color.Color8(73, 225, 255),
            Color.Color8(18, 42, 110),
            Color.Color8(24, 92, 132),
            FontSize: 19,
            WaveAmplitude: 7.0f,
            WaveFrequency: 3.0f,
            EntryTiltDegrees: 3.0f),
        RewardPresentationKind.WorkLifetimeMilestone => new WordArtPreset(
            Color.Color8(255, 178, 38),
            Color.Color8(112, 28, 38),
            Color.Color8(116, 62, 18),
            FontSize: 19,
            WaveAmplitude: 10.0f,
            WaveFrequency: 2.8f,
            EntryTiltDegrees: -5.0f),
        _ => new WordArtPreset(
            Win98ThemeFactory.Dark,
            Win98ThemeFactory.Dark,
            Win98ThemeFactory.Shadow,
            FontSize: 18,
            WaveAmplitude: 0.0f,
            WaveFrequency: 0.0f,
            EntryTiltDegrees: 0.0f),
    };

    private static string EscapeBbCode(string text) =>
        (text ?? string.Empty).Replace("[", "[lb]", StringComparison.Ordinal);

    private static Label CenteredLabel(string name, int fontSize, Color color)
    {
        var label = new Label
        {
            Name = name,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", Win98ThemeFactory.Px(fontSize));
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>
    /// The glowy plate: a generated radial halo and the 16×16 icon at 4× nearest so it stays
    /// pixel-crisp. It used to throw four hard-pixel glints around the icon as well; the owner
    /// cut them (instruction 2026-08-21) — the halo alone is the shine.
    /// </summary>
    private sealed partial class GlowIcon : Control
    {
        private const int IconPixels = 96;
        private const int HaloPixels = 160;
        private static ImageTexture? _halo;

        public Texture2D? Icon { get; set; }
        public float GlowAlpha { get; set; } = GlowAlphaCenter;

        public override void _Draw()
        {
            Vector2 center = Size * 0.5f;
            ImageTexture halo = Halo();
            var haloRect = new Rect2(
                (center - new Vector2(HaloPixels / 2, HaloPixels / 2)).Round(),
                new Vector2(HaloPixels, HaloPixels));
            DrawTextureRect(halo, haloRect, false, new Color(1.0f, 0.94f, 0.62f, GlowAlpha));

            if (Icon is not null)
            {
                var iconRect = new Rect2(
                    (center - new Vector2(IconPixels / 2, IconPixels / 2)).Round(),
                    new Vector2(IconPixels, IconPixels));
                DrawTextureRect(Icon, iconRect, false);
            }
        }

        public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

        /// <summary>One 64×64 radial falloff, generated once and shared by every popup.</summary>
        private static ImageTexture Halo()
        {
            if (_halo is not null)
                return _halo;

            const int size = HaloPixels;
            Image image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, new byte[size * size * 4]);
            var center = new Vector2((size - 1) / 2.0f, (size - 1) / 2.0f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = new Vector2(x, y).DistanceTo(center) / ((size - 1) / 2.0f);
                float falloff = Mathf.Clamp(1.0f - distance, 0.0f, 1.0f);
                image.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(falloff, 1.6f)));
            }

            _halo = ImageTexture.CreateFromImage(image);
            return _halo;
        }
    }
}
