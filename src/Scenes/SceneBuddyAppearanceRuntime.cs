using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Scenes;

/// <summary>
/// Character/paint presentation owner for a non-compatibility Scene actor. The persistent Character
/// ID lives on BuddyIdentityState; this node owns only the loaded/compiled visual projection for the
/// lifetime of that live actor. It never writes Character selection and therefore cannot redirect a
/// secondary Buddy through the legacy single-selection UI state.
/// </summary>
public partial class SceneBuddyAppearanceRuntime : Node
{
    private CharacterSelectionCoordinator? _coordinator;
    private RuntimePaintTextureBridge? _paintTextures;
    private CancellationTokenSource? _lifetime;
    private Task<CharacterActivationResult>? _loadTask;

    public bool IsConfigured => _coordinator is not null && _paintTextures is not null;
    public bool IsLoaded { get; private set; }
    public CharacterActivationResult? LoadResult { get; private set; }

    public void Configure(
        CharacterStore store,
        BuddyIdentityState buddy,
        BuddyVisualPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(buddy);
        ArgumentNullException.ThrowIfNull(presenter);
        if (IsInsideTree())
            throw new InvalidOperationException("Scene Buddy appearance must be configured before entering the tree.");
        if (!presenter.IsInitialized)
            throw new InvalidOperationException("Scene Buddy appearance requires an initialized visual presenter.");

        var selection = new CharacterSelectionState(buddy.CharacterId);
        _coordinator = new CharacterSelectionCoordinator(
            store,
            selection,
            presenter.RigView,
            store.FeatureCatalog);
        _paintTextures = new RuntimePaintTextureBridge(presenter.RigView);
    }

    public override void _Ready()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Scene Buddy appearance runtime entered the tree before configuration.");
        _lifetime = new CancellationTokenSource();
        CallDeferred(MethodName.BeginDeferredLoad);
    }

    /// <summary>
    /// Ensures this actor has loaded and applied its persistent Character and Buddy paint. Scene
    /// switching awaits this before activation; initial boot may let the deferred task complete in
    /// place while the sandbox finishes composing.
    /// </summary>
    public Task<CharacterActivationResult> EnsureLoadedAsync(CancellationToken token = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Scene Buddy appearance runtime is not configured.");
        if (_loadTask is null)
        {
            CancellationToken lifetime = _lifetime?.Token ?? token;
            _loadTask = LoadAndApplyAsync(lifetime);
        }
        return token.CanBeCanceled
            ? _loadTask.WaitAsync(token)
            : _loadTask;
    }

    private async void BeginDeferredLoad()
    {
        try
        {
            await EnsureLoadedAsync(_lifetime?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Diagnostics.Log.Error("SceneAppearance", $"Secondary Buddy appearance load failed: {exception.Message}");
        }
    }

    private async Task<CharacterActivationResult> LoadAndApplyAsync(CancellationToken token)
    {
        CharacterSelectionCoordinator coordinator = _coordinator!;
        CharacterActivationResult result = await coordinator.LoadStartupAsync(token);
        token.ThrowIfCancellationRequested();

        // This actor has no independent physics-process selection router. The async preparation is
        // complete, so consume its atomic visual activation on the main continuation immediately.
        coordinator.PhysicsTick();
        _paintTextures!.Apply(coordinator.AppliedPaintPayload);
        LoadResult = result;
        IsLoaded = true;
        return result;
    }

    public override void _ExitTree()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _paintTextures?.Dispose();
        _paintTextures = null;
    }
}
