using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Buddy.Behavior;

public partial class ObjectInteractionComponent
{
    /// <summary>
    /// Transitional class-local adapter for the large object-interaction worker. The original
    /// worker historically named its injected aggregate <c>BuddyProgressState</c>; declaring the
    /// adapter as a nested type lets that untouched source keep its narrow semantic calls while
    /// routing them through <see cref="BuddyRuntimeProgressBinding"/>. The name is intentionally
    /// scoped to <see cref="ObjectInteractionComponent"/> so no sibling behavior component is
    /// shadowed and no second persistent state is created.
    /// </summary>
    public sealed class BuddyProgressState
    {
        private readonly BuddyRuntimeProgressBinding _binding;

        private BuddyProgressState(BuddyRuntimeProgressBinding binding)
        {
            _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        }

        public MoodBand MoodBand => _binding.MoodBand;

        public bool WouldEat(float hungerFill) => _binding.WouldEat(hungerFill);

        public bool ApplyCareMood(float delta) => _binding.ApplyCareMood(delta);

        public void FillHunger(float amount) => _binding.FillHunger(amount);

        public FunOutcome EngageFun(FunActivityId activity) => _binding.EngageFun(activity);

        public void RecordSuccessfulCatch() => _binding.RecordSuccessfulCatch();

        public bool IsContentHarmful(string contentId) => _binding.IsContentHarmful(contentId);

        public static implicit operator BuddyProgressState(
            DesktopBuddy.Domain.Persistence.BuddyProgressState legacy) =>
            new(new BuddyRuntimeProgressBinding(
                legacy ?? throw new ArgumentNullException(nameof(legacy))));

        public static implicit operator BuddyProgressState(BuddyRuntimeProgressBinding binding) =>
            new(binding ?? throw new ArgumentNullException(nameof(binding)));
    }

    /// <summary>
    /// Explicit Scene-actor entry point. Initial Demo callers keep using the historical aggregate
    /// overload; both paths converge on the same existing initialization body through the adapter.
    /// </summary>
    public void Initialize(
        DesktopBuddy.Objects.LooseObjectRegistry registry,
        BuddyRuntimeProgressBinding progress,
        SocialTuningSet? socialTuning = null)
    {
        ArgumentNullException.ThrowIfNull(progress);
        Initialize(registry, (BuddyProgressState)progress, socialTuning);
    }
}
