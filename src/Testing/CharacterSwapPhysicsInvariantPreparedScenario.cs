using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Physics invariant at the actual activation boundary: async load/compile has completed,
/// then no engine frame elapses between the before/after samples around PhysicsTick().
/// </summary>
public sealed class CharacterSwapPhysicsInvariantPreparedScenario : IScenario
{
    public string Id => "character_swap_physics_invariant";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        (BuddyLab lab, string root, CharacterStore store) =
            await CharacterSelectionScenarioSupport.CreateLabAsync(tree, Id);
        try
        {
            Guid id = Guid.Parse("62000000-0000-4000-8000-000000000002");
            await store.SaveAsync(
                CharacterSelectionScenarioSupport.Character(id, "Blue", "#2368D8"),
                CancellationToken.None);
            var selection = new CharacterSelectionState();
            var memory = new InMemoryProgressStore();
            SaveCoordinator saves = CharacterSelectionScenarioSupport.Saves(
                selection, memory, out BuddyProgressState progress);
            var coordinator = new CharacterSelectionCoordinator(
                store, selection, lab.VisualPresenter.RigView, saves);

            CharacterActivationResult queued = await coordinator.QueueUseCharacterAsync(
                id, CancellationToken.None);
            BuddyVisualRigTrustSnapshot trusted = lab.VisualPresenter.RigView.CaptureTrustSnapshot();
            var before = new BodyInvariant[PuppetRigProfile.RequiredPartCount];
            for (int index = 0; index < before.Length; index++)
                before[index] = BodyInvariant.Capture(lab.Buddy.Rig.GetPart((BuddyPartId)index));
            ProgressSnapshot progressBefore = progress.Snapshot();
            bool beforeTickUnchanged = selection.ActiveCharacterId is null &&
                lab.VisualPresenter.RigView.ActiveAppearance is null;

            coordinator.PhysicsTick();

            bool bodiesEqual = true;
            for (int index = 0; index < before.Length; index++)
                bodiesEqual &= before[index] == BodyInvariant.Capture(lab.Buddy.Rig.GetPart((BuddyPartId)index));
            bool invariant = queued.WasQueued && beforeTickUnchanged && bodiesEqual &&
                lab.VisualPresenter.RigView.TrustedGeometryMatches(trusted) &&
                progress.Snapshot() == progressBefore &&
                selection.ActiveCharacterId == id &&
                lab.VisualPresenter.RigView.ActiveAppearance?.CharacterId == id;
            checks.Add(new StartupCheck("a6_swap_visual_only_at_fixed_tick", invariant,
                $"queued={queued.Status} before_tick={beforeTickUnchanged} bodies={bodiesEqual}"));
        }
        finally
        {
            CharacterSelectionScenarioSupport.Cleanup(lab, root);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }

    private readonly record struct BodyInvariant(
        Vector2 Position,
        Vector2 Velocity,
        float Rotation,
        float AngularVelocity,
        float Mass,
        float Radius,
        uint CollisionLayer,
        uint CollisionMask)
    {
        public static BodyInvariant Capture(PuppetPartBody body) => new(
            body.GlobalPosition,
            body.LinearVelocity,
            body.GlobalRotation,
            body.AngularVelocity,
            body.Mass,
            body.Radius,
            body.CollisionLayer,
            body.CollisionMask);
    }
}
