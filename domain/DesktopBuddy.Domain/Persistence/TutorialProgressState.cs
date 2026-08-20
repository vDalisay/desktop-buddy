using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>Stable semantic IDs for the concise Steam Demo first-session walkthrough.</summary>
public static class TutorialStepIds
{
    public const string GrabBuddy = "demo.onboarding.grab_buddy";

    public const string OpenInventory = "demo.onboarding.open_inventory";
    public const string PurchaseBaseballBat = "demo.onboarding.purchase_baseball_bat";
    public const string ChargedBatHit = "demo.onboarding.charged_bat_hit";
    public const string UnequipTool = "demo.onboarding.unequip_tool";

    public const string OpenPaintBuddy = "demo.onboarding.open_paint_buddy";

    /// <summary>
    /// Paint needs a character to paint on. The walkthrough used to assume one already existed,
    /// which left a fresh player staring at "Create or select a local character" with a disabled
    /// Save (owner feedback 2026-08-20).
    /// </summary>
    public const string CreateBuddy = "demo.onboarding.create_buddy";

    public const string SelectPaintBrush = "demo.onboarding.select_paint_brush";
    public const string SelectPaintColor = "demo.onboarding.select_paint_color";
    /// <summary>
    /// Painting and saving are one lesson: the prompt rings the canvas and Save together and
    /// completes on the save (owner instruction 2026-08-20). The old separate
    /// <c>save_paint_buddy</c> step is retired; unknown ids are filtered on load, so a record
    /// written before this still resumes.
    /// </summary>
    public const string PaintBuddy = "demo.onboarding.paint_buddy";
    public const string UsePaintedBuddy = "demo.onboarding.use_painted_buddy";
    public const string AdmirePaintedBuddy = "demo.onboarding.admire_painted_buddy";

    public const string OpenPaintBackground = "demo.onboarding.open_paint_background";
    public const string SelectBackgroundSpray = "demo.onboarding.select_background_spray";
    public const string SelectBackgroundColor = "demo.onboarding.select_background_color";
    public const string PaintBackground = "demo.onboarding.paint_background";
    public const string FloatPaintBackgroundPanel = "demo.onboarding.float_paint_background_panel";
    public const string SaveAndExitPaintBackground = "demo.onboarding.save_exit_paint_background";

    public const string OpenBuddyStudio = "demo.onboarding.open_buddy_studio";
    public const string SelectNoseCategory = "demo.onboarding.select_nose_category";
    public const string SelectNoseButtonStyle = "demo.onboarding.select_nose_button_style";
    public const string BuyStudioItem = "demo.onboarding.buy_studio_item";
    public const string EquipStudioItem = "demo.onboarding.equip_studio_item";
    public const string SaveBuddyStudio = "demo.onboarding.save_buddy_studio";
    public const string ExitBuddyStudio = "demo.onboarding.exit_buddy_studio";
    public const string AdmireStudioBuddy = "demo.onboarding.admire_studio_buddy";

    public const string EnterWorkMode = "demo.onboarding.enter_work_mode";
    public const string DragWorkCompanion = "demo.onboarding.drag_work_companion";
    public const string ResizeWorkCompanion = "demo.onboarding.resize_work_companion";
    public const string ToggleWorkCounter = "demo.onboarding.toggle_work_counter";
    public const string ExitWorkMode = "demo.onboarding.exit_work_mode";

    /// <summary>Terminal sign-off. The walkthrough says goodbye instead of vanishing.</summary>
    public const string Farewell = "demo.onboarding.farewell";

    public static readonly IReadOnlyList<string> Ordered =
    [
        GrabBuddy,
        OpenInventory,
        PurchaseBaseballBat,
        ChargedBatHit,
        UnequipTool,
        OpenPaintBuddy,
        CreateBuddy,
        SelectPaintBrush,
        SelectPaintColor,
        PaintBuddy,
        UsePaintedBuddy,
        AdmirePaintedBuddy,
        OpenPaintBackground,
        SelectBackgroundSpray,
        SelectBackgroundColor,
        PaintBackground,
        FloatPaintBackgroundPanel,
        SaveAndExitPaintBackground,
        OpenBuddyStudio,
        SelectNoseCategory,
        SelectNoseButtonStyle,
        BuyStudioItem,
        EquipStudioItem,
        SaveBuddyStudio,
        ExitBuddyStudio,
        AdmireStudioBuddy,
        EnterWorkMode,
        DragWorkCompanion,
        ResizeWorkCompanion,
        ToggleWorkCounter,
        ExitWorkMode,
        Farewell,
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

    /// <summary>The next runtime action the guidance controller should observe.</summary>
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

    /// <summary>
    /// Replay from the first step. This writes an empty record rather than removing the key, so a
    /// replay is never mistaken for the "existing player, no v2 record" auto-skip case.
    /// </summary>
    public bool Restart() => _progress.SetExtensionValue(ExtensionKey, string.Empty);

    private static int IndexOf(string id)
    {
        for (int index = 0; index < TutorialStepIds.Ordered.Count; index++)
            if (string.Equals(TutorialStepIds.Ordered[index], id, StringComparison.Ordinal))
                return index;
        return int.MaxValue;
    }
}
