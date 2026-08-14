using System;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

public partial class BuddyStudioWorkspace
{
    private bool _assetForgeNavigationInstalled;
    private bool _assetForgeStudioPreviewApplied;
    private bool _assetForgePanning;
    private Vector2 _assetForgeViewPan;
    private CharacterFeatureSlot _assetForgeNavigationSlot = (CharacterFeatureSlot)(-1);

    /// <summary>Called from the existing Asset Forge companion _Process hook.</summary>
    private void AssetForgeProcessNavigation()
    {
        if (!_assetForgeNavigationInstalled && IsInsideTree() && GodotObject.IsInstanceValid(_previewInput))
            InstallAssetForgePreviewNavigation();

        if (_assetForgeNavigationSlot != _slot)
        {
            _assetForgeNavigationSlot = _slot;
            _assetForgeViewPan = Vector2.Zero;
        }

        if (GodotObject.IsInstanceValid(_previewRig) && _assetForgeStudioPreviewApplied != _previewAttached)
        {
            _assetForgeViewPan = Vector2.Zero;
            _assetForgePanning = false;
            _previewInput.MouseDefaultCursorShape = CursorShape.Arrow;
            _previewRig!.SetStudioPreviewMode(_previewAttached);
            _assetForgeStudioPreviewApplied = _previewAttached;
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
            ApplyAssetForgeView();
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
        _viewZoom = 1f;
        _assetForgeViewPan = Vector2.Zero;
        ApplyAssetForgeView();
    }

    private void ApplyAssetForgeView()
    {
        if (!_previewAttached || !GodotObject.IsInstanceValid(_previewCamera)) return;
        ViewFrame frame = FrameFor(_slot);
        Vector2 focus = frame.Focus + _assetForgeViewPan;
        _previewCamera.Position = new Vector3(focus.X, focus.Y, _cameraHomePosition.Z);
        _previewCamera.Size = Math.Max(0.001f, frame.Size / _viewZoom);
    }
}
