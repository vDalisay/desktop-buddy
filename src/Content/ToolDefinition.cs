using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy;
using Godot;

namespace DesktopBuddy.Content;

/// <summary>
/// The authored definition of one FR-013 catalogue entry. Resources are the static-data
/// author (ARCHITECTURE §6, NFR-006.2): stable ID, kind, price, progression slot,
/// visibility, translation keys, and the engine references a slice needs all live here,
/// and the domain receives only the validated <see cref="CatalogueEntry"/> snapshot.
///
/// <para>
/// Prices are authored in <b>whole displayed credits</b> (FR-011.15). Milli-credits are an
/// internal representation, so no authored file can express a part-credit price.
/// </para>
/// </summary>
[GlobalClass]
public partial class ToolDefinition : GameResource
{
    /// <summary>The stable persisted ID (ARCHITECTURE §5). Never repurposed.</summary>
    [Export] public string ContentId { get; set; } = string.Empty;

    [Export] public CatalogueEntryKind Kind { get; set; } = CatalogueEntryKind.PurchasableTool;

    /// <summary>
    /// Price in whole credits; <c>0</c> means "not calibrated yet" and is legal only while
    /// <see cref="Visible"/> is false. Task 12 replaces the provisional numbers.
    /// </summary>
    [Export(PropertyHint.Range, "0,100000,1")] public int PriceCredits { get; set; }

    /// <summary>Position in the FR-013.4 progression order; unique across the catalogue.</summary>
    [Export(PropertyHint.Range, "0,256,1")] public int ProgressionOrder { get; set; }

    /// <summary>
    /// Whether the shop and tool grid may show this entry at all. Stays false until the
    /// slice passes its automated gates, is driven through real input, and the owner
    /// accepts its feel (owner rule, 2026-07-28) — an invisible entry cannot be bought.
    /// </summary>
    [Export] public bool Visible { get; set; }

    [Export] public string NameKey { get; set; } = string.Empty;
    [Export] public string DescriptionKey { get; set; } = string.Empty;

    /// <summary>Shop/tool-grid icon. Final art is Milestone 7.</summary>
    [Export] public Texture2D? Icon { get; set; }

    /// <summary>
    /// The scene this entry spawns, for the entries that spawn one (launchables, guns).
    /// Declared required by <see cref="RequiresLaunchScene"/> so a visible entry cannot
    /// ship with an empty slot.
    /// </summary>
    [Export] public PackedScene? LaunchScene { get; set; }

    [Export] public bool RequiresLaunchScene { get; set; }

    /// <summary>Shop/tool-grid icon is required once the entry is shown.</summary>
    [Export] public bool RequiresIcon { get; set; }

    public long PriceMilliCredits => PriceCredits * RewardLedger.MilliCreditsPerCredit;

    /// <summary>The immutable snapshot the domain rules run against.</summary>
    public CatalogueEntry ToEntry() => new(
        ContentId,
        Kind,
        PriceMilliCredits,
        ProgressionOrder,
        Visible,
        NameKey,
        DescriptionKey);

    /// <summary>
    /// Asset rules only — the ones the domain snapshot cannot express because it carries no
    /// engine references. A visible entry may not have an empty required slot.
    /// </summary>
    public Godot.Collections.Array<string> ValidateAssets()
    {
        var errors = new Godot.Collections.Array<string>();
        string label = string.IsNullOrWhiteSpace(ContentId) ? "<empty>" : ContentId;
        if (!Visible)
            return errors;

        if (RequiresLaunchScene && LaunchScene is null)
            errors.Add($"'{label}' is visible and needs a launch scene, but none is assigned");
        if (RequiresIcon && Icon is null)
            errors.Add($"'{label}' is visible and needs an icon, but none is assigned");
        return errors;
    }

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();

        // One rule set: the same structural validation the domain enforces, reported here
        // as readable startup messages instead of a constructor throw.
        foreach (string error in ToolCatalogue.Validate(new[] { ToEntry() }))
        {
            errors.Add(error);
        }

        foreach (string error in ValidateAssets())
        {
            errors.Add(error);
        }

        return errors;
    }
}
