using System;
using System.Threading;
using DesktopBuddy.App;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Domain.Persistence;
using Godot;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>
/// Runtime router for fixed-tick appearance activation and render-frame paint texture binding.
/// PNG decode and file I/O finish before the coordinator publishes an activation.
/// </summary>
public partial class CharacterSelectionRuntime : Node
{
    private SandboxRoot _sandbox = null!;
    private RunContext _context = null!;
    private CharacterSelectionCoordinator? _coordinator;
    private SceneCharacterSelectionBinding? _sceneSelectionBinding;
    private RuntimePaintTextureBridge? _paintTextures;
    private CancellationTokenSource? _lifetime;
    private long _boundPaintSequence;

    public CharacterSelectionCoordinator? Coordinator => _coordinator;
    public bool IsInitialized => _coordinator is not null;
    public CharacterActivationResult? StartupResult { get; private set; }

    public void Configure(SandboxRoot sandbox, RunContext context)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("CharacterSelectionRuntime must be configured before entering the tree.");
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // Character activation is a presentation/persistence boundary, not gameplay simulation.
        // Buddy Studio deliberately pauses the gameplay tree, but a queued "Use Character" must
        // still reach the live rig before the editor is dismissed or Work Mode is entered.
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        ProcessPhysicsPriority = -100;
        CallDeferred(MethodName.InitializeDeferred);
    }

    public override void _PhysicsProcess(double delta) => _coordinator?.PhysicsTick();

    public override void _Process(double delta)
    {
        if (_coordinator is null || _paintTextures is null ||
            _boundPaintSequence == _coordinator.AppliedPaintSequence)
            return;
        _paintTextures.Apply(_coordinator.AppliedPaintPayload);
        _boundPaintSequence = _coordinator.AppliedPaintSequence;
    }

    public override void _ExitTree()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _sceneSelectionBinding?.Dispose();
        _sceneSelectionBinding = null;
        _paintTextures?.Dispose();
        _paintTextures = null;
    }

    private async void InitializeDeferred()
    {
        if (_context.CharacterSelection is null || _context.Characters is null)
        {
            SetPhysicsProcess(false);
            SetProcess(false);
            return;
        }
        if (!_sandbox.VisualPresenter.IsInitialized)
            throw new InvalidOperationException("Character selection initialized before the shared visual rig.");

        _lifetime = new CancellationTokenSource();
        _paintTextures = new RuntimePaintTextureBridge(_sandbox.VisualPresenter.RigView);

        if (_context.SceneProgress is { } scenes)
        {
            // Production still owns one compatibility actor while multi-Buddy spawning is brought
            // online. Its stable migrated identity is the authority for Character selection; the
            // legacy SaveCoordinator is intentionally not involved after the Scene manifest exists.
            _sceneSelectionBinding = new SceneCharacterSelectionBinding(
                scenes,
                BuddyIdentityId.LegacyPrimary,
                _context.CharacterSelection);
            _coordinator = new CharacterSelectionCoordinator(
                _context.Characters,
                _context.CharacterSelection,
                _sandbox.VisualPresenter.RigView,
                BuddyGeneratedCosmeticRegistry.Current.FeatureCatalog);
        }
        else
        {
            _coordinator = new CharacterSelectionCoordinator(
                _context.Characters,
                _context.CharacterSelection,
                _sandbox.VisualPresenter.RigView,
                _context.Saves,
                BuddyGeneratedCosmeticRegistry.Current.FeatureCatalog);
        }

        StartupResult = await _coordinator.LoadStartupAsync(_lifetime.Token);
    }
}
