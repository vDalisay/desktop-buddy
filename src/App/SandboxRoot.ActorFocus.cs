using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Interaction;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Scenes;
using Godot;

namespace DesktopBuddy.App;

public partial class SandboxRoot
{
    private readonly List<(InteractionDamageComponent Damage, Action<AcceptedImpact> Handler)> _focusHooks = [];
    private BuddyPlacementId _focusedPlacementId;

    /// <summary>
    /// The cast member the player is currently working with: the one they last grabbed or hit, or
    /// the one they picked from the Scene menu. Customization and Work take this Buddy; gameplay
    /// itself stays routed to whichever actor was actually touched, never to this.
    /// </summary>
    public BuddyActorRuntime? FocusedActor { get; private set; }

    public event Action<BuddyActorRuntime?>? FocusedActorChanged;

    /// <summary>
    /// Raised after the active Scene's live cast is composed or recomposed. Observers that bind to
    /// per-Buddy components must re-attach here; the previous actors are already gone.
    /// </summary>
    public event Action? SceneRosterChanged;

    /// <summary>
    /// The Character worn by the focused Buddy in a Scene run, so customization and Work follow the
    /// selected cast member instead of the one compatibility selection. Null outside Scene runs.
    /// </summary>
    public Guid? FocusedBuddyCharacterId
    {
        get
        {
            if (FocusedActor is not { } actor || _sceneRuntime is not { UsesSplitProgress: true } runtime)
                return null;
            return runtime.ProgressFor(actor).BuddyProgress?.CharacterId;
        }
    }

    /// <summary>
    /// Dresses the focused Buddy in a Character when that Buddy is not the authored compatibility
    /// actor, and commits the change. Returns false when the caller should use the existing single
    /// selection path instead: no Scene run, no focus, or the authored actor is the focused one.
    /// </summary>
    public async Task<bool> TryApplyCharacterToFocusedBuddyAsync(
        Guid? characterId,
        CancellationToken token = default)
    {
        if (_runContext?.SceneProgress is not { } scenes || FocusedActor is not { } actor ||
            _sceneRuntime is null || ReferenceEquals(actor, _sceneRuntime.Actors.Count > 0 ? _sceneRuntime.Actors[0] : null))
        {
            return false;
        }
        if (!_sceneSpawnedAppearanceRuntimes.TryGetValue(actor.PlacementId, out SceneBuddyAppearanceRuntime? appearance) ||
            !GodotObject.IsInstanceValid(appearance))
        {
            return false;
        }

        await appearance.UseCharacterAsync(characterId, token);
        await scenes.FlushAsync(force: true, token);
        return true;
    }

    /// <summary>Focuses one placement of the active Scene. Returns false when it is not live.</summary>
    public bool TryFocusActor(BuddyPlacementId placementId)
    {
        if (_sceneRuntime is null)
            return false;
        foreach (BuddyActorRuntime actor in _sceneRuntime.Actors)
        {
            if (actor.PlacementId != placementId)
                continue;
            SetFocusedActor(actor);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Called after every Scene runtime composition: hitting a Buddy focuses it, and the player's
    /// existing choice is re-resolved against the new roster.
    /// </summary>
    private void OnSceneRuntimeComposed()
    {
        foreach ((InteractionDamageComponent damage, Action<AcceptedImpact> handler) in _focusHooks)
        {
            if (GodotObject.IsInstanceValid(damage))
                damage.ImpactAccepted -= handler;
        }
        _focusHooks.Clear();

        if (_sceneRuntime is not null)
        {
            foreach (BuddyActorRuntime actor in _sceneRuntime.Actors)
            {
                BuddyActorRuntime hit = actor;
                void Handler(AcceptedImpact _) => NoteActorInteraction(hit);
                actor.Damage.ImpactAccepted += Handler;
                _focusHooks.Add((actor.Damage, Handler));
            }
        }

        EnsureFocusedActor();
        SceneRosterChanged?.Invoke();
    }

    /// <summary>
    /// Re-resolves focus against the live roster. The player's choice survives a Scene recomposition
    /// whenever that placement is still present; otherwise focus falls back to the first actor, and
    /// an empty room has no focused Buddy at all.
    /// </summary>
    private void EnsureFocusedActor()
    {
        if (_sceneRuntime is null || _sceneRuntime.Actors.Count == 0)
        {
            SetFocusedActor(null);
            return;
        }

        foreach (BuddyActorRuntime actor in _sceneRuntime.Actors)
        {
            if (actor.PlacementId == _focusedPlacementId)
            {
                SetFocusedActor(actor);
                return;
            }
        }
        SetFocusedActor(_sceneRuntime.Actors[0]);
    }

    /// <summary>Interacting with a Buddy is the ordinary way a player chooses one.</summary>
    private void NoteActorInteraction(BuddyActorRuntime actor)
    {
        if (!ReferenceEquals(actor, FocusedActor))
            SetFocusedActor(actor);
    }

    private void SetFocusedActor(BuddyActorRuntime? actor)
    {
        if (ReferenceEquals(actor, FocusedActor))
            return;
        FocusedActor = actor;
        if (actor is not null)
            _focusedPlacementId = actor.PlacementId;
        FocusedActorChanged?.Invoke(actor);
    }
}
