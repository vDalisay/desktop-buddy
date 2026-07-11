namespace DesktopBuddy.Domain.Autonomy;

/// <summary>
/// Injectable random stream for gameplay decisions. Each consumer family owns
/// its own stream so presentation randomness cannot perturb behavior outcomes.
/// </summary>
public interface IRandomSource
{
    int NextInt(int minimumInclusive, int maximumExclusive);
}
