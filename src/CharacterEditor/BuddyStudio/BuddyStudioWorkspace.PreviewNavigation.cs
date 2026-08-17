using System;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

public partial class BuddyStudioWorkspace
{
    private const float AssetForgeShoesDefaultZoom = 0.78f;
    private const double PreviewTransitionSeconds = 0.14;
    private bool _assetForgeNavigationInstalled;
    private bool _assetForgeStudioPreviewApplied;
    private bool _assetForgePanning;
    private Vector2 _assetForgeViewPan;
    private CharacterFeatureSlot _assetForgeNavigationSlot = (CharacterFeatureSlot)(-1);
    private Tween? _previewViewTween;

    /// <summary>
    /// Presentation-only switch for the fast category/zoom interpolation. The shared Win98 motion
    /// preference wires this property later in the capture-polish pass; disabling it preserves the
    /// existing rigid/authentic Windows 98 response without changing any view state.
    /// </summary>
    public bool SmoothViewMotionEnabled { get; set; } = true;

    /// <summary>Called from the existing Asset Forge companion _Process hook.</summary>
    private void AssetForgeProcessNavigation()
    {
        if (!_assetForgeNavigationInstalled && IsInsideTree() && GodotObject.IsInstanceValid(_previewInput))
            InstallAssetForgePreviewNavigation();

        if (_assetForgeNavigationSlot != _slot)
        {
            _assetForgeNavigationSlot = _slot;
            _assetForgeViewPan = Vector2.Zero;
            _viewZoom = AssetForgeDefaultViewZoom();
            ApplyAssetForgeView(animate: _assetForgeStudioPreviewApplied);
        }

        if (GodotObject.IsInstanceValid(_previewRig) && _assetForgeStudioPreviewApplied != _previewAttached)
        {
            _assetForgeViewPan = Vector2.Zero;
            _assetForgePanning = false;
            _previewInput.MouseDefaultCursorShape = CursorShape.Arrow;
            _previewRig!.SetStudioPreviewMode(_previewAttached);
            _assetForgeStudioPreviewApplied = _previewAttached;
            if (_previewAttached)
            {
                _viewZoom = AssetForgeDefaultViewZoom();
                ApplyAssetForgeView(animate: false);
            }
            else
            {
                StopPreviewViewTween();
            }
        }
    }

    private void InstallAssetForgePreviewNavigation()
    {
        ReplaceViewButton("BuddyStudioZoomOut", "Zoom −", "Zoom out without a fixed limit.", () => AssetForgeZoomBy(1f / 1.25f));
        ReplaceViewButton("BuddyStudioZoomIn", "Zoom +", "Zoom in without a fixed limit.", () => AssetForgeZoomBy(1.25f));
        ReplaceViewButton("BuddyStudioResetView", "Reset View", "Reset zoom and pan for this category.", AssetForgeResetView);

        _previewInput.GuiInput += OnAssetForgePreviewNavigationInput;
        TreeExiting += () =>
        {
            StopPreviewViewTween();
            if (GodotObject.IsInstanceValid(_previewRig)) _previewRig!.SetStudioPreviewMode(false);
        };
        _assetForgeNavigationInstalled = true;
    }

    private void ReplaceViewButton(string name, string text, string tooltip, Action action)
    {
        Button? old = FindChild(name, recursive: true, owned: false) as Button;
        if (!GodotObject.IsInstanceValid(old) || old!.GetParent() is not Control parent) return;
        int index = old.GetIndex();
        parent.RemoveChild(old);
        old.QueueFree();
        var replacement = new Button
        {
            Name = name,
            Text = text,
            TooltipText = tooltip,
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(0, 30),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        replacement.Pressed += action;
        parent.AddChild(replacement);
        parent.MoveChild(replacement, Math.Min(index, parent.GetChildCount() - 1));
    }

    private void OnAssetForgePreviewNavigationInput(InputEvent input)
    {
        if (!_previewAttached || _moveMode) return;

        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
            {
                AssetForgeZoomBy(1.15f);
                _previewInput.AcceptEvent();
                return;
            }
            if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
            {
                AssetForgeZoomBy(1f / 1.15f);
                _previewInput.AcceptEvent();
                return;
            }
            if (button.ButtonIndex == MouseButton.Middle)
            {
                _assetForgePanning = button.Pressed;
                _previewInput.MouseDefaultCursorShape = _assetForgePanning ? CursorShape.Move : CursorShape.Arrow;
                _previewInput.AcceptEvent();
                return;
            }
        }

        if (_assetForgePanning && input is InputEventMouseMotion motion)
        {
            float worldPerPixel = _previewCamera.Size / Math.Max(1f, _previewInput.Size.Y);
            _assetForgeViewPan += new Vector2(-motion.Relative.X, motion.Relative.Y) * worldPerPixel;
            // Direct manipulation should stay directly under the pointer; only discrete zoom,
            // reset and category framing receive the short presentation tween.
            ApplyAssetForgeView(animate: false);
            _previewInput.AcceptEvent();
        }
    }

    private void AssetForgeZoomBy(float factor)
    {
        if (!float.IsFinite(factor) || factor <= 0f) return;
        _viewZoom = Math.Max(0.0001f, _viewZoom * factor);
        ApplyAssetForgeView();
    }

    private void AssetForgeResetView()
    {
        _viewZoom = AssetForgeDefaultViewZoom();
        _assetForgeViewPan = Vector2.Zero;
        ApplyAssetForgeView();
    }

    private float AssetForgeDefaultViewZoom() =>
        _slot == CharacterFeatureSlot.Shoes ? AssetForgeShoesDefaultZoom : 1f;

    private void ApplyAssetForgeView(bool animate = true)
    {
        if (!_previewAttached || !GodotObject.IsInstanceValid(_previewCamera)) return;
        ViewFrame frame = FrameFor(_slot);
        Vector2 focus = frame.Focus + _assetForgeViewPan;
        Vector3 targetPosition = new(focus.X, focus.Y, _cameraHomePosition.Z);
        float targetSize = Math.Max(0.001f, frame.Size / _viewZoom);

        StopPreviewViewTween();
        if (!animate || !SmoothViewMotionEnabled || !IsInsideTree())
        {
            _previewCamera.Position = targetPosition;
            _previewCamera.Size = targetSize;
            return;
        }

        // Retarget from the camera's current presentation every time. Rapid wheel input therefore
        // never queues old animations and remains responsive while still reading smoothly in capture.
        _previewViewTween = CreateTween();
        _previewViewTween.SetParallel(true);
        _previewViewTween.TweenProperty(_previewCamera, "position", targetPosition, PreviewTransitionSeconds)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _previewViewTween.TweenProperty(_previewCamera, "size", targetSize, PreviewTransitionSeconds)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
    }

    private void StopPreviewViewTween()
    {
        if (_previewViewTween is not null && GodotObject.IsInstanceValid(_previewViewTween))
            _previewViewTween.Kill();
        _previewViewTween = null;
    }
}
