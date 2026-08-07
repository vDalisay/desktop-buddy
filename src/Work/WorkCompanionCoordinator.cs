using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Characters;
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
    private Win98WindowFrame? _win98Frame;
    private bool _win98FrameWasVisible;
    private bool _sandboxPhysicsWasProcessing;
    private long _pendingKeyboard;
    private long _pendingMouse;
    private bool _positionDirty;
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

    public override void _Process(double delta)
    {
        if (!IsActive)
            return;
        DrainActivity();
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
        try
        {
            _sandbox.Shell.ReturnToWorkMode();
            _sandbox.Shell.SetProcessInput(false);
            _sandbox.Shell.SetProcessUnhandledInput(false);

            var firstEntry = new WorkFirstEntryRewardService(
                _context.Progress,
                _work,
                _context.CharacterSelection,
                _context.Characters,
                _context.Saves);
            WorkFirstEntryRewardResult reward = await firstEntry.EnsureAsync(token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(reward.Detail))
                Log.Warn(Category, reward.Detail!);

            CompiledCharacterAppearance? appearance = await ResolveAppearanceAsync(token).ConfigureAwait(false);

            Rect2I workRect = _sandbox.Shell.ResolveInitialWorkCompanionRect(WorkCompanionView.PreferredSize);
            _sandbox.Window.EnterWorkCompanionWindow(workRect);

            HideNormalPresentation();
            _sandboxPhysicsWasProcessing = _sandbox.IsPhysicsProcessing();
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
            _view.DraggedBy += OnDraggedBy;
            _view.DragFinished += OnDragFinished;
            GetTree().Root.AddChild(_view);

            _session = new WorkSessionState();
            UpdateCounter();

            _activitySource = _activitySourceFactory();
            _activitySource.Activity += OnRawActivity;
            WorkActivitySourceResult started = _activitySource.Start();
            if (!started.Success)
            {
                Log.Error(Category, started.Detail ?? "Global Work activity source failed to start.");
                await RollbackFailedEntryAsync().ConfigureAwait(false);
                return false;
            }

            IsActive = true;
            ActiveChanged?.Invoke(true);
            Log.Info(Category,
                $"Work session started at {_sandbox.Window.WorkCompanionRect}; lifetimeActions={_work.Lifetime.TotalActions}.");
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Error(Category, $"Work Mode entry failed: {exception}");
            await RollbackFailedEntryAsync().ConfigureAwait(false);
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

            long rewardMilli = 0;
            if (_session is not null)
            {
                foreach (WorkMilestoneEarned earned in _session.DrainPendingRewards())
                {
                    rewardMilli = WorkCounterSnapshot.SaturatingAdd(rewardMilli, earned.RewardMilliCredits);
                }
            }
            if (rewardMilli > 0)
                _context.Economy.DepositWorkMilestone(rewardMilli);

            await PersistPreferencesAsync(forcePosition: _positionDirty).ConfigureAwait(false);
            await _context.Saves.FlushProgressAsync(force: true, token).ConfigureAwait(false);

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
            Log.Info(Category, $"Work session ended; settledMilliCredits={rewardMilli}.");
        }
        finally
        {
            _transitioning = false;
        }
    }

    public override void _ExitTree()
    {
        TearDownActivitySource();
        if (GodotObject.IsInstanceValid(_view))
            _view!.QueueFree();
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
            _view?.NotifyActivity(WorkActivityKind.KeyboardPress);
        }
        if (mouse > 0)
        {
            _session.Record(WorkActivityKind.MouseClick, mouse);
            _work.Record(WorkActivityKind.MouseClick, mouse);
            _view?.NotifyActivity(WorkActivityKind.MouseClick);
        }

        IReadOnlyList<WorkMilestoneEarned> newlyEarned = _session.Evaluate(_work, _milestones);
        UpdateCounter();
        if (newlyEarned.Count > 0)
            _ = FlushMilestoneObservedAsync();
    }

    private async Task<CompiledCharacterAppearance?> ResolveAppearanceAsync(CancellationToken token)
    {
        Guid? activeId = _context.CharacterSelection?.ActiveCharacterId;
        if (!activeId.HasValue || _context.Characters is null)
            return _sandbox.VisualPresenter.RigView.ActiveAppearance;

        CharacterLoadResult loaded = await _context.Characters.LoadAsync(activeId.Value, token).ConfigureAwait(false);
        if (loaded.Document is null)
            return _sandbox.VisualPresenter.RigView.ActiveAppearance;

        CharacterNormalizationResult normalized = CharacterDocumentNormalizer.Normalize(loaded.Document);
        CharacterCompileResult compiled = CharacterCompiler.Compile(normalized.Document, CharacterFeatureCatalog.Shipped);
        return compiled.Appearance ?? _sandbox.VisualPresenter.RigView.ActiveAppearance;
    }

    private void HideNormalPresentation()
    {
        _canvasVisibility.Clear();
        _node3DVisibility.Clear();
        CaptureAndHide(_sandbox);

        _win98Frame = GetTree().Root.FindChild(nameof(Win98WindowFrame), true, false) as Win98WindowFrame;
        if (GodotObject.IsInstanceValid(_win98Frame))
        {
            _win98FrameWasVisible = _win98Frame!.Visible;
            _win98Frame.Visible = false;
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

        if (GodotObject.IsInstanceValid(_win98Frame))
            _win98Frame!.Visible = _win98FrameWasVisible;
        _win98Frame = null;
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
        _positionDirty = true;
    }

    private void OnDragFinished() => _ = PersistPreferencesObservedAsync(forcePosition: true);
    private void OnExitRequested() => _ = ExitObservedAsync();

    private async Task PersistPreferencesAsync(bool forcePosition = false)
    {
        if (!GodotObject.IsInstanceValid(_view))
            return;
        Rect2I rect = _sandbox.Window.WorkCompanionRect;
        await _sandbox.Shell.SaveWorkPreferencesAsync(
            rect.Position,
            forcePosition || _sandbox.Shell.CurrentLocalSettings.WorkPositionSet,
            _view!.AnimationsEnabled,
            _view.ShowLifetime).ConfigureAwait(false);
        if (forcePosition)
            _positionDirty = false;
    }

    private async Task PersistPreferencesObservedAsync(bool forcePosition = false)
    {
        try
        {
            await PersistPreferencesAsync(forcePosition).ConfigureAwait(false);
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
            await _context.Saves.FlushProgressAsync(force: true).ConfigureAwait(false);
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
            await ExitAsync().ConfigureAwait(false);
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
        _sandbox.SetPhysicsProcess(_sandboxPhysicsWasProcessing || !_sandboxPhysicsWasProcessing);
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
        _view.DraggedBy -= OnDraggedBy;
        _view.DragFinished -= OnDragFinished;
        _view.QueueFree();
        _view = null;
    }
}
