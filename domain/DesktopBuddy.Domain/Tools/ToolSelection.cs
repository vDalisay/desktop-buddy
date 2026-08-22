using System;
using DesktopBuddy.Domain.Mood;

namespace DesktopBuddy.Domain.Tools;

/// <summary>
/// Stable tool ordinals. New entries append only so legacy integer saves remain migratable.
/// </summary>
public enum ToolId
{
    Grab = 0,
    Pet = 1,
    Tickle = 2,
    BoxingGlove = 3,
    Baseball = 4,
    Meal = 5,
    BaseballBat = 6,
    Pistol = 7,
    Grenade = 8,
    FireSprayer = 9,
    SoccerBall = 10,
    Drink = 11,
    Shotgun = 12,
    RepairKit = 13,

    /// <summary>
    /// The toy gun the player owns first. Appended rather than swapped with
    /// <see cref="Pistol"/>: ordinals are persisted, and <c>tool.pistol</c> already means
    /// the real gun everywhere it has shipped.
    /// </summary>
    NerfBlaster = 14,

    /// <summary>
    /// The purchasable stronger grab. Appended rather than inserted next to
    /// <see cref="Grab"/>: ordinals are persisted.
    /// </summary>
    PowerGrab = 15,

    /// <summary>
    /// Grab with a rope: the same pick-up, plus secondary to tie what is held to the spot
    /// under the pointer and let go of it. Appended rather than inserted next to
    /// <see cref="Grab"/>: ordinals are persisted.
    /// </summary>
    RopeSuspender = 16,
}

/// <summary>How a tool physically acts on the buddy (RAGDOLL §9).</summary>
public enum ToolCategory
{
    Grab,
    Care,
    Damage,
    PhysicsToy,
}

/// <summary>Structural tool facts shared by logic and the Godot layer.</summary>
public static class ToolCatalog
{
    public static ToolCategory CategoryOf(ToolId tool) => tool switch
    {
        ToolId.Grab or ToolId.PowerGrab or ToolId.RopeSuspender => ToolCategory.Grab,
        ToolId.Pet or ToolId.Tickle => ToolCategory.Care,
        // Consumables act through the care/consume machinery, not the damage pipeline;
        // their pain, when a launch hurts, still arrives as an ordinary physical impact.
        ToolId.Meal or ToolId.Drink or ToolId.RepairKit => ToolCategory.Care,
        // The Nerf Blaster is a damage tool by mechanism, not by outcome: it fires through
        // the same pipeline and its darts are authored to score next to nothing.
        ToolId.BoxingGlove or ToolId.BaseballBat or ToolId.NerfBlaster or ToolId.Pistol or
            ToolId.Grenade or ToolId.FireSprayer or ToolId.Shotgun => ToolCategory.Damage,
        ToolId.Baseball or ToolId.SoccerBall => ToolCategory.PhysicsToy,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown tool."),
    };

    /// <summary>The care channel a tool feeds, or <c>null</c> for non-care tools.</summary>
    public static CareKind? CareKindOf(ToolId tool) => tool switch
    {
        ToolId.Pet => CareKind.Pet,
        ToolId.Tickle => CareKind.Tickle,
        _ => null,
    };
}

/// <summary>
/// Holds the currently selected tool. A new save starts with <see cref="ToolId.Grab"/>
/// selected (RAGDOLL §9). Ordinary Play/Work input-mode toggles do not carry tool semantics;
/// deliberate tool selection routes through this state. The dedicated Work companion applies
/// one explicit product rule on exit: it selects normal Grab before direct buddy play resumes.
/// </summary>
public sealed class ToolSelection
{
    public const ToolId DefaultTool = ToolId.Grab;

    public ToolId Selected { get; private set; } = DefaultTool;

    public void Select(ToolId tool) => Selected = tool;
}
