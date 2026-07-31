using System;

namespace DesktopBuddy.Domain.Mood;

/// <summary>How one accepted impact changes persistent mood and harmful memory.</summary>
public enum ImpactMoodEffectKind
{
    /// <summary>Record harmful memory and apply the shared pain-sized mood loss.</summary>
    Harm,

    /// <summary>Apply a small authored positive delta without recording harmful memory.</summary>
    Enjoyment,

    /// <summary>Apply the shared pain-sized mood loss without persistent harmful memory.</summary>
    Annoyance,
}

/// <summary>
/// Immutable mood instruction carried with an accepted physical impact. Physics pain,
/// knockout, and payout remain independent of this response.
/// </summary>
public readonly record struct ImpactMoodEffect(
    ImpactMoodEffectKind Kind,
    float EnjoymentMoodGain = 0.0f)
{
    public static ImpactMoodEffect Harm => default;
    public static ImpactMoodEffect Annoyance => new(ImpactMoodEffectKind.Annoyance);

    public static ImpactMoodEffect Enjoyment(float moodGain)
    {
        if (!float.IsFinite(moodGain) || moodGain <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(moodGain));
        return new ImpactMoodEffect(ImpactMoodEffectKind.Enjoyment, moodGain);
    }
}

/// <summary>Owner-confirmed transient Nerf tolerance.</summary>
public readonly record struct NerfMoodToleranceTuning(
    int EnjoyedHitCount,
    float MoodGainPerEnjoyedHit,
    double ResetAfterSeconds)
{
    public static NerfMoodToleranceTuning Default => new(
        EnjoyedHitCount: 20,
        MoodGainPerEnjoyedHit: 0.25f,
        ResetAfterSeconds: 10.0);

    public void Validate()
    {
        if (EnjoyedHitCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(EnjoyedHitCount));
        if (!float.IsFinite(MoodGainPerEnjoyedHit) || MoodGainPerEnjoyedHit <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(MoodGainPerEnjoyedHit));
        if (!double.IsFinite(ResetAfterSeconds) || ResetAfterSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(ResetAfterSeconds));
    }
}

public readonly record struct NerfMoodHit(
    int HitNumber,
    bool Enjoyed,
    bool ToleranceReset,
    ImpactMoodEffect MoodEffect);

/// <summary>
/// Counts accepted Nerf contacts in one continuous barrage. Misses never reach this model.
/// Hits one through twenty are playful; later hits are annoying until ten routed seconds
/// pass without another accepted Nerf hit. The state is deliberately transient and never
/// enters progress persistence.
/// </summary>
public sealed class NerfMoodToleranceModel
{
    private const double Epsilon = 1e-9;

    private readonly NerfMoodToleranceTuning _tuning;
    private double _lastObservedSeconds = double.NegativeInfinity;
    private double _lastHitSeconds = double.NegativeInfinity;

    public NerfMoodToleranceModel(NerfMoodToleranceTuning tuning)
    {
        tuning.Validate();
        _tuning = tuning;
    }

    public NerfMoodToleranceModel() : this(NerfMoodToleranceTuning.Default)
    {
    }

    public int HitsInCurrentBarrage { get; private set; }
    public bool IsAnnoyed => HitsInCurrentBarrage > _tuning.EnjoyedHitCount;

    /// <summary>Advances the no-hit reset clock. Returns true only on a real reset.</summary>
    public bool Update(double nowSeconds)
    {
        ValidateMonotonic(nowSeconds);
        if (HitsInCurrentBarrage == 0 ||
            nowSeconds - _lastHitSeconds + Epsilon < _tuning.ResetAfterSeconds)
        {
            return false;
        }

        HitsInCurrentBarrage = 0;
        _lastHitSeconds = double.NegativeInfinity;
        return true;
    }

    /// <summary>Registers one accepted hit; a fired shot that misses never calls this.</summary>
    public NerfMoodHit RegisterHit(double nowSeconds)
    {
        bool reset = Update(nowSeconds);
        _lastHitSeconds = nowSeconds;
        HitsInCurrentBarrage++;
        bool enjoyed = HitsInCurrentBarrage <= _tuning.EnjoyedHitCount;
        return new NerfMoodHit(
            HitsInCurrentBarrage,
            enjoyed,
            reset,
            enjoyed
                ? ImpactMoodEffect.Enjoyment(_tuning.MoodGainPerEnjoyedHit)
                : ImpactMoodEffect.Annoyance);
    }

    public void Reset()
    {
        HitsInCurrentBarrage = 0;
        _lastObservedSeconds = double.NegativeInfinity;
        _lastHitSeconds = double.NegativeInfinity;
    }

    private void ValidateMonotonic(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        if (nowSeconds + Epsilon < _lastObservedSeconds)
            throw new ArgumentOutOfRangeException(nameof(nowSeconds), "Nerf tolerance time must be monotonic.");
        _lastObservedSeconds = nowSeconds;
    }
}
