using System;
using System.Linq;
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
    private Camera3D _paintCamera = null!;
    private ConfirmationDialog _eraseAllConfirmation = null!;
    private bool _paintAttachStarted;

    public bool IsPaintMode => _paintControls is not null && _paintControls.Visible;
    public PaintWorkspace PaintWorkspace => _paintCanvas.Workspace;

    private void ProcessPainting()
    {
        if (!IsInitialized)
            return;
        if (_paintCanvas is null)
        {
            // Build once. A throw in here used to be retried every frame, which buried the
            // one real error under a wall of identical ones.
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
        // The preview camera is added without a name, so Godot calls it "@Camera3D@3" and a
        // name lookup finds nothing. Search by type instead.
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

        // The canvas exists before anything that binds to it: wiring a control to
        // _paintCanvas.Method while the field is still null throws on the delegate, not on use.
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
        controls.AddChild(_paintControls);

        // Both were appended after the preview, which puts them past the bottom of the
        // scrolling column. Paint controls belong directly above the buddy they act on.
        int previewIndex = previewContainer.GetIndex();
        controls.MoveChild(_paintModeButton, previewIndex);
        controls.MoveChild(_paintControls, previewIndex + 1);

        var toolRow = new HBoxContainer();
        _paintControls.AddChild(toolRow);
        Button brush = Button(PaintUiText.Get(PaintUiText.Brush), "PaintBrushButton");
        Button eraser = Button(PaintUiText.Get(PaintUiText.Eraser), "PaintEraserButton");
        brush.Pressed += () => { _paintCanvas.Workspace.SelectedTool = PaintTool.Brush; RefreshPaintStatus(); };
        eraser.Pressed += () => { _paintCanvas.Workspace.SelectedTool = PaintTool.Eraser; RefreshPaintStatus(); };
        toolRow.AddChild(brush);
        toolRow.AddChild(eraser);

        var color = new ColorPickerButton
        {
            Name = "PaintColorWheel",
            Color = Colors.White,
            TooltipText = PaintUiText.Get(PaintUiText.ColorTooltip),
        };
        color.ColorChanged += value =>
        {
            _paintCanvas.Workspace.SelectedColor = new PaintColor(
                (byte)Math.Clamp(Math.Round(value.R * 255), 0, 255),
                (byte)Math.Clamp(Math.Round(value.G * 255), 0, 255),
                (byte)Math.Clamp(Math.Round(value.B * 255), 0, 255));
        };
        toolRow.AddChild(color);

        var sizeRow = new HBoxContainer();
        _paintControls.AddChild(sizeRow);
        Button smaller = Button("−", "PaintSizeDecreaseButton");
        Button larger = Button("+", "PaintSizeIncreaseButton");
        smaller.Pressed += () => { _paintCanvas.Workspace.AdjustBrush(-1); RefreshPaintStatus(); };
        larger.Pressed += () => { _paintCanvas.Workspace.AdjustBrush(1); RefreshPaintStatus(); };
        _brushSize = new Label { Name = "PaintBrushSize" };
        sizeRow.AddChild(new Label { Text = PaintUiText.Get(PaintUiText.BrushSize) });
        sizeRow.AddChild(smaller);
        sizeRow.AddChild(_brushSize);
        sizeRow.AddChild(larger);

        var commandRow = new HBoxContainer();
        _paintControls.AddChild(commandRow);
        _undoPaintButton = Button(PaintUiText.Get(PaintUiText.Undo), "PaintUndoButton");
        _undoPaintButton.Pressed += () =>
        {
            if (_paintCanvas.Workspace.Undo()) QueueAllPaintTextures();
            RefreshPaintStatus();
        };
        commandRow.AddChild(_undoPaintButton);
        Button eraseAll = Button(PaintUiText.Get(PaintUiText.EraseAll), "PaintEraseAllButton");
        eraseAll.Pressed += () => _eraseAllConfirmation.PopupCentered();
        commandRow.AddChild(eraseAll);

        var viewRow = new HBoxContainer();
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
        };
        _paintControls.AddChild(_paintStatus);
        _paintControls.AddChild(new Label
        {
            Text = PaintUiText.Get(PaintUiText.InputHelp),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
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

    private void TogglePaintMode()
    {
        bool enabled = !_paintControls.Visible;
        _paintControls.Visible = enabled;
        _paintCanvas.Visible = enabled;
        _paintCanvas.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        _paintModeButton.Text = PaintUiText.Get(
            enabled ? PaintUiText.AppearanceControls : PaintUiText.Open);
        // Paint mode drives the shared preview camera, so leaving it restores the default framing.
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
        if (_paintCamera is null) return;
        _paintCamera.Size = (float)(PaintCanvasControl.BaseCameraSize / _paintCanvas.View.Zoom);
        // The canvas maps pointer positions in 2D world units (Y-down); 3D is Y-up.
        PaintPoint center = _paintCanvas.CameraCenter;
        _paintCamera.Position = new Vector3((float)center.X, (float)-center.Y, 600);
        RefreshPaintStatus();
    }

    private void RefreshPaintStatus()
    {
        if (_paintCanvas is null || _paintStatus is null) return;
        _brushSize.Text = _paintCanvas.Workspace.BrushDiameter.ToString();
        _undoPaintButton.Disabled = !_paintCanvas.Workspace.CanUndo;
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
