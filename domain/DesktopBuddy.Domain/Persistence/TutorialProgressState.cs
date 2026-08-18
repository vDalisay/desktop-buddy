using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>Stable IDs for the lightweight Steam Demo first-session guidance.</summary>
public static class TutorialStepIds
{
    public const string GrabBuddy = "demo.onboarding.grab_buddy";
    public const string EarnCredits = "demo.onboarding.earn_credits";
    public const string OpenShop = "demo.onboarding.open_shop";
    public const string PurchaseContent = "demo.onboarding.purchase_content";
    public const string OpenPaintBuddy = "demo.onboarding.open_paint_buddy";
    public const string EnterWorkMode = "demo.onboarding.enter_work_mode";
    public const string ExitWorkMode = "demo.onboarding.exit_work_mode";

    public static readonly IReadOnlyList<string> Ordered =
    [
        GrabBuddy,
        EarnCredits,
        OpenShop,
        PurchaseContent,
        OpenPaintBuddy,
        EnterWorkMode,
        ExitWorkMode,
    ];

    public static bool IsKnown(string value) => Ordered.Contains(value, StringComparer.Ordinal);
}

public readonly record struct TutorialProgressSnapshot(
    IReadOnlyList<string> CompletedStepIds,
    bool Skipped)
{
    public bool IsComplete
    {
        get
        {
            if (Skipped)
                return true;
            foreach (string id in TutorialStepIds.Ordered)
                if (!CompletedStepIds.Contains(id, StringComparer.Ordinal))
                    return false;
            return true;
        }
    }
}

/// <summary>
/// Semantic onboarding progress backed by the existing cloud-eligible progress extension map.
/// The compact v1 payload keeps tutorial state independent from volatile UI state without a
/// progress-schema bump. A future schema can promote this record without changing the stable IDs.
/// </summary>
public sealed class TutorialProgressState
{
    public const string ExtensionKey = "demo.onboarding.v1";
    private const string SkippedToken = "skip";

    private readonly BuddyProgressState _progress;

    public TutorialProgressState(BuddyProgressState progress) =>
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));

    public TutorialProgressSnapshot Snapshot()
    {
        string? encoded = null;
        if (_progress.Extensions?.Values is { } values)
            values.TryGetValue(ExtensionKey, out encoded);

        if (string.IsNullOrWhiteSpace(encoded))
            return new TutorialProgressSnapshot(Array.Empty<string>(), false);
        if (string.Equals(encoded, SkippedToken, StringComparison.Ordinal))
            return new TutorialProgressSnapshot(Array.Empty<string>(), true);

        var completed = encoded
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(TutorialStepIds.IsKnown)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => IndexOf(id))
            .ToArray();
        return new TutorialProgressSnapshot(completed, false);
    }

    public bool HasPersistedRecord =>
        _progress.Extensions?.Values?.ContainsKey(ExtensionKey) == true;

    public bool IsComplete => Snapshot().IsComplete;

    public bool IsCompleted(string stepId)
    {
        if (!TutorialStepIds.IsKnown(stepId))
            throw new ArgumentException("Unknown tutorial step ID.", nameof(stepId));
        TutorialProgressSnapshot snapshot = Snapshot();
        return snapshot.Skipped || snapshot.CompletedStepIds.Contains(stepId, StringComparer.Ordinal);
    }

    public string? NextIncompleteStepId
    {
        get
        {
            TutorialProgressSnapshot snapshot = Snapshot();
            if (snapshot.Skipped)
                return null;
            foreach (string id in TutorialStepIds.Ordered)
                if (!snapshot.CompletedStepIds.Contains(id, StringComparer.Ordinal))
                    return id;
            return null;
        }
    }

    public bool MarkCompleted(string stepId)
    {
        if (!TutorialStepIds.IsKnown(stepId))
            throw new ArgumentException("Unknown tutorial step ID.", nameof(stepId));

        TutorialProgressSnapshot snapshot = Snapshot();
        if (snapshot.Skipped || snapshot.CompletedStepIds.Contains(stepId, StringComparer.Ordinal))
            return false;

        var completed = new HashSet<string>(snapshot.CompletedStepIds, StringComparer.Ordinal)
        {
            stepId,
        };
        string encoded = string.Join(
            '|',
            TutorialStepIds.Ordered.Where(completed.Contains));
        return _progress.SetExtensionValue(ExtensionKey, encoded);
    }

    public bool Skip() => _progress.SetExtensionValue(ExtensionKey, SkippedToken);

    private static int IndexOf(string id)
    {
        for (int index = 0; index < TutorialStepIds.Ordered.Count; index++)
            if (string.Equals(TutorialStepIds.Ordered[index], id, StringComparison.Ordinal))
                return index;
        return int.MaxValue;
    }
}
