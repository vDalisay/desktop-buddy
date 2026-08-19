using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>Stable semantic IDs for the concise Steam Demo first-session walkthrough.</summary>
public static class TutorialStepIds
{
    public const string GrabBuddy = "demo.onboarding.grab_buddy";
    public const string EarnCredits = "demo.onboarding.earn_credits";

    public const string OpenInventory = "demo.onboarding.open_inventory";
    public const string PurchaseBaseballBat = "demo.onboarding.purchase_baseball_bat";
    public const string EquipBaseballBat = "demo.onboarding.equip_baseball_bat";

    public const string OpenPaintBuddy = "demo.onboarding.open_paint_buddy";
    public const string PaintBuddy = "demo.onboarding.paint_buddy";
    public const string SavePaintBuddy = "demo.onboarding.save_paint_buddy";
    public const string UsePaintedBuddy = "demo.onboarding.use_painted_buddy";

    public const string OpenPaintBackground = "demo.onboarding.open_paint_background";
    public const string PaintBackground = "demo.onboarding.paint_background";
    public const string SaveAndExitPaintBackground = "demo.onboarding.save_exit_paint_background";

    public const string OpenBuddyStudio = "demo.onboarding.open_buddy_studio";
    public const string BuyAndEquipStudioItem = "demo.onboarding.buy_equip_studio_item";
    public const string UnequipStudioItem = "demo.onboarding.unequip_studio_item";
    public const string SaveBuddyStudio = "demo.onboarding.save_buddy_studio";
    public const string ExitBuddyStudio = "demo.onboarding.exit_buddy_studio";

    public const string EnterWorkMode = "demo.onboarding.enter_work_mode";
    public const string DragWorkCompanion = "demo.onboarding.drag_work_companion";
    public const string ResizeWorkCompanion = "demo.onboarding.resize_work_companion";
    public const string ExitWorkMode = "demo.onboarding.exit_work_mode";

    public static readonly IReadOnlyList<string> Ordered =
    [
        GrabBuddy,
        EarnCredits,
        OpenInventory,
        PurchaseBaseballBat,
        EquipBaseballBat,
        OpenPaintBuddy,
        PaintBuddy,
        SavePaintBuddy,
        UsePaintedBuddy,
        OpenPaintBackground,
        PaintBackground,
        SaveAndExitPaintBackground,
        OpenBuddyStudio,
        BuyAndEquipStudioItem,
        UnequipStudioItem,
        SaveBuddyStudio,
        ExitBuddyStudio,
        EnterWorkMode,
        DragWorkCompanion,
        ResizeWorkCompanion,
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
/// V2 deliberately does not reinterpret the old broad v1 hints: an existing loaded player with no
/// v2 record is still auto-skipped by the runtime controller, while fresh/reset progress starts the
/// action-driven sequence from Grab Buddy.
/// </summary>
public sealed class TutorialProgressState
{
    public const string ExtensionKey = "demo.onboarding.v2";
    public const string LegacyExtensionKey = "demo.onboarding.v1";
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
            .OrderBy(IndexOf)
            .ToArray();
        return new TutorialProgressSnapshot(completed, false);
    }

    public bool HasPersistedRecord =>
        _progress.Extensions?.Values?.ContainsKey(ExtensionKey) == true;

    public bool HasLegacyRecord =>
        _progress.Extensions?.Values?.ContainsKey(LegacyExtensionKey) == true;

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
