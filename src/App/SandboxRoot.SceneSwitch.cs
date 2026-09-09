using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Scenes;
using Godot;

namespace DesktopBuddy.App;

public enum SceneRuntimeSwitchStatus
{
    Succeeded = 0,
    NoChange,
    Unavailable,
    InvalidTarget,
    Busy,
    ModalStateBlocked,
    PersistenceFailed,
    RuntimeFailed,
}

public readonly record struct SceneRuntimeSwitchResult(
    SceneRuntimeSwitchStatus Status,
    SceneId ActiveSceneId,
    string? Detail = null)
{
    public bool Succeeded => Status is SceneRuntimeSwitchStatus.Succeeded or SceneRuntimeSwitchStatus.NoChange;
}

public partial class SandboxRoot
{
    private bool _sceneSwitchInProgress;

    public bool IsSceneSwitchInProgress => _sceneSwitchInProgress;

    /// <summary>
    /// Performs the owner-locked Scene switch transaction from the master release plan. The outgoing
    /// Scene is persisted before any live actor is removed. Only after that commit do we tear down
    /// secondary actors/transients, change the active environment, rebind the authored compatibility
    /// actor, instantiate the target roster, apply every Buddy appearance and finally persist the
    /// new active Scene. A failed post-teardown step reconstructs the outgoing Scene before returning.
    /// </summary>
    public async Task<SceneRuntimeSwitchResult> SwitchSceneAsync(
        SceneId targetSceneId,
        CancellationToken token = default)
    {
        if (!targetSceneId.IsValid)
            return new SceneRuntimeSwitchResult(SceneRuntimeSwitchStatus.InvalidTarget, default, "Target Scene ID is invalid.");
        if (_sceneSwitchInProgress)
            return new SceneRuntimeSwitchResult(
                SceneRuntimeSwitchStatus.Busy,
                SceneProgress?.ActiveSceneId ?? default,
                "Another Scene switch is already in progress.");
        if (_runContext?.SceneProgress is not { } scenes || _sceneRuntime is null || !_sceneRuntime.UsesSplitProgress)
        {
            return new SceneRuntimeSwitchResult(
                SceneRuntimeSwitchStatus.Unavailable,
                SceneProgress?.ActiveSceneId ?? default,
                "This run does not have an active split Scene runtime.");
        }
        if (scenes.ActiveSceneId == targetSceneId)
            return new SceneRuntimeSwitchResult(SceneRuntimeSwitchStatus.NoChange, scenes.ActiveSceneId);
        if (Shell.Mode != InputMode.Play || Lifecycle.IsEditorModeActive || HasOpenSceneEnvironmentEditor())
        {
            return new SceneRuntimeSwitchResult(
                SceneRuntimeSwitchStatus.ModalStateBlocked,
                scenes.ActiveSceneId,
                "Close Work/Edit/room customization state before switching Scenes.");
        }

        SceneProgressBindingRegistry targetBindings;
        try
        {
            targetBindings = scenes.CreateBindings(targetSceneId);
        }
        catch (Exception exception)
        {
            return new SceneRuntimeSwitchResult(
                SceneRuntimeSwitchStatus.InvalidTarget,
                scenes.ActiveSceneId,
                exception.Message);
        }
        if (targetBindings.Count == 0)
        {
            return new SceneRuntimeSwitchResult(
                SceneRuntimeSwitchStatus.InvalidTarget,
                scenes.ActiveSceneId,
                "The staged compatibility runtime requires at least one Buddy in the target Scene.");
        }

        CharacterSelectionRuntime? characterRuntime =
            GetNodeOrNull<CharacterSelectionRuntime>(nameof(CharacterSelectionRuntime));
        if (characterRuntime is null || !characterRuntime.IsInitialized)
        {
            return new SceneRuntimeSwitchResult(
                SceneRuntimeSwitchStatus.Unavailable,
                scenes.ActiveSceneId,
                "Character compatibility runtime has not finished initialization.");
        }

        SceneId outgoingSceneId = scenes.ActiveSceneId;
        var transaction = new SceneSwitchTransaction(outgoingSceneId, targetSceneId);
        bool runtimeTornDown = false;
        bool rootPhysicsWasActive = IsPhysicsProcessing();
        _sceneSwitchInProgress = true;
        SetPhysicsProcess(false);
        Lifecycle.SetEditorMode(true);

        try
        {
            transaction.CompleteStep(SceneSwitchStep.ResolveModalState);

            CaptureActiveSceneBuddyAnchors(scenes);
            transaction.CompleteStep(SceneSwitchStep.CaptureBuddyAnchors);

            await scenes.FlushAsync(force: true, token);
            transaction.CompleteStep(SceneSwitchStep.PersistOutgoingScene);

            TearDownActiveSceneRuntimeForSwitch();
            runtimeTornDown = true;
            transaction.CompleteStep(SceneSwitchStep.TearDownActiveRuntime);

            SceneLibraryResult switched = scenes.SwitchScene(targetSceneId);
            if (!switched.Succeeded)
                throw new InvalidOperationException($"Scene library switch failed: {switched.Status}.");
            if (!TryRebindSceneEnvironment(scenes))
                throw new InvalidOperationException("Active room editor/presentation could not rebind to the target Scene.");
            transaction.CompleteStep(SceneSwitchStep.SwitchEnvironment);

            ComposeSceneRuntimeAfterSwitch(targetBindings);
            transaction.CompleteStep(SceneSwitchStep.InstantiateTargetRuntime);

            await characterRuntime.RebindSceneCompatibilityActorAsync(
                targetBindings.OrderedBindings[0],
                token);
            await EnsureSecondarySceneAppearancesLoadedAsync(token);
            transaction.CompleteStep(SceneSwitchStep.ApplyAppearanceAndPaint);

            ResetPresentationInterpolation();
            RefreshWorkModeHitRegions();
            transaction.CompleteStep(SceneSwitchStep.RestoreInput);

            transaction.CompleteStep(SceneSwitchStep.ActivateTargetScene);
            await scenes.FlushAsync(force: true, token);

            return new SceneRuntimeSwitchResult(SceneRuntimeSwitchStatus.Succeeded, scenes.ActiveSceneId);
        }
        catch (OperationCanceledException)
        {
            if (runtimeTornDown)
                await RecoverOutgoingSceneAfterFailedSwitchAsync(scenes, outgoingSceneId, characterRuntime);
            throw;
        }
        catch (Exception exception)
        {
            SceneSwitchStep? failedAt = transaction.NextRequiredStep;
            if (runtimeTornDown)
                await RecoverOutgoingSceneAfterFailedSwitchAsync(scenes, outgoingSceneId, characterRuntime);

            SceneRuntimeSwitchStatus status = failedAt == SceneSwitchStep.PersistOutgoingScene ||
                                              (transaction.IsComplete && scenes.IsDirty)
                ? SceneRuntimeSwitchStatus.PersistenceFailed
                : SceneRuntimeSwitchStatus.RuntimeFailed;
            Diagnostics.Log.Error(
                "SceneSwitch",
                $"Scene switch {outgoingSceneId} -> {targetSceneId} failed at {failedAt}: {exception.Message}");
            return new SceneRuntimeSwitchResult(status, scenes.ActiveSceneId, exception.Message);
        }
        finally
        {
            Lifecycle.SetEditorMode(false);
            SetPhysicsProcess(rootPhysicsWasActive);
            _sceneSwitchInProgress = false;
        }
    }

    private void CaptureActiveSceneBuddyAnchors(SceneProgressCoordinator scenes)
    {
        if (_sceneRuntime is null)
            throw new InvalidOperationException("Cannot capture Scene anchors without a live runtime.");

        Rect2 bounds = Boundaries.InnerBounds;
        float width = Math.Max(1.0f, bounds.Size.X);
        float height = Math.Max(1.0f, bounds.Size.Y);
        foreach (BuddyActorRuntime actor in _sceneRuntime.Actors)
        {
            Vector2 torso = actor.Buddy.Rig.Torso.GlobalPosition;
            float x = Mathf.Clamp((torso.X - bounds.Position.X) / width, 0.0f, 1.0f);
            float y = Mathf.Clamp((torso.Y - bounds.Position.Y) / height, 0.0f, 1.0f);
            SceneLibraryResult moved = scenes.MoveBuddy(
                scenes.ActiveSceneId,
                actor.PlacementId,
                new CanonicalRoomPosition(x, y));
            if (!moved.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not capture Buddy placement {actor.PlacementId}: {moved.Status}.");
            }
        }
    }

    private void TearDownActiveSceneRuntimeForSwitch()
    {
        if (_sceneRuntime is null)
            return;

        // Stop every actor-local object commitment before shared room objects disappear.
        foreach (BuddyActorRuntime actor in _sceneRuntime.Actors)
            actor.Buddy.ObjectInteraction.Reset();

        if (Grab.IsGrabbing)
            Grab.Release(countsAsThrow: false);
        Ropes.CutAll();
        CursorTools.ClearCursor();
        Launcher.CancelImmediately();
        Grenades.CancelImmediately();
        FireSprayer.ClearBurning();
        Pipeline.ClearRollingPain();
        Gore.ClearAll();
        Buddy.Arbiter.SetStatusHazard(false, 0.0f);
        ClearLooseObjectsForReplacement();

        // SceneRuntimeHost is the only routed tick owner. Drop it before detaching secondary nodes,
        // then remove those nodes from the tree immediately so inactive physics bodies cannot collide
        // during the remainder of the switch frame. QueueFree handles safe destruction afterwards.
        _sceneRuntime = null;
        for (int index = _sceneSpawnedActorNodes.Count - 1; index >= 0; index--)
        {
            Node node = _sceneSpawnedActorNodes[index];
            if (!GodotObject.IsInstanceValid(node))
                continue;
            Node? parent = node.GetParent();
            parent?.RemoveChild(node);
            node.QueueFree();
        }
        _sceneSpawnedActorNodes.Clear();
        _sceneSpawnedAppearanceRuntimes.Clear();
    }

    private void ComposeSceneRuntimeAfterSwitch(SceneProgressBindingRegistry bindings)
    {
        if (bindings.Count == 0)
            throw new InvalidOperationException("Target Scene has no Buddy roster to compose.");
        if (_sceneRuntime is not null || _sceneSpawnedActorNodes.Count != 0 ||
            _sceneSpawnedAppearanceRuntimes.Count != 0)
        {
            throw new InvalidOperationException("Another Scene runtime is still live during target composition.");
        }

        var actors = new List<BuddyActorRuntime>(bindings.Count)
        {
            RebindAuthoredSceneActor(bindings.OrderedBindings[0]),
        };
        for (int index = 1; index < bindings.OrderedBindings.Count; index++)
            actors.Add(ComposeAdditionalSceneActor(bindings.OrderedBindings[index], index));
        _sceneRuntime = new SceneRuntimeHost(bindings, actors);
    }

    private BuddyActorRuntime RebindAuthoredSceneActor(SceneBuddyProgressBinding binding)
    {
        CareStroke.SetStroke(false, Pointer.WorldCursor);
        CareStroke.SetWiggle(false);
        Pipeline.RebindProgress(binding.Progress, Economy);
        Buddy.Arbiter.RebindProgress(binding.Progress);
        Buddy.ObjectInteraction.RebindProgress(binding.Progress);
        ToolReactions.ResetForSceneRebind();
        ProgressBinding = binding.Progress;

        ApplyScenePlacement(Buddy, binding.Placement.Position, resetInitializedRig: true);
        Buddy.Recovery.ResetForSessionResume();
        Reactions.ResetForSceneRebind();
        Lifecycle.RebindBuddyProgress(binding.Progress);

        return new BuddyActorRuntime(
            binding.Placement.PlacementId,
            binding.Placement.BuddyIdentityId,
            Buddy,
            Pipeline,
            CareStroke,
            ToolReactions,
            Reactions,
            VisualPresenter);
    }

    private async Task RecoverOutgoingSceneAfterFailedSwitchAsync(
        SceneProgressCoordinator scenes,
        SceneId outgoingSceneId,
        CharacterSelectionRuntime characterRuntime)
    {
        try
        {
            if (_sceneRuntime is not null || _sceneSpawnedActorNodes.Count > 0)
                TearDownActiveSceneRuntimeForSwitch();

            if (scenes.ActiveSceneId != outgoingSceneId)
            {
                SceneLibraryResult switchedBack = scenes.SwitchScene(outgoingSceneId);
                if (!switchedBack.Succeeded)
                    throw new InvalidOperationException($"Could not restore outgoing Scene: {switchedBack.Status}.");
            }
            if (!TryRebindSceneEnvironment(scenes))
                throw new InvalidOperationException("Could not restore outgoing Scene environment.");

            SceneProgressBindingRegistry outgoingBindings = scenes.CreateActiveBindings();
            ComposeSceneRuntimeAfterSwitch(outgoingBindings);
            await characterRuntime.RebindSceneCompatibilityActorAsync(
                outgoingBindings.OrderedBindings[0],
                CancellationToken.None);
            await EnsureSecondarySceneAppearancesLoadedAsync(CancellationToken.None);
            ResetPresentationInterpolation();
            await scenes.FlushAsync(force: true, CancellationToken.None);
        }
        catch (Exception recoveryException)
        {
            Diagnostics.Log.Error(
                "SceneSwitch",
                $"Outgoing Scene recovery failed for {outgoingSceneId}: {recoveryException}");
        }
    }

    private bool HasOpenSceneEnvironmentEditor()
    {
#if !DESKTOP_BUDDY_PUBLIC_WEB
        return GetTree().Root.FindChild(
                   nameof(DesktopBuddy.Environment.EnvironmentCustomizationBootstrap),
                   recursive: true,
                   owned: false)
               is DesktopBuddy.Environment.EnvironmentCustomizationBootstrap environment &&
               environment.HasOpenSceneEditor;
#else
        return false;
#endif
    }

    private bool TryRebindSceneEnvironment(SceneProgressCoordinator scenes)
    {
#if !DESKTOP_BUDDY_PUBLIC_WEB
        return GetTree().Root.FindChild(
                   nameof(DesktopBuddy.Environment.EnvironmentCustomizationBootstrap),
                   recursive: true,
                   owned: false)
               is not DesktopBuddy.Environment.EnvironmentCustomizationBootstrap environment ||
               environment.TryRebindActiveSceneNow(scenes);
#else
        return true;
#endif
    }
}
