using System;
using System.Threading.Tasks;
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
    private const ulong BrowserPaintSmokeTimeoutFrames = 2_400;

    private bool _workCommandRemoved;
    private bool _legacyWorkCommandRemoved;
    private bool _browserBootWatchdogArmed;
    private ulong _browserBootDeadlineMsec;
    private bool _browserRuntimeWatchdogArmed;
    private ulong _browserRuntimeDeadlineMsec;
    private ulong _browserRuntimeFrame;
    private Vector2I _lastBrowserViewportSize = new(-1, -1);
    private bool _browserRuntimeReadyReported;
    private BrowserWasmProcessSynchronizationContext? _browserSynchronizationContext;
    private bool _browserChromeGlyphsNormalized;
    private bool _browserPaintGlyphsNormalized;
    private bool _browserPaintSmokeEnabled;
    private BrowserPaintSmokeStage _browserPaintSmokeStage;
    private Task? _browserPaintOpenTask;
    private ulong _browserPaintSmokeFrame;
    private int _browserPaintSettleFrames;

    private enum BrowserPaintSmokeStage
    {
        None,
        OpeningFirstEditor,
        WaitingForFirstSave,
        WaitingForNewCharacterSave,
        WaitingForUseClose,
        SettlingAfterUse,
        OpeningDirtyExitEditor,
        WaitingForDirtyExitPrompt,
        WaitingForUnsavedSaveClose,
        Complete,
        Failed,
    }

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
            _browserPaintSmokeEnabled = BrowserPaintSmokeRequested();
            if (_browserPaintSmokeEnabled)
                GD.Print("DESKTOP_BUDDY_WEB_PAINT_SMOKE_ARMED");
        }
    }

    public override void _Process(double delta)
    {
        if (_browserSynchronizationContext is not null)
        {
            _browserSynchronizationContext.Install();
            _browserSynchronizationContext.Drain();
        }

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
            EnsureBrowserSynchronizationContext();
            NormalizeBrowserGlyphs(host!);
            RunBrowserPaintSmoke(host!);

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

    private void EnsureBrowserSynchronizationContext()
    {
        if (_browserSynchronizationContext is null)
        {
            _browserSynchronizationContext = new BrowserWasmProcessSynchronizationContext();
            GD.Print("DESKTOP_BUDDY_WEB_PROCESS_SYNC_CONTEXT_READY");
        }

        _browserSynchronizationContext.Install();
        _browserSynchronizationContext.Drain();
    }

    private void NormalizeBrowserGlyphs(CharacterEditorHost host)
    {
        if (!_browserChromeGlyphsNormalized)
        {
            bool paintReady = false;
            bool maximizeReady = false;

            if (GetTree().Root.FindChild("Win98PaintCommand", true, false) is MenuButton paint)
            {
                paint.Text = "Paint >";
                paintReady = true;
            }

            Win98WindowFrame? frame =
                GetTree().Root.FindChild(nameof(Win98WindowFrame), true, false) as Win98WindowFrame;
            if (GodotObject.IsInstanceValid(frame))
            {
                foreach (Node child in frame!.TitleBarCommands.GetChildren())
                {
                    if (child is Button button &&
                        string.Equals(button.TooltipText, "Maximize or restore", StringComparison.Ordinal))
                    {
                        button.Text = "[]";
                        maximizeReady = true;
                        break;
                    }
                }
            }

            _browserChromeGlyphsNormalized = paintReady && maximizeReady;
            if (_browserChromeGlyphsNormalized)
                GD.Print("DESKTOP_BUDDY_WEB_ASCII_CHROME_GLYPHS_READY");
        }

        if (!_browserPaintGlyphsNormalized && host.IsEditorOpen &&
            host.FindChild("PaintRotateRow", true, false) is HBoxContainer rotateRow &&
            rotateRow.GetChildCount() >= 2 &&
            rotateRow.GetChild(0) is Button rotateLeft &&
            rotateRow.GetChild(1) is Button rotateRight)
        {
            rotateLeft.Text = "<";
            rotateRight.Text = ">";
            _browserPaintGlyphsNormalized = true;
            GD.Print("DESKTOP_BUDDY_WEB_ASCII_PAINT_GLYPHS_READY");
        }
    }

    private void RunBrowserPaintSmoke(CharacterEditorHost host)
    {
        if (!_browserPaintSmokeEnabled ||
            _browserPaintSmokeStage is BrowserPaintSmokeStage.Complete or BrowserPaintSmokeStage.Failed)
        {
            return;
        }

        _browserPaintSmokeFrame++;
        if (_browserPaintSmokeFrame >= BrowserPaintSmokeTimeoutFrames)
        {
            FailBrowserPaintSmoke("Timed out while exercising Paint Buddy Save/Use/Exit.");
            return;
        }

        try
        {
            switch (_browserPaintSmokeStage)
            {
                case BrowserPaintSmokeStage.None:
                    GD.Print("DESKTOP_BUDDY_WEB_PAINT_SMOKE:open-first");
                    _browserPaintOpenTask = host.OpenWin98PaintEditorAsync();
                    _browserPaintSmokeStage = BrowserPaintSmokeStage.OpeningFirstEditor;
                    break;

                case BrowserPaintSmokeStage.OpeningFirstEditor:
                    if (!BrowserOpenCompleted(host))
                        break;
                    if (FindVisibleButton(host, "SaveCharacterButton") is not Button firstSave)
                        break;
                    GD.Print("DESKTOP_BUDDY_WEB_PAINT_SMOKE:save-built-in");
                    firstSave.EmitSignal(Button.SignalName.Pressed);
                    _browserPaintSmokeStage = BrowserPaintSmokeStage.WaitingForFirstSave;
                    break;

                case BrowserPaintSmokeStage.WaitingForFirstSave:
                    if (host.Session.IsDirty)
                        break;
                    CharacterEditorActionResult created = host.Session.NewCharacter("Browser Smoke Character");
                    if (!created.Completed)
                    {
                        FailBrowserPaintSmoke(created.Detail ?? "Could not create a second smoke-test character.");
                        break;
                    }
                    if (FindVisibleButton(host, "SaveCharacterButton") is not Button newSave)
                        break;
                    GD.Print("DESKTOP_BUDDY_WEB_PAINT_SMOKE:save-new-character");
                    newSave.EmitSignal(Button.SignalName.Pressed);
                    _browserPaintSmokeStage = BrowserPaintSmokeStage.WaitingForNewCharacterSave;
                    break;

                case BrowserPaintSmokeStage.WaitingForNewCharacterSave:
                    if (host.Session.IsDirty)
                        break;
                    if (host.Session.SelectedCharacterId is not Guid selected ||
                        !host.Session.CurrentPage.Exists(entry => entry.CharacterId == selected))
                    {
                        break;
                    }
                    if (FindVisibleButton(host, "UseCharacterButton") is not Button use)
                        break;
                    GD.Print("DESKTOP_BUDDY_WEB_PAINT_SMOKE:use-character");
                    use.EmitSignal(Button.SignalName.Pressed);
                    _browserPaintSmokeStage = BrowserPaintSmokeStage.WaitingForUseClose;
                    break;

                case BrowserPaintSmokeStage.WaitingForUseClose:
                    if (host.IsEditorOpen)
                        break;
                    _browserPaintSettleFrames = 8;
                    _browserPaintSmokeStage = BrowserPaintSmokeStage.SettlingAfterUse;
                    GD.Print("DESKTOP_BUDDY_WEB_PAINT_SMOKE:use-closed");
                    break;

                case BrowserPaintSmokeStage.SettlingAfterUse:
                    if (_browserPaintSettleFrames-- > 0)
                        break;
                    GD.Print("DESKTOP_BUDDY_WEB_PAINT_SMOKE:open-dirty-exit");
                    _browserPaintOpenTask = host.OpenWin98PaintEditorAsync();
                    _browserPaintSmokeStage = BrowserPaintSmokeStage.OpeningDirtyExitEditor;
                    break;

                case BrowserPaintSmokeStage.OpeningDirtyExitEditor:
                    if (!BrowserOpenCompleted(host))
                        break;
                    NormalizeBrowserGlyphs(host);
                    if (!_browserChromeGlyphsNormalized || !_browserPaintGlyphsNormalized)
                        break;
                    if (host.FindChild("PaintRotateRow", true, false) is HBoxContainer smokeRotate &&
                        smokeRotate.GetChildCount() >= 2 &&
                        smokeRotate.GetChild(0) is Button smokeLeft &&
                        smokeRotate.GetChild(1) is Button smokeRight)
                    {
                        smokeRight.EmitSignal(Button.SignalName.Pressed);
                        smokeLeft.EmitSignal(Button.SignalName.Pressed);
                    }
                    CharacterEditorActionResult renamed = host.Session.Rename("Browser Smoke Character Edited");
                    if (!renamed.Completed || !host.Session.IsDirty)
                    {
                        FailBrowserPaintSmoke(renamed.Detail ?? "Could not dirty the smoke-test character before Exit.");
                        break;
                    }
                    if (FindVisibleButton(host, "CloseCharacterEditorButton") is not Button exit)
                        break;
                    GD.Print("DESKTOP_BUDDY_WEB_PAINT_SMOKE:exit-dirty");
                    exit.EmitSignal(Button.SignalName.Pressed);
                    _browserPaintSmokeStage = BrowserPaintSmokeStage.WaitingForDirtyExitPrompt;
                    break;

                case BrowserPaintSmokeStage.WaitingForDirtyExitPrompt:
                    if (host.Session.PendingAction != CharacterEditorPendingAction.Close)
                        break;
                    if (host.FindChild("UnsavedChangesPrompt", true, false) is not PanelContainer prompt ||
                        !prompt.Visible)
                    {
                        break;
                    }
                    Button? promptSave = FindVisibleButtonByText(prompt, "Save");
                    if (!GodotObject.IsInstanceValid(promptSave))
                        break;
                    GD.Print("DESKTOP_BUDDY_WEB_PAINT_SMOKE:unsaved-save");
                    promptSave!.EmitSignal(Button.SignalName.Pressed);
                    _browserPaintSmokeStage = BrowserPaintSmokeStage.WaitingForUnsavedSaveClose;
                    break;

                case BrowserPaintSmokeStage.WaitingForUnsavedSaveClose:
                    if (host.IsEditorOpen)
                        break;
                    _browserPaintSmokeStage = BrowserPaintSmokeStage.Complete;
                    GD.Print("DESKTOP_BUDDY_WEB_PAINT_SMOKE_COMPLETE");
                    break;
            }
        }
        catch (Exception exception)
        {
            FailBrowserPaintSmoke(exception.ToString());
        }
    }

    private bool BrowserOpenCompleted(CharacterEditorHost host)
    {
        if (_browserPaintOpenTask is { IsFaulted: true })
        {
            FailBrowserPaintSmoke(_browserPaintOpenTask.Exception?.ToString() ?? "Paint editor open task faulted.");
            return false;
        }

        // The experimental single-threaded Web runtime can strand the tail of
        // OpenWin98PaintEditorAsync on a ProcessFrame await even though the editor is already
        // fully visible and its shipping controls are live. For the interaction smoke, visible
        // shipping state is the authoritative completion boundary. Requiring Task.IsCompleted
        // here made the smoke stop before it ever clicked Save, which hid the exact user path we
        // need CI to exercise.
        if (!host.IsEditorOpen ||
            host.FindChild("PaintPrimaryActions", true, false) is not HBoxContainer)
        {
            return false;
        }

        _browserPaintOpenTask = null;
        return true;
    }

    private static Button? FindVisibleButton(CharacterEditorHost host, string name)
    {
        foreach (Node node in host.FindChildren(name, nameof(Button), true, false))
        {
            if (node is Button button && button.Visible && !button.Disabled)
                return button;
        }
        return null;
    }

    private static Button? FindVisibleButtonByText(Node root, string text)
    {
        foreach (Node node in root.FindChildren("*", nameof(Button), true, false))
        {
            if (node is Button button && button.Visible && !button.Disabled &&
                string.Equals(button.Text, text, StringComparison.Ordinal))
            {
                return button;
            }
        }
        return null;
    }

    private void FailBrowserPaintSmoke(string detail)
    {
        _browserPaintSmokeStage = BrowserPaintSmokeStage.Failed;
        GD.PushError($"RuntimeError: Browser Paint Buddy smoke failed: {detail}");
        GD.Print($"DESKTOP_BUDDY_WEB_PAINT_SMOKE_FAILED:{detail}");
    }

    private static bool BrowserPaintSmokeRequested()
    {
        try
        {
            Variant requested = JavaScriptBridge.Eval(
                "new URLSearchParams(globalThis.location.search).get('desktop_buddy_smoke') === '1'",
                useGlobalExecutionContext: true);
            return requested.AsBool();
        }
        catch (Exception exception)
        {
            GD.Print($"DESKTOP_BUDDY_WEB_PAINT_SMOKE_QUERY_UNAVAILABLE:{exception.Message}");
            return false;
        }
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
