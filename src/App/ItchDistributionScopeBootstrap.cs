using System;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Applies distribution-only shell removals that cannot be expressed by catalogue filtering.
/// The itch build intentionally has no Work Mode: remove its hotkey, status autoload and both
/// legacy/current shell buttons. In browser-WASM it also keeps a small startup watchdog alive so
/// CI cannot mistake an allocated but permanently grey canvas for a successful game boot.
/// </summary>
public sealed partial class ItchDistributionScopeBootstrap : Node
{
    private const ulong BrowserBootTimeoutMsec = 15_000;

    private bool _workCommandRemoved;
    private bool _legacyWorkCommandRemoved;
    private bool _browserBootWatchdogArmed;
    private ulong _browserBootDeadlineMsec;
    private Vector2I _lastBrowserViewportSize = new(-1, -1);
    private bool _browserPresentationReported;

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
            _browserBootWatchdogArmed = true;
            _browserBootDeadlineMsec = Time.GetTicksMsec() + BrowserBootTimeoutMsec;
        }
    }

    public override void _Process(double delta)
    {
        if (!_workCommandRemoved)
            _workCommandRemoved = HideControl("Win98WorkCommand");
        if (!_legacyWorkCommandRemoved)
            _legacyWorkCommandRemoved = HideControl("DockInteractionModeButton");

        if (OperatingSystem.IsBrowser())
            MaintainBrowserSandbox();

        if (_browserBootWatchdogArmed)
        {
            if (HasBootedSandbox())
            {
                _browserBootWatchdogArmed = false;
            }
            else if (Time.GetTicksMsec() >= _browserBootDeadlineMsec)
            {
                _browserBootWatchdogArmed = false;
                GD.PushError(
                    "RuntimeError: Desktop Buddy browser boot did not attach SandboxRoot within 15 seconds. " +
                    "Treat the Web smoke test as failed even if the Godot canvas exists.");
            }
        }

        // Native itch builds can go idle once their one-shot removals are done. Browser play
        // keeps this node alive because the DOM canvas can resize independently of Godot's
        // desktop Window abstraction (itch iframe resize, DevTools docking, browser resize).
        if (_workCommandRemoved && _legacyWorkCommandRemoved && !_browserBootWatchdogArmed &&
            !OperatingSystem.IsBrowser())
        {
            SetProcess(false);
        }
    }

    private void MaintainBrowserSandbox()
    {
        SandboxRoot? sandbox = GetTree().Root.FindChild("Sandbox", true, false) as SandboxRoot;
        if (!GodotObject.IsInstanceValid(sandbox) || !sandbox!.Boundaries.IsInitialized)
            return;

        // The shipping game is Mii3D. The experimental Web/AOT path has been observed to
        // materialize the exported enum at its zero value (LegacyCircles), despite the C#
        // initializer. Make the itch browser contract explicit instead of relying on that
        // experimental property-default path. The underlying 2D bodies remain the simulation;
        // only their presentation is switched, exactly as on the native shipping build.
        if (sandbox.Mode != PresentationMode.Mii3D)
            sandbox.SetPresentationMode(PresentationMode.Mii3D);
        if (!_browserPresentationReported)
        {
            _browserPresentationReported = true;
            GD.Print("DESKTOP_BUDDY_WEB_PRESENTATION:Mii3D");
        }

        Vector2 visible = sandbox.GetViewport().GetVisibleRect().Size;
        var viewportSize = new Vector2I(
            Math.Max(1, (int)Math.Round(visible.X)),
            Math.Max(1, (int)Math.Round(visible.Y)));
        if (viewportSize == _lastBrowserViewportSize)
            return;
        _lastBrowserViewportSize = viewportSize;

        // Native DesktopWindowController owns an OS window rect. In Web there is only the DOM
        // canvas, and its dimensions can change without a meaningful desktop-window resize.
        // Feed the real canvas size directly back into the same room-layout policy used by
        // native Compact mode so floor, walls, grab containment and both cameras fill the page.
        Vector2I roomClientSize = viewportSize;
        if (sandbox.Window.LayoutMode == WindowLayoutMode.Compact &&
            !sandbox.Window.WorkCompanionActive)
        {
            roomClientSize.Y -= Win98ThemeFactory.ChromeHeight;
        }

        if (roomClientSize.X < RoomLayoutPolicy.MinimumRoomWidth ||
            roomClientSize.Y < RoomLayoutPolicy.MinimumRoomHeight)
        {
            roomClientSize = new Vector2I(
                RoomLayoutPolicy.DefaultClientWidth,
                RoomLayoutPolicy.DefaultClientHeight);
        }

        double storedZoom = sandbox.Shell.CurrentLocalSettings.ZoomPercent / 100.0;
        sandbox.Boundaries.RequestLayout(roomClientSize, storedZoom);
        GD.Print(
            $"DESKTOP_BUDDY_WEB_ROOM_REQUEST:{roomClientSize.X}x{roomClientSize.Y} " +
            $"viewport={viewportSize.X}x{viewportSize.Y}");
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
