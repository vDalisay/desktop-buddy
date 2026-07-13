namespace DesktopBuddy.Domain.Mood;

/// <summary>The two care interactions that raise mood without paying money (RAGDOLL §8).</summary>
public enum CareKind
{
    Pet,
    Tickle,
}

/// <summary>
/// Accumulates valid Pet/Tickle contact time and yields a <c>+1</c> mood award for every
/// <c>3</c> valid-contact seconds, independently per care kind (RAGDOLL §8 care table).
/// Cadence counts valid contact only: the caller feeds elapsed time solely while a held
/// stroke is over a real buddy body, so holding input over empty space accumulates
/// nothing. Care never awards immediate money. The caller applies each returned award to
/// <see cref="MoodModel.ApplyMoodDelta"/>.
/// </summary>
public sealed class CareModel
{
    public const double SecondsPerReward = 3.0;
    private const double ThresholdEpsilon = 1e-9;

    private readonly double[] _accumulated = new double[2];

    /// <summary>
    /// Adds valid-contact seconds for one care kind and returns how many <c>+1</c> awards
    /// that crosses (normally 0 or 1 per fixed tick). Non-positive time is ignored.
    /// </summary>
    public int AccumulateValidContact(CareKind kind, double validContactSeconds)
    {
        if (validContactSeconds <= 0.0)
        {
            return 0;
        }

        int index = (int)kind;
        _accumulated[index] += validContactSeconds;

        int awards = 0;
        while (_accumulated[index] + ThresholdEpsilon >= SecondsPerReward)
        {
            _accumulated[index] -= SecondsPerReward;
            if (_accumulated[index] < 0.0 && _accumulated[index] > -ThresholdEpsilon)
            {
                _accumulated[index] = 0.0;
            }
            awards++;
        }

        return awards;
    }

    /// <summary>Progress toward the next award for a care kind, in seconds <c>[0, 3)</c>.</summary>
    public double ProgressSeconds(CareKind kind) => _accumulated[(int)kind];

    /// <summary>Clears accumulated progress (e.g. hard reposition / tool switch).</summary>
    public void Reset()
    {
        _accumulated[0] = 0.0;
        _accumulated[1] = 0.0;
    }
}
