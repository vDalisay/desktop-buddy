using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.UI.Win98;
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
    private Control _eraseAllBlocker = null!;
    private PanelContainer _eraseAllConfirmation = null!;
    private bool _paintAttachStarted;

    public bool IsPaintMode => _paintControls is not null && _paintControls.Visible;
    public PaintWorkspace PaintWorkspace => _paintCanvas.Workspace;

    public async Task OpenPaintEditorAsync()
    {
        CharacterEditorActionResult opened = await _session.OpenActiveAsync(
            _context.CharacterSelection?.ActiveCharacterId);
        if (!opened.Completed)
        {
            Handle(opened);
            return;
        }
        await OpenEditorAsync();
        if (!IsEditorOpen) return;

        for (int frame = 0; frame < 120 && !GodotObject.IsInstanceValid(_paintCanvas); frame++)
        {
            if (!_paintAttachStarted) TryAttachPaintingWorkspace();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (!GodotObject.IsInstanceValid(_paintCanvas) || !GodotObject.IsInstanceValid(_paintControls))
            return;

        SetPaintMode(true);
        if (FindChild("CharacterControlsScroll", true, false) is ScrollContainer scroll)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            scroll.ScrollVertical = Mathf.RoundToInt(_paintControls.Position.Y);
        }
    }

    private void ProcessPainting()
    {
        if (!IsInitialized) return;
        if (_paintCanvas is null)
        {
            if (!_paintAttachStarted) TryAttachPaintingWorkspace();
            return;
        }

        // Timed into the diagnostics: uploading a dirty part is a full 512x512 surface copy plus
        // a texture update, and it is the one paint cost that cannot be measured outside the
        // engine. The rest of the paint pipeline is domain code with its own benchmarks.
        long start = Stopwatch.GetTimestamp();
        _paintTextures.FlushFrame(_paintCanvas.Workspace.Surfaces);
        _paintFlushTicks += Stopwatch.GetTimestamp() - start;
    }

    private void TryAttachPaintingWorkspace()
    {
        if (FindChild("CharacterPreview", recursive: true, owned: false) is not SubViewportContainer preview ||
            preview.GetParent() is not VBoxContainer controls)
            return;
        if (preview.FindChildren("*", nameof(Camera3D), recursive: true, owned: false)
                .FirstOrDefault() is not Camera3D camera)
            return;

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
            _paintCanvas.SelectPaintTool(PaintTool.Brush);
            brush.ButtonPressed = true;
            eraser.ButtonPressed = false;
            RefreshPaintStatus();
        };
        eraser.Pressed += () =>
        {
            _paintCanvas.SelectPaintTool(PaintTool.Eraser);
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
        HoldRepeat(smaller, -1);
        HoldRepeat(larger, 1);
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
        eraseAll.Pressed += () =>
        {
            _paintCanvas.CancelCurve();
            _eraseAllBlocker.Visible = true;
            _eraseAllConfirmation.Visible = true;
        };
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

        _eraseAllBlocker = Win98Dialog.Blocker(_editorRoot, "PaintEraseAllBlocker");
        _eraseAllConfirmation = Win98Dialog.Create(
            "PaintEraseAllConfirmation",
            PaintUiText.Get(PaintUiText.EraseAllTitle),
            new Vector2(360, 160),
            out VBoxContainer eraseBody,
            HideEraseAllConfirmation,
            draggable: false);
        _eraseAllBlocker.AddChild(_eraseAllConfirmation);
        eraseBody.AddChild(new Label
        {
            Text = PaintUiText.Get(PaintUiText.EraseAllBody),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var eraseActions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        eraseBody.AddChild(eraseActions);
        Win98Dialog.Action(eraseActions, PaintUiText.Get(PaintUiText.EraseAll), () =>
        {
            _paintCanvas.Workspace.EraseAll();
            QueueAllPaintTextures();
            RefreshPaintStatus();
            HideEraseAllConfirmation();
        }).Name = "PaintEraseAllConfirmButton";
        Win98Dialog.Action(eraseActions, "Cancel", HideEraseAllConfirmation).Name = "PaintEraseAllCancelButton";
        RefreshPaintStatus();
    }

    private void HideEraseAllConfirmation()
    {
        if (GodotObject.IsInstanceValid(_eraseAllConfirmation))
            _eraseAllConfirmation.Visible = false;
        if (GodotObject.IsInstanceValid(_eraseAllBlocker))
            _eraseAllBlocker.Visible = false;
    }

    private void HoldRepeat(Button button, int step)
    {
        var timer = new Timer { Name = "Repeat", OneShot = false, ProcessMode = Node.ProcessModeEnum.Always };
        button.AddChild(timer);

        void Step()
        {
            _paintCanvas.AdjustBrushAndRefreshPreview(step);
            RefreshPaintStatus();
        }

        timer.Timeout += () => { timer.WaitTime = 0.05; Step(); };
        button.ButtonDown += () => { Step(); timer.Start(0.35); };
        button.ButtonUp += timer.Stop;
    }

    private void BuildPresetPalette()
    {
        var paletteFrame = new PanelContainer
        {
            Name = "PaintPresetPalette",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _paintControls.AddChild(paletteFrame);

        var paletteScroll = new ScrollContainer
        {
            Name = "PaintPresetPaletteScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        paletteFrame.AddChild(paletteScroll);

        var palette = new GridContainer
        {
            Name = "PaintPresetPaletteGrid",
            Columns = 8,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        palette.AddThemeConstantOverride("h_separation", 1);
        palette.AddThemeConstantOverride("v_separation", 1);
        paletteScroll.AddChild(palette);
    }

    private void SetPaintColor(Color value)
    {
        _paintCanvas.Workspace.SelectedColor = new PaintColor(
            (byte)Math.Clamp(Math.Round(value.R * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(value.G * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(value.B * 255), 0, 255));
        if (GodotObject.IsInstanceValid(_currentColorSwatch))
            _currentColorSwatch.Color = value;
        _paintCanvas.RefreshPendingCurvePreview();
        RefreshPaintStatus();
    }

    private void UndoPaint()
    {
        bool changed = _paintCanvas.CurvePending
            ? _paintCanvas.CancelCurve()
            : _paintCanvas.Workspace.Undo();
        if (changed) QueueAllPaintTextures();
        RefreshPaintStatus();
    }

    private void RedoPaint()
    {
        bool changed = _paintCanvas.CurvePending
            ? _paintCanvas.CancelCurve()
            : _paintCanvas.Workspace.Redo();
        if (changed) QueueAllPaintTextures();
        RefreshPaintStatus();
    }

    private void TogglePaintMode() => SetPaintMode(!IsPaintMode);

    private void SetPaintMode(bool enabled)
    {
        if (!GodotObject.IsInstanceValid(_paintControls) || !GodotObject.IsInstanceValid(_paintCanvas))
            return;

        if (!enabled) _paintCanvas.CancelCurve();
        _paintControls.Visible = enabled;
        _paintCanvas.Visible = enabled;
        _paintCanvas.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        _paintModeButton.Text = PaintUiText.Get(enabled ? PaintUiText.AppearanceControls : PaintUiText.Open);
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
        _paintCanvas.CancelCurve();
        _paintCanvas.View.SetZoom(zoom, default);
        ApplyPaintView();
    }

    private void ApplyPaintView()
    {
        if (_paintCamera is null) return;
        _paintCamera.Size = (float)(PaintCanvasControl.BaseCameraSize / _paintCanvas.View.Zoom);
        PaintPoint center = _paintCanvas.CameraCenter;
        _paintCamera.Position = new Vector3((float)center.X, (float)-center.Y, 600);
        RefreshPaintStatus();
    }

    private void RefreshPaintStatus()
    {
        if (_paintCanvas is null || _paintStatus is null) return;
        _brushSize.Text = _paintCanvas.Workspace.BrushDiameter.ToString();
        _undoPaintButton.Disabled = !_paintCanvas.Workspace.CanUndo && !_paintCanvas.CurvePending;
        _redoPaintButton.Disabled = !_paintCanvas.Workspace.CanRedo || _paintCanvas.CurvePending;

        if (_paintCanvas.CurvePending)
        {
            _paintStatus.Text = _paintCanvas.CurvePhase switch
            {
                BuddyPaintCurvePhase.BaselineDragging => "Curve: drag the straight baseline.",
                BuddyPaintCurvePhase.AwaitFirstBend => "Curve: drag a point on the baseline for the first bend.",
                BuddyPaintCurvePhase.FirstBendDragging => "Curve: release to set the first bend.",
                BuddyPaintCurvePhase.AwaitSecondBend => "Curve: drag a second point to make the final bend.",
                BuddyPaintCurvePhase.SecondBendDragging => "Curve: release to commit the curved line.",
                _ => "Curve",
            };
            return;
        }

        string hovered = _paintCanvas.HoveredPart?.ToString() ?? PaintUiText.Get(PaintUiText.Canvas);
        string tool = _paintCanvas.Workspace.SelectedTool switch
        {
            PaintTool.Brush => PaintUiText.Get(PaintUiText.Brush),
            PaintTool.Pen => "Pen",
            PaintTool.Eraser => PaintUiText.Get(PaintUiText.Eraser),
            PaintTool.Spray => "Spray",
            PaintTool.Curve => "Curve",
            _ => "Paint",
        };
        _paintStatus.Text = PaintUiText.Format(PaintUiText.Status, tool, hovered, _paintCanvas.View.Zoom);
    }
}
