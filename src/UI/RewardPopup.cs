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
/// The one reward popup: a centred Win98 dialog carrying the icon of whatever just arrived,
/// its name, and the credit amount in the shell's money green. Purchases, Work milestones and
/// lifetime milestones all come through <see cref="Show"/>; there is deliberately no second
/// entry point, no notification framework and no per-source configuration.
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
    private const float BreathScale = 0.12f;
    private const float EntryScale = 0.72f;
    private const float ExitScale = 0.94f;
    private const float GlowAlphaCenter = 0.72f;
    private const float GlowAlphaSwing = 0.18f;

    private static readonly Color MoneyGreen = Color.Color8(0, 112, 0);
    private static readonly Vector2 PanelSize = new(288, 268);
    private const int HaloHeight = 150;
    private const int MoneyLineHeight = 42;

    private readonly Queue<Request> _queue = new();

    private PanelContainer? _panel;
    private GlowIcon _glow = null!;
    private Label _title = null!;
    private Label _amount = null!;

    private double _elapsed;
    private Phase _phase = Phase.Idle;
    private float _exitScale = 1.0f;

    /// <summary>Every reward that has been shown, for scenarios and the demo.</summary>
    public int ShownCount { get; private set; }

    public bool IsShowing => _phase != Phase.Idle;

    public string CurrentTitle => GodotObject.IsInstanceValid(_title) ? _title.Text : string.Empty;

    private enum Phase { Idle, In, Dwell, Out }

    private readonly record struct Request(Texture2D Icon, string Title, long AmountMilliCredits);

    /// <summary>
    /// Queues one reward. <paramref name="amountMilliCredits"/> of zero hides the money line.
    /// Safe to call from any node in the tree; does nothing if the autoload is absent.
    /// </summary>
    public static void Show(Node context, Texture2D icon, string title, long amountMilliCredits)
    {
        if (!GodotObject.IsInstanceValid(context) || !context.IsInsideTree())
            return;
        if (context.GetTree().Root.GetNodeOrNull<RewardPopup>(nameof(RewardPopup)) is { } popup)
            popup.Enqueue(icon, title, amountMilliCredits);
    }

    public void Enqueue(Texture2D icon, string title, long amountMilliCredits)
    {
        // Two milestones can cross on one Work drain, so rewards queue and play in order.
        _queue.Enqueue(new Request(icon, title ?? string.Empty, amountMilliCredits));
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
                scale = animate ? 1.0f + (BreathScale * PingPong(_elapsed)) : 1.0f;
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

        // Photosensitivity Safe holds the halo flat; it is the one thing here that brightens
        // and dims, which is exactly what the setting exists to tame.
        _glow.GlowAlpha = effects.PhotosensitivitySafe || !animate
            ? GlowAlphaCenter
            : GlowAlphaCenter + (GlowAlphaSwing * ((PingPong(_elapsed) * 2.0f) - 1.0f));
        _glow.ShowGlints = !effects.ReducedParticles;
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
        SandboxRoot? sandbox = GetTree().Root.FindChild(nameof(SandboxRoot), true, false) as SandboxRoot;
        return GodotObject.IsInstanceValid(sandbox) && sandbox!.Settings is { } settings
            ? settings
            : new LocalSettingsSave();
    }

    private void Begin()
    {
        if (_queue.Count == 0)
            return;

        EnsurePanel();
        Request request = _queue.Dequeue();
        _glow.Icon = request.Icon;
        _title.Text = request.Title;
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
        if (GodotObject.IsInstanceValid(_panel))
        {
            _panel!.Visible = false;
            _panel.Scale = Vector2.One;
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
        _amount = CenteredLabel("RewardAmount", 22, MoneyGreen);
        body.AddChild(_amount);
    }

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
    /// The glowy plate: a generated radial halo, the 16×16 icon at 4× nearest so it stays
    /// pixel-crisp, and four hard-pixel glints standing in for a modern rarity shine.
    /// </summary>
    private sealed partial class GlowIcon : Control
    {
        private const int IconPixels = 96;
        private const int HaloPixels = 160;
        private static readonly Vector2[] GlintOffsets =
            [new(-62, -48), new(58, -44), new(-54, 46), new(64, 42)];
        private static ImageTexture? _halo;

        public Texture2D? Icon { get; set; }
        public float GlowAlpha { get; set; } = GlowAlphaCenter;
        public bool ShowGlints { get; set; } = true;

        public override void _Draw()
        {
            Vector2 center = Size * 0.5f;
            ImageTexture halo = Halo();
            var haloRect = new Rect2(
                (center - new Vector2(HaloPixels / 2, HaloPixels / 2)).Round(),
                new Vector2(HaloPixels, HaloPixels));
            DrawTextureRect(halo, haloRect, false, new Color(1.0f, 0.94f, 0.62f, GlowAlpha));

            if (ShowGlints)
            {
                Color glint = new(1, 1, 1, Math.Min(1.0f, GlowAlpha + 0.35f));
                foreach (Vector2 offset in GlintOffsets)
                {
                    Vector2 at = (center + offset).Round();
                    DrawRect(new Rect2(at.X - 5, at.Y, 11, 1), glint, true);
                    DrawRect(new Rect2(at.X, at.Y - 5, 1, 11), glint, true);
                    DrawRect(new Rect2(at.X - 1, at.Y - 1, 3, 3), glint, true);
                }
            }

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
