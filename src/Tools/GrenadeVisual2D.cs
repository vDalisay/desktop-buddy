using System;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// The legacy-circles counterpart of <see cref="Presentation3D.GrenadeVisual3D"/>: the same
/// grenade and the same blast, drawn flat. The house rule is that every visual ships in
/// both presentation modes, and a grenade drawn as the generic loose-object disc would be
/// indistinguishable from a baseball at exactly the moment the player most needs to tell
/// them apart.
///
/// <para>While it is active it takes over drawing from the <see cref="LooseObjectBody"/>
/// itself — the body is hidden, the same way the 3D slot hides it — so the two never draw
/// on top of each other.</para>
/// </summary>
[GlobalClass]
public partial class GrenadeVisual2D : Node2D
{
    private GrenadeProfile _profile = null!;
    private LooseObjectBody? _body;
    private bool _pinIn = true;
    private bool _presentationActive;
    private int _flashTicks;
    private int _ringTicks;
    private Vector2 _blastCenter;

    public bool IsInitialized { get; private set; }
    public bool IsAttached => GodotObject.IsInstanceValid(_body);

    /// <summary>True while the flat detonation star is on screen.</summary>
    public bool IsFlashVisible => IsInitialized && _presentationActive && _flashTicks > 0;

    /// <summary>The ring's current drawn radius in world pixels; zero when not drawn.</summary>
    public float RingRadiusPx { get; private set; }

    /// <summary>The largest radius the last ring reached — the size the blast read as.</summary>
    public float PeakRingRadiusPx { get; private set; }

    public bool ShowsPin => _pinIn;

    public void Initialize(GrenadeProfile profile)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile))
            throw new ArgumentException("The grenade visual requires a live profile.", nameof(profile));

        _profile = profile;
        IsInitialized = true;
        QueueRedraw();
    }

    public void Attach(LooseObjectBody body, bool pinIn)
    {
        RequireInitialized();
        ArgumentNullException.ThrowIfNull(body);
        _body = body;
        _pinIn = pinIn;
        ApplyBodyVisibility();
        QueueRedraw();
    }

    public void Detach(LooseObjectBody body)
    {
        if (!GodotObject.IsInstanceValid(_body) || _body != body)
            return;

        if (GodotObject.IsInstanceValid(body))
            body.Visible = true;
        _body = null;
        QueueRedraw();
    }

    public void NotifyPinPulled()
    {
        _pinIn = false;
        QueueRedraw();
    }

    public void NotifyDetonated(Vector2 center)
    {
        if (!IsInitialized)
            return;

        _blastCenter = center;
        _flashTicks = Mathf.Max(0, _profile.FlashTicks);
        _ringTicks = Mathf.Max(1, _profile.RingTicks);
        PeakRingRadiusPx = 0.0f;
        QueueRedraw();
    }

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        Visible = active;
        ApplyBodyVisibility();
        QueueRedraw();
    }

    /// <summary>Advances the blast envelopes on the owning root's routed tick.</summary>
    public void PhysicsTick()
    {
        if (!IsInitialized)
            return;

        if (_flashTicks > 0)
            _flashTicks--;
        if (_ringTicks > 0)
        {
            _ringTicks--;
            int authored = Mathf.Max(1, _profile.RingTicks);
            RingRadiusPx = _profile.BlastFullRadiusPx *
                           (1.0f - ((float)_ringTicks / authored));
            PeakRingRadiusPx = Mathf.Max(PeakRingRadiusPx, RingRadiusPx);
        }
        else
        {
            RingRadiusPx = 0.0f;
        }

        // The grenade tumbles and flies, so its silhouette is redrawn every routed tick
        // whether or not anything about the blast changed.
        ApplyBodyVisibility();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!IsInitialized || !_presentationActive)
            return;

        DrawGrenade();
        DrawBlast();
    }

    private void DrawGrenade()
    {
        if (!GodotObject.IsInstanceValid(_body))
            return;

        LooseObjectBody body = _body!;
        // The presenter sits at the world origin, so world coordinates draw directly.
        Vector2 centre = body.GlobalPosition;
        float radius = body.Radius;
        DrawCircle(centre, radius, _profile.BodyColor, true, -1.0f, true);
        DrawArc(centre, radius, 0.0f, Mathf.Tau, 20, _profile.CapColor, 1.6f, true);

        // Cap and lever, in the body's own frame so a tumbling grenade tumbles.
        float rotation = body.GlobalRotation;
        Vector2 up = Vector2.Up.Rotated(rotation);
        Vector2 right = Vector2.Right.Rotated(rotation);
        Vector2 cap = centre + (up * radius * 0.95f);
        DrawCircle(cap, radius * 0.34f, _profile.CapColor, true, -1.0f, true);
        DrawLine(
            cap + (right * radius * 0.26f),
            cap + (right * radius * 0.44f) - (up * radius * 1.20f),
            _profile.CapColor,
            radius * 0.22f,
            true);

        if (_pinIn)
        {
            Vector2 pin = cap - (right * radius * 0.55f);
            DrawArc(pin, radius * 0.26f, 0.0f, Mathf.Tau, 10, _profile.PinColor, 1.6f, true);
        }
    }

    private void DrawBlast()
    {
        int authoredFlash = Mathf.Max(1, _profile.FlashTicks);
        if (_profile.FlashTicks > 0 && _flashTicks > 0)
        {
            float strength = (float)_flashTicks / authoredFlash;
            float reach = _profile.BlastFullRadiusPx * 0.9f * strength;
            var star = new Color(_profile.BlastColor, 0.95f * strength);
            DrawCircle(_blastCenter, reach * 0.45f, star, true, -1.0f, true);
            for (int ray = 0; ray < 4; ray++)
            {
                Vector2 direction = Vector2.Right.Rotated(Mathf.Pi * ray / 4.0f);
                DrawLine(
                    _blastCenter - (direction * reach),
                    _blastCenter + (direction * reach),
                    star,
                    3.0f,
                    true);
            }
        }

        if (_ringTicks > 0 && RingRadiusPx > 0.0f)
        {
            int authoredRing = Mathf.Max(1, _profile.RingTicks);
            float progress = 1.0f - ((float)_ringTicks / authoredRing);
            var ring = new Color(_profile.BlastColor, 0.75f * (1.0f - progress));
            DrawArc(_blastCenter, RingRadiusPx, 0.0f, Mathf.Tau, 32, ring, 2.5f, true);
        }
    }

    /// <summary>
    /// While this presenter is drawing the grenade, the body must not also draw itself —
    /// the same handover the 3D slot performs.
    /// </summary>
    private void ApplyBodyVisibility()
    {
        if (GodotObject.IsInstanceValid(_body))
            _body!.Visible = !_presentationActive;
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("GrenadeVisual2D used before initialization.");
    }
}
