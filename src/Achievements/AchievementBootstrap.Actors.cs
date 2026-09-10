using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy;
using DesktopBuddy.Interaction;
using DesktopBuddy.Scenes;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Per-Buddy achievement observation. Qualifying actions belong to whichever cast member they
/// happened to, so these observers follow the live roster instead of the first actor, and are
/// re-attached whenever a Scene switch or cast change composes new actors.
/// </summary>
public partial class AchievementBootstrap
{
    private readonly List<Action> _actorDetachers = [];
    private bool _actorObserversWired;

    /// <summary>How many live Buddies are currently observed. One per actor, never stale.</summary>
    public int ObservedActorCount => _actorDetachers.Count;

    private void WireActorObservers()
    {
        UnwireActorObservers();
        if (!GodotObject.IsInstanceValid(_sandbox))
            return;

        foreach ((InteractionDamageComponent damage, BuddyRoot buddy) in ObservedActors())
        {
            damage.ImpactAccepted += OnImpactAccepted;
            damage.ImpactAccepted += OnBankShotImpact;
            damage.CareMoodChanged += OnCareMoodChanged;
            buddy.ObjectInteraction.ConsumeSucceeded += OnCareItemTaken;
            _actorDetachers.Add(() =>
            {
                if (GodotObject.IsInstanceValid(damage))
                {
                    damage.ImpactAccepted -= OnImpactAccepted;
                    damage.ImpactAccepted -= OnBankShotImpact;
                    damage.CareMoodChanged -= OnCareMoodChanged;
                }
                if (GodotObject.IsInstanceValid(buddy) && GodotObject.IsInstanceValid(buddy.ObjectInteraction))
                    buddy.ObjectInteraction.ConsumeSucceeded -= OnCareItemTaken;
            });
        }

        if (!_actorObserversWired)
        {
            _sandbox.SceneRosterChanged += WireActorObservers;
            _actorObserversWired = true;
        }
    }

    private void UnwireActorObservers()
    {
        foreach (Action detach in _actorDetachers)
            detach();
        _actorDetachers.Clear();
    }

    private void StopObservingActors()
    {
        UnwireActorObservers();
        if (_actorObserversWired && GodotObject.IsInstanceValid(_sandbox))
            _sandbox.SceneRosterChanged -= WireActorObservers;
        _actorObserversWired = false;
    }

    /// <summary>The live cast, or the single authored Buddy on a run without a Scene roster.</summary>
    private IEnumerable<(InteractionDamageComponent Damage, BuddyRoot Buddy)> ObservedActors()
    {
        if (_sandbox.ActiveSceneRuntime is { } runtime && runtime.Actors.Count > 0)
        {
            foreach (BuddyActorRuntime actor in runtime.Actors)
                yield return (actor.Damage, actor.Buddy);
            yield break;
        }

        if (GodotObject.IsInstanceValid(_sandbox.Pipeline) && GodotObject.IsInstanceValid(_sandbox.Buddy))
            yield return (_sandbox.Pipeline, _sandbox.Buddy);
    }
}
