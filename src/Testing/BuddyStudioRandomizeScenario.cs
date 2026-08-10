using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class BuddyStudioRandomizeScenario : IScenario
{
    public string Id => "buddy_studio_randomize";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterDocument baseline = CharacterDocument.CreateDefault(
            Guid.Parse("8b500000-0000-4000-8000-000000000001"),
            "Random Studio") with
        {
            Paint = CharacterPaintManifest.ForNonBlank([PaintPart.Head, PaintPart.Torso]),
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["backgroundMarker"] = JsonSerializer.SerializeToElement("unchanged"),
            },
        };
        CharacterFeatureCatalog catalogue = CharacterFeatureCatalog.Shipped;
        var noOwnership = new HashSet<string>(StringComparer.Ordinal);
        CharacterDocument first = CharacterRandomizer.Randomize(baseline, catalogue, noOwnership, 77);
        CharacterDocument replay = CharacterRandomizer.Randomize(baseline, catalogue, noOwnership, 77);
        CharacterDocument different = CharacterRandomizer.Randomize(baseline, catalogue, noOwnership, 78);
        bool deterministic = string.Equals(
            CharacterDocumentEditor.Canonical(first),
            CharacterDocumentEditor.Canonical(replay),
            StringComparison.Ordinal);
        bool varied = !string.Equals(
            CharacterDocumentEditor.Canonical(first),
            CharacterDocumentEditor.Canonical(different),
            StringComparison.Ordinal);
        checks.Add(new StartupCheck(
            "bs5_twelve_category_randomize_is_seed_deterministic",
            deterministic && varied,
            $"deterministic={deterministic} varied={varied}"));

        bool allTwelveValid = true;
        int slots = 0;
        foreach (CharacterFeatureSlot slot in Enum.GetValues<CharacterFeatureSlot>().Distinct())
        {
            slots++;
            CharacterFeatureDocument feature = CharacterDocumentEditor.ReadFeatureDocument(first, slot);
            CosmeticDefinition definition = catalogue.ResolveDefinition(slot, feature.FeatureId, out bool known);
            NormalizedFeatureTransform transform = CharacterDocumentEditor.ReadFeatureTransform(first, slot);
            allTwelveValid &= known && definition.IsFreeDefault &&
                definition.TransformBounds.Contains(transform) &&
                definition.ColorChannels.All(channel => feature.Colors.ContainsKey(channel.Id)) &&
                feature.Colors.Keys.All(key => definition.ColorChannels.Any(channel =>
                    string.Equals(channel.Id, key, StringComparison.Ordinal)));
        }
        checks.Add(new StartupCheck(
            "bs5_all_twelve_categories_use_free_valid_definitions",
            allTwelveValid && slots == 12,
            $"slots={slots} valid={allTwelveValid}"));

        JsonElement marker = default;
        bool paintPreserved = baseline.Paint == first.Paint &&
            first.ExtensionData.TryGetValue("backgroundMarker", out marker) &&
            marker.GetString() == "unchanged";
        checks.Add(new StartupCheck(
            "bs5_randomize_preserves_paint_and_extension_state",
            paintPreserved,
            $"paint={baseline.Paint == first.Paint} marker={marker.GetString()}"));

        bool paidExcluded = true;
        bool paidEligibleWhenOwned = false;
        bool hiddenHairRetained = false;
        var owned = new HashSet<string>(StringComparer.Ordinal)
        {
            ContentIds.CosmeticWorkGlasses,
            ContentIds.CosmeticHairShortSweep,
            ContentIds.CosmeticHeadwearSoftCap,
        };
        for (ulong candidateSeed = 1; candidateSeed <= 256; candidateSeed++)
        {
            CharacterDocument freeResult = CharacterRandomizer.Randomize(
                baseline, catalogue, noOwnership, candidateSeed);
            paidExcluded &= CharacterDocumentEditor.ReadFeatureId(
                freeResult, CharacterFeatureSlot.Glasses) != CharacterFeatureIds.GlassesWorkClassic;

            CharacterDocument ownedResult = CharacterRandomizer.Randomize(
                baseline, catalogue, owned, candidateSeed);
            paidEligibleWhenOwned |= CharacterDocumentEditor.ReadFeatureId(
                ownedResult, CharacterFeatureSlot.Glasses) == CharacterFeatureIds.GlassesWorkClassic;
            hiddenHairRetained |=
                CharacterDocumentEditor.ReadFeatureId(
                    ownedResult, CharacterFeatureSlot.Headwear) == CharacterFeatureIds.HeadwearSoftCap &&
                CharacterDocumentEditor.ReadFeatureId(
                    ownedResult, CharacterFeatureSlot.Hair) == CharacterFeatureIds.HairShortSweep;
        }
        checks.Add(new StartupCheck(
            "bs5_paid_choices_require_existing_ownership",
            paidExcluded && paidEligibleWhenOwned && owned.SetEquals([
                ContentIds.CosmeticWorkGlasses,
                ContentIds.CosmeticHairShortSweep,
                ContentIds.CosmeticHeadwearSoftCap,
            ]),
            $"excluded={paidExcluded} eligible={paidEligibleWhenOwned} owned_count={owned.Count}"));
        checks.Add(new StartupCheck(
            "bs5_hides_hair_never_deletes_randomized_hair",
            hiddenHairRetained,
            $"cap_with_saved_hair={hiddenHairRetained}"));

        return Task.FromResult(new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]));
    }
}
