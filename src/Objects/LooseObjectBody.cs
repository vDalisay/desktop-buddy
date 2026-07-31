using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Objects;

/// <summary>
/// Registry-backed loose physics object. It owns only its physical body,
/// authored profile reference, and current impact attribution; the registry owns
/// cap/eviction, runtime identity, throw tokens, rest, and protection state.
/// No per-body physics callback is registered (ARCHITECTURE §23).
/// </summary>
[GlobalClass]
public partial class LooseObjectBody : RigidBody2D, IImpactSource
{
    private const float OutlineWidth = 2.0f;
    private Color _outlineColor = new("183042");
    private Color _fillColor = new("ffd27a");
    private LooseObjectRegistry? _registry;
    private string _impactContentId = ContentIds.LooseObject;
    private bool _profileConfigured;

    public float Radius { get; private set; } = 12.0f;
    public int RuntimeId { get; private set; }
    public LooseObjectProfile? Profile { get; private set; }

    public int InteractionId { get; } = InteractionIds.Next();

    public string ContentId => _impactContentId;
    public string SemanticContentId => Profile?.ContentId ?? ContentIds.LooseObject;

    public void Configure(LooseObjectProfile profile)
    {
        bool valid = GodotObject.IsInstanceValid(profile) && profile.IsRuntimeValid;
        if (!valid)
            throw new System.InvalidOperationException("LooseObjectBody requires a valid profile.");

        _profileConfigured = true;
        Profile = profile;
        _fillColor = profile.FillColor;
        _outlineColor = profile.OutlineColor;
        Configure(profile.Radius, profile.Mass, profile.LinearDamp, profile.AngularDamp);
        // Restitution is the only physical property Godot does not take from the body itself.
        // Authored bounce of 0 is the engine default, so a profile that authors none is left
        // without a material at all and behaves bit-identically to before the field existed.
        if (profile.Bounce > 0.0f)
            PhysicsMaterialOverride = new PhysicsMaterial { Bounce = profile.Bounce };
    }

    /// <summary>Legacy scenario helper; registry-backed runtime objects use the profile overload.</summary>
    public void Configure(float radius, float mass, float linearDamp, float angularDamp)
    {
        Radius = radius;
        Mass = mass;
        LinearDamp = linearDamp;
        AngularDamp = angularDamp;
        LinearDampMode = DampMode.Replace;
        AngularDampMode = DampMode.Replace;
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = radius } });
        CollisionLayer = CollisionLayers.LooseObjects;
        CollisionMask = CollisionLayers.MaskLooseObjects;
        CanSleep = true;
        QueueRedraw();
    }

    internal void AttachRegistration(
        LooseObjectRegistry registry,
        LooseObjectProfile profile,
        int runtimeId)
    {
        _registry = registry;
        Profile = profile;
        RuntimeId = runtimeId;
        _impactContentId = ContentIds.LooseObject;
    }

    internal void DetachRegistration()
    {
        _registry = null;
        RuntimeId = 0;
    }

    internal void SetImpactAttribution(string contentId) =>
        _impactContentId = string.IsNullOrWhiteSpace(contentId)
            ? ContentIds.LooseObject
            : contentId;

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, _fillColor, true, -1.0f, true);
        DrawArc(Vector2.Zero, Radius, 0.0f, Mathf.Tau, 32, _outlineColor, OutlineWidth, true);
    }

    public override void _Ready()
    {
        // FR-014 audit: a profile-configured object is a shipped loose object and must be
        // inside the one registry. Registration happens right after the parent adds the child,
        // so the check is deferred to the end of this frame. The legacy radius/mass overload is
        // exempt on purpose — it exists for M1/M3 scenario props that predate the registry.
        if (BuildInfo.IsDebugBuild)
            CallDeferred(nameof(AuditRegistration));
    }

    private void AuditRegistration()
    {
        if (!_profileConfigured || RuntimeId != 0 || !IsInsideTree())
            return;

        Log.Error(
            "LooseObject",
            $"'{SemanticContentId}' entered the scene without registering with the " +
            "LooseObjectRegistry; it escapes the FR-014 budget. Spawn it through the " +
            "root's loose-object factory.");
    }

    public override void _ExitTree()
    {
        if (_registry is { IsInitialized: true })
            _registry.Unregister(this);
    }
}
