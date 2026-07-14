using System;

namespace DesktopBuddy.Domain.Mood;

/// <summary>The two direct care interactions (RAGDOLL §8 / DECISIONS 2026-07-14).</summary>
public enum CareKind
{
    Pet,
    Tickle,
}

public enum TickleDisposition
{
    Friendly,
    Angry,
}

/// <summary>Confirmed timing plus empirical Pet-distance tuning.</summary>
public readonly record struct CareTuning(
    double PetDistancePerReward,
    double FavoriteSpotMultiplier,
    double SecondsPerReward,
    double TickleFriendlySeconds,
    double TickleCooldownSeconds,
    double FriendlyHopIntervalSeconds,
    double AngryHopIntervalSeconds)
{
    public static CareTuning Default => new(
        PetDistancePerReward: 180.0,
        FavoriteSpotMultiplier: 1.2,
        SecondsPerReward: 3.0,
        TickleFriendlySeconds: 6.0,
        TickleCooldownSeconds: 8.0,
        FriendlyHopIntervalSeconds: 1.5,
        AngryHopIntervalSeconds: 0.75);

    public void Validate()
    {
        ValidatePositive(PetDistancePerReward, nameof(PetDistancePerReward));
        ValidatePositive(FavoriteSpotMultiplier, nameof(FavoriteSpotMultiplier));
        ValidatePositive(SecondsPerReward, nameof(SecondsPerReward));
        ValidatePositive(TickleFriendlySeconds, nameof(TickleFriendlySeconds));
        ValidatePositive(TickleCooldownSeconds, nameof(TickleCooldownSeconds));
        ValidatePositive(FriendlyHopIntervalSeconds, nameof(FriendlyHopIntervalSeconds));
        ValidatePositive(AngryHopIntervalSeconds, nameof(AngryHopIntervalSeconds));
        if (TickleFriendlySeconds < SecondsPerReward)
            throw new ArgumentOutOfRangeException(nameof(TickleFriendlySeconds));
    }

    private static void ValidatePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
            throw new ArgumentOutOfRangeException(name, "Care tuning must be finite and positive.");
    }
}

public readonly record struct PetCareResult(
    int PositiveMoodAwards,
    bool Completed,
    double DistanceProgress,
    double ValidSecondsProgress);

public readonly record struct TickleCareResult(
    int PositiveMoodAwards,
    int NegativeMoodAwards,
    TickleDisposition Disposition,
    bool BecameAngry,
    bool CooldownReset,
    bool HopRequested,
    double ContactSeconds,
    double NoContactSeconds);

/// <summary>
/// Pure direct-care state. Pet requires both weighted rubbing distance and the
/// confirmed three valid-contact seconds; completion resets its hidden bar.
/// Tickle grants friendly rewards at three/six seconds, then becomes Angry,
/// applies negative mood on the same cadence, and resets only after eight
/// seconds without valid contact. No wall-clock time enters this model.
/// </summary>
public sealed class CareModel
{
    private const double Epsilon = 1e-9;

    private readonly CareTuning _tuning;
    private double _petDistance;
    private double _petValidSeconds;
    private double _tickleContactSeconds;
    private double _tickleNoContactSeconds;
    private double _tickleNegativeSeconds;
    private double _tickleHopSeconds;
    private double _nextFriendlyRewardSeconds;

    public CareModel(CareTuning tuning)
    {
        tuning.Validate();
        _tuning = tuning;
        _nextFriendlyRewardSeconds = tuning.SecondsPerReward;
    }

    public CareModel() : this(CareTuning.Default)
    {
    }

    public TickleDisposition TickleDisposition { get; private set; }
    public double PetDistanceProgress => _petDistance;
    public double PetValidSecondsProgress => _petValidSeconds;
    public double TickleContactSeconds => _tickleContactSeconds;
    public double TickleNoContactSeconds => _tickleNoContactSeconds;

    public PetCareResult AccumulatePet(
        double travelledDistance,
        bool favoriteSpot,
        double validContactSeconds)
    {
        ValidateNonNegative(travelledDistance, nameof(travelledDistance));
        ValidateNonNegative(validContactSeconds, nameof(validContactSeconds));

        _petDistance = Math.Min(
            _tuning.PetDistancePerReward,
            _petDistance + travelledDistance * (favoriteSpot ? _tuning.FavoriteSpotMultiplier : 1.0));
        _petValidSeconds = Math.Min(
            _tuning.SecondsPerReward,
            _petValidSeconds + validContactSeconds);
        bool complete = _petDistance + Epsilon >= _tuning.PetDistancePerReward &&
                        _petValidSeconds + Epsilon >= _tuning.SecondsPerReward;
        if (complete)
        {
            // The owner specified a bar reset, not remainder carry-over.
            _petDistance = 0.0;
            _petValidSeconds = 0.0;
        }

        return new PetCareResult(
            complete ? 1 : 0,
            complete,
            _petDistance,
            _petValidSeconds);
    }

    /// <summary>Advance Tickle on every simulation tick, including no-contact cooldown ticks.</summary>
    public TickleCareResult TickTickle(bool validContact, double elapsedSeconds)
    {
        ValidateNonNegative(elapsedSeconds, nameof(elapsedSeconds));
        bool reset = false;
        bool becameAngry = false;
        bool hop = false;
        int positive = 0;
        int negative = 0;

        if (!validContact)
        {
            if (_tickleContactSeconds > 0.0 || TickleDisposition == TickleDisposition.Angry)
            {
                _tickleNoContactSeconds += elapsedSeconds;
                if (_tickleNoContactSeconds + Epsilon >= _tuning.TickleCooldownSeconds)
                {
                    ResetTickle();
                    reset = true;
                }
            }

            return Result(positive, negative, becameAngry, reset, hop);
        }

        _tickleNoContactSeconds = 0.0;
        double previousContact = _tickleContactSeconds;
        _tickleContactSeconds += elapsedSeconds;
        _tickleHopSeconds += elapsedSeconds;

        while (_nextFriendlyRewardSeconds <= _tuning.TickleFriendlySeconds + Epsilon &&
               _tickleContactSeconds + Epsilon >= _nextFriendlyRewardSeconds)
        {
            positive++;
            _nextFriendlyRewardSeconds += _tuning.SecondsPerReward;
        }

        if (TickleDisposition == TickleDisposition.Friendly &&
            _tickleContactSeconds + Epsilon >= _tuning.TickleFriendlySeconds)
        {
            TickleDisposition = TickleDisposition.Angry;
            becameAngry = true;
        }

        if (TickleDisposition == TickleDisposition.Angry)
        {
            double angrySecondsThisTick = previousContact >= _tuning.TickleFriendlySeconds
                ? elapsedSeconds
                : Math.Max(0.0, _tickleContactSeconds - _tuning.TickleFriendlySeconds);
            _tickleNegativeSeconds += angrySecondsThisTick;
            while (_tickleNegativeSeconds + Epsilon >= _tuning.SecondsPerReward)
            {
                _tickleNegativeSeconds -= _tuning.SecondsPerReward;
                if (_tickleNegativeSeconds < 0.0 && _tickleNegativeSeconds > -Epsilon)
                    _tickleNegativeSeconds = 0.0;
                negative++;
            }
        }

        double hopInterval = TickleDisposition == TickleDisposition.Angry
            ? _tuning.AngryHopIntervalSeconds
            : _tuning.FriendlyHopIntervalSeconds;
        if (_tickleHopSeconds + Epsilon >= hopInterval)
        {
            _tickleHopSeconds = 0.0;
            hop = true;
        }

        return Result(positive, negative, becameAngry, reset, hop);
    }

    public void Reset()
    {
        _petDistance = 0.0;
        _petValidSeconds = 0.0;
        ResetTickle();
    }

    private void ResetTickle()
    {
        _tickleContactSeconds = 0.0;
        _tickleNoContactSeconds = 0.0;
        _tickleNegativeSeconds = 0.0;
        _tickleHopSeconds = 0.0;
        _nextFriendlyRewardSeconds = _tuning.SecondsPerReward;
        TickleDisposition = TickleDisposition.Friendly;
    }

    private TickleCareResult Result(
        int positive,
        int negative,
        bool becameAngry,
        bool reset,
        bool hop) => new(
            positive,
            negative,
            TickleDisposition,
            becameAngry,
            reset,
            hop,
            _tickleContactSeconds,
            _tickleNoContactSeconds);

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(name, "Care input must be finite and non-negative.");
    }
}
