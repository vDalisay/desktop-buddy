using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain : Control
{
    private LineEdit _source = null!, _displayName = null!, _featureId = null!, _contentId = null!;
    private SpinBox _price = null!, _sort = null!, _alpha = null!, _depth = null!, _roundness = null!, _bias = null!, _templeThickness = null!, _templeLength = null!, _templeDrop = null!;
    private OptionButton _geometryResolution = null!, _textureResolution = null!, _shapeMode = null!, _symmetry = null!;
    private AssetForgePreview _preview = null!;
    private Label _status = null!, _hashes = null!;
    private Button _export = null!;
    private CheckBox _reference = null!;
    private FileDialog _sourceDialog = null!, _openRecipeDialog = null!, _saveRecipeDialog = null!, _templateDialog = null!;
    private GeneratedAsset? _generated;
    private string? _sourcePath;

    public override void _Ready()
    {
        BuildUi();
        ApplyRecipe(AssetRecipe.GlassesDefaults());
        SetStatus("Choose a 1024×1024 transparent RGBA PNG, then Generate.");
    }

    private void BuildUi()
    {
        var root = new VBoxContainer { Name = "Root" };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        var toolbar = new HBoxContainer { Name = "Toolbar" };
        root.AddChild(toolbar);
        AddButton(toolbar, "Open Image…", ChooseSource);
        AddButton(toolbar, "Save Glasses Template…", () => _templateDialog.PopupCenteredRatio(0.65f));
        AddButton(toolbar, "Open Recipe…", () => _openRecipeDialog.PopupCenteredRatio(0.65f));
        AddButton(toolbar, "Save Recipe…", () => _saveRecipeDialog.PopupCenteredRatio(0.65f));
        toolbar.AddChild(new VSeparator());
        AddButton(toolbar, "Generate", Generate);
        _export = AddButton(toolbar, "Export to Game", Export);
        _export.Disabled = true;
        toolbar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _reference = new CheckBox { Text = "Show Buddy head", ButtonPressed = true };
        _reference.Toggled += visible => _preview.SetReferenceVisible(visible);
        toolbar.AddChild(_reference);
        AddButton(toolbar, "Reset View", () => _preview.ResetView());

        var repositoryTools = new HBoxContainer { Name = "RepositoryTools" };
        root.AddChild(repositoryTools);
        repositoryTools.AddChild(new Label { Text = "Saved authoring:" });
        AddButton(repositoryTools, "Regenerate", RegenerateCurrent);
        AddButton(repositoryTools, "Regenerate All", RegenerateAll);
        AddButton(repositoryTools, "Verify", VerifyCurrent);
        AddButton(repositoryTools, "Verify All", VerifyAll);
        repositoryTools.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        repositoryTools.AddChild(new Label { Text = "Verify/Regenerate run without Godot generation state." });

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8);
        root.AddChild(body);
        var leftScroll = new ScrollContainer { CustomMinimumSize = new Vector2(365, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(leftScroll);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 5);
        leftScroll.AddChild(left);
        left.AddChild(new Label { Text = "Buddy Studio > Glasses / glasses@1" });
        _source = Field(left, "Source PNG");
        _source.Editable = false;
        _displayName = Field(left, "Display name");
        _featureId = Field(left, "Feature ID");
        _contentId = Field(left, "Ownership content ID");
        _price = Spin(left, "Price (credits)", 1, 100000, 1);
        _sort = Spin(left, "Sort order", 0, 100000, 1);
        left.AddChild(new HSeparator());
        left.AddChild(new Label { Text = "Preset controls" });
        _depth = Spin(left, "Frame depth", 0.01, 1.0, 0.01);
        _roundness = Spin(left, "Frame roundness", 0.0, 1.0, 0.01);
        _bias = Spin(left, "Frame thickness bias", -8, 8, 1);
        _templeThickness = Spin(left, "Temple thickness", 0.01, 0.3, 0.005);
        _templeLength = Spin(left, "Temple length", 0.05, 1.5, 0.01);
        _templeDrop = Spin(left, "Temple drop", -0.5, 0.5, 0.01);
        var advancedToggle = new CheckButton { Text = "Advanced" };
        left.AddChild(advancedToggle);
        var advanced = new VBoxContainer { Visible = false };
        advancedToggle.Toggled += value => advanced.Visible = value;
        left.AddChild(advanced);
        _alpha = Spin(advanced, "Alpha threshold", 0.01, 0.99, 0.01);
        _geometryResolution = Options(advanced, "Geometry resolution", ["64", "128", "256"]);
        _textureResolution = Options(advanced, "Runtime texture", ["256", "512", "1024"]);
        _shapeMode = Options(advanced, "Shape mode", ["Flat extrusion", "Rounded extrusion"]);
        _symmetry = Options(advanced, "Symmetry", ["Off", "Left → Right", "Right → Left", "Average both"]);

        var previewPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(previewPanel);
        _preview = new AssetForgePreview { Name = "Preview", CustomMinimumSize = new Vector2(620, 520) };
        previewPanel.AddChild(_preview);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(_status);
        _hashes = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(_hashes);

        _sourceDialog = Dialog(FileDialog.FileModeEnum.OpenFile, "Choose 1024×1024 RGBA PNG", ["*.png ; PNG image"]);
        _sourceDialog.FileSelected += SetSource;
        _openRecipeDialog = Dialog(FileDialog.FileModeEnum.OpenFile, "Open Asset Forge recipe", ["*.json ; Asset Forge recipe"]);
        _openRecipeDialog.FileSelected += OpenRecipe;
        _saveRecipeDialog = Dialog(FileDialog.FileModeEnum.SaveFile, "Save Asset Forge recipe", ["*.json ; Asset Forge recipe"]);
        _saveRecipeDialog.FileSelected += SaveRecipe;
        _templateDialog = Dialog(FileDialog.FileModeEnum.SaveFile, "Save 1024×1024 Glasses authoring guide", ["*.png ; PNG image"]);
        _templateDialog.FileSelected += SaveTemplate;
    }

    private void ChooseSource() => _sourceDialog.PopupCenteredRatio(0.72f);

    private void SetSource(string path)
    {
        _sourcePath = path;
        _source.Text = path;
        _generated = null;
        _export.Disabled = true;
        SetStatus("Source selected. Generate to build the deterministic asset.");
    }

    private void Generate()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_sourcePath) || !File.Exists(_sourcePath))
                throw new InvalidOperationException("Choose a source PNG first.");
            AssetRecipe recipe = ReadRecipeFromUi();
            _generated = AssetForgeGenerator.Generate(File.ReadAllBytes(_sourcePath), recipe);
            _preview.ShowGenerated(_generated, _sourcePath);
            _export.Disabled = false;
            MaskDiagnostics d = _generated.Diagnostics;
            SetStatus($"Generated: {d.Components} component(s), {d.Holes} hole(s), {_generated.VertexCount:N0} vertices, {_generated.TriangleCount:N0} triangles.");
            _hashes.Text = $"Input {_generated.InputHash[..12]}  Recipe {_generated.RecipeHash[..12]}  Geometry {_generated.GeometryHash[..12]}  Asset {_generated.CanonicalAssetHash[..12]}  ✓ deterministic output";
        }
        catch (Exception exception)
        {
            _generated = null;
            _export.Disabled = true;
            SetStatus("Generate failed: " + exception.Message);
        }
    }

    private void Export()
    {
        try
        {
            if (_generated is null || string.IsNullOrWhiteSpace(_sourcePath))
                throw new InvalidOperationException("Generate the asset first.");
            byte[] thumbnail;
            try { thumbnail = _preview.CaptureThumbnailPng(); }
            catch { thumbnail = _generated.AlbedoPng; }
            string repo = RepositoryRoot();
            ExportResult result = RepositoryExporter.ExportGlasses(repo, File.ReadAllBytes(_sourcePath), _generated, thumbnail);
            AssetVerificationResult verification = RepositoryAssetVerifier.Verify(repo, _generated.Recipe.FeatureId);
            _hashes.Text = FormatVerification(verification);
            SetStatus(verification.Passed
                ? $"Exported and verified {_generated.Recipe.DisplayName}. Build/run Desktop Buddy to inspect Buddy Studio > Glasses.\n{result.AssetDirectory}"
                : $"Export completed, but Verify found drift. Review the diagnostics below before committing.\n{result.AssetDirectory}");
        }
        catch (Exception exception)
        {
            SetStatus("Export failed; repository content was rolled back when possible: " + exception.Message);
        }
    }

    private void OpenRecipe(string path)
    {
        try
        {
            AssetRecipe recipe = RecipeCodec.Read(File.ReadAllText(path));
            ApplyRecipe(recipe);
            string source = Path.Combine(Path.GetDirectoryName(path)!, recipe.SourceFile);
            if (File.Exists(source)) SetSource(source);
            else
            {
                _sourcePath = null;
                _source.Text = $"Missing beside recipe: {recipe.SourceFile}";
                _generated = null;
                _export.Disabled = true;
            }
            SetStatus("Recipe opened.");
        }
        catch (Exception exception)
        {
            SetStatus("Open recipe failed: " + exception.Message);
        }
    }

    private void SaveRecipe(string path)
    {
        try
        {
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";
            File.WriteAllText(path, RecipeCodec.WriteCanonical(ReadRecipeFromUi()));
            SetStatus("Recipe saved: " + path);
        }
        catch (Exception exception)
        {
            SetStatus("Save recipe failed: " + exception.Message);
        }
    }

    private void SaveTemplate(string path)
    {
        try
        {
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
            File.WriteAllBytes(path, AuthoringTemplateGenerator.CreateGlassesTemplatePng());
            SetStatus("Glasses guide saved. Use it as a reference layer; hide/remove the guide before exporting your transparent source PNG.\n" + path);
        }
        catch (Exception exception)
        {
            SetStatus("Save template failed: " + exception.Message);
        }
    }

    private void VerifyCurrent()
    {
        try
        {
            AssetVerificationResult result = RepositoryAssetVerifier.Verify(RepositoryRoot(), _featureId.Text.Trim());
            _hashes.Text = FormatVerification(result);
            SetStatus(result.Passed ? $"Verify passed for {result.FeatureId}." : $"Verify failed for {result.FeatureId}.");
        }
        catch (Exception exception)
        {
            SetStatus("Verify failed: " + exception.Message);
        }
    }

    private void VerifyAll()
    {
        try
        {
            RepositoryVerificationResult result = RepositoryAssetVerifier.VerifyAll(RepositoryRoot());
            _hashes.Text = FormatVerification(result);
            SetStatus(result.Passed
                ? $"Verify All passed: {result.PassedCount}/{result.Assets.Count} authored asset(s) match committed generated content."
                : $"Verify All failed: {result.PassedCount}/{result.Assets.Count} authored asset(s) passed. Review diagnostics below.");
        }
        catch (Exception exception)
        {
            SetStatus("Verify All failed: " + exception.Message);
        }
    }

    private void RegenerateCurrent()
    {
        try
        {
            RepositoryRegenerationResult result = RepositoryAssetRegenerator.Regenerate(RepositoryRoot(), _featureId.Text.Trim());
            _hashes.Text = FormatVerification(result.Verification);
            SetStatus(result.Verification.Passed
                ? $"Regenerated and verified {string.Join(", ", result.RegeneratedFeatureIds)}."
                : "Regeneration completed, but Verify All still reports drift.");
        }
        catch (Exception exception)
        {
            SetStatus("Regenerate failed: " + exception.Message);
        }
    }

    private void RegenerateAll()
    {
        try
        {
            RepositoryRegenerationResult result = RepositoryAssetRegenerator.RegenerateAll(RepositoryRoot());
            _hashes.Text = FormatVerification(result.Verification);
            SetStatus(result.Verification.Passed
                ? $"Regenerated {result.RegeneratedFeatureIds.Count} authored asset(s); Verify All passed."
                : $"Regenerated {result.RegeneratedFeatureIds.Count} authored asset(s), but Verify All still reports drift.");
        }
        catch (Exception exception)
        {
            SetStatus("Regenerate All failed: " + exception.Message);
        }
    }

    private AssetRecipe ReadRecipeFromUi()
    {
        AssetRecipe defaults = AssetRecipe.GlassesDefaults();
        return defaults with
        {
            DisplayName = _displayName.Text.Trim(),
            FeatureId = _featureId.Text.Trim(),
            ContentId = _contentId.Text.Trim(),
            PriceCredits = (int)_price.Value,
            SortOrder = (int)_sort.Value,
            Geometry = defaults.Geometry with
            {
                AlphaThreshold = _alpha.Value,
                Depth = _depth.Value,
                Roundness = _roundness.Value,
                ThicknessBiasPixels = (int)_bias.Value,
                TempleThickness = _templeThickness.Value,
                TempleLength = _templeLength.Value,
                TempleDrop = _templeDrop.Value,
                GeometryResolution = int.Parse(_geometryResolution.GetItemText(_geometryResolution.Selected)),
                RuntimeTextureResolution = int.Parse(_textureResolution.GetItemText(_textureResolution.Selected)),
                ShapeMode = (ShapeMode)_shapeMode.Selected,
                SymmetryMode = (SymmetryMode)_symmetry.Selected,
            },
        };
    }

    private void ApplyRecipe(AssetRecipe recipe)
    {
        _displayName.Text = recipe.DisplayName;
        _featureId.Text = recipe.FeatureId;
        _contentId.Text = recipe.ContentId;
        _price.Value = recipe.PriceCredits;
        _sort.Value = recipe.SortOrder;
        _alpha.Value = recipe.Geometry.AlphaThreshold;
        _depth.Value = recipe.Geometry.Depth;
        _roundness.Value = recipe.Geometry.Roundness;
        _bias.Value = recipe.Geometry.ThicknessBiasPixels;
        _templeThickness.Value = recipe.Geometry.TempleThickness;
        _templeLength.Value = recipe.Geometry.TempleLength;
        _templeDrop.Value = recipe.Geometry.TempleDrop;
        SelectText(_geometryResolution, recipe.Geometry.GeometryResolution.ToString());
        SelectText(_textureResolution, recipe.Geometry.RuntimeTextureResolution.ToString());
        _shapeMode.Select((int)recipe.Geometry.ShapeMode);
        _symmetry.Select((int)recipe.Geometry.SymmetryMode);
    }

    private string RepositoryRoot() => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".."));

    private static string FormatVerification(AssetVerificationResult result)
    {
        var lines = new List<string> { $"{(result.Passed ? "OK" : "FAIL")} {result.FeatureId}" };
        lines.AddRange(result.Diagnostics.Select(static diagnostic => "  " + diagnostic));
        return string.Join("\n", lines);
    }

    private static string FormatVerification(RepositoryVerificationResult result)
    {
        var lines = new List<string>();
        foreach (AssetVerificationResult asset in result.Assets)
        {
            lines.Add($"{(asset.Passed ? "OK" : "FAIL")} {asset.FeatureId}");
            lines.AddRange(asset.Diagnostics.Take(6).Select(static diagnostic => "  " + diagnostic));
        }
        foreach (string diagnostic in result.RepositoryDiagnostics) lines.Add("FAIL repository  " + diagnostic);
        if (lines.Count == 0) lines.Add("OK repository  no authored Asset Forge assets yet");
        return string.Join("\n", lines);
    }

    private FileDialog Dialog(FileDialog.FileModeEnum mode, string title, string[] filters)
    {
        // Native picker: Godot's built-in browser opens inside the project folder and makes
        // reaching an image elsewhere on disk painful. Godot falls back to it if unsupported.
        var dialog = new FileDialog
        {
            FileMode = mode,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = title,
            Filters = filters,
            UseNativeDialog = true,
            CurrentDir = OS.GetSystemDir(OS.SystemDir.Pictures),
        };
        AddChild(dialog);
        return dialog;
    }

    private static Button AddButton(Container parent, string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private static LineEdit Field(Container parent, string label)
    {
        parent.AddChild(new Label { Text = label });
        var field = new LineEdit();
        parent.AddChild(field);
        return field;
    }

    private static SpinBox Spin(Container parent, string label, double min, double max, double step)
    {
        parent.AddChild(new Label { Text = label });
        var box = new SpinBox { MinValue = min, MaxValue = max, Step = step, AllowGreater = false, AllowLesser = false };
        parent.AddChild(box);
        return box;
    }

    private static OptionButton Options(Container parent, string label, string[] values)
    {
        parent.AddChild(new Label { Text = label });
        var option = new OptionButton();
        foreach (string value in values) option.AddItem(value);
        parent.AddChild(option);
        return option;
    }

    private static void SelectText(OptionButton option, string wanted)
    {
        for (int i = 0; i < option.ItemCount; i++)
            if (option.GetItemText(i) == wanted)
            {
                option.Select(i);
                return;
            }
    }

    private void SetStatus(string text) => _status.Text = text;
}
