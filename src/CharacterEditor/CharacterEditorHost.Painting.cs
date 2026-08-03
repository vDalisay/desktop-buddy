using System;
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

    public bool IsPaintMode => _paintControls is not null && _paintControls.Visible;
    public PaintWorkspace PaintWorkspace => _paintCanvas.Workspace;

    private void ProcessPainting()
    {
        if (!IsInitialized)
            return;
        if (_paintCanvas is null)
        {
            TryAttachPaintingWorkspace();
            return;
        }
        _paintTextures.FlushFrame(_paintCanvas.Workspace.Surfaces);
    }

    private void TryAttachPaintingWorkspace()
    {
        if (FindChild("CharacterPreview", recursive: true, owned: false) is not SubViewportContainer preview ||
            preview.GetParent() is not VBoxContainer controls ||
            preview.FindChild("Camera3D", recursive: true, owned: false) is not Camera3D camera)
        {
            return;
        }
        BuildPaintingArea(controls, preview, camera);
    }

    private void BuildPaintingArea(VBoxContainer controls, SubViewportContainer previewContainer, Camera3D camera)
    {
        _paintCamera = camera;
        _paintTextures = new PaintTextureBridge(_preview);
        _paintModeButton = Button("Paint", "PaintModeButton");
        _paintModeButton.TooltipText = "Paint directly on the buddy body.";
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
        Button brush = Button("Brush", "PaintBrushButton");
        Button eraser = Button("Eraser", "PaintEraserButton");
        brush.Pressed += () => { _paintCanvas.Workspace.SelectedTool = PaintTool.Brush; RefreshPaintStatus(); };
        eraser.Pressed += () => { _paintCanvas.Workspace.SelectedTool = PaintTool.Eraser; RefreshPaintStatus(); };
        toolRow.AddChild(brush);
        toolRow.AddChild(eraser);

        var color = new ColorPickerButton
        {
            Name = "PaintColorWheel",
            Color = Colors.White,
            TooltipText = "Choose an opaque paint color.",
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
        sizeRow.AddChild(new Label { Text = "Brush size" });
        sizeRow.AddChild(smaller);
        sizeRow.AddChild(_brushSize);
        sizeRow.AddChild(larger);

        var commandRow = new HBoxContainer();
        _paintControls.AddChild(commandRow);
        _undoPaintButton = Button("Undo", "PaintUndoButton");
        _undoPaintButton.Pressed += () =>
        {
            if (_paintCanvas.Workspace.Undo()) QueueAllPaintTextures();
            RefreshPaintStatus();
        };
        commandRow.AddChild(_undoPaintButton);
        Button eraseAll = Button("Erase All", "PaintEraseAllButton");
        eraseAll.Pressed += () => _eraseAllConfirmation.PopupCentered();
        commandRow.AddChild(eraseAll);

        var viewRow = new HBoxContainer();
        _paintControls.AddChild(viewRow);
        Button zoomOut = Button("Zoom −", "PaintZoomOutButton");
        Button zoomIn = Button("Zoom +", "PaintZoomInButton");
        Button resetView = Button("Reset View", "PaintResetViewButton");
        zoomOut.Pressed += () => SetPaintZoom(_paintCanvas.View.Zoom - 0.2);
        zoomIn.Pressed += () => SetPaintZoom(_paintCanvas.View.Zoom + 0.2);
        resetView.Pressed += _paintCanvas.ResetView;
        viewRow.AddChild(zoomOut);
        viewRow.AddChild(zoomIn);
        viewRow.AddChild(resetView);

        _paintStatus = new Label { Name = "PaintHoverStatus", Text = "Move over a body part to paint." };
        _paintControls.AddChild(_paintStatus);
        _paintControls.AddChild(new Label
        {
            Text = "Left drag: paint • Wheel: brush size • Middle drag or Space+drag: pan • Ctrl+wheel: zoom",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        _paintCanvas = new PaintCanvasControl { Name = "CharacterPaintCanvas", Visible = false };
        if (previewContainer.GetChildOrNull<SubViewport>(0) is SubViewport viewport)
            _paintCanvas.ViewportSize = viewport.Size;
        _paintCanvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        previewContainer.AddChild(_paintCanvas);
        _paintCanvas.WorkspaceChanged += () => { QueueAllPaintTextures(); RefreshPaintStatus(); };
        _paintCanvas.ViewChanged += ApplyPaintView;
        _paintCanvas.HoverChanged += _ => RefreshPaintStatus();

        _eraseAllConfirmation = new ConfirmationDialog
        {
            Name = "PaintEraseAllConfirmation",
            Title = "Erase all paint?",
            DialogText = "This clears paint from all six body parts. You can undo it once confirmed.",
            OkButtonText = "Erase All",
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
        _paintModeButton.Text = enabled ? "Appearance Controls" : "Paint";
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
        string hovered = _paintCanvas.HoveredPart?.ToString() ?? "Canvas";
        _paintStatus.Text = $"{_paintCanvas.Workspace.SelectedTool} • {hovered} • Zoom {_paintCanvas.View.Zoom:0.0}×";
    }
}
