using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Which silhouette a gun is drawn as. There is deliberately no usable default: a gun
/// whose visual was never authored must fail validation rather than ship as whatever
/// shape happens to be first in the enum.
/// </summary>
public enum GunVisual3DKind
{
    Unspecified = 0,
    NerfBlaster = 1,
    RealPistol = 2,
}

/// <summary>
/// Provisional, laboratory-tunable tuning for one cursor gun (RAGDOLL §9.1/§9.2).
/// Every gun runs the same <see cref="GunMachine"/> cadence model, the same
/// <see cref="CursorAim"/> aim, and the same pooled projectile, so the Pistol and the
/// Shotgun differ only in the numbers authored here.
///
/// <para>Cadence and reload are authored in <b>routed physics ticks</b>, not seconds:
/// the spec states them in seconds, and the conversion at the project's fixed 120 Hz
/// is recorded in each field's documentation. Authoring ticks keeps the one clock that
/// gameplay is allowed to know about (ARCHITECTURE §7) as the only clock in the
/// data.</para>
///
/// <para>Pain comes only from the measured solver impulse of a projectile that really
/// hit, through the shared curve; there is no per-gun damage multiplier anywhere. In
/// practice <see cref="MuzzleSpeed"/> is the lever that moves it and
/// <see cref="ProjectileMass"/> barely does: the impulse a small fast body's contact
/// reports is dominated by how deep it got in one step, so mass mostly decides how hard
/// the buddy is shoved rather than how much the shot hurts. Both are provisional until
/// the M5 economy calibration and the owner's feel gate.</para>
/// </summary>
[GlobalClass]
public partial class GunProfile : GameResource
{
    [Export] public string ContentId { get; set; } = ContentIds.ToolPistol;

    /// <summary>Rounds one magazine holds. Reserve ammunition is unlimited (§9.2).</summary>
    [Export(PropertyHint.Range, "1,64,1,or_greater")] public int MagazineCapacity { get; set; } = 8;

    /// <summary>Minimum ticks between two fired shots — <c>30</c> is the Pistol's 0.25 s.</summary>
    [Export(PropertyHint.Range, "1,1200,1,or_greater")] public int ShotIntervalTicks { get; set; } = 30;

    /// <summary>Ticks a reload takes — <c>144</c> is the Pistol's 1.2 s.</summary>
    [Export(PropertyHint.Range, "1,3600,1,or_greater")] public int ReloadTicks { get; set; } = 144;

    /// <summary>Projectiles one shot releases: <c>1</c> for a bullet, <c>6</c> for the Shotgun.</summary>
    [Export(PropertyHint.Range, "1,32,1,or_greater")] public int ProjectilesPerShot { get; set; } = 1;

    /// <summary>
    /// Half-angle of the spread cone in degrees, or <c>0</c> for a single true shot.
    /// A multi-projectile gun without spread would stack every pellet on one line.
    /// </summary>
    [Export(PropertyHint.Range, "0,45,0.1")] public float SpreadHalfAngleDegrees { get; set; }

    // --- Aim (shared by every cursor weapon, RAGDOLL §9.1) ---

    /// <summary>
    /// Ticks for the smoothed pointer velocity to halve at rest. This is the weight of the
    /// weapon in the hand: bigger ignores more jitter and keeps following through longer.
    /// </summary>
    [Export(PropertyHint.Range, "1,60,0.5,or_greater")] public float AimSmoothingHalfLifeTicks { get; set; } = 14.0f;

    /// <summary>
    /// Smoothed pointer speed, in pixels per routed tick, below which the aim holds instead
    /// of steering. Far below the retired raw threshold of one pixel per tick, so a slow
    /// deliberate aim steers where it used to be discarded as jitter.
    /// </summary>
    [Export(PropertyHint.Range, "0.05,8,0.05,or_greater")] public float MinimumAimSpeedPxPerTick { get; set; } = 0.35f;

    /// <summary>The most the aim may turn in one routed tick; smaller feels heavier.</summary>
    [Export(PropertyHint.Range, "0.5,45,0.5,or_greater")] public float MaxAimTurnDegreesPerTick { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "0.1,45,0.1,or_greater")] public float WheelDegreesPerStep { get; set; } = 5.0f;

    [Export(PropertyHint.Range, "0,89,0.1")] public float MaximumAimOffsetDegrees { get; set; } = 60.0f;

    // --- Projectile ---

    /// <summary>
    /// The pixels of travel per routed tick a projectile may make. <b>This bound is what
    /// stops a shot from passing through the buddy</b>, and it is a correctness rule
    /// rather than a tuning value.
    ///
    /// <para>Godot's own continuous collision cannot be used for it. That feature avoids
    /// tunneling by <b>replacing the body's velocity</b> with the reduced velocity that
    /// lands it exactly on the surface it was about to cross: the shot stops in the right
    /// place, but the momentum it was carrying is destroyed instead of transferred, the
    /// solver reports a correspondingly tiny impulse, and the shared pain curve scores a
    /// visibly perfect hit as nothing at all. Measured 2026-07-31 on one point-blank head
    /// shot: pain <c>85</c> with <c>CcdMode.Disabled</c>, pain <c>0</c> with
    /// <c>CastRay</c>, and a clean pass straight through the head with
    /// <c>CastShape</c>.</para>
    ///
    /// <para>So coverage is guaranteed geometrically instead: while a projectile's
    /// per-tick travel stays inside the smallest target's diameter, some sample of its
    /// flight always overlaps that target, the ordinary discrete solver resolves the
    /// contact at full speed, and the impulse it reports is the real one. <c>24 px</c>
    /// keeps a shot inside the smallest buddy part's <c>30 px</c> diameter with margin.
    /// Raising it needs the measurement redone, not just a bigger number.</para>
    /// </summary>
    public const float MaximumTravelPerTickPx = 24.0f;

    [Export(PropertyHint.Range, "100,20000,1,or_greater")] public float MuzzleSpeed { get; set; } = 2400.0f;

    /// <summary>How far ahead of the cursor a shot is born, clear of the muzzle itself.</summary>
    [Export(PropertyHint.Range, "0,128,0.1,or_greater")] public float MuzzleOffsetPx { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "0.5,32,0.1,or_greater")] public float ProjectileRadius { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0.001,10,0.001,or_greater")] public float ProjectileMass { get; set; } = 0.05f;

    /// <summary>
    /// A projectile's own gravity scale. A bullet crosses the default `480x360` room in
    /// about a fifth of a second and is ballistically flat over that; the wheel offset is
    /// the aim, not a drop compensation.
    /// </summary>
    [Export(PropertyHint.Range, "0,4,0.01")] public float ProjectileGravityScale { get; set; }

    /// <summary>Ticks a live projectile may exist before it is recycled unconditionally.</summary>
    [Export(PropertyHint.Range, "2,1200,1,or_greater")] public int ProjectileLifetimeTicks { get; set; } = 120;

    /// <summary>Pixels a live projectile may travel before it is recycled.</summary>
    [Export(PropertyHint.Range, "16,20000,1,or_greater")] public float ProjectileMaxTravelPx { get; set; } = 2400.0f;

    /// <summary>
    /// How many routed ticks a projectile that has connected keeps its physics before
    /// it is taken out of the world. The solver spreads one impact over several steps,
    /// and the first step it reports can be a glancing touch of almost no impulse, so a
    /// projectile withdrawn on that first report never delivers the hit the player saw
    /// land. The shared impact router discards below-threshold touches on its own, so
    /// this window only ever lets the real impulse through.
    /// </summary>
    [Export(PropertyHint.Range, "1,32,1,or_greater")] public int ContactSettleTicks { get; set; } = 4;

    /// <summary>
    /// How many ticks a spent projectile lingers, inert and unable to hit anything
    /// else, before it returns to the pool. This is not decoration: the interaction
    /// pipeline resolves a contact's source on the routed tick <b>after</b> the solver
    /// produced it (ARCHITECTURE §7), so a projectile freed the instant it hit would
    /// have its pain silently dropped for want of a valid collider.
    /// </summary>
    [Export(PropertyHint.Range, "1,32,1,or_greater")] public int SpentLingerTicks { get; set; } = 3;

    /// <summary>
    /// Preallocated projectile slots. The pool is the FR-014 answer for projectiles:
    /// they are bounded here and never consume one of the 24 loose-object slots
    /// (RAGDOLL §10). Sized for the whole magazine in flight at once plus headroom.
    /// </summary>
    [Export(PropertyHint.Range, "1,256,1,or_greater")] public int PoolCapacity { get; set; } = 24;

    [Export] public Color ProjectileColor { get; set; } = new("ffe08a");
    [Export] public Color TrailColor { get; set; } = new("ffb347");

    /// <summary>The gun's body colour: toy green, or gunmetal.</summary>
    [Export] public Color MuzzleColor { get; set; } = new("3a3f4b");

    /// <summary>
    /// The gun's second colour — the Nerf Blaster's orange tip, the Pistol's near-black
    /// grip. Two authored colours are all the silhouettes need to read apart.
    /// </summary>
    [Export] public Color AccentColor { get; set; } = new("1c1f26");

    // --- Visual (the drawn gun; the collider-free presentation half) ---

    /// <summary>The silhouette this gun is built as; must be authored (see the enum).</summary>
    [Export] public GunVisual3DKind Visual3DKind { get; set; } = GunVisual3DKind.Unspecified;

    /// <summary>
    /// Muzzle-to-grip length of the drawn gun in world pixels. The grip sits at the
    /// cursor and the barrel runs forward along the aim, so this is also how far ahead of
    /// the pointer the weapon reaches.
    /// </summary>
    [Export(PropertyHint.Range, "8,256,0.5,or_greater")] public float VisualLengthPx { get; set; } = 56.0f;

    /// <summary>
    /// Where along <see cref="VisualLengthPx"/> the barrel mouth is. Validation ties
    /// <see cref="MuzzleOffsetPx"/> to it, so a round cannot be born somewhere the player
    /// can see is not the end of the barrel.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,1,0.01")] public float MuzzleTipFraction { get; set; } = 0.95f;

    /// <summary>Camera-axis depth the drawn gun sits at, matching the cursor tools.</summary>
    [Export] public float VisualDepthOffset { get; set; } = 144.0f;

    /// <summary>Pixels of agreement required between the authored muzzle and the mesh tip.</summary>
    public const float MuzzleAgreementPx = 2.0f;

    /// <summary>Where the barrel mouth is, in pixels ahead of the cursor.</summary>
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

    /// <summary>The engine-free cadence constants this profile authors.</summary>
    public GunConstants ToGunConstants() => new(
        MagazineCapacity,
        ShotIntervalTicks,
        ReloadTicks,
        ProjectilesPerShot);

    /// <summary>The engine-free aim constants this profile authors.</summary>
    public CursorAimConstants ToAimConstants() => new(
        AimSmoothingHalfLifeTicks,
        MinimumAimSpeedPxPerTick,
        MaxAimTurnDegreesPerTick,
        WheelDegreesPerStep,
        MaximumAimOffsetDegrees);

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (!ContentIds.TryParseTool(ContentId, out _))
        {
            errors.Add(
                $"{nameof(ContentId)} must name a tool known to this build, not '{ContentId}'");
        }

        if (!ToGunConstants().IsWellFormed())
        {
            errors.Add(
                $"{nameof(MagazineCapacity)}, {nameof(ShotIntervalTicks)}, " +
                $"{nameof(ReloadTicks)}, and {nameof(ProjectilesPerShot)} must all be positive");
        }

        if (!ToAimConstants().IsWellFormed())
        {
            errors.Add(
                $"{nameof(AimSmoothingHalfLifeTicks)}, {nameof(MinimumAimSpeedPxPerTick)}, " +
                $"{nameof(MaxAimTurnDegreesPerTick)}, and {nameof(WheelDegreesPerStep)} must " +
                $"all be finite and positive, and {nameof(MaximumAimOffsetDegrees)} non-negative");
        }

        // A spread of exactly zero on a multi-projectile gun would put every pellet on
        // one line, which is a single bullet wearing a crowd.
        if (ProjectilesPerShot > 1 && !(float.IsFinite(SpreadHalfAngleDegrees) && SpreadHalfAngleDegrees > 0.0f))
        {
            errors.Add(
                $"{nameof(SpreadHalfAngleDegrees)} must be positive when " +
                $"{nameof(ProjectilesPerShot)} exceeds one");
        }

        if (!float.IsFinite(SpreadHalfAngleDegrees) || SpreadHalfAngleDegrees < 0.0f)
        {
            errors.Add($"{nameof(SpreadHalfAngleDegrees)} must be finite and non-negative");
        }

        if (!float.IsFinite(MuzzleSpeed) || MuzzleSpeed <= 0.0f)
        {
            errors.Add($"{nameof(MuzzleSpeed)} must be finite and positive");
        }
        else if (MuzzleSpeed > MaximumTravelPerTickPx * Engine.PhysicsTicksPerSecond)
        {
            // Rejected rather than clamped: a gun authored above this bound still looks
            // and sounds right and simply stops hurting anything, which is the hardest
            // kind of regression to notice. See MaximumTravelPerTickPx.
            errors.Add(
                $"{nameof(MuzzleSpeed)} must not exceed " +
                $"{MaximumTravelPerTickPx * Engine.PhysicsTicksPerSecond} px/s " +
                $"({MaximumTravelPerTickPx} px per routed tick), above which a shot can " +
                "step over its target between solver frames");
        }

        if (!float.IsFinite(MuzzleOffsetPx) || MuzzleOffsetPx < 0.0f)
        {
            errors.Add($"{nameof(MuzzleOffsetPx)} must be finite and non-negative");
        }

        if (Visual3DKind == GunVisual3DKind.Unspecified)
        {
            errors.Add(
                $"{nameof(Visual3DKind)} must be authored: a gun has no default silhouette");
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
            // The drawn barrel and the point rounds are born at are one fact authored
            // twice, and the two drifting apart is the bug the owner reported as ammo
            // that does not come out of the gun.
            errors.Add(
                $"{nameof(MuzzleOffsetPx)} ({MuzzleOffsetPx:F1}) must agree with the drawn " +
                $"barrel mouth ({VisualMuzzleTipPx:F1}) within {MuzzleAgreementPx} px");
        }

        if (!float.IsFinite(ProjectileRadius) || ProjectileRadius <= 0.0f)
        {
            errors.Add($"{nameof(ProjectileRadius)} must be finite and positive");
        }

        if (!float.IsFinite(ProjectileMass) || ProjectileMass <= 0.0f)
        {
            errors.Add($"{nameof(ProjectileMass)} must be finite and positive");
        }

        if (!float.IsFinite(ProjectileGravityScale) || ProjectileGravityScale < 0.0f)
        {
            errors.Add($"{nameof(ProjectileGravityScale)} must be finite and non-negative");
        }

        if (ProjectileLifetimeTicks <= 1)
        {
            errors.Add($"{nameof(ProjectileLifetimeTicks)} must exceed one tick");
        }

        if (!float.IsFinite(ProjectileMaxTravelPx) || ProjectileMaxTravelPx <= 0.0f)
        {
            errors.Add($"{nameof(ProjectileMaxTravelPx)} must be finite and positive");
        }

        if (ContactSettleTicks < 1)
        {
            errors.Add($"{nameof(ContactSettleTicks)} must be at least one tick");
        }

        if (SpentLingerTicks < 1)
        {
            errors.Add(
                $"{nameof(SpentLingerTicks)} must be at least one tick so the pipeline can " +
                "still resolve a spent projectile's attribution");
        }

        // The pool must cover a full magazine in flight, or a legitimate fast burst
        // would silently drop shots the player paid rounds for.
        if (PoolCapacity < MagazineCapacity * ProjectilesPerShot)
        {
            errors.Add(
                $"{nameof(PoolCapacity)} must cover a whole magazine in flight " +
                $"({MagazineCapacity * ProjectilesPerShot} projectiles)");
        }

        return errors;
    }
}
