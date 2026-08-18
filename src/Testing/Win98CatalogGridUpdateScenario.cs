using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Regression gate for the catalogue hot path: presentation-only refreshes preserve tile nodes and
/// selection, while a structural ID/order change still rebuilds the grid.
/// </summary>
public sealed class Win98CatalogGridUpdateScenario : IScenario
{
    public string Id => "win98_catalog_grid_update";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var grid = new Win98CatalogGrid { Name = "CatalogGridUpdateScenario" };
        tree.Root.AddChild(grid);
        grid.SetItems(
        [
            new Win98CatalogItemPresentation("alpha", "Alpha", "10 cr", Tooltip: "Alpha initial"),
            new Win98CatalogItemPresentation("beta", "Beta", "20 cr", Tooltip: "Beta initial"),
        ]);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        try
        {
            Button? alphaBefore = grid.FindChild("Catalog_alpha", true, false) as Button;
            Button? betaBefore = grid.FindChild("Catalog_beta", true, false) as Button;
            bool selected = grid.Select("alpha", notify: false);
            ulong alphaId = GodotObject.IsInstanceValid(alphaBefore) ? alphaBefore!.GetInstanceId() : 0;
            ulong betaId = GodotObject.IsInstanceValid(betaBefore) ? betaBefore!.GetInstanceId() : 0;

            grid.SetItems(
            [
                new Win98CatalogItemPresentation("alpha", "Alpha renamed", "Owned", Tooltip: "Alpha updated", Accented: true),
                new Win98CatalogItemPresentation("beta", "Beta", "15 cr", Tooltip: "Beta updated"),
            ]);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            Button? alphaAfter = grid.FindChild("Catalog_alpha", true, false) as Button;
            Button? betaAfter = grid.FindChild("Catalog_beta", true, false) as Button;
            bool preserved = selected &&
                GodotObject.IsInstanceValid(alphaAfter) && alphaAfter!.GetInstanceId() == alphaId &&
                GodotObject.IsInstanceValid(betaAfter) && betaAfter!.GetInstanceId() == betaId &&
                grid.SelectedId == "alpha" && grid.IsPreviewOutlined("alpha") &&
                grid.IsPersistentAccented("alpha") && alphaAfter.TooltipText == "Alpha updated";
            checks.Add(new StartupCheck(
                "catalogue_presentation_refresh_preserves_tiles_and_selection",
                preserved,
                $"alpha={alphaId}->{alphaAfter?.GetInstanceId()} beta={betaId}->{betaAfter?.GetInstanceId()} selected={grid.SelectedId}"));

            grid.SetItems(
            [
                new Win98CatalogItemPresentation("alpha", "Alpha", "Owned", Selectable: false, Tooltip: "Unavailable"),
                new Win98CatalogItemPresentation("beta", "Beta", "15 cr"),
            ]);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Button? disabledAlpha = grid.FindChild("Catalog_alpha", true, false) as Button;
            bool disabledSelectionClears = GodotObject.IsInstanceValid(disabledAlpha) && disabledAlpha!.Disabled &&
                grid.SelectedId is null && !grid.IsPreviewOutlined("alpha");
            checks.Add(new StartupCheck(
                "catalogue_refresh_clears_selection_when_item_becomes_disabled",
                disabledSelectionClears,
                $"disabled={disabledAlpha?.Disabled} selected={grid.SelectedId ?? "<none>"}"));

            ulong alphaBeforeStructural = disabledAlpha?.GetInstanceId() ?? 0;
            grid.SetItems(
            [
                new Win98CatalogItemPresentation("alpha", "Alpha", "Owned"),
                new Win98CatalogItemPresentation("gamma", "Gamma", "30 cr"),
            ]);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            Button? alphaRebuilt = grid.FindChild("Catalog_alpha", true, false) as Button;
            Button? gamma = grid.FindChild("Catalog_gamma", true, false) as Button;
            bool structuralRebuild = GodotObject.IsInstanceValid(alphaRebuilt) &&
                alphaRebuilt!.GetInstanceId() != alphaBeforeStructural &&
                GodotObject.IsInstanceValid(gamma) &&
                grid.FindChild("Catalog_beta", true, false) is null;
            checks.Add(new StartupCheck(
                "catalogue_structural_change_rebuilds_tiles",
                structuralRebuild,
                $"alpha={alphaBeforeStructural}->{alphaRebuilt?.GetInstanceId()} gamma={GodotObject.IsInstanceValid(gamma)}"));
        }
        finally
        {
            grid.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }
}