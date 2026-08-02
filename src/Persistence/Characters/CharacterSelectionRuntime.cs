using System;
using System.Threading;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>
/// Thin runtime router for the selection coordinator. It defers composition until the
/// parent sandbox has initialized its visual rig, then owns startup loading and one
/// fixed-tick activation call. No gameplay state is sampled or mutated here.
/// </summary>
public partial class CharacterSelectionRuntime : Node
{
    private SandboxRoot _sandbox = null!;
    private RunContext _context = null!;
    private CharacterSelectionCoordinator? _coordinator;
    private CancellationTokenSource? _lifetime;

    public CharacterSelectionCoordinator? Coordinator => _coordinator;
    public bool IsInitialized => _coordinator is not null;
    public CharacterActivationResult? StartupResult { get; private set; }

    public void Configure(SandboxRoot sandbox, RunContext context)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("CharacterSelectionRuntime must be configured before entering the tree.");
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public override void _Ready()
    {
        ProcessPhysicsPriority = -100;
        CallDeferred(MethodName.InitializeDeferred);
    }

    public override void _PhysicsProcess(double delta)
    {
        _coordinator?.PhysicsTick();
    }

    public override void _ExitTree()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
    }

    private async void InitializeDeferred()
    {
        if (_context.CharacterSelection is null || _context.Characters is null)
        {
            SetPhysicsProcess(false);
            return;
        }
        if (!_sandbox.VisualPresenter.IsInitialized)
        {
            throw new InvalidOperationException(
                "Character selection runtime initialized before the shared visual rig.");
        }

        _lifetime = new CancellationTokenSource();
        _coordinator = new CharacterSelectionCoordinator(
            _context.Characters,
            _context.CharacterSelection,
            _sandbox.VisualPresenter.RigView,
            _context.Saves);
        StartupResult = await _coordinator.LoadStartupAsync(_lifetime.Token);
    }
}
