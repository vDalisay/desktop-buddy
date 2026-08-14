using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Work;

/// <summary>
/// Runtime owner of one Work session. The native hook thread can only increment anonymous
/// pending counters; every domain mutation, reward evaluation and Godot operation occurs on
/// the main thread from <see cref="_Process"/>.
/// </summary>
public partial class WorkCompanionCoordinator : Node
{
    private const string Category = "WorkMode";

    private readonly Dictionary<CanvasItem, bool> _canvasVisibility = [];
    private readonly Dictionary<Node3D, bool> _node3DVisibility = [];
    private SandboxRoot _sandbox = null!;
    private RunContext _context = null!;
    private WorkProgressState _work = null!;
    private WorkMilestoneCatalogue _milestones = null!;
    private Func<IWorkActivitySource> _activitySourceFactory = null!;
    private IWorkActivitySource? _activitySource;
    private WorkSessionState? _session;
    private WorkCompanionView? _view;
    private CanvasLayer? _win98Shell;
    private bool _win98ShellWasVisible;
    private bool _sandboxPhysicsWasProcessing;
    private long _pendingKeyboard;
    private long _pendingMouse;
    private long _sessionSettledMilliCredits;
    private bool _geometryDirty;
    private double _resizeSaveDelay;
    private Vector2I _lastWorkSize;
    private bool _transitioning;

    public bool IsActive { get; private set; }
    public WorkSessionState? Session => _session;
    public WorkProgressState Progress => _work;

    public event Action<bool>? ActiveChanged;

    public void Configure(
        SandboxRoot sandbox,
        RunContext context,
        Func<IWorkActivitySource>? activitySourceFactory = null,
        WorkMilestoneCatalogue? milestones = null)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("WorkCompanionCoordinator must be configured before entering the tree.");
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _work = context.WorkProgress ?? throw new InvalidOperationException("RunContext has no Work progress state.");
        _activitySourceFactory = activitySourceFactory ?? (() => new WindowsWorkActivitySource());
        _milestones = milestones ?? WorkMilestoneDefaults.Create();
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _sandbox.Shell.InputModeChanged += OnShellInputModeChanged;
        _sandbox.Window.ClientBoundsChanged += OnWorkClientBoundsChanged;
    }

    public override void _Process(double delta)
    {
        if (!IsActive)
            return;
        DrainActivity();
        if (_resizeSaveDelay > 0.0)
        {
            _resizeSaveDelay = Math.Max(0.0, _resizeSaveDelay - delta);
            if (_resizeSaveDelay == 0.0)
                _ = PersistPreferencesObservedAsync(forceGeometry: true);
        }
    }

    public async Task<bool> EnterAsync(CancellationToken token = default)
    {
        if (IsActive)
            return true;
        if (_transitioning)
            return false;
        if (DisplayServer.GetName() == "headless" && _activitySourceFactory.Target is null)
            return false;

        CharacterEditorHost? editor = _sandbox.GetNodeOrNull<CharacterEditorHost>(nameof(CharacterEditorHost));
        if (editor is { IsEditorOpen: true })
            return false;

        _transitioning = true;
        // Capture this before any awaited reward/character work. Entry can fail before the
        // presentation is hidden, and rollback must never guess whether physics was running.
        _sandboxPhysicsWasProcessing = _sandbox.IsPhysicsProcessing();
        try
        {
            _sandbox.Shell.ReturnToWorkMode();
            _sandbox.Shell.SetProcessInput(false);
            _sandbox.Shell.SetProcessUnhandledInput(false);

            var firstEntry = new WorkFirstEntryRewardService(
                _context.Progress,
                _work,
                _context.Saves);
            await firstEntry.EnsureAsync(token);

            CompiledCharacterAppearance? appearance = await ResolveAppearanceAsync(token);

            Rect2I workRect = _sandbox.Shell.ResolveInitialWorkCompanionRect(WorkCompanionView.PreferredSize);
            _sandbox.Window.EnterWorkCompanionWindow(workRect);

            HideNormalPresentation();
            _sandbox.SetPhysicsProcess(false);

            LocalSettingsSave settings = _sandbox.Shell.CurrentLocalSettings;
            _view = new WorkCompanionView { Name = nameof(WorkCompanionView) };
            _view.Configure(
                _sandbox,
                settings.WorkShowLifetimeCounter,
                settings.WorkAnimationsEnabled,
                appearance);
            _view.ExitRequested += OnExitRequested;
            _view.CounterModeToggleRequested += OnCounterModeToggleRequested;
            _view.AnimationPreferenceChanged += OnAnimationPreferenceChanged;
            _view.ResizeRequested += OnResizeRequested;
            _view.DraggedBy += OnDraggedBy;
            _view.DragFinished += OnDragFinished;
            GetTree().Root.AddChild(_view);
            _lastWorkSize = _sandbox.Window.WorkCompanionRect.Size;

            _session = _work.ActiveSession is { } journal
                ? new WorkSessionState(journal)
                : new WorkSessionState();
            _sessionSettledMilliCredits = 0;
            UpdateCounter();

            _activitySource = _activitySourceFactory();
            _activitySource.Activity += OnRawActivity;
            WorkActivitySourceResult started = _activitySource.Start();
            if (!started.Success)
            {
                Log.Error(Category, started.Detail ?? "Global Work activity source failed to start.");
                await RollbackFailedEntryAsync();
                return false;
            }

            _work.CheckpointSession(_session.Snapshot());
            await _context.Saves.FlushProgressAsync(force: true, token);

            IsActive = true;
            ActiveChanged?.Invoke(true);
            Log.Info(Category,
                $"Work session started at {_sandbox.Window.WorkCompanionRect}; lifetimeActions={_work.Lifetime.TotalActions}.");
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Error(Category, $"Work Mode entry failed: {exception}");
            await RollbackFailedEntryAsync();
            return false;
        }
        finally
        {
            _transitioning = false;
        }
    }

    public async Task ExitAsync(CancellationToken token = default)
    {
        if (!IsActive || _transitioning)
            return;

        _transitioning = true;
        try
        {
            _activitySource?.Stop();
            DrainActivity();

            try
            {
                await PersistPreferencesAsync(forceGeometry: _geometryDirty);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.Error(Category, $"Final Work preference save failed: {exception.Message}");
            }
            _work.ClearActiveSession();
            try
            {
                await _context.Saves.FlushProgressAsync(force: true, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed save remains dirty for the app-level quit/focus retry, but must not
                // trap the player in a companion whose global capture has already stopped.
                Log.Error(Category, $"Final Work progress save failed: {exception.Message}");
            }

            TearDownActivitySource();
            TearDownView();
            RestoreNormalPresentation();
            _sandbox.Window.ExitWorkCompanionWindow();
            _sandbox.SetPhysicsProcess(_sandboxPhysicsWasProcessing);
            _sandbox.Shell.SetProcessInput(true);
            _sandbox.Shell.SetProcessUnhandledInput(true);
            if (_sandbox.Shell.Mode == InputMode.Work)
                _sandbox.Shell.ToggleInteractionMode();

            _session = null;
            IsActive = false;
            ActiveChanged?.Invoke(false);
            ShowExitSummaryIfEarned();
            Log.Info(Category, $"Work session ended; settledMilliCredits={_sessionSettledMilliCredits}.");
        }
        finally
        {
            _transitioning = false;
        }
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_sandbox) && GodotObject.IsInstanceValid(_sandbox.Shell))
            _sandbox.Shell.InputModeChanged -= OnShellInputModeChanged;
        if (GodotObject.IsInstanceValid(_sandbox) && GodotObject.IsInstanceValid(_sandbox.Window))
            _sandbox.Window.ClientBoundsChanged -= OnWorkClientBoundsChanged;

        // Quit/shutdown can bypass the double-click exit path. Stop capture first, consume the
        // final anonymous deltas, then synchronously checkpoint before nodes disappear. This is
        // deliberately tree-exit-only; no hook callback ever performs disk I/O.
        if (IsActive)
        {
            _activitySource?.Stop();
            DrainActivity();
            _work.ClearActiveSession();
            try
            {
                PersistPreferencesAsync(forceGeometry: _geometryDirty).GetAwaiter().GetResult();
                _context.Saves.FlushProgressAsync(force: true).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Log.Error(Category, $"Final Work Mode checkpoint failed: {exception.Message}");
            }
        }

        TearDownActivitySource();
        if (GodotObject.IsInstanceValid(_view))
            _view!.QueueFree();
    }

    private void OnShellInputModeChanged(InputMode mode)
    {
        if (mode == InputMode.Work && !IsActive && !_transitioning)
            _ = EnterFromWorkCommandObservedAsync();
    }

    private async Task EnterFromWorkCommandObservedAsync()
    {
        try
        {
            bool entered = await EnterAsync();
            // The old Work command changes the shell state before this coordinator hears the
            // event. If entry is rejected (for example because the editor is open), restore
            // Play rather than leaving the legacy click-through Work state half-entered.
            if (!entered && !IsActive && _sandbox.Shell.Mode == InputMode.Work)
                _sandbox.Shell.ToggleInteractionMode();
        }
        catch (Exception exception)
        {
            Log.Error(Category, $"Work Mode command failed: {exception}");
            if (!IsActive && _sandbox.Shell.Mode == InputMode.Work)
                _sandbox.Shell.ToggleInteractionMode();
        }
    }

    private void OnRawActivity(WorkActivityKind kind)
    {
        // Hook callback thread: anonymous aggregate mutation only. Never call Godot here.
        if (kind == WorkActivityKind.KeyboardPress)
            Interlocked.Increment(ref _pendingKeyboard);
        else if (kind == WorkActivityKind.MouseClick)
            Interlocked.Increment(ref _pendingMouse);
    }

    private void DrainActivity()
    {
        if (_session is null)
            return;

        long keyboard = Interlocked.Exchange(ref _pendingKeyboard, 0);
        long mouse = Interlocked.Exchange(ref _pendingMouse, 0);
        if (keyboard <= 0 && mouse <= 0)
            return;

        if (keyboard > 0)
        {
            _session.Record(WorkActivityKind.KeyboardPress, keyboard);
            _work.Record(WorkActivityKind.KeyboardPress, keyboard);
            _view?.NotifyActivity(WorkActivityKind.KeyboardPress, keyboard);
        }
        if (mouse > 0)
        {
            _session.Record(WorkActivityKind.MouseClick, mouse);
            _work.Record(WorkActivityKind.MouseClick, mouse);
            _view?.NotifyActivity(WorkActivityKind.MouseClick, mouse);
        }

        // Settle immediately. Evaluate claims a lifetime milestone as its claimability test and
        // that claim is durable, so any gap between claiming and paying is a window in which a
        // crash burns the reward permanently.
        IReadOnlyList<WorkMilestoneEarned> newlyEarned = _session.Evaluate(_work, _milestones);
        _work.CheckpointSession(_session.Snapshot());
        UpdateCounter();
        if (newlyEarned.Count > 0)
        {
            long rewardMilli = 0;
            foreach (WorkMilestoneEarned earned in newlyEarned)
                rewardMilli = WorkCounterSnapshot.SaturatingAdd(rewardMilli, earned.RewardMilliCredits);
            if (rewardMilli > 0)
                _context.Economy.DepositPassive(rewardMilli);
            _sessionSettledMilliCredits = WorkCounterSnapshot.SaturatingAdd(
                _sessionSettledMilliCredits, rewardMilli);
            _ = FlushMilestoneObservedAsync();
        }
    }

    private async Task<CompiledCharacterAppearance?> ResolveAppearanceAsync(CancellationToken token)
    {
        BuddyVisualRigView liveRig = _sandbox.VisualPresenter.RigView;
        CompiledCharacterAppearance? liveAppearance = liveRig.ActiveAppearance;
        Guid? activeId = _context.CharacterSelection?.ActiveCharacterId;

        // CharacterId alone is not a freshness guarantee: Studio can save/equip another cosmetic
        // on the same character while the live rig is still waiting for its queued activation.
        // Work Mode must therefore compile the persisted active character every time it enters.
        if (!activeId.HasValue || _context.Characters is null)
            return liveAppearance;

        CharacterLoadResult loaded = await _context.Characters.LoadAsync(activeId.Value, token);
        if (loaded.Document is null)
            return liveAppearance;

        CharacterNormalizationResult normalized = CharacterDocumentNormalizer.Normalize(loaded.Document);
        CharacterCompileResult compiled = CharacterCompiler.Compile(
            normalized.Document,
            BuddyGeneratedCosmeticRegistry.Current.FeatureCatalog);
        return compiled.Appearance ?? liveAppearance;
    }

    private void HideNormalPresentation()
    {
        _canvasVisibility.Clear();
        _node3DVisibility.Clear();
        CaptureAndHide(_sandbox);

        // Hide the whole shell layer, not just the frame: the command bar (Shop/Tools/Settings)
        // and its flyouts are siblings of the frame and stayed visible over the companion.
        _win98Shell = GetTree().Root.FindChild(
            nameof(Win98BuddyShellController), true, false) as CanvasLayer;
        if (GodotObject.IsInstanceValid(_win98Shell))
        {
            _win98ShellWasVisible = _win98Shell!.Visible;
            _win98Shell.Visible = false;
        }
    }

    private void CaptureAndHide(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child == this)
                continue;
            if (child is CanvasItem canvas)
            {
                _canvasVisibility[canvas] = canvas.Visible;
                canvas.Visible = false;
            }
            if (child is Node3D spatial)
            {
                _node3DVisibility[spatial] = spatial.Visible;
                spatial.Visible = false;
            }
            CaptureAndHide(child);
        }
    }

    private void RestoreNormalPresentation()
    {
        foreach ((CanvasItem item, bool visible) in _canvasVisibility)
            if (GodotObject.IsInstanceValid(item))
                item.Visible = visible;
        foreach ((Node3D item, bool visible) in _node3DVisibility)
            if (GodotObject.IsInstanceValid(item))
                item.Visible = visible;
        _canvasVisibility.Clear();
        _node3DVisibility.Clear();

        if (GodotObject.IsInstanceValid(_win98Shell))
            _win98Shell!.Visible = _win98ShellWasVisible;
        _win98Shell = null;
    }

    private void ShowExitSummaryIfEarned()
    {
        if (_sessionSettledMilliCredits <= 0)
            return;
        var frame = GetTree().Root.FindChild(nameof(Win98WindowFrame), true, false) as Win98WindowFrame;
        if (!GodotObject.IsInstanceValid(frame))
            return;
        double credits = _sessionSettledMilliCredits / 1000.0;
        frame!.StatusText = $"Work session complete: +{credits:0.###} credits";
    }

    private void UpdateCounter()
    {
        if (_session is null || !GodotObject.IsInstanceValid(_view))
            return;
        _view!.SetCounter(_session.Counters.TotalActions, _work.Lifetime.TotalActions);
    }

    private void OnCounterModeToggleRequested()
    {
        if (!GodotObject.IsInstanceValid(_view))
            return;
        _view!.SetCounterMode(!_view.ShowLifetime);
        UpdateCounter();
        _ = PersistPreferencesObservedAsync();
    }

    private void OnAnimationPreferenceChanged(bool enabled) =>
        _ = PersistPreferencesObservedAsync();

    private void OnDraggedBy(Vector2I delta)
    {
        Rect2I current = _sandbox.Window.WorkCompanionRect;
        _sandbox.Window.MoveWorkCompanion(current.Position + delta);
        _geometryDirty = true;
    }

    private void OnDragFinished() => _ = PersistPreferencesObservedAsync(forceGeometry: true);
    private void OnResizeRequested() => _sandbox.Window.StartWorkCompanionResize();
    private void OnExitRequested() => _ = ExitObservedAsync();

    private void OnWorkClientBoundsChanged(Rect2I rect)
    {
        if (!IsActive || rect.Size == _lastWorkSize)
            return;
        _lastWorkSize = rect.Size;
        _geometryDirty = true;
        _resizeSaveDelay = 0.35;
    }

    private async Task PersistPreferencesAsync(bool forceGeometry = false)
    {
        if (!GodotObject.IsInstanceValid(_view))
            return;
        Rect2I rect = _sandbox.Window.WorkCompanionRect;
        await _sandbox.Shell.SaveWorkPreferencesAsync(
            rect,
            forceGeometry || _sandbox.Shell.CurrentLocalSettings.WorkPositionSet,
            _view!.AnimationsEnabled,
            _view.ShowLifetime);
        if (forceGeometry)
            _geometryDirty = false;
    }

    private async Task PersistPreferencesObservedAsync(bool forceGeometry = false)
    {
        try
        {
            await PersistPreferencesAsync(forceGeometry);
        }
        catch (Exception exception)
        {
            Log.Error(Category, $"Work preference save failed: {exception.Message}");
        }
    }

    private async Task FlushMilestoneObservedAsync()
    {
        try
        {
            await _context.Saves.FlushProgressAsync(force: true);
        }
        catch (Exception exception)
        {
            Log.Error(Category, $"Work milestone progress save failed: {exception.Message}");
        }
    }

    private async Task ExitObservedAsync()
    {
        try
        {
            await ExitAsync();
        }
        catch (Exception exception)
        {
            Log.Error(Category, $"Work Mode exit failed: {exception}");
        }
    }

    private async Task RollbackFailedEntryAsync()
    {
        TearDownActivitySource();
        TearDownView();
        RestoreNormalPresentation();
        if (_sandbox.Window.WorkCompanionActive)
            _sandbox.Window.ExitWorkCompanionWindow();
        _sandbox.SetPhysicsProcess(_sandboxPhysicsWasProcessing);
        _sandbox.Shell.SetProcessInput(true);
        _sandbox.Shell.SetProcessUnhandledInput(true);
        if (_sandbox.Shell.Mode == InputMode.Work)
            _sandbox.Shell.ToggleInteractionMode();
        _session = null;
        IsActive = false;
        await Task.CompletedTask;
    }

    private void TearDownActivitySource()
    {
        if (_activitySource is null)
            return;
        _activitySource.Activity -= OnRawActivity;
        _activitySource.Dispose();
        _activitySource = null;
        _capturePausedForSuspend = false;
        Interlocked.Exchange(ref _pendingKeyboard, 0);
        Interlocked.Exchange(ref _pendingMouse, 0);
    }

    private void TearDownView()
    {
        if (!GodotObject.IsInstanceValid(_view))
        {
            _view = null;
            return;
        }
        _view!.ExitRequested -= OnExitRequested;
        _view.CounterModeToggleRequested -= OnCounterModeToggleRequested;
        _view.AnimationPreferenceChanged -= OnAnimationPreferenceChanged;
        _view.ResizeRequested -= OnResizeRequested;
        _view.DraggedBy -= OnDraggedBy;
        _view.DragFinished -= OnDragFinished;
        _view.QueueFree();
        _view = null;
    }
}
