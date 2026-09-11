using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Scenes;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Buddy-to-Buddy collision, a per-Scene setting that defaults to on (owner instruction
/// 2026-09-10). Before this, Buddy parts never had the Buddy layer in their mask, so separate
/// Buddies passed through each other like ghosts.
/// </summary>
public partial class SandboxRoot
{
    /// <summary>Whether the active Scene's Buddies collide. True when Scenes are not in this build.</summary>
    public bool BuddiesCollide => SceneProgress?.ActiveScene.BuddiesCollide ?? true;

    /// <summary>Changes the active Scene's setting, applies it to the live roster and saves it.</summary>
    public async void SetBuddiesCollide(bool collide)
    {
        if (SceneProgress is not { } scenes)
            return;
        SceneLibraryResult result = scenes.SetBuddiesCollide(scenes.ActiveSceneId, collide);
        if (!result.Succeeded)
            return;

        ApplyBuddyCollision();
        try
        {
            await scenes.FlushAsync(force: true);
        }
        catch (System.Exception exception)
        {
            Diagnostics.Log.Error("SceneCast", $"Could not save the Buddy collision setting: {exception.Message}");
        }
    }

    /// <summary>
    /// Opens or closes the Buddy layer in every live actor's part masks.
    ///
    /// <para>One rig's own six parts must never collide with each other — they overlap at rest and
    /// the joints hold them together — so each rig carries collision exceptions between all of its
    /// parts before its mask ever admits the Buddy layer. That keeps the toggle a pure mask change.</para>
    /// </summary>
    private void ApplyBuddyCollision()
    {
        if (_sceneRuntime is null)
            return;

        uint mask = BuddiesCollide
            ? CollisionLayers.MaskBuddyParts | CollisionLayers.BuddyParts
            : CollisionLayers.MaskBuddyParts;
        foreach (BuddyActorRuntime actor in _sceneRuntime.Actors)
        {
            IReadOnlyList<PuppetPartBody> parts = actor.Buddy.Rig.Parts;
            for (int first = 0; first < parts.Count; first++)
            {
                for (int second = first + 1; second < parts.Count; second++)
                    parts[first].AddCollisionExceptionWith(parts[second]);
            }
            foreach (PuppetPartBody part in parts)
            {
                // An inactive part (an empty room's parked authored rig) keeps its zero mask.
                if (part.CollisionLayer != 0)
                    part.CollisionMask = mask;
            }
        }
    }
}
