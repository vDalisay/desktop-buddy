using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;

namespace DesktopBuddy.Persistence.Characters;

public partial class CharacterSelectionRuntime
{
    /// <summary>
    /// Rebinds the existing single-rig Character compatibility surface to the target Scene's first
    /// actor. Selection state is a presentation observable in Scene mode, so it adopts the target
    /// Buddy's authoritative Character ID without emitting a persistence mutation, then the normal
    /// loader/validator prepares and applies that Character and its paint to the authored rig.
    /// </summary>
    public async Task<CharacterActivationResult> RebindSceneCompatibilityActorAsync(
        SceneBuddyProgressBinding binding,
        CancellationToken token = default)
    {
        if (_context.SceneProgress is not { } scenes)
            throw new InvalidOperationException("Character runtime is not backed by Scene progress.");
        if (_context.CharacterSelection is not { } selection || _coordinator is null || _paintTextures is null)
            throw new InvalidOperationException("Character runtime cannot rebind before initialization completes.");

        BuddyIdentityState buddy = binding.Progress.BuddyProgress
            ?? throw new InvalidOperationException("Scene Character binding has no persistent Buddy identity.");
        if (buddy.BuddyIdentityId != binding.Placement.BuddyIdentityId)
            throw new InvalidOperationException("Scene Character binding does not match its placement identity.");

        bool resumePhysics = IsPhysicsProcessing();
        SetPhysicsProcess(false);
        try
        {
            _sceneSelectionBinding?.Dispose();
            _sceneSelectionBinding = null;

            // Scene mode does not persist CharacterSelectionState itself. Preserve its compatibility
            // revision and replace only the observed value, avoiding a Changed event that would write
            // through whichever Buddy binding represented the outgoing Scene.
            selection.Restore(new CharacterSelectionSnapshot(buddy.CharacterId, selection.Revision));
            _sceneSelectionBinding = new SceneCharacterSelectionBinding(
                scenes,
                binding.Placement.BuddyIdentityId,
                selection);

            CharacterActivationResult result = await _coordinator.LoadStartupAsync(token);
            token.ThrowIfCancellationRequested();

            // Gameplay is paused for the Scene transaction, so consume the prepared activation here
            // instead of exposing one frame where the target physics actor wears the outgoing skin.
            _coordinator.PhysicsTick();
            if (_boundPaintSequence != _coordinator.AppliedPaintSequence)
            {
                _paintTextures.Apply(_coordinator.AppliedPaintPayload);
                _boundPaintSequence = _coordinator.AppliedPaintSequence;
            }

            return result;
        }
        finally
        {
            SetPhysicsProcess(resumePhysics);
        }
    }
}
