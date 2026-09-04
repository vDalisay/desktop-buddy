using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

public partial class BuddyStudioWorkspace
{
    private const float AssetForgeShoesDefaultZoom = 0.78f;
    private const double PreviewTransitionSeconds = 0.14;
    private bool _assetForgeNavigationInstalled;
    private bool _assetForgeStudioPreviewApplied;
    private bool _assetForgePanning;
    private bool _previewSpinning;
    private float _previewSpinDegrees;
    private Vector2 _assetForgeViewPan;
    private CharacterFeatureSlot _assetForgeNavigationSlot = (CharacterFeatureSlot)(-1);
    private Tween? _previewViewTween;

    /// <summary>
    /// Presentation-only local kill switch used by focused tests. The persisted Win98 motion
    /// preference and Reduced Motion policy are evaluated in addition to this switch.
    /// </summary>
    public bool SmoothViewMotionEnabled { get; set; } = true;

    /// <summary>Called from the existing Asset Forge companion _Process hook.</summary>
    private void AssetForgeProcessNavigation()
    {
        if (!_assetForgeNavigationInstalled && IsInsideTree() && GodotObject.IsInstanceValid(_previewInput))
            InstallAssetForgePreviewNavigation();

        ApplyPreviewSpin();

        if (_assetForgeNavigationSlot != _slot)
        {
            _assetForgeNavigationSlot = _slot;
            _assetForgeViewPan = Vector2.Zero;
            _previewSpinDegrees = 0f;
            _viewZoom = AssetForgeDefaultViewZoom();
            ApplyAssetForgeView(animate: _assetForgeStudioPreviewApplied);
        }

        if (GodotObject.IsInstanceValid(_previewRig) && _assetForgeStudioPreviewApplied != _previewAttached)
        {
            _assetForgeViewPan = Vector2.Zero;
            _assetForgePanning = false;
            _previewSpinning = false;
            _previewSpinDegrees = 0f;
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
            if (button.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                _previewSpinning = button.Pressed;
                _previewInput.MouseDefaultCursorShape =
                    _previewSpinning ? CursorShape.Hsize : CursorShape.Arrow;
                _previewInput.AcceptEvent();
                return;
            }
        }

        if (_previewSpinning && input is InputEventMouseMotion spin)
        {
            _previewSpinDegrees = Mathf.Wrap(
                _previewSpinDegrees + (spin.Relative.X * SpinDegreesPerPixel), -180f, 180f);
            ApplyPreviewSpin();
            _previewInput.AcceptEvent();
            return;
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
        _previewSpinDegrees = 0f;
        ApplyPreviewSpin();
        ApplyAssetForgeView();
    }

    /// <summary>How far a rotisserie drag turns the buddy per pixel of pointer travel.</summary>
    private const float SpinDegreesPerPixel = 0.55f;

    /// <summary>
    /// Turns the previewed buddy on the spot so the player can look at the back of a hat or the
    /// side of an ear (owner instruction 2026-08-21). Written every frame rather than once on
    /// release: the rig rebuilds its own transform whenever the appearance changes, and a spin
    /// that silently snapped back on the next equip would read as a bug.
    /// </summary>
    private void ApplyPreviewSpin()
    {
        if (!_previewAttached || !GodotObject.IsInstanceValid(_previewRig))
            return;

        Vector3 rotation = _previewRig!.RotationDegrees;
        if (!Mathf.IsEqualApprox(rotation.Y, _previewSpinDegrees))
            _previewRig.SetPreviewYawDegrees(_previewSpinDegrees);
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
        if (!animate || !AllowsSmoothPreviewMotion() || !IsInsideTree())
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

    private bool AllowsSmoothPreviewMotion()
    {
        if (!SmoothViewMotionEnabled || !IsInsideTree())
            return false;

        SandboxRoot? sandbox = GetTree().Root.FindChild(
            nameof(SandboxRoot), recursive: true, owned: false) as SandboxRoot;
        return !GodotObject.IsInstanceValid(sandbox) ||
               Win98MotionPolicy.Allows(sandbox!.Shell.CurrentLocalSettings);
    }

    private void StopPreviewViewTween()
    {
        if (_previewViewTween is not null && GodotObject.IsInstanceValid(_previewViewTween))
            _previewViewTween.Kill();
        _previewViewTween = null;
    }
}
