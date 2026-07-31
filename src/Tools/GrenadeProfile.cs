using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Provisional, laboratory-tunable tuning for the Grenade (M5 Task 6 plan §2.7). The
/// grenade's <b>physical</b> body — radius, mass, damping, colours — is authored in its
/// <see cref="Objects.LooseObjectProfile"/> like every other launchable; this Resource
/// authors the parts a plain loose object has no concept of: the fuse, the blast, and the
/// presentation punctuation.
///
/// <para><b>The blast is an impulse source, not a damage source.</b>
/// <see cref="EquivalentImpulseAtCenter"/> is fed to the same shared
/// <c>PainCurve.PainFor</c> every collision goes through, with distance falloff as the
/// only shaping. There is no per-tool damage multiplier here or anywhere — the sacred rule
/// (`DECISIONS.md`) holds because the curve still owns impulse→pain, exactly as it does
/// for a bat or a bullet.</para>
///
/// <para>Durations are in <b>routed physics ticks</b>, so a paused laboratory holds the
/// fuse by construction (ARCHITECTURE §7).</para>
/// </summary>
[GlobalClass]
public partial class GrenadeProfile : GameResource
{
    [Export] public string ContentId { get; set; } = ContentIds.ToolGrenade;

    // --- Fuse ---

    /// <summary>Ticks from release to detonation — <c>360</c> is the owner's 3.0 s.</summary>
    [Export(PropertyHint.Range, "1,3600,1,or_greater")] public int FuseTicks { get; set; } = 360;

    // --- Blast ---

    /// <summary>
    /// The impulse a buddy part right at the centre is scored as having taken, before
    /// falloff. Tuned, not asserted: the target is the owner's "five pistol bullets"
    /// (plan §2.2), measured in the laboratory against a real solid bullet hit.
    /// </summary>
    [Export(PropertyHint.Range, "1,20000,1,or_greater")] public float EquivalentImpulseAtCenter { get; set; } = 1150.0f;

    /// <summary>Within this radius the blast is at full strength.</summary>
    [Export(PropertyHint.Range, "1,512,0.5,or_greater")] public float BlastFullRadiusPx { get; set; } = 48.0f;

    /// <summary>
    /// Where the blast has faded to nothing. Linear between the two radii. There is no
    /// occlusion model: the room is one open box, so nothing can stand behind anything.
    /// </summary>
    [Export(PropertyHint.Range, "2,1024,0.5,or_greater")] public float BlastZeroRadiusPx { get; set; } = 180.0f;

    /// <summary>
    /// The physical shove, in impulse units, applied at the centre to every dynamic body
    /// on <c>BuddyParts | LooseObjects</c>. Separate from the pain impulse because they
    /// answer different questions: this one decides what the room looks like afterwards,
    /// and only the buddy is scored for pain at all.
    /// </summary>
    [Export(PropertyHint.Range, "0,20000,1,or_greater")] public float ShoveImpulseAtCenter { get; set; } = 900.0f;

    // --- Presentation (none of it may touch the routed tick or the pain path) ---

    /// <summary>Peak camera kick on detonation. "Medium" against the pistol's 1.5.</summary>
    [Export(PropertyHint.Range, "0,32,0.1")] public float KickAmplitudePx { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0,120,1,or_greater")] public int KickDecayTicks { get; set; } = 14;

    /// <summary>Ticks the additive detonation flash is drawn for.</summary>
    [Export(PropertyHint.Range, "0,30,1,or_greater")] public int FlashTicks { get; set; } = 3;

    /// <summary>Ticks the expanding blast ring takes to reach <see cref="BlastFullRadiusPx"/> and fade.</summary>
    [Export(PropertyHint.Range, "1,120,1,or_greater")] public int RingTicks { get; set; } = 20;

    /// <summary>
    /// Impact speed, in px/s, below which hitting the floor makes no sound. Without a
    /// floor a rolling grenade would machine-gun the thud cue.
    /// </summary>
    [Export(PropertyHint.Range, "0,4000,1,or_greater")] public float ThudMinImpactSpeed { get; set; } = 250.0f;

    /// <summary>Minimum routed ticks between two thuds, for the same reason.</summary>
    [Export(PropertyHint.Range, "1,600,1,or_greater")] public int ThudMinIntervalTicks { get; set; } = 12;

    [Export(PropertyHint.Range, "-60,6,0.5")] public float AudioVolumeDb { get; set; } = -6.0f;

    // --- Colours (the mesh and the 2D fallback read the same three) ---

    /// <summary>Olive-drab body.</summary>
    [Export] public Color BodyColor { get; set; } = new("4a5d33");

    /// <summary>Darker cap and lever.</summary>
    [Export] public Color CapColor { get; set; } = new("2c3620");

    /// <summary>The light pin ring — also the colour of the dropped pin body.</summary>
    [Export] public Color PinColor { get; set; } = new("c9c3a6");

    /// <summary>Additive flash / ring colour.</summary>
    [Export] public Color BlastColor { get; set; } = new("ffd489");

    /// <summary>Camera-axis depth the drawn grenade sits at, matching the loose objects.</summary>
    [Export] public float VisualDepthOffset { get; set; } = 140.0f;

    /// <summary>How far the pin flies when it drops, in px/s.</summary>
    [Export(PropertyHint.Range, "0,600,1,or_greater")] public float PinEjectSpeed { get; set; } = 90.0f;

    /// <summary>Ticks a dropped pin lies on the floor before it fades and re-pools.</summary>
    [Export(PropertyHint.Range, "30,3600,1,or_greater")] public int PinLingerTicks { get; set; } = 480;

    /// <summary>Cosmetic pins the component preallocates.</summary>
    public const int PinPoolCapacity = 3;

    /// <summary>The engine-free fuse constants this profile authors.</summary>
    public GrenadeFuseConstants ToFuseConstants() => new(FuseTicks);

    /// <summary>
    /// The blast's strength at <paramref name="distancePx"/> from the centre, as a
    /// fraction in <c>[0,1]</c>. Full inside the inner radius, linear to nothing at the
    /// outer one. Shared by pain and shove so the two can never disagree about reach.
    /// </summary>
    public float FalloffAt(float distancePx)
    {
        if (!float.IsFinite(distancePx) || distancePx <= BlastFullRadiusPx)
            return distancePx < 0.0f ? 0.0f : 1.0f;
        if (distancePx >= BlastZeroRadiusPx)
            return 0.0f;

        float span = BlastZeroRadiusPx - BlastFullRadiusPx;
        return Mathf.Clamp(1.0f - ((distancePx - BlastFullRadiusPx) / span), 0.0f, 1.0f);
    }

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (ContentId != ContentIds.ToolGrenade)
        {
            errors.Add(
                $"{nameof(ContentId)} must be '{ContentIds.ToolGrenade}', not '{ContentId}'");
        }

        if (!ToFuseConstants().IsWellFormed())
        {
            errors.Add($"{nameof(FuseTicks)} must be positive");
        }

        if (!float.IsFinite(EquivalentImpulseAtCenter) || EquivalentImpulseAtCenter <= 0.0f)
        {
            errors.Add($"{nameof(EquivalentImpulseAtCenter)} must be finite and positive");
        }

        if (!float.IsFinite(ShoveImpulseAtCenter) || ShoveImpulseAtCenter < 0.0f)
        {
            errors.Add($"{nameof(ShoveImpulseAtCenter)} must be finite and non-negative");
        }

        if (!float.IsFinite(BlastFullRadiusPx) || BlastFullRadiusPx <= 0.0f ||
            !float.IsFinite(BlastZeroRadiusPx) || BlastZeroRadiusPx <= BlastFullRadiusPx)
        {
            // A zero-width falloff band would make the blast a step function: full damage
            // one pixel in, nothing one pixel out.
            errors.Add(
                $"{nameof(BlastFullRadiusPx)} must be finite and positive, and " +
                $"{nameof(BlastZeroRadiusPx)} strictly greater than it");
        }

        if (!float.IsFinite(KickAmplitudePx) || KickAmplitudePx < 0.0f || KickDecayTicks < 0)
        {
            errors.Add(
                $"{nameof(KickAmplitudePx)} must be finite and non-negative and " +
                $"{nameof(KickDecayTicks)} non-negative");
        }
        else if (KickAmplitudePx > 0.0f && KickDecayTicks <= 0)
        {
            // An envelope with no length never decays, so the kick would be permanent.
            errors.Add(
                $"{nameof(KickDecayTicks)} must be positive when {nameof(KickAmplitudePx)} is");
        }

        if (FlashTicks < 0 || RingTicks < 1)
        {
            errors.Add(
                $"{nameof(FlashTicks)} must be non-negative and {nameof(RingTicks)} positive");
        }

        if (!float.IsFinite(ThudMinImpactSpeed) || ThudMinImpactSpeed < 0.0f ||
            ThudMinIntervalTicks < 1)
        {
            errors.Add(
                $"{nameof(ThudMinImpactSpeed)} must be finite and non-negative and " +
                $"{nameof(ThudMinIntervalTicks)} positive");
        }

        if (!float.IsFinite(AudioVolumeDb) || !float.IsFinite(VisualDepthOffset))
        {
            errors.Add(
                $"{nameof(AudioVolumeDb)} and {nameof(VisualDepthOffset)} must be finite");
        }

        if (!float.IsFinite(PinEjectSpeed) || PinEjectSpeed < 0.0f || PinLingerTicks < 1)
        {
            errors.Add(
                $"{nameof(PinEjectSpeed)} must be finite and non-negative and " +
                $"{nameof(PinLingerTicks)} positive");
        }

        return errors;
    }
}
