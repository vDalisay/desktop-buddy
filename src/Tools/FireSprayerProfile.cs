using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Provisional, laboratory-tunable tuning for the Fire Sprayer (RAGDOLL §9.2 Fire Sprayer
/// row and §9.3 Burning). Every quantity §9.3 leaves open — burn pain, tick cadence, mood
/// loss, visuals, particles, audio — is authored here and is a §9.3 "tuning value" by
/// construction.
///
/// <para>It is deliberately <b>not</b> a <see cref="GunProfile"/>. <see cref="GunMachine"/>
/// is a press-edge cadence/magazine machine; the sprayer is hold-to-stream with no
/// magazine, no reload, and no press edge, and forcing it through that model would mean
/// authoring a fake capacity nobody ever sees (owner default 2: no ammunition, heat, or
/// duration limit).</para>
///
/// <para>The aim constants are the same block the guns author, because RAGDOLL §9.1 says
/// Pistol, Shotgun, and Fire Sprayer share one aim model. They are copied rather than
/// referenced so a weapon's feel stays one authored resource.</para>
///
/// <para><b>Nothing here scales damage.</b> Burn pain reaches the buddy as an equivalent
/// impulse through the shared curve, exactly as the grenade's blast does; there is no
/// per-tool multiplier anywhere (`DECISIONS.md`, the no-per-tool-multiplier rule).</para>
/// </summary>
[GlobalClass]
public partial class FireSprayerProfile : GameResource
{
    [Export] public string ContentId { get; set; } = ContentIds.ToolFireSprayer;

    // --- Aim (shared by every cursor weapon, RAGDOLL §9.1) ---

    [Export(PropertyHint.Range, "1,60,0.5,or_greater")] public float AimSmoothingHalfLifeTicks { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "0.05,8,0.05,or_greater")] public float MinimumAimSpeedPxPerTick { get; set; } = 0.35f;

    [Export(PropertyHint.Range, "0.5,45,0.5,or_greater")] public float MaxAimTurnDegreesPerTick { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "0.1,45,0.1,or_greater")] public float WheelDegreesPerStep { get; set; } = 5.0f;

    [Export(PropertyHint.Range, "0,89,0.1")] public float MaximumAimOffsetDegrees { get; set; } = 60.0f;

    // --- Stream ---

    /// <summary>
    /// Routed ticks between two droplets while primary is held. <c>4</c> is 30 droplets a
    /// second at the project's fixed 120 Hz. There is no press edge: the stream starts on
    /// the first held tick and stops on the tick primary is released.
    /// </summary>
    [Export(PropertyHint.Range, "1,60,1,or_greater")] public int EmitIntervalTicks { get; set; } = 4;

    [Export(PropertyHint.Range, "50,4000,1,or_greater")] public float SprayDropletSpeed { get; set; } = 700.0f;

    /// <summary>A sagging stream rather than a bullet: the sprayer is a close-range weapon.</summary>
    [Export(PropertyHint.Range, "0,4,0.01")] public float DropletGravityScale { get; set; } = 0.4f;

    [Export(PropertyHint.Range, "2,600,1,or_greater")] public int DropletLifetimeTicks { get; set; } = 45;

    [Export(PropertyHint.Range, "16,4000,1,or_greater")] public float DropletMaxTravelPx { get; set; } = 260.0f;

    /// <summary>
    /// Half-angle of the stream's lateral fan. The fan is driven by the droplet's own
    /// index through a deterministic triangle wave, never by a random source: the fan
    /// pattern is gameplay, and a replayed seed must reproduce the stream exactly.
    /// </summary>
    [Export(PropertyHint.Range, "0,45,0.1")] public float SprayHalfAngleDegrees { get; set; } = 7.0f;

    /// <summary>How far ahead of the cursor a droplet is born, clear of the nozzle.</summary>
    [Export(PropertyHint.Range, "0,128,0.1,or_greater")] public float MuzzleOffsetPx { get; set; } = 49.4f;

    [Export(PropertyHint.Range, "0.5,32,0.1,or_greater")] public float DropletRadius { get; set; } = 1.5f;

    /// <summary>
    /// Cosmetically tiny (owner default 4: the stream pushes nothing). The sprayer harms
    /// through Burning only and has no knockback lane at all.
    /// </summary>
    [Export(PropertyHint.Range, "0.001,10,0.001,or_greater")] public float DropletMass { get; set; } = 0.05f;

    /// <summary>
    /// Preallocated droplet slots. Droplets are bounded here and never enter the loose-object
    /// registry, so they cannot consume one of the 24 FR-014 slots (RAGDOLL §10). Sized to
    /// cover a full lifetime of continuous emission plus headroom.
    /// </summary>
    [Export(PropertyHint.Range, "1,256,1,or_greater")] public int PoolCapacity { get; set; } = 48;

    // --- Burning (RAGDOLL §9.3; all tuning values) ---

    /// <summary>Ticks one fire contact grants — <c>480</c> is §9.3's 4 s at 120 Hz.</summary>
    [Export(PropertyHint.Range, "1,3600,1,or_greater")] public int BurnApplyTicks { get; set; } = 480;

    /// <summary>The cap remaining may never exceed — <c>960</c> is §9.3's 8 s.</summary>
    [Export(PropertyHint.Range, "1,7200,1,or_greater")] public int BurnCapTicks { get; set; } = 960;

    /// <summary>Ticks between two attributed burn pain events — <c>60</c> is 0.5 s.</summary>
    [Export(PropertyHint.Range, "1,1200,1,or_greater")] public int BurnPainIntervalTicks { get; set; } = 60;

    /// <summary>
    /// The equivalent impulse one burn event hands the shared pain curve.
    ///
    /// <para>Measured against the shipped conversion profile (anchors 350/700/1500/3000 to
    /// pain 0/20/55/100) on 2026-08-01: <c>430</c> scores <b>4.57 pain</b> per event, so a
    /// full 4 s burn totals about <b>36.6</b> pain over eight events and a sustained 8 s cap
    /// burn about <b>73.1</b> over sixteen — painful and profitable, and never a knockout by
    /// itself, because the most any rolling 5 s window can hold is ten events (<b>45.7</b>)
    /// against the 100-pain threshold (owner default 1).</para>
    /// </summary>
    [Export(PropertyHint.Range, "1,20000,1,or_greater")] public float BurnEquivalentImpulse { get; set; } = 430.0f;

    // --- Scorch marks (owner feedback 2026-08-01; presentation only) ---

    /// <summary>Routed ticks of continuous burning to reach <see cref="MaxScorchDarkness"/>.</summary>
    [Export(PropertyHint.Range, "1,3600,1,or_greater")] public int ScorchTicksToFull { get; set; } = 720;

    /// <summary>
    /// The darkest a part may ever get, as a fraction toward <see cref="ScorchColor"/>.
    /// Below one on purpose: a fully black limb reads as a hole in the buddy rather than a
    /// burnt one, and the buddy cannot be permanently damaged.
    /// </summary>
    [Export(PropertyHint.Range, "0.05,1,0.01")] public float MaxScorchDarkness { get; set; } = 0.72f;

    /// <summary>Ticks a mark holds at full strength once the fire is out — <c>1200</c> is 10 s.</summary>
    [Export(PropertyHint.Range, "0,7200,1,or_greater")] public int ScorchHoldTicks { get; set; } = 1200;

    /// <summary>Ticks the mark then takes to fade back to clean skin — <c>600</c> is 5 s.</summary>
    [Export(PropertyHint.Range, "1,7200,1,or_greater")] public int ScorchFadeTicks { get; set; } = 600;

    /// <summary>The soot colour a scorched part is tinted toward.</summary>
    [Export] public Color ScorchColor { get; set; } = new("241c18");

    // --- Presentation (all of it; nothing below reaches gameplay) ---

    /// <summary>Colour of a droplet in flight.</summary>
    [Export] public Color FlameColor { get; set; } = new("ff9a3c");

    /// <summary>The hot core of the stream and of the flicker on a burning part.</summary>
    [Export] public Color FlameCoreColor { get; set; } = new("ffe07a");

    /// <summary>Rising motes over a burning part.</summary>
    [Export] public Color EmberColor { get; set; } = new("ff5a1f");

    /// <summary>
    /// What the stream cools to at the end of its reach. The mist is drawn as flame at the
    /// nozzle blending to this as each droplet ages, which is what makes a stream of
    /// discrete droplets read as one billowing, smoky column (owner feedback 2026-08-01).
    /// </summary>
    [Export] public Color SmokeColor { get; set; } = new("4a4038");

    /// <summary>
    /// How much wider than the droplet's collider the mist puff grows by the end of its
    /// life. Purely a drawn size: the collider never changes, so the stream looks like fire
    /// and hits like the authored `1.5 px` circle it has always been.
    /// </summary>
    [Export(PropertyHint.Range, "1,16,0.1,or_greater")] public float MistSpreadFactor { get; set; } = 6.5f;

    /// <summary>The drawn sprayer's colours, on the gun silhouette's two-colour rule.</summary>
    [Export] public Color BodyColor { get; set; } = new("3a3f4b");

    [Export] public Color AccentColor { get; set; } = new("b2431f");

    /// <summary>Nozzle-to-grip length of the drawn sprayer in world pixels.</summary>
    [Export(PropertyHint.Range, "8,256,0.5,or_greater")] public float VisualLengthPx { get; set; } = 52.0f;

    /// <summary>Where along <see cref="VisualLengthPx"/> the nozzle mouth is.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.01")] public float MuzzleTipFraction { get; set; } = 0.95f;

    [Export] public float VisualDepthOffset { get; set; } = 144.0f;

    /// <summary>Flicker cycles a second on a burning part when photosensitivity-safe.</summary>
    [Export(PropertyHint.Range, "0.5,3,0.1")] public float SafeFlickerHz { get; set; } = 3.0f;

    /// <summary>The faster flicker a player gets only by opting out of the safe cap.</summary>
    [Export(PropertyHint.Range, "0.5,24,0.1")] public float FullFlickerHz { get; set; } = 8.0f;

    /// <summary>Ember motes drawn around a burning part; zero draws none.</summary>
    [Export(PropertyHint.Range, "0,64,1")] public int EmberCount { get; set; } = 9;

    /// <summary>How far, in part radii, the embers rise before they fade.</summary>
    [Export(PropertyHint.Range, "0.5,8,0.1")] public float EmberReachFactor { get; set; } = 2.4f;

    /// <summary>Routed ticks one ember cycle takes.</summary>
    [Export(PropertyHint.Range, "1,600,1,or_greater")] public int EmberCycleTicks { get; set; } = 72;

    [Export(PropertyHint.Range, "-60,12,0.1")] public float AudioVolumeDb { get; set; } = -12.0f;

    /// <summary>Pixels of agreement required between the authored nozzle and the drawn tip.</summary>
    public const float MuzzleAgreementPx = 2.0f;

    /// <summary>Where the nozzle mouth is, in pixels ahead of the cursor.</summary>
    public float VisualMuzzleTipPx => VisualLengthPx * MuzzleTipFraction;

    /// <summary>The tool this profile serves; only meaningful once <see cref="Validate"/> passes.</summary>
    public ToolId Tool
    {
        get
        {
            ContentIds.TryParseTool(ContentId, out ToolId tool);
            return tool;
        }
    }

    public CursorAimConstants ToAimConstants() => new(
        AimSmoothingHalfLifeTicks,
        MinimumAimSpeedPxPerTick,
        MaxAimTurnDegreesPerTick,
        WheelDegreesPerStep,
        MaximumAimOffsetDegrees);

    public BurningConstants ToBurningConstants() => new(
        BurnApplyTicks, BurnCapTicks, BurnPainIntervalTicks);

    /// <summary>The engine-free scorch timing this profile authors.</summary>
    public ScorchConstants ToScorchConstants() => new(
        ScorchTicksToFull, MaxScorchDarkness, ScorchHoldTicks, ScorchFadeTicks);

    /// <summary>
    /// The lateral offset, in degrees, droplet <paramref name="index"/> leaves the nozzle
    /// at. A deterministic triangle wave over an eight-droplet period, sweeping the fan
    /// -1, -0.5, 0, +0.5, +1, +0.5, 0, -0.5 and repeating: the stream visibly wobbles
    /// without a random source anywhere in it, so a replayed seed reproduces the spray
    /// exactly. The fan is gameplay, not presentation.
    /// </summary>
    public float FanDegrees(int index)
    {
        if (SprayHalfAngleDegrees <= 0.0f)
            return 0.0f;

        const int period = 8;
        int step = ((index % period) + period) % period;
        float wave = step <= period / 2
            ? step / (period * 0.5f)
            : (period - step) / (period * 0.5f);
        return ((wave * 2.0f) - 1.0f) * SprayHalfAngleDegrees;
    }

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (ContentId != ContentIds.ToolFireSprayer)
        {
            errors.Add(
                $"{nameof(ContentId)} must be '{ContentIds.ToolFireSprayer}', not '{ContentId}'");
        }

        if (!ToAimConstants().IsWellFormed())
        {
            errors.Add(
                $"{nameof(AimSmoothingHalfLifeTicks)}, {nameof(MinimumAimSpeedPxPerTick)}, " +
                $"{nameof(MaxAimTurnDegreesPerTick)}, and {nameof(WheelDegreesPerStep)} must " +
                $"all be finite and positive, and {nameof(MaximumAimOffsetDegrees)} non-negative");
        }

        if (EmitIntervalTicks < 1)
        {
            errors.Add($"{nameof(EmitIntervalTicks)} must be at least one tick");
        }

        if (!float.IsFinite(SprayDropletSpeed) || SprayDropletSpeed <= 0.0f)
        {
            errors.Add($"{nameof(SprayDropletSpeed)} must be finite and positive");
        }
        else if (SprayDropletSpeed > GunProfile.MaximumTravelPerTickPx * Engine.PhysicsTicksPerSecond)
        {
            // The same geometric coverage rule every projectile in this project obeys: a
            // droplet that steps over the buddy between solver frames never ignites it, and
            // the failure is silent (see GunProfile.MaximumTravelPerTickPx).
            errors.Add(
                $"{nameof(SprayDropletSpeed)} must not exceed " +
                $"{GunProfile.MaximumTravelPerTickPx * Engine.PhysicsTicksPerSecond} px/s " +
                $"({GunProfile.MaximumTravelPerTickPx} px per routed tick), above which a " +
                "droplet can step over its target between solver frames");
        }

        if (!float.IsFinite(DropletGravityScale) || DropletGravityScale < 0.0f)
        {
            errors.Add($"{nameof(DropletGravityScale)} must be finite and non-negative");
        }

        if (DropletLifetimeTicks <= 1)
        {
            errors.Add($"{nameof(DropletLifetimeTicks)} must exceed one tick");
        }

        if (!float.IsFinite(DropletMaxTravelPx) || DropletMaxTravelPx <= 0.0f)
        {
            errors.Add($"{nameof(DropletMaxTravelPx)} must be finite and positive");
        }

        if (!float.IsFinite(SprayHalfAngleDegrees) || SprayHalfAngleDegrees < 0.0f)
        {
            errors.Add($"{nameof(SprayHalfAngleDegrees)} must be finite and non-negative");
        }

        if (!float.IsFinite(MuzzleOffsetPx) || MuzzleOffsetPx < 0.0f)
        {
            errors.Add($"{nameof(MuzzleOffsetPx)} must be finite and non-negative");
        }

        if (!float.IsFinite(DropletRadius) || DropletRadius <= 0.0f)
        {
            errors.Add($"{nameof(DropletRadius)} must be finite and positive");
        }

        if (!float.IsFinite(DropletMass) || DropletMass <= 0.0f)
        {
            errors.Add($"{nameof(DropletMass)} must be finite and positive");
        }

        // A stream must be able to keep a whole lifetime of droplets in the air at once, or
        // a legitimate held spray would silently thin out at range.
        int inFlight = (DropletLifetimeTicks / Mathf.Max(1, EmitIntervalTicks)) + 1;
        if (PoolCapacity < inFlight)
        {
            errors.Add(
                $"{nameof(PoolCapacity)} must cover a full lifetime of continuous emission " +
                $"({inFlight} droplets)");
        }

        if (!ToBurningConstants().IsWellFormed())
        {
            errors.Add(
                $"{nameof(BurnApplyTicks)} and {nameof(BurnPainIntervalTicks)} must be " +
                $"positive and {nameof(BurnCapTicks)} at least {nameof(BurnApplyTicks)}");
        }

        if (!float.IsFinite(BurnEquivalentImpulse) || BurnEquivalentImpulse <= 0.0f)
        {
            errors.Add($"{nameof(BurnEquivalentImpulse)} must be finite and positive");
        }

        if (!ToScorchConstants().IsWellFormed())
        {
            errors.Add(
                $"{nameof(ScorchTicksToFull)} and {nameof(ScorchFadeTicks)} must be positive, " +
                $"{nameof(ScorchHoldTicks)} non-negative, and {nameof(MaxScorchDarkness)} " +
                "within (0,1]");
        }

        if (!float.IsFinite(VisualLengthPx) || VisualLengthPx <= 0.0f ||
            !float.IsFinite(MuzzleTipFraction) ||
            MuzzleTipFraction <= 0.0f || MuzzleTipFraction > 1.0f ||
            !float.IsFinite(VisualDepthOffset))
        {
            errors.Add(
                $"{nameof(VisualLengthPx)} must be finite and positive, " +
                $"{nameof(MuzzleTipFraction)} within (0,1], and " +
                $"{nameof(VisualDepthOffset)} finite");
        }
        else if (Mathf.Abs(MuzzleOffsetPx - VisualMuzzleTipPx) > MuzzleAgreementPx)
        {
            // The drawn nozzle and the point droplets are born at are one fact authored
            // twice; the two drifting apart is what reads as fire leaving the wrong place.
            errors.Add(
                $"{nameof(MuzzleOffsetPx)} ({MuzzleOffsetPx:F1}) must agree with the drawn " +
                $"nozzle mouth ({VisualMuzzleTipPx:F1}) within {MuzzleAgreementPx} px");
        }

        if (!float.IsFinite(SafeFlickerHz) || SafeFlickerHz <= 0.0f ||
            !float.IsFinite(FullFlickerHz) || FullFlickerHz < SafeFlickerHz)
        {
            errors.Add(
                $"{nameof(SafeFlickerHz)} must be finite and positive and " +
                $"{nameof(FullFlickerHz)} at least as fast — the safe cap is a cap");
        }

        if (SafeFlickerHz > 3.0f)
        {
            // FR-017.3 / the plan's photosensitivity rule: the safe look is the shipped
            // look, so its cap is a correctness bound rather than a tuning value.
            errors.Add($"{nameof(SafeFlickerHz)} must not exceed 3 Hz");
        }

        if (EmberCount < 0 || EmberCycleTicks < 1 ||
            !float.IsFinite(EmberReachFactor) || EmberReachFactor <= 0.0f)
        {
            errors.Add(
                $"{nameof(EmberCount)} must be non-negative, {nameof(EmberCycleTicks)} " +
                $"positive, and {nameof(EmberReachFactor)} finite and positive");
        }

        if (!float.IsFinite(MistSpreadFactor) || MistSpreadFactor < 1.0f)
        {
            errors.Add($"{nameof(MistSpreadFactor)} must be finite and at least one");
        }

        if (!float.IsFinite(AudioVolumeDb))
        {
            errors.Add($"{nameof(AudioVolumeDb)} must be finite");
        }

        return errors;
    }
}
