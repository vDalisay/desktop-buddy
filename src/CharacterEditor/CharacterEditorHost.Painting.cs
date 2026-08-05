using System;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class CharacterEditorHost
{
    private PaintCanvasControl _paintCanvas = null!;
    private PaintTextureBridge _paintTextures = null!;
    private VBoxContainer _paintControls = null!;
    private Label _paintStatus = null!;
    private Label _brushSize = null!;
    private Button _paintModeButton = null!;
    private Button _undoPaintButton = null!;
    private Button _redoPaintButton = null!;
    private ColorPickerButton _paintColorPicker = null!;
    private ColorRect _currentColorSwatch = null!;
    private Camera3D _paintCamera = null!;
    private ConfirmationDialog _eraseAllConfirmation = null!;
    private bool _paintAttachStarted;

    public bool IsPaintMode => _paintControls is not null && _paintControls.Visible;
    public PaintWorkspace PaintWorkspace => _paintCanvas.Workspace;

    /// <summary>
    /// Opens the editor directly in its paint workspace. This is the product entry point used
    /// by the Win98 Paint / Character menu; the appearance form remains reachable through the
    /// in-editor mode button rather than being shown first.
    /// </summary>
    public async Task OpenPaintEditorAsync()
    {
        await OpenEditorAsync();
        if (!IsEditorOpen)
            return;

        // Painting is attached after the base editor UI and preview camera have entered the
        // tree. Give that deferred composition a bounded opportunity to complete.
        for (int frame = 0; frame < 120 && !GodotObject.IsInstanceValid(_paintCanvas); frame++)
        {
            if (!_paintAttachStarted)
                TryAttachPaintingWorkspace();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (!GodotObject.IsInstanceValid(_paintCanvas) ||
            !GodotObject.IsInstanceValid(_paintControls))
        {
            return;
        }

        SetPaintMode(true);

        // The legacy appearance form and preview share one ScrollContainer. Move directly to
        // the paint toolbar/canvas so opening Paint never lands at the top of the long form.
        if (FindChild("CharacterControlsScroll", true, false) is ScrollContainer scroll)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            scroll.ScrollVertical = Mathf.RoundToInt(_paintControls.Position.Y);
        }
    }

    private void ProcessPainting()
    {
        if (!IsInitialized)
            return;
        if (_paintCanvas is null)
        {
            if (!_paintAttachStarted)
                TryAttachPaintingWorkspace();
            return;
        }
        _paintTextures.FlushFrame(_paintCanvas.Workspace.Surfaces);
    }

    private void TryAttachPaintingWorkspace()
    {
        if (FindChild("CharacterPreview", recursive: true, owned: false) is not SubViewportContainer preview ||
            preview.GetParent() is not VBoxContainer controls)
        {
            return;
        }
        if (preview.FindChildren("*", nameof(Camera3D), recursive: true, owned: false)
                .FirstOrDefault() is not Camera3D camera)
        {
            return;
        }
        _paintAttachStarted = true;
        BuildPaintingArea(controls, preview, camera);
    }

    private void BuildPaintingArea(VBoxContainer controls, SubViewportContainer previewContainer, Camera3D camera)
    {
        _paintCamera = camera;
        _paintTextures = new PaintTextureBridge(_preview);

        _paintCanvas = new PaintCanvasControl { Name = "CharacterPaintCanvas", Visible = false };
        _paintCanvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        previewContainer.AddChild(_paintCanvas);
        _paintCanvas.WorkspaceChanged += () => { QueueAllPaintTextures(); RefreshPaintStatus(); };
        _paintCanvas.ViewChanged += ApplyPaintView;
        _paintCanvas.HoverChanged += _ => RefreshPaintStatus();

        _paintModeButton = Button(PaintUiText.Get(PaintUiText.Open), "PaintModeButton");
        _paintModeButton.TooltipText = PaintUiText.Get(PaintUiText.OpenTooltip);
        _paintModeButton.Pressed += TogglePaintMode;
        controls.AddChild(_paintModeButton);

        _paintControls = new VBoxContainer { Name = "CharacterPaintControls", Visible = false };
        _paintControls.AddThemeConstantOverride("separation", 2);
        controls.AddChild(_paintControls);

        int previewIndex = previewContainer.GetIndex();
        controls.MoveChild(_paintModeButton, previewIndex);
        controls.MoveChild(_paintControls, previewIndex + 1);

        var toolRow = new HBoxContainer { Name = "PaintToolRow" };
        toolRow.AddThemeConstantOverride("separation", 2);
        _paintControls.AddChild(toolRow);
        Button brush = Button(PaintUiText.Get(PaintUiText.Brush), "PaintBrushButton");
        Button eraser = Button(PaintUiText.Get(PaintUiText.Eraser), "PaintEraserButton");
        brush.ToggleMode = true;
        eraser.ToggleMode = true;
        brush.ButtonPressed = true;
        brush.Pressed += () =>
        {
            _paintCanvas.Workspace.SelectedTool = PaintTool.Brush;
            brush.ButtonPressed = true;
            eraser.ButtonPressed = false;
            RefreshPaintStatus();
        };
        eraser.Pressed += () =>
        {
            _paintCanvas.Workspace.SelectedTool = PaintTool.Eraser;
            brush.ButtonPressed = false;
            eraser.ButtonPressed = true;
            RefreshPaintStatus();
        };
        toolRow.AddChild(brush);
        toolRow.AddChild(eraser);

        _currentColorSwatch = new ColorRect
        {
            Name = "PaintCurrentColor",
            Color = Colors.White,
            CustomMinimumSize = new Vector2(28, 24),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TooltipText = "Current paint color",
        };
        toolRow.AddChild(_currentColorSwatch);

        _paintColorPicker = new ColorPickerButton
        {
            Name = "PaintColorWheel",
            Color = Colors.White,
            TooltipText = PaintUiText.Get(PaintUiText.ColorTooltip),
            CustomMinimumSize = new Vector2(84, 24),
        };
        _paintColorPicker.ColorChanged += SetPaintColor;
        toolRow.AddChild(_paintColorPicker);

        BuildPresetPalette();

        var sizeRow = new HBoxContainer { Name = "PaintBrushSizeRow" };
        sizeRow.AddThemeConstantOverride("separation", 2);
        _paintControls.AddChild(sizeRow);
        Button smaller = Button("−", "PaintSizeDecreaseButton");
        Button larger = Button("+", "PaintSizeIncreaseButton");
        smaller.Pressed += () => { _paintCanvas.Workspace.AdjustBrush(-1); RefreshPaintStatus(); };
        larger.Pressed += () => { _paintCanvas.Workspace.AdjustBrush(1); RefreshPaintStatus(); };
        _brushSize = new Label
        {
            Name = "PaintBrushSize",
            CustomMinimumSize = new Vector2(34, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        sizeRow.AddChild(new Label { Text = PaintUiText.Get(PaintUiText.BrushSize) });
        sizeRow.AddChild(smaller);
        sizeRow.AddChild(_brushSize);
        sizeRow.AddChild(larger);

        var commandRow = new HBoxContainer { Name = "PaintHistoryRow" };
        commandRow.AddThemeConstantOverride("separation", 2);
        _paintControls.AddChild(commandRow);
        _undoPaintButton = Button(PaintUiText.Get(PaintUiText.Undo), "PaintUndoButton");
        _undoPaintButton.TooltipText = "Undo the last paint action (Ctrl+Z).";
        _undoPaintButton.Pressed += UndoPaint;
        commandRow.AddChild(_undoPaintButton);

        _redoPaintButton = Button("Redo", "PaintRedoButton");
        _redoPaintButton.TooltipText = "Redo the last undone paint action (Ctrl+Y or Ctrl+Shift+Z).";
        _redoPaintButton.Pressed += RedoPaint;
        commandRow.AddChild(_redoPaintButton);

        Button eraseAll = Button(PaintUiText.Get(PaintUiText.EraseAll), "PaintEraseAllButton");
        eraseAll.Pressed += () => _eraseAllConfirmation.PopupCentered();
        commandRow.AddChild(eraseAll);

        var viewRow = new HBoxContainer { Name = "PaintViewRow" };
        viewRow.AddThemeConstantOverride("separation", 2);
        _paintControls.AddChild(viewRow);
        Button zoomOut = Button(PaintUiText.Get(PaintUiText.ZoomOut), "PaintZoomOutButton");
        Button zoomIn = Button(PaintUiText.Get(PaintUiText.ZoomIn), "PaintZoomInButton");
        Button resetView = Button(PaintUiText.Get(PaintUiText.ResetView), "PaintResetViewButton");
        zoomOut.Pressed += () => SetPaintZoom(_paintCanvas.View.Zoom - 0.2);
        zoomIn.Pressed += () => SetPaintZoom(_paintCanvas.View.Zoom + 0.2);
        resetView.Pressed += _paintCanvas.ResetView;
        viewRow.AddChild(zoomOut);
        viewRow.AddChild(zoomIn);
        viewRow.AddChild(resetView);

        // Both labels moved to the Win98 status bar (Win98PaintStatusBootstrap); kept hidden so
        // the localization scenario and RefreshPaintStatus keep working unchanged.
        _paintStatus = new Label
        {
            Name = "PaintHoverStatus",
            Text = PaintUiText.Get(PaintUiText.HoverHelp),
            Visible = false,
        };
        _paintControls.AddChild(_paintStatus);
        _paintControls.AddChild(new Label
        {
            Text = PaintUiText.Get(PaintUiText.InputHelp),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
        });

        _eraseAllConfirmation = new ConfirmationDialog
        {
            Name = "PaintEraseAllConfirmation",
            Title = PaintUiText.Get(PaintUiText.EraseAllTitle),
            DialogText = PaintUiText.Get(PaintUiText.EraseAllBody),
            OkButtonText = PaintUiText.Get(PaintUiText.EraseAll),
        };
        _eraseAllConfirmation.Confirmed += () =>
        {
            _paintCanvas.Workspace.EraseAll();
            QueueAllPaintTextures();
            RefreshPaintStatus();
        };
        AddChild(_eraseAllConfirmation);
        RefreshPaintStatus();
    }

    private void BuildPresetPalette()
    {
        var paletteFrame = new PanelContainer
        {
            Name = "PaintPresetPalette",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _paintControls.AddChild(paletteFrame);

        var palette = new GridContainer
        {
            Name = "PaintPresetPaletteGrid",
            Columns = 8,
        };
        palette.AddThemeConstantOverride("h_separation", 1);
        palette.AddThemeConstantOverride("v_separation", 1);
        paletteFrame.AddChild(palette);

        Color[] colors =
        [
            Colors.Black, new Color("#808080"), Colors.White, new Color("#800000"),
            Colors.Red, new Color("#808000"), Colors.Yellow, new Color("#008000"),
            Colors.Lime, new Color("#008080"), Colors.Cyan, new Color("#000080"),
            Colors.Blue, new Color("#800080"), Colors.Magenta, new Color("#C0C0C0"),
        ];

        for (int index = 0; index < colors.Length; index++)
        {
            Color preset = colors[index];
            var swatch = new Button
            {
                Name = $"PaintPalette{index}",
                CustomMinimumSize = new Vector2(22, 18),
                TooltipText = $"Use #{preset.ToHtml(false)}",
                FocusMode = Control.FocusModeEnum.All,
            };
            var normal = new StyleBoxFlat { BgColor = preset };
            normal.SetBorderWidthAll(1);
            normal.BorderColor = Colors.Black;
            swatch.AddThemeStyleboxOverride("normal", normal);
            swatch.AddThemeStyleboxOverride("hover", normal);
            swatch.AddThemeStyleboxOverride("pressed", normal);
            swatch.Pressed += () =>
            {
                _paintColorPicker.Color = preset;
                SetPaintColor(preset);
            };
            palette.AddChild(swatch);
        }
    }

    private void SetPaintColor(Color value)
    {
        _paintCanvas.Workspace.SelectedColor = new PaintColor(
            (byte)Math.Clamp(Math.Round(value.R * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(value.G * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(value.B * 255), 0, 255));
        if (GodotObject.IsInstanceValid(_currentColorSwatch))
            _currentColorSwatch.Color = value;
        RefreshPaintStatus();
    }

    private void UndoPaint()
    {
        if (_paintCanvas.Workspace.Undo())
            QueueAllPaintTextures();
        RefreshPaintStatus();
    }

    private void RedoPaint()
    {
        if (_paintCanvas.Workspace.Redo())
            QueueAllPaintTextures();
        RefreshPaintStatus();
    }

    private void TogglePaintMode() => SetPaintMode(!IsPaintMode);

    private void SetPaintMode(bool enabled)
    {
        if (!GodotObject.IsInstanceValid(_paintControls) ||
            !GodotObject.IsInstanceValid(_paintCanvas))
        {
            return;
        }

        _paintControls.Visible = enabled;
        _paintCanvas.Visible = enabled;
        _paintCanvas.MouseFilter = enabled
            ? Control.MouseFilterEnum.Stop
            : Control.MouseFilterEnum.Ignore;
        _paintModeButton.Text = PaintUiText.Get(
            enabled ? PaintUiText.AppearanceControls : PaintUiText.Open);
        _paintCanvas.ResetView();
        if (enabled)
        {
            QueueAllPaintTextures();
            _paintCanvas.GrabFocus();
        }
        RefreshPaintStatus();
    }

    private void QueueAllPaintTextures()
    {
        foreach ((PaintPart part, PaintSurface surface) in _paintCanvas.Workspace.Surfaces)
            _paintTextures.Queue(part, surface);
    }

    private void SetPaintZoom(double zoom)
    {
        _paintCanvas.View.SetZoom(zoom, default);
        ApplyPaintView();
    }

    private void ApplyPaintView()
    {
        if (_paintCamera is null)
            return;
        _paintCamera.Size = (float)(PaintCanvasControl.BaseCameraSize / _paintCanvas.View.Zoom);
        PaintPoint center = _paintCanvas.CameraCenter;
        _paintCamera.Position = new Vector3((float)center.X, (float)-center.Y, 600);
        RefreshPaintStatus();
    }

    private void RefreshPaintStatus()
    {
        if (_paintCanvas is null || _paintStatus is null)
            return;
        _brushSize.Text = _paintCanvas.Workspace.BrushDiameter.ToString();
        _undoPaintButton.Disabled = !_paintCanvas.Workspace.CanUndo;
        _redoPaintButton.Disabled = !_paintCanvas.Workspace.CanRedo;
        string hovered = _paintCanvas.HoveredPart?.ToString() ?? PaintUiText.Get(PaintUiText.Canvas);
        string tool = PaintUiText.Get(
            _paintCanvas.Workspace.SelectedTool == PaintTool.Brush
                ? PaintUiText.Brush
                : PaintUiText.Eraser);
        _paintStatus.Text = PaintUiText.Format(
            PaintUiText.Status,
            tool,
            hovered,
            _paintCanvas.View.Zoom);
    }
}
