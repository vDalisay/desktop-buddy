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
        ToolId.Grab => ToolCategory.Grab,
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
/// selected (RAGDOLL §9). Selection changes only on an explicit pick and never from a
/// Work/Play transition — the input-mode state machine models no tool, so a mode change
/// cannot mutate this (RAGDOLL "Overlay and Interface", M2 invariant).
/// </summary>
public sealed class ToolSelection
{
    public const ToolId DefaultTool = ToolId.Grab;

    public ToolId Selected { get; private set; } = DefaultTool;

    public void Select(ToolId tool) => Selected = tool;
}
