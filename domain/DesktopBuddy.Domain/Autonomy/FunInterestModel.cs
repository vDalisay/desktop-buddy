using System;

namespace DesktopBuddy.Domain.Autonomy;

/// <summary>
/// The things a buddy can find fun. Each one carries its own interest meter and its own
/// per-buddy taste, so a buddy can be delighted by catch and bored of tickling at the same
/// moment (owner instruction 2026-07-27).
/// </summary>
public enum FunActivityId
{
    /// <summary>Catching a player-thrown ball before it touches the ground.</summary>
    Catch = 0,
    Pet = 1,
    Tickle = 2,
    Treat = 3,
}

/// <summary>
/// How fast one buddy tires of each fun activity: the interest cost of a single engagement,
/// in meter points. This <b>is</b> the buddy's taste — the owner's worked example is a buddy
/// that loves catch paying <c>1</c> a throw (a hundred throws before it tires) against one
/// that dislikes it paying <c>20</c> (bored after five).
///
/// <para>Held as a fixed field per activity rather than a dictionary so it stays a
/// value-comparable struct that a save round-trip reproduces exactly, and so adding an
/// activity is a deliberate schema change rather than a silent default.</para>
/// </summary>
public readonly record struct FunPreferences(
    int CatchDrain,
    int PetDrain,
    int TickleDrain,
    int TreatDrain)
{
    /// <summary>A buddy that loves this: a hundred engagements before interest runs out.</summary>
    public const int MinDrain = 1;

    /// <summary>A buddy that dislikes this: bored after five.</summary>
    public const int MaxDrain = 20;

    /// <summary>
    /// The neutral taste used before a save exists, by saveless test composition, and by
    /// saves written before tastes were recorded. Deliberately a fixed midpoint and not a
    /// fresh roll: a personality is sampled once at creation, and rolling one at load time
    /// would hand the same buddy a different character every launch.
    /// </summary>
    public static FunPreferences Default => new(5, 5, 5, 5);

    /// <summary>The total <see cref="FunActivityId"/> → drain mapping.</summary>
    public int DrainFor(FunActivityId activity) => activity switch
    {
        FunActivityId.Catch => CatchDrain,
        FunActivityId.Pet => PetDrain,
        FunActivityId.Tickle => TickleDrain,
        FunActivityId.Treat => TreatDrain,
        _ => throw new ArgumentOutOfRangeException(
            nameof(activity),
            activity,
            "Unknown fun activity: extend FunPreferences.DrainFor when adding a FunActivityId."),
    };

    /// <summary>Clamps loaded or migrated values into the valid drain range.</summary>
    public static FunPreferences FromPersisted(
        int catchDrain,
        int petDrain,
        int tickleDrain,
        int treatDrain) =>
        new(
            Math.Clamp(catchDrain, MinDrain, MaxDrain),
            Math.Clamp(petDrain, MinDrain, MaxDrain),
            Math.Clamp(tickleDrain, MinDrain, MaxDrain),
            Math.Clamp(treatDrain, MinDrain, MaxDrain));

    /// <summary>
    /// Samples one buddy's tastes for a <b>new save only</b>. The caller must pass the
    /// dedicated save-creation RNG stream, never the behavior or presentation stream
    /// (ARCHITECTURE §23) — mixing them would make a buddy's tastes depend on how it was
    /// played. Each activity is drawn independently across the full range, so a buddy that
    /// adores catch and cannot stand being tickled is an ordinary member of the population.
    /// </summary>
    public static FunPreferences Sample(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return new FunPreferences(
            random.NextInt(MinDrain, MaxDrain + 1),
            random.NextInt(MinDrain, MaxDrain + 1),
            random.NextInt(MinDrain, MaxDrain + 1),
            random.NextInt(MinDrain, MaxDrain + 1));
    }
}

/// <summary>One activity's remaining novelty, for snapshotting into a save.</summary>
public readonly record struct FunActivityInterest(FunActivityId Activity, float Interest);

/// <summary>The result of one engagement with a fun activity.</summary>
/// <param name="WasFun">
/// Whether the buddy actually enjoyed this one. False once the meter is spent — that is the
/// whole point of the system: the tenth identical trick in a row lands flat.
/// </param>
/// <param name="InterestBefore">Meter value before the drain, for diagnostics and tests.</param>
/// <param name="InterestAfter">Meter value after the drain.</param>
public readonly record struct FunOutcome(
    bool WasFun,
    float InterestBefore,
    float InterestAfter);

/// <summary>
/// Per-activity novelty. Every fun activity carries a <c>0–100</c> interest meter that a
/// buddy spends by doing the thing and recovers by not doing it (owner instruction
/// 2026-07-27: interest fades with repetition, and a recharge timer decides when a toy is
/// fun again).
///
/// <para><b>Taste is the drain, novelty is the meter.</b> Two buddies playing the same game
/// tire of it at different rates because <see cref="FunPreferences"/> sets what one round
/// costs them; the meter, the threshold, and the recharge are shared machinery. Keeping
/// taste in exactly one number is what makes "this buddy loves catch" a statement about the
/// save rather than a special case in the behavior code.</para>
///
/// <para><b>Recharge is wall-clock, not engagement-driven.</b> A bored buddy recovers by the
/// passage of time whether or not the player keeps trying, so leaving the ball alone for a
/// while is what makes it interesting again.</para>
///
/// <para>Pure, deterministic, allocation-free, and drawing from no RNG stream: interest is
/// persistent state, so it may never depend on a per-session roll (ARCHITECTURE §23).</para>
/// </summary>
public sealed class FunInterestModel
{
    /// <summary>Full interest. A fresh buddy finds everything new.</summary>
    public const float MaximumInterest = 100.0f;

    /// <summary>At or below this the activity is no longer fun; it is not below zero.</summary>
    public const float MinimumInterest = 0.0f;

    /// <summary>
    /// How much novelty a spent activity must recover before it is fun again. This is the
    /// recharge timer the owner asked for: without it, boredom would last a single tick,
    /// because the first sliver of recharge would put the meter back above zero and the buddy
    /// would laugh at the very next throw. The gap makes being bored a state you have to wait
    /// out — at the default recharge, about fifty seconds.
    /// </summary>
    public const float ComebackInterest = 25.0f;

    /// <summary>
    /// Meter points recovered per second of elapsed time. At <c>0.5</c> a fully spent meter
    /// is completely fresh again after about three and a half minutes, and an activity that
    /// cost a middling buddy five points is fun again about ten seconds later — long enough
    /// that spamming one trick visibly stops landing, short enough that a player who moves on
    /// and comes back finds the buddy interested.
    /// </summary>
    public const float DefaultRechargePerSecond = 0.5f;

    private readonly float[] _interest;
    private readonly bool[] _bored;
    private readonly float _rechargePerSecond;

    private FunPreferences _preferences;

    public FunInterestModel(
        FunPreferences? preferences = null,
        float rechargePerSecond = DefaultRechargePerSecond)
    {
        if (!float.IsFinite(rechargePerSecond) || rechargePerSecond < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rechargePerSecond),
                rechargePerSecond,
                "Recharge must be finite and non-negative.");
        }

        _preferences = preferences ?? FunPreferences.Default;
        _rechargePerSecond = rechargePerSecond;
        _interest = new float[ActivityCount];
        _bored = new bool[ActivityCount];
        Reset();
    }

    /// <summary>How many activities carry a meter; the persisted payload mirrors this.</summary>
    public static int ActivityCount => 4;

    public FunPreferences Preferences => _preferences;

    /// <summary>Current interest in one activity, <c>0–100</c>.</summary>
    public float InterestIn(FunActivityId activity) => _interest[IndexOf(activity)];

    /// <summary>
    /// Whether doing this right now would actually be fun. A spent activity stays boring
    /// until it has recovered <see cref="ComebackInterest"/>, so boredom is something the
    /// player waits out rather than a state that ends on the next physics tick.
    /// </summary>
    public bool IsFun(FunActivityId activity)
    {
        int index = IndexOf(activity);
        return !_bored[index] && _interest[index] > MinimumInterest;
    }

    /// <summary>
    /// Spends one engagement's worth of interest and reports whether the buddy enjoyed it.
    /// The verdict is read <b>before</b> the drain, so the engagement that empties the meter
    /// is itself still fun and the one after it is not.
    /// </summary>
    public FunOutcome Engage(FunActivityId activity)
    {
        int index = IndexOf(activity);
        float before = _interest[index];
        bool wasFun = IsFun(activity);
        _interest[index] = Math.Clamp(
            before - _preferences.DrainFor(activity),
            MinimumInterest,
            MaximumInterest);
        if (_interest[index] <= MinimumInterest)
        {
            _bored[index] = true;
        }

        return new FunOutcome(wasFun, before, _interest[index]);
    }

    /// <summary>Recovers interest in everything over a monotonic elapsed span.</summary>
    public void Recharge(double elapsedSeconds)
    {
        if (elapsedSeconds <= 0.0 || !double.IsFinite(elapsedSeconds))
        {
            return;
        }

        float gain = (float)(elapsedSeconds * _rechargePerSecond);
        if (gain <= 0.0f)
        {
            return;
        }

        for (int index = 0; index < _interest.Length; index++)
        {
            _interest[index] = Math.Min(MaximumInterest, _interest[index] + gain);
            if (_bored[index] && _interest[index] >= ComebackInterest)
            {
                _bored[index] = false;
            }
        }
    }

    /// <summary>Restores every meter to full. New-save creation and hard reset only.</summary>
    public void Reset()
    {
        for (int index = 0; index < _interest.Length; index++)
        {
            _interest[index] = MaximumInterest;
            _bored[index] = false;
        }
    }

    /// <summary>Assigns sampled or loaded tastes. Save composition only.</summary>
    public void SetPreferences(FunPreferences preferences) => _preferences = preferences;

    /// <summary>An immutable read of every meter, for the save writer.</summary>
    public FunActivityInterest[] Snapshot()
    {
        var snapshot = new FunActivityInterest[ActivityCount];
        for (int index = 0; index < snapshot.Length; index++)
        {
            snapshot[index] = new FunActivityInterest((FunActivityId)index, _interest[index]);
        }

        return snapshot;
    }

    /// <summary>
    /// Restores a persisted meter, clamped into range.
    ///
    /// <para>The boredom latch is derived rather than persisted: anything below
    /// <see cref="ComebackInterest"/> reloads as still bored. That is the conservative
    /// reading — a buddy saved with almost no novelty left should not be instantly delighted
    /// on load — and it self-corrects within a couple of seconds of recharge.</para>
    /// </summary>
    public void RestoreInterest(FunActivityId activity, float interest)
    {
        int index = IndexOf(activity);
        _interest[index] = !float.IsFinite(interest)
            ? MaximumInterest
            : Math.Clamp(interest, MinimumInterest, MaximumInterest);
        _bored[index] = _interest[index] < ComebackInterest;
    }

    private static int IndexOf(FunActivityId activity)
    {
        int index = (int)activity;
        if (index < 0 || index >= ActivityCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activity), activity, "Unknown fun activity.");
        }

        return index;
    }
}
