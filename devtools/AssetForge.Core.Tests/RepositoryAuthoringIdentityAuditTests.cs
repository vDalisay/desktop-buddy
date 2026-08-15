using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class RepositoryAuthoringIdentityAuditTests
{
    [Fact]
    public void Duplicate_environment_stable_id_in_two_authoring_folders_is_reported()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe first = AssetRecipe.SofaDefaults() with { AssetId = "decoration.sofa.duplicate" };
            AssetRecipe second = first with { DisplayName = "Other authoring source" };
            WriteRecipe(root, "authoring/asset-forge/sofas/a/recipe.json", first);
            WriteRecipe(root, "authoring/asset-forge/sofas/b/recipe.json", second);

            IReadOnlyList<string> diagnostics = RepositoryAuthoringIdentityAudit.Audit(root);

            string duplicate = Assert.Single(diagnostics);
            Assert.Contains("duplicate Asset Forge Environment stable ID 'decoration.sofa.duplicate'", duplicate, StringComparison.Ordinal);
            Assert.Contains("sofas/a/recipe.json", duplicate, StringComparison.Ordinal);
            Assert.Contains("sofas/b/recipe.json", duplicate, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Malformed_recipe_is_reported_without_hiding_valid_identity_checks()
    {
        string root = TempRepository();
        try
        {
            WriteText(root, "authoring/asset-forge/sofas/bad/recipe.json", "{ definitely-not-json }");
            WriteRecipe(root, "authoring/asset-forge/sofas/good/recipe.json", AssetRecipe.SofaDefaults() with
            {
                AssetId = "decoration.sofa.good",
            });

            IReadOnlyList<string> diagnostics = RepositoryAuthoringIdentityAudit.Audit(root);

            Assert.Single(diagnostics);
            Assert.Contains("invalid authored recipe", diagnostics[0], StringComparison.Ordinal);
            Assert.Contains("sofas/bad/recipe.json", diagnostics[0], StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteRecipe(string root, string relative, AssetRecipe recipe) =>
        WriteText(root, relative, RecipeCodec.WriteCanonical(recipe));

    private static void WriteText(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string TempRepository()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-af-id-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "DesktopBuddy.csproj"), "<Project />");
        return root;
    }
}
