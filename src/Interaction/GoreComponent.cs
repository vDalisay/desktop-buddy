using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Interaction;

/// <summary>
/// Gore Mode: piercing hits open bleeding wounds, wounds spray and drip, and what lands
/// stains the buddy and the room.
///
/// <para><b>This is presentation and only presentation.</b> It is a listener on
/// <see cref="InteractionDamageComponent.ImpactAccepted"/>, downstream of every decision
/// that matters: pain, payout, mood, harmful memory and the knockout window have all
/// already been applied by the time a wound is opened here, and nothing on this component
/// is readable from the pipeline. Turning Gore Mode on cannot change one tick of what a
/// run simulates, which is the same contract <see cref="EffectsSettings"/> carries and the
/// reason the buddy stays immortal (FR-004.3) with it switched on.</para>
///
/// <para><b>Two gates, asked separately.</b> <see cref="DemoScope.IncludesGore"/> is
/// whether the build ships the feature at all — false for the itch.io preset — and
/// <see cref="EffectsSettings.Gore"/> is whether the player asked for it. Both are
/// required. The build gate is asked here rather than trusted from the settings row, so a
/// hand-edited <c>settings.json</c> carried onto a build without the feature stays
/// inert.</para>
///
/// <para><b>What bleeds.</b> Piercing weapons only — the sword and the two real guns. A
/// bat, a boxing glove, a football and a Nerf dart are blunt: they hurt exactly as much as
/// they did before and they draw no blood, which is what keeps Gore Mode a thing the
/// player turns on for the weapons it is about rather than a wash of red over every
/// interaction in the game.</para>
/// </summary>
[GlobalClass]
public partial class GoreComponent : Node2D
{
    /// <summary>
    /// Pain that counts as a full-severity wound. Well under the curve's 100 ceiling: a
    /// solid pistol shot should open a proper wound without needing a maximum-pain hit,
    /// and everything past this simply pins at full.
    /// </summary>
    private const float FullSeverityPain = 55.0f;

    /// <summary>
    /// Pain below which a piercing hit is a graze that draws no blood. It sits above the
    /// curve's own zero-pain floor so that a spent bullet rolling into a foot cannot open
    /// a wound.
    /// </summary>
    private const float MinimumWoundingPain = 4.0f;

    /// <summary>Radius of the stain left on the part by the opening hit, at full severity.</summary>
    private const float OpeningStainRadius = 7.0f;

    private static readonly string[] PiercingContentIds =
    [
        ContentIds.ToolSword,
        ContentIds.ToolPistol,
        ContentIds.ToolShotgun,
    ];

    private readonly BleedWound[] _wounds = new BleedWound[6];

    private BleedingConstants _constants = BleedingConstants.Default;
    private EffectsSettings _effects = EffectsSettings.Default;
    private BloodStainLayer2D _stains = null!;

    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public BuddyRoot Buddy { get; set; } = null!;

    /// <summary>
    /// The room drops land in. Optional: without it drips simply expire in the air, which
    /// is what an isolated composition should get rather than a crash.
    /// </summary>
    [Export] public BoundaryController Boundaries { get; set; } = null!;

    public bool IsInitialized { get; private set; }

    /// <summary>Wounds opened since the run started. Scenario-observable, never gameplay.</summary>
    public int WoundsOpened { get; private set; }

    /// <summary>Drips emitted since the run started.</summary>
    public int DripsEmitted { get; private set; }

    /// <summary>Drops currently in the air.</summary>
    public int LiveDroplets => GodotObject.IsInstanceValid(_stains) ? _stains.LiveDroplets : 0;

    /// <summary>Where blood that has landed is kept.</summary>
    public BloodStainLayer2D Stains => _stains;

    /// <summary>Both gates: this build ships Gore Mode and the player has it switched on.</summary>
    public bool IsActive => DemoScope.IncludesGore && _effects.Gore;

    /// <summary>True while any part is bleeding.</summary>
    public bool IsBleeding
    {
        get
        {
            for (int index = 0; index < _wounds.Length; index++)
            {
                if (_wounds[index].IsBleeding)
                    return true;
            }

            return false;
        }
    }

    public BleedWound WoundOn(BuddyPart part) => _wounds[(int)part];

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(Buddy))
        {
            throw new InvalidOperationException("GoreComponent dependencies are incomplete.");
        }

        _stains = new BloodStainLayer2D { Name = "BloodStainLayer" };
        AddChild(_stains);
        _stains.Initialize(
            Buddy.Rig,
            GodotObject.IsInstanceValid(Boundaries) ? Boundaries : null);

        ZAsRelative = false;
        ZIndex = 151;
        Pipeline.ImpactAccepted += OnImpactAccepted;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Pipeline))
            Pipeline.ImpactAccepted -= OnImpactAccepted;
    }

    /// <summary>
    /// Switching Gore Mode off stops the bleeding and wipes what is already there. A
    /// setting the player turned off must leave nothing on screen, and leaving dried stains
    /// behind would make the toggle look broken.
    /// </summary>
    public void ApplyEffectsSettings(EffectsSettings settings)
    {
        bool wasActive = IsActive;
        _effects = settings;
        if (wasActive && !IsActive)
            ClearAll();
    }

    /// <summary>
    /// Patches the buddy up: every wound closed and every mark gone. The Repair Kit's entry
    /// point, and the fail-safe for a hard reposition.
    /// </summary>
    public void ClearAll()
    {
        for (int index = 0; index < _wounds.Length; index++)
            _wounds[index] = BleedingStatus.Clear(_wounds[index]);

        // Sprays still playing are part of "no trace left behind". Drops in the air are
        // cleared by the layer itself, which owns them as data rather than as nodes.
        foreach (Node child in GetChildren())
        {
            if (child is BloodSpray2D)
                child.QueueFree();
        }

        if (GodotObject.IsInstanceValid(_stains))
            _stains.Clear();
    }

    /// <summary>
    /// Opens a wound that did not come through the damage pipeline. The Sword's skewer is
    /// the caller: running a blade into someone is a matter of geometry, so it never
    /// produces the impulse an accepted impact would have carried, but it very much
    /// produces a wound.
    ///
    /// <para>Gated like everything else here, so a build or a player without Gore Mode gets
    /// the blade going in and no blood — impaling is the Sword's mechanic, the blood is
    /// this component's.</para>
    /// </summary>
    public void OpenWound(BuddyPart part, float severity, Vector2 worldPoint)
    {
        if (!IsActive)
            return;

        int slot = (int)part;
        if (slot < 0 || slot >= _wounds.Length)
            return;

        BleedOpenResult opened = BleedingStatus.Open(_wounds[slot], severity, _constants);
        if (!opened.IsValid)
            return;

        _wounds[slot] = opened.Wound;
        WoundsOpened++;
        SpawnSpray(worldPoint, Vector2.Up, severity);
        StainPart(part, worldPoint, severity);
    }

    /// <summary>The mark the opening hit leaves on the buddy, in the struck part's space.</summary>
    private void StainPart(BuddyPart part, Vector2 worldPoint, float severity)
    {
        if (!TryPart(part, out PuppetPartBody? body))
            return;

        _stains.AddPartStain(
            (BuddyPartId)(int)part,
            body!.ToLocal(worldPoint),
            OpeningStainRadius * (0.55f + (0.45f * severity)));
    }

    private void OnImpactAccepted(AcceptedImpact impact)
    {
        if (!IsActive || impact.Pain < MinimumWoundingPain || !IsPiercing(impact.ContentId))
            return;

        int slot = (int)impact.Part;
        if (slot < 0 || slot >= _wounds.Length)
            return;

        float severity = Mathf.Clamp(impact.Pain / FullSeverityPain, 0.0f, 1.0f);
        BleedOpenResult opened = BleedingStatus.Open(_wounds[slot], severity, _constants);
        if (!opened.IsValid)
            return;

        _wounds[slot] = opened.Wound;
        WoundsOpened++;

        // Out of the wound, not into it: the spray follows the contact normal back the way
        // the weapon came, with a little lift so it arcs rather than hugging the surface.
        Vector2 outward = impact.Normal.LengthSquared() > 0.001f
            ? -impact.Normal.Normalized()
            : Vector2.Up;
        SpawnSpray(impact.Point, (outward + (Vector2.Up * 0.4f)).Normalized(), severity);
        StainPart(impact.Part, impact.Point, severity);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsActive)
            return;

        for (int slot = 0; slot < _wounds.Length; slot++)
        {
            if (!_wounds[slot].IsBleeding)
                continue;

            BleedTickResult result = BleedingStatus.Tick(_wounds[slot], _constants);
            if (!result.IsValid)
                continue;

            _wounds[slot] = result.Wound;
            if (result.DripDue)
                Drip((BuddyPart)slot, result.Wound.Intensity(_constants));
        }
    }

    /// <summary>
    /// One drop leaves the wound. It starts at the underside of the part carrying the
    /// part's own motion, so blood thrown from a swinging arm travels with the arm instead
    /// of falling straight down out of a moving body.
    /// </summary>
    private void Drip(BuddyPart part, float intensity)
    {
        if (!TryPart(part, out PuppetPartBody? body))
            return;

        // Reduced Particles thins drips as it thins everything else: every third drop.
        if (_effects.ParticleStride > 1 && DripsEmitted % _effects.ParticleStride != 0)
        {
            DripsEmitted++;
            return;
        }

        DripsEmitted++;

        // A drop leaves the underside of the part, offset around it so a wound does not
        // emit a single vertical thread of beads. The layer silently drops it if the air is
        // already full; the cadence is the wound's business, not the renderer's.
        float lean = (float)GD.RandRange(-body!.Radius * 0.55, body.Radius * 0.55);
        Vector2 origin = body.GlobalPosition + new Vector2(lean, body.Radius * 0.7f);

        _stains.AddDroplet(
            origin,
            // Inherited part motion, damped, plus a little sideways scatter: a drop is
            // flung off a moving limb, not welded to it.
            (body.LinearVelocity * 0.35f) + new Vector2((float)GD.RandRange(-22.0, 22.0), 0.0f),
            1.3f + (1.5f * Mathf.Clamp(intensity, 0.0f, 1.0f)));
    }

    private void SpawnSpray(Vector2 worldPoint, Vector2 direction, float severity)
    {
        var spray = new BloodSpray2D { Name = "BloodSpray", GlobalPosition = worldPoint };
        AddChild(spray);
        spray.GlobalPosition = worldPoint;
        spray.Start(direction, severity, _effects.ParticleStride);
    }

    private bool TryPart(BuddyPart part, out PuppetPartBody? body)
    {
        body = null;
        if (!GodotObject.IsInstanceValid(Buddy) || !GodotObject.IsInstanceValid(Buddy.Rig) ||
            !Buddy.Rig.IsInitialized)
        {
            return false;
        }

        body = Buddy.Rig.GetPart((BuddyPartId)(int)part);
        return GodotObject.IsInstanceValid(body);
    }

    private static bool IsPiercing(string contentId) =>
        Array.IndexOf(PiercingContentIds, contentId) >= 0;
}
