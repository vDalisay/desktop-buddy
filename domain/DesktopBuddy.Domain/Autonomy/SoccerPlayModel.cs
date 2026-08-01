using System;
using System.Numerics;

namespace DesktopBuddy.Domain.Autonomy;

/// <summary>The soccer trap/kick beat, a sibling of the catch lifecycle.</summary>
public enum SoccerPlayPhase
{
    Idle,

    /// <summary>The ball is stopped under the foot and the dwell beat is counting down.</summary>
    Trapping,
}

/// <summary>What the runtime should do about the ball this tick.</summary>
public enum SoccerPlayCommand
{
    None,

    /// <summary>Hold the ball still under the planted foot.</summary>
    Trap,

    /// <summary>One-shot: send the ball away at <see cref="SoccerPlayIntent.KickVelocity"/>.</summary>
    Kick,
}

/// <summary>Why a committed trap ended without reaching the kick.</summary>
public enum SoccerPlayAbort
{
    None,
    BallLost,
    HigherPriority,
    Unconscious,
}

/// <summary>
/// Authored per-ball tuning for the trap/kick beat. Every duration is routed ticks, so a
/// paused laboratory freezes the dwell exactly (ARCHITECTURE §8).
/// </summary>
/// <param name="TrapDistance">
/// Horizontal distance from the buddy to the ball's <b>near surface</b> within which the foot
/// can meet it. Measured to the surface for the same reason the ground scoop is: the body
/// cannot close the last radius.
/// </param>
/// <param name="TrapHeight">
/// How far above the foot line the ball's centre may sit and still count as rolling. A ball
/// sailing past head height is a catch, not a trap.
/// </param>
/// <param name="MinimumApproachSpeed">
/// Below this closing speed the ball is not "rolling toward" anything and is left to the
/// ordinary pickup lifecycle.
/// </param>
/// <param name="MaximumApproachSpeed">
/// Above this the ball is a projectile, not a pass; the buddy does not stick a foot out at it.
/// </param>
/// <param name="DwellTicks">The beat between trapping the ball and kicking it — owner: "after a second".</param>
/// <param name="KickSpeed">Speed the ball leaves the foot at.</param>
/// <param name="MaximumKickLoftDegrees">
/// The widest angle off horizontal the kick may take. Owner: "either straight or angled a bit
/// towards the player", so the spread is a loft <i>upward</i> along the outgoing direction —
/// the player is up-screen of the floor the ball rolls on.
/// </param>
/// <param name="KickLoftChoices">
/// How many evenly spaced loft options the kick picks between, the first always being dead
/// straight. <c>1</c> disables the spread entirely.
/// </param>
public readonly record struct SoccerPlayTuning(
    float TrapDistance,
    float TrapHeight,
    float MinimumApproachSpeed,
    float MaximumApproachSpeed,
    int DwellTicks,
    float KickSpeed,
    float MaximumKickLoftDegrees,
    int KickLoftChoices)
{
    /// <summary>Provisional playground-ball feel, pending the owner's gate.</summary>
    public static SoccerPlayTuning Default =>
        new(34.0f, 30.0f, 40.0f, 900.0f, 120, 520.0f, 24.0f, 3);

    public bool IsValid =>
        float.IsFinite(TrapDistance) && TrapDistance > 0.0f &&
        float.IsFinite(TrapHeight) && TrapHeight > 0.0f &&
        float.IsFinite(MinimumApproachSpeed) && MinimumApproachSpeed > 0.0f &&
        float.IsFinite(MaximumApproachSpeed) &&
        MaximumApproachSpeed > MinimumApproachSpeed &&
        DwellTicks > 0 &&
        float.IsFinite(KickSpeed) && KickSpeed > 0.0f &&
        float.IsFinite(MaximumKickLoftDegrees) &&
        MaximumKickLoftDegrees is >= 0.0f and < 90.0f &&
        KickLoftChoices >= 1;
}

/// <summary>
/// Everything the model needs to know about the one ball it might play with, read fresh each
/// routed tick. Runtime IDs are per-instance and transient.
/// </summary>
/// <param name="RuntimeId">Registry ID; <c>0</c> means "no ball".</param>
/// <param name="Available">
/// The ball is registered, nobody is holding it, and it is not in harmful memory. A ball the
/// player has picked up stops being playable the moment they lift it.
/// </param>
/// <param name="SurfaceDistance">Horizontal gap between the buddy and the ball's near surface.</param>
/// <param name="DirectionFromBuddy">
/// Signed direction from the buddy toward the ball (<c>-1</c> or <c>+1</c>). This is also the
/// direction the ball came from, so it is where the kick sends it back.
/// </param>
/// <param name="ClosingSpeed">
/// Horizontal speed toward the buddy. Positive means closing; a ball rolling away is not
/// trappable, which is what stops the buddy trapping its own kick.
/// </param>
/// <param name="HeightAboveFeet">The ball centre's height above the foot line; negative below it.</param>
public readonly record struct SoccerBallReading(
    int RuntimeId,
    bool Available,
    float SurfaceDistance,
    float DirectionFromBuddy,
    float ClosingSpeed,
    float HeightAboveFeet)
{
    public static SoccerBallReading None => default;

    public bool IsValid =>
        RuntimeId != 0 &&
        float.IsFinite(SurfaceDistance) &&
        float.IsFinite(DirectionFromBuddy) &&
        float.IsFinite(ClosingSpeed) &&
        float.IsFinite(HeightAboveFeet);
}

/// <summary>The model's resolved intent for one routed tick.</summary>
public readonly record struct SoccerPlayIntent(
    SoccerPlayCommand Command,
    SoccerPlayPhase Phase,
    int RuntimeId,
    /// <summary>Set only on the <see cref="SoccerPlayCommand.Kick"/> tick.</summary>
    Vector2 KickVelocity,
    /// <summary>Loft above horizontal actually chosen, for diagnostics and scenarios.</summary>
    float KickLoftDegrees,
    int DwellTicksRemaining,
    SoccerPlayAbort Abort)
{
    public static SoccerPlayIntent None => new(
        SoccerPlayCommand.None, SoccerPlayPhase.Idle, 0, Vector2.Zero, 0.0f, 0,
        SoccerPlayAbort.None);

    /// <summary>True while the model wants arbiter priority 5.</summary>
    public bool IsCommitted => Phase != SoccerPlayPhase.Idle;
}

/// <summary>
/// The pure "treat it like a soccer ball" beat (owner instruction 2026-08-01): a ball rolling
/// at the buddy is stopped dead under a foot, sat on for a beat, and then kicked back the way
/// it came at a randomized angle — dead straight or lofted a little.
///
/// <para><b>Why this is not part of <see cref="ObjectInteractionModel"/>.</b> That machine is
/// catch → hold → inspect → outcome, and every phase of it assumes the object ends up in the
/// hands. A trap never picks the ball up. Running it as a sibling keeps the catch lifecycle
/// untouched — which matters, because the same ball is still catchable out of the air, and
/// only the rolling case is diverted here. The runtime marks a ball this model owns as
/// <see cref="ObjectCandidate.Ignored"/>, the existing channel for "leave that one alone",
/// so the two never contend for it.</para>
///
/// <para><b>Only what the data opts in.</b> The model is handed a
/// <see cref="SoccerPlayTuning"/> per tick rather than owning one, so whether an object plays
/// this way is a property of its authored profile. Nothing that authors no tuning is ever
/// read into a <see cref="SoccerBallReading"/> at all.</para>
///
/// <para><b>Randomness is injected and seeded.</b> The loft choice comes from an
/// <see cref="IRandomSource"/> on its own stream, so a scenario replays the same kick for the
/// same seed and presentation randomness can never perturb it.</para>
///
/// <para>Allocation-free: a handful of fields, and every payload is a
/// <c>readonly record struct</c> (ARCHITECTURE §23).</para>
/// </summary>
public sealed class SoccerPlayModel
{
    private readonly IRandomSource _random;

    private SoccerPlayPhase _phase = SoccerPlayPhase.Idle;
    private int _runtimeId;
    private int _dwellTicksRemaining;

    public SoccerPlayModel(IRandomSource random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public SoccerPlayPhase Phase => _phase;
    public int TrappedRuntimeId => _runtimeId;
    public int DwellTicksRemaining => _dwellTicksRemaining;
    public bool IsCommitted => _phase != SoccerPlayPhase.Idle;

    /// <summary>Traps completed; one per ball stopped under the foot.</summary>
    public int TrapCount { get; private set; }

    /// <summary>Kicks released; one per trap that survived its dwell.</summary>
    public int KickCount { get; private set; }
    public float LastKickLoftDegrees { get; private set; }
    public Vector2 LastKickVelocity { get; private set; }
    public SoccerPlayAbort LastAbort { get; private set; }

    /// <summary>
    /// Advances the beat one routed tick.
    /// </summary>
    /// <param name="tuning">The ball's own authored tuning; must be valid.</param>
    /// <param name="ball">This tick's reading, or <see cref="SoccerBallReading.None"/>.</param>
    /// <param name="suppressed">A higher arbiter priority owns actuation: give the ball up.</param>
    /// <param name="conscious">False cancels everything (priority 1).</param>
    public SoccerPlayIntent Tick(
        in SoccerPlayTuning tuning,
        in SoccerBallReading ball,
        bool suppressed,
        bool conscious)
    {
        if (!conscious)
            return AbortTo(SoccerPlayAbort.Unconscious);

        if (suppressed)
            return AbortTo(SoccerPlayAbort.HigherPriority);

        if (!tuning.IsValid)
            return AbortTo(SoccerPlayAbort.BallLost);

        if (_phase == SoccerPlayPhase.Idle)
            return TryTrap(tuning, ball);

        // The trap only holds while the ball it trapped is still there and still free. The
        // player reaching in and lifting it ends the beat immediately.
        if (!ball.IsValid || !ball.Available || ball.RuntimeId != _runtimeId)
            return AbortTo(SoccerPlayAbort.BallLost);

        _dwellTicksRemaining--;
        if (_dwellTicksRemaining > 0)
        {
            return new SoccerPlayIntent(
                SoccerPlayCommand.Trap,
                SoccerPlayPhase.Trapping,
                _runtimeId,
                Vector2.Zero,
                0.0f,
                _dwellTicksRemaining,
                SoccerPlayAbort.None);
        }

        return Kick(tuning, ball);
    }

    /// <summary>Drops all beat state. Used by hard reposition and session resume.</summary>
    public void Reset()
    {
        _phase = SoccerPlayPhase.Idle;
        _runtimeId = 0;
        _dwellTicksRemaining = 0;
        LastAbort = SoccerPlayAbort.None;
    }

    /// <summary>
    /// Whether this ball belongs to the foot rather than to the hands right now: it is low and
    /// rolling at the buddy, whatever distance it is still at.
    ///
    /// <para>This is the predicate the runtime hides the ball from the catch lifecycle with,
    /// and it deliberately has no distance term. The contest is decided the moment the ball
    /// starts rolling in, not when it arrives: without that, the ordinary pickup commits to a
    /// distant rolling ball, walks out to meet it, and there is nothing left to trap. A ball
    /// sailing in above <see cref="SoccerPlayTuning.TrapHeight"/> is still a catch, a ball at
    /// rest is still an ordinary pickup, and a ball rolling away belongs to nobody.</para>
    /// </summary>
    public static bool IsReserved(in SoccerPlayTuning tuning, in SoccerBallReading ball) =>
        tuning.IsValid &&
        ball.IsValid &&
        ball.Available &&
        ball.HeightAboveFeet <= tuning.TrapHeight &&
        ball.ClosingSpeed >= tuning.MinimumApproachSpeed &&
        ball.ClosingSpeed <= tuning.MaximumApproachSpeed;

    /// <summary>
    /// Whether this reading is a reserved ball that has arrived inside foot range, so the trap
    /// can happen this tick.
    /// </summary>
    public static bool IsTrappable(in SoccerPlayTuning tuning, in SoccerBallReading ball) =>
        IsReserved(tuning, ball) && ball.SurfaceDistance <= tuning.TrapDistance;

    private SoccerPlayIntent TryTrap(in SoccerPlayTuning tuning, in SoccerBallReading ball)
    {
        if (!IsTrappable(tuning, ball))
            return SoccerPlayIntent.None;

        _phase = SoccerPlayPhase.Trapping;
        _runtimeId = ball.RuntimeId;
        _dwellTicksRemaining = tuning.DwellTicks;
        LastAbort = SoccerPlayAbort.None;
        TrapCount++;

        return new SoccerPlayIntent(
            SoccerPlayCommand.Trap,
            SoccerPlayPhase.Trapping,
            _runtimeId,
            Vector2.Zero,
            0.0f,
            _dwellTicksRemaining,
            SoccerPlayAbort.None);
    }

    private SoccerPlayIntent Kick(in SoccerPlayTuning tuning, in SoccerBallReading ball)
    {
        float loft = ChooseLoftDegrees(tuning);
        // Back the way it came — which is away from the buddy and toward whoever sent it —
        // lofted upward by the chosen angle. Screen space: -Y is up.
        float radians = loft * MathF.PI / 180.0f;
        float outward = ball.DirectionFromBuddy >= 0.0f ? 1.0f : -1.0f;
        var velocity = new Vector2(
            outward * tuning.KickSpeed * MathF.Cos(radians),
            -tuning.KickSpeed * MathF.Sin(radians));

        int runtimeId = _runtimeId;
        Reset();
        KickCount++;
        LastKickLoftDegrees = loft;
        LastKickVelocity = velocity;

        return new SoccerPlayIntent(
            SoccerPlayCommand.Kick,
            SoccerPlayPhase.Idle,
            runtimeId,
            velocity,
            loft,
            0,
            SoccerPlayAbort.None);
    }

    /// <summary>
    /// "Either straight or angled a bit" — evenly spaced options from dead flat up to the
    /// authored maximum, chosen off the injected stream.
    /// </summary>
    private float ChooseLoftDegrees(in SoccerPlayTuning tuning)
    {
        if (tuning.KickLoftChoices <= 1 || tuning.MaximumKickLoftDegrees <= 0.0f)
            return 0.0f;

        int choice = _random.NextInt(0, tuning.KickLoftChoices);
        return tuning.MaximumKickLoftDegrees * choice / (tuning.KickLoftChoices - 1);
    }

    private SoccerPlayIntent AbortTo(SoccerPlayAbort reason)
    {
        if (_phase == SoccerPlayPhase.Idle)
        {
            LastAbort = SoccerPlayAbort.None;
            return SoccerPlayIntent.None;
        }

        int runtimeId = _runtimeId;
        Reset();
        LastAbort = reason;
        return new SoccerPlayIntent(
            SoccerPlayCommand.None,
            SoccerPlayPhase.Idle,
            runtimeId,
            Vector2.Zero,
            0.0f,
            0,
            reason);
    }
}
