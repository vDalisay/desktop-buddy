using System.Linq;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Startup;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class AssetForgeGeneratedReplacementScenario : ITestScenario
{
    public const string ScenarioId = "asset_forge_generated_replacements";
    private const string TopFeatureId = "top.ci_pear_torso";
    private const string TopContentId = "cosmetic.top.ci_pear_torso";
    private const string ShoesFeatureId = "shoes.ci_soft_foot";
    private const string ShoesContentId = "cosmetic.shoes.ci_soft_foot";

    public string Id => ScenarioId;

    public void Run(TestScenarioContext context)
    {
        BuddyGeneratedCosmeticRegistry registry = BuddyGeneratedCosmeticRegistry.Current;
        Require(registry.FeatureCatalog.Contains(CharacterFeatureSlot.Tops, TopFeatureId), "Generated Top fixture was not composed into Tops.");
        Require(registry.FeatureCatalog.Contains(CharacterFeatureSlot.Shoes, ShoesFeatureId), "Generated Shoes fixture was not composed into Shoes.");
        Require(registry.TryGet(TopFeatureId, out GeneratedBuddyCosmeticResource topResource), "Generated Top resource was not loaded.");
        Require(registry.TryGet(ShoesFeatureId, out GeneratedBuddyCosmeticResource shoesResource), "Generated Shoes resource was not loaded.");
        Require(topResource.Slot == CharacterFeatureSlot.Tops, "Generated Top resource has wrong slot.");
        Require(shoesResource.Slot == CharacterFeatureSlot.Shoes, "Generated Shoes resource has wrong slot.");

        ContentCatalogue catalogue = CatalogueLoader.LoadLaunchCatalogue();
        Require(catalogue.TryGet(TopContentId, out CatalogueEntry topSale) && topSale.PriceMilliCredits == 175_000, "Generated Top commerce entry missing or wrong price.");
        Require(catalogue.TryGet(ShoesContentId, out CatalogueEntry shoesSale) && shoesSale.PriceMilliCredits == 160_000, "Generated Shoes commerce entry missing or wrong price.");

        var visualCatalog = new BuddyCosmeticVisualCatalog(registry.FeatureCatalog, registry);
        BuddyCosmeticVisualDefinition topVisualDefinition = visualCatalog.Resolve(CharacterFeatureSlot.Tops, TopFeatureId, out bool topFallback);
        BuddyCosmeticVisualDefinition shoesVisualDefinition = visualCatalog.Resolve(CharacterFeatureSlot.Shoes, ShoesFeatureId, out bool shoesFallback);
        Require(!topFallback && topVisualDefinition.ApplicationMode == BuddyCosmeticApplicationMode.PartReplacement, "Generated Top is not registered as a part replacement.");
        Require(!shoesFallback && shoesVisualDefinition.ApplicationMode == BuddyCosmeticApplicationMode.PairedPartReplacement, "Generated Shoes are not registered as a paired part replacement.");

        BuddyVisualProfile profile = ResourceLoader.Load<BuddyVisualProfile>("res://data/buddy/lab_buddy_visual.tres")
            ?? throw new InvalidOperationException("Trusted Buddy visual profile could not be loaded.");
        var root = new Node3D { Name = "AssetForgeReplacementScenarioRoot" };
        context.SceneRoot.AddChild(root);
        try
        {
            var rig = new BuddyVisualRigView { Name = "ReplacementRig" };
            root.AddChild(rig);
            rig.Initialize(profile, new TestGeometrySource());
            BuddyVisualRigTrustSnapshot before = rig.CaptureTrustSnapshot();

            CharacterDocument document = CharacterDefaults.CreateDocument("asset-forge-generated-replacements", "Asset Forge Replacement");
            document = CharacterDocumentEditor.WriteFeatureId(document, CharacterFeatureSlot.Tops, TopFeatureId);
            document = CharacterDocumentEditor.WriteFeatureId(document, CharacterFeatureSlot.Shoes, ShoesFeatureId);
            CompiledCharacterAppearance compiled = CharacterCompiler.Compile(document, registry.FeatureCatalog);
            rig.ApplyAppearance(compiled);

            Require(rig.IsPartVisualReplaced(BuddyPartId.Torso), "Generated Top did not enter torso replacement state.");
            Require(rig.IsPartVisualReplaced(BuddyPartId.LeftFoot) && rig.IsPartVisualReplaced(BuddyPartId.RightFoot), "Generated Shoes did not replace both feet.");
            Require(!rig.GetPartMesh(BuddyPartId.Torso).Visible && !rig.GetPartOutline(BuddyPartId.Torso).Visible, "Trusted torso visual remained visible under generated replacement.");
            Require(!rig.GetPartMesh(BuddyPartId.LeftFoot).Visible && !rig.GetPartMesh(BuddyPartId.RightFoot).Visible, "Trusted foot meshes remained visible under generated Shoes.");
            Require(GodotObject.IsInstanceValid(rig.GetCosmeticVisual(CharacterFeatureSlot.Tops)), "Generated Top visual root was not instantiated.");
            Require(GodotObject.IsInstanceValid(rig.GetCosmeticVisual(CharacterFeatureSlot.Shoes)), "Generated left Shoe visual root was not instantiated.");
            Require(GodotObject.IsInstanceValid(rig.GetPairedCosmeticVisual(CharacterFeatureSlot.Shoes)), "Generated right Shoe visual root was not instantiated.");
            Require(CountPhysicsNodes(rig.GetCosmeticVisual(CharacterFeatureSlot.Tops)!) == 0, "Generated Top introduced physics nodes.");
            Require(CountPhysicsNodes(rig.GetCosmeticVisual(CharacterFeatureSlot.Shoes)!) == 0, "Generated Shoes introduced physics nodes.");
            Require(rig.TrustedGeometryMatches(before), "Generated replacements mutated trusted Buddy geometry/physics presentation inputs.");

            CharacterDocument defaults = CharacterDefaults.CreateDocument("asset-forge-generated-replacements-default", "Asset Forge Default");
            rig.ApplyAppearance(CharacterCompiler.Compile(defaults, registry.FeatureCatalog));
            Require(!rig.IsPartVisualReplaced(BuddyPartId.Torso) && !rig.IsPartVisualReplaced(BuddyPartId.LeftFoot) && !rig.IsPartVisualReplaced(BuddyPartId.RightFoot), "Removing replacements did not clear replacement state.");
            Require(rig.GetPartMesh(BuddyPartId.Torso).Visible && rig.GetPartMesh(BuddyPartId.LeftFoot).Visible && rig.GetPartMesh(BuddyPartId.RightFoot).Visible, "Trusted base visuals did not return after replacement removal.");
            Require(rig.GetCosmeticVisual(CharacterFeatureSlot.Tops) is null && rig.GetCosmeticVisual(CharacterFeatureSlot.Shoes) is null, "Replacement visual roots remained active after defaults were restored.");
            Require(rig.TrustedGeometryMatches(before), "Replacement removal changed trusted geometry.");

            context.Report.Pass(
                Id,
                $"Generated Top/Shoes resolved through trusted catalogue; base torso/feet were visually replaced and restored; physics nodes=0; prices={topSale.PriceMilliCredits}/{shoesSale.PriceMilliCredits}.");
        }
        finally
        {
            root.QueueFree();
        }
    }

    private static int CountPhysicsNodes(Node root) => root.FindChildren("*", string.Empty, true, false)
        .Count(static node => node is PhysicsBody3D or CollisionObject3D or CollisionShape3D);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestGeometrySource : IBuddyVisualTransformSource
    {
        public Vector3 WorldToVisual(Vector2 worldPoint) => new(worldPoint.X, -worldPoint.Y, 0f);
        public float WorldLengthToVisual(float worldLength) => worldLength;
        public float WorldAngleToVisual(float worldAngleRadians) => -worldAngleRadians;
    }
}
