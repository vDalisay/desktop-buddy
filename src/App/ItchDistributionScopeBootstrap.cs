using System;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Applies distribution-only shell removals that cannot be expressed by catalogue filtering.
/// The itch build intentionally has no Work Mode: remove its hotkey, status autoload and both
/// legacy/current shell buttons. Browser-WASM also owns a runtime-readiness watchdog: a canvas
/// is not considered healthy until the shipping 3D presentation, room bounds and command bar
/// have all actually composed.
/// </summary>
public sealed partial class ItchDistributionScopeBootstrap : Node
{
    private const ulong BrowserBootTimeoutMsec = 15_000;
    private const ulong BrowserRuntimeTimeoutMsec = 12_000;
    private const ulong BrowserRuntimeTimeoutFrames = 900;

    private bool _workCommandRemoved;
    private bool _legacyWorkCommandRemoved;
    private bool _browserBootWatchdogArmed;
    private ulong _browserBootDeadlineMsec;
    private bool _browserRuntimeWatchdogArmed;
    private ulong _browserRuntimeDeadlineMsec;
    private ulong _browserRuntimeFrame;
    private Vector2I _lastBrowserViewportSize = new(-1, -1);
    private bool _browserRuntimeReadyReported;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (!DemoScope.IsItchIo)
        {
            SetProcess(false);
            return;
        }

        if (InputMap.HasAction("toggle_input_mode"))
            InputMap.ActionEraseEvents("toggle_input_mode");

        Node? milestoneBootstrap = GetNodeOrNull<Node>("/root/WorkMilestoneProgressBootstrap");
        if (GodotObject.IsInstanceValid(milestoneBootstrap))
            milestoneBootstrap.QueueFree();

        if (OperatingSystem.IsBrowser())
        {
            ulong now = Time.GetTicksMsec();
            _browserBootWatchdogArmed = true;
            _browserBootDeadlineMsec = now + BrowserBootTimeoutMsec;
            _browserRuntimeWatchdogArmed = true;
            _browserRuntimeDeadlineMsec = now + BrowserRuntimeTimeoutMsec;
        }
    }

    public override void _Process(double delta)
    {
        if (!_workCommandRemoved)
            _workCommandRemoved = HideControl("Win98WorkCommand");
        if (!_legacyWorkCommandRemoved)
            _legacyWorkCommandRemoved = HideControl("DockInteractionModeButton");

        if (OperatingSystem.IsBrowser())
            MaintainBrowserRuntime();

        if (_browserBootWatchdogArmed)
        {
            if (HasBootedSandbox())
            {
                _browserBootWatchdogArmed = false;
            }
            else if (Time.GetTicksMsec() >= _browserBootDeadlineMsec)
            {
                _browserBootWatchdogArmed = false;
                throw new InvalidOperationException(
                    "RuntimeError: Desktop Buddy browser boot did not attach SandboxRoot within 15 seconds. " +
                    "Treat the Web smoke test as failed even if the Godot canvas exists.");
            }
        }

        // Native itch builds can go idle once their one-shot removals are done. Browser play
        // remains live because the DOM canvas can resize independently of Godot's desktop
        // Window abstraction (itch iframe resize, DevTools docking, browser resize).
        if (_workCommandRemoved && _legacyWorkCommandRemoved && !_browserBootWatchdogArmed &&
            !OperatingSystem.IsBrowser())
        {
            SetProcess(false);
        }
    }

    private void MaintainBrowserRuntime()
    {
        SandboxRoot? sandbox = GetTree().Root.FindChild("Sandbox", true, false) as SandboxRoot;
        if (!GodotObject.IsInstanceValid(sandbox))
            return;

        _browserRuntimeFrame++;
        Vector2I expectedRoom = ResolveExpectedBrowserRoom(sandbox!);
        if (sandbox!.Boundaries.IsInitialized && expectedRoom.X > 0 && expectedRoom.Y > 0)
        {
            RoomLayout current = sandbox.Boundaries.CurrentLayout;
            bool roomMatches = current.ClientWidth == expectedRoom.X &&
                               current.ClientHeight == expectedRoom.Y;
            if (!roomMatches && expectedRoom != _lastBrowserViewportSize)
            {
                _lastBrowserViewportSize = expectedRoom;
                double storedZoom = sandbox.Shell.CurrentLocalSettings.ZoomPercent / 100.0;
                sandbox.Boundaries.RequestLayout(expectedRoom, storedZoom);
                GD.Print($"DESKTOP_BUDDY_WEB_ROOM_REQUEST:{expectedRoom.X}x{expectedRoom.Y}");
            }
            else if (roomMatches)
            {
                _lastBrowserViewportSize = expectedRoom;
            }
        }

        bool lifecycleReady = GodotObject.IsInstanceValid(sandbox.Lifecycle);
        bool trayReady = GodotObject.IsInstanceValid(sandbox.TrayCommands);
        bool visualInitialized = GodotObject.IsInstanceValid(sandbox.VisualPresenter) &&
                                 sandbox.VisualPresenter.IsInitialized;
        bool legacyVisible = AnyLegacyBuddyPartVisible(sandbox);

        // Only reconcile after the root has reached its late composition seam. Calling the
        // presentation switch earlier can touch presenters that SandboxRoot has not initialized
        // yet and would hide the exception we are trying to diagnose.
        if (lifecycleReady && trayReady && visualInitialized &&
            (sandbox.Mode != PresentationMode.Mii3D || !sandbox.VisualPresenter.Visible || legacyVisible))
        {
            sandbox.SetPresentationMode(PresentationMode.Mii3D);
            legacyVisible = AnyLegacyBuddyPartVisible(sandbox);
        }

        CharacterSelectionRuntime? selectionRuntime =
            sandbox.GetNodeOrNull<CharacterSelectionRuntime>(nameof(CharacterSelectionRuntime));
        CharacterEditorHost? host =
            sandbox.GetNodeOrNull<CharacterEditorHost>(nameof(CharacterEditorHost));
        Control? commandBar = GetTree().Root.FindChild("Win98CommandBar", true, false) as Control;

        bool roomReady = sandbox.Boundaries.IsInitialized && expectedRoom.X > 0 && expectedRoom.Y > 0 &&
                         sandbox.Boundaries.CurrentLayout.ClientWidth == expectedRoom.X &&
                         sandbox.Boundaries.CurrentLayout.ClientHeight == expectedRoom.Y;
        bool presentationReady = visualInitialized && sandbox.VisualPresenter.Visible &&
                                 sandbox.Mode == PresentationMode.Mii3D && !legacyVisible;
        bool characterRuntimeReady = selectionRuntime?.Coordinator is not null;
        bool characterUiReady = host is { IsInitialized: true };
        bool commandBarReady = GodotObject.IsInstanceValid(commandBar) && commandBar!.Visible;

        bool ready = lifecycleReady && trayReady && roomReady && presentationReady &&
                     characterRuntimeReady && characterUiReady && commandBarReady;
        if (ready)
        {
            _browserRuntimeWatchdogArmed = false;
            if (!_browserRuntimeReadyReported)
            {
                _browserRuntimeReadyReported = true;
                GD.Print(
                    $"DESKTOP_BUDDY_WEB_RUNTIME_READY room={expectedRoom.X}x{expectedRoom.Y} " +
                    "presentation=Mii3D characterUi=True commandBar=True");
            }
            return;
        }

        if (_browserRuntimeFrame is 1 or 30 or 120 or 300 or 600)
        {
            GD.Print(
                $"DESKTOP_BUDDY_WEB_RUNTIME_WAIT frame={_browserRuntimeFrame} " +
                $"lifecycleReady={lifecycleReady} trayReady={trayReady} " +
                $"visualInitialized={visualInitialized} visualVisible={sandbox.VisualPresenter.Visible} " +
                $"mode={sandbox.Mode} legacyVisible={legacyVisible} roomReady={roomReady} " +
                $"room={sandbox.Boundaries.CurrentLayout.ClientWidth}x{sandbox.Boundaries.CurrentLayout.ClientHeight} " +
                $"expectedRoom={expectedRoom.X}x{expectedRoom.Y} " +
                $"characterRuntimeReady={characterRuntimeReady} hostPresent={host is not null} " +
                $"characterUiReady={characterUiReady} commandBarReady={commandBarReady}");
        }

        if (!_browserRuntimeWatchdogArmed)
            return;

        // The experimental browser runtime has shown that Time.GetTicksMsec can fail to
        // advance consistently even while render-frame callbacks keep running. Keep the
        // monotonic deadline for normal browsers, but also enforce the watchdog by rendered
        // frames so CI can never sit on a healthy-looking canvas without explaining which
        // shipping-surface condition is still missing.
        bool timedOut = Time.GetTicksMsec() >= _browserRuntimeDeadlineMsec ||
                        _browserRuntimeFrame >= BrowserRuntimeTimeoutFrames;
        if (!timedOut)
            return;

        _browserRuntimeWatchdogArmed = false;
        throw new InvalidOperationException(
            "RuntimeError: Desktop Buddy browser runtime did not reach the shipping itch surface " +
            $"within {BrowserRuntimeTimeoutMsec / 1000} seconds / {BrowserRuntimeTimeoutFrames} rendered frames. " +
            $"lifecycleReady={lifecycleReady} trayReady={trayReady} " +
            $"visualInitialized={visualInitialized} visualVisible={sandbox.VisualPresenter.Visible} " +
            $"mode={sandbox.Mode} legacyVisible={legacyVisible} roomReady={roomReady} " +
            $"room={sandbox.Boundaries.CurrentLayout.ClientWidth}x{sandbox.Boundaries.CurrentLayout.ClientHeight} " +
            $"expectedRoom={expectedRoom.X}x{expectedRoom.Y} " +
            $"characterRuntimeReady={characterRuntimeReady} hostPresent={host is not null} " +
            $"characterUiReady={characterUiReady} commandBarReady={commandBarReady}.");
    }

    private static Vector2I ResolveExpectedBrowserRoom(SandboxRoot sandbox)
    {
        Vector2 visible = sandbox.GetViewport().GetVisibleRect().Size;
        var client = new Vector2I(
            Math.Max(1, (int)Math.Round(visible.X)),
            Math.Max(1, (int)Math.Round(visible.Y)));

        if (sandbox.Window.LayoutMode == WindowLayoutMode.Compact &&
            !sandbox.Window.WorkCompanionActive)
        {
            client.Y -= Win98ThemeFactory.ChromeHeight;
        }

        if (client.X < RoomLayoutPolicy.MinimumRoomWidth ||
            client.Y < RoomLayoutPolicy.MinimumRoomHeight)
        {
            client = new Vector2I(
                RoomLayoutPolicy.DefaultClientWidth,
                RoomLayoutPolicy.DefaultClientHeight);
        }

        return client;
    }

    private static bool AnyLegacyBuddyPartVisible(SandboxRoot sandbox)
    {
        foreach (var part in sandbox.Buddy.Rig.Parts)
        {
            if (part.Visible)
                return true;
        }
        return false;
    }

    private bool HasBootedSandbox()
    {
        Node? bootstrap = GetNodeOrNull<Node>("/root/Bootstrap");
        if (!GodotObject.IsInstanceValid(bootstrap))
            return false;

        foreach (Node child in bootstrap.GetChildren())
        {
            if (child is SandboxRoot)
                return true;
        }

        return false;
    }

    private bool HideControl(string nodeName)
    {
        Control? control = GetTree().Root.FindChild(nodeName, true, false) as Control;
        if (!GodotObject.IsInstanceValid(control))
            return false;

        control.Visible = false;
        control.MouseFilter = Control.MouseFilterEnum.Ignore;
        control.FocusMode = Control.FocusModeEnum.None;
        return true;
    }
}
