using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Grab;
using DesktopBuddy.Scenes;
using Godot;

namespace DesktopBuddy.App;

public partial class SandboxRoot
{
    private SceneRuntimeHost? _sceneRuntime;

    /// <summary>
    /// Active Scene actor host when this executable actually includes the Scene product surface.
    /// Initial Steam Demo, itch.io and fail-closed untagged builds stay on the existing direct
    /// single-Buddy route.
    /// </summary>
    public SceneRuntimeHost? ActiveSceneRuntime => _sceneRuntime;

    private static BuildScopePolicy ResolveBuildScope() => BuildScopePolicy.Resolve(
        itchIo: OS.HasFeature(BuildFeatureTags.ItchIo),
        steamDemo: OS.HasFeature(BuildFeatureTags.SteamDemo),
        nextFestDemo: OS.HasFeature(BuildFeatureTags.NextFestDemo),
        fullRelease: OS.HasFeature(BuildFeatureTags.FullRelease));

    /// <summary>
    /// First production activation of the Scene runtime seam. This deliberately wraps only the
    /// already-existing single Buddy actor; it does not create a second actor or switch persistence
    /// ownership yet. The purpose of this packet is to put Next Fest/Full Release execution through
    /// the deterministic Scene host while leaving Initial Demo behavior untouched.
    /// </summary>
    private void InitializeSceneRuntimeHostIfEnabled()
    {
        if (!ResolveBuildScope().IncludesScenes)
        {
            _sceneRuntime = null;
            return;
        }

        // Temporary compatibility document until the atomic Initial Demo -> Next Fest migration
        // loads the real active Scene. Environment data is not read from this placeholder and the
        // runtime host owns no persistence; stable legacy IDs make the binding deterministic.
        SceneDocument compatibilityScene = LegacySceneMigrationPolicy.CreateDefaultScene(
            BuddyIdentityId.LegacyPrimary,
            new EnvironmentLayout([]),
            new CanonicalRoomPosition(0.5f, 0.5f));
        var actor = new BuddyActorRuntime(
            BuddyPlacementId.LegacyPrimary,
            BuddyIdentityId.LegacyPrimary,
            Buddy,
            Pipeline,
            CareStroke,
            ToolReactions,
            Reactions,
            VisualPresenter);
        _sceneRuntime = new SceneRuntimeHost(compatibilityScene, [actor]);
    }

    private void CaptureBuddyTickSnapshot()
    {
        if (_sceneRuntime is not null)
            _sceneRuntime.CaptureTickSnapshots();
        else
            VisualPresenter.CaptureTickSnapshot();
    }

    /// <summary>
    /// Routes the actor-local portion of the fixed tick. Shared tools, objects, grenades, fire,
    /// cameras and room services remain on SandboxRoot exactly where they are today.
    /// </summary>
    private void TickBuddyActors(double delta, GrabState grab, PuppetPartBody? grabbedBody)
    {
        if (_sceneRuntime is null)
        {
            bool buddyPartGrabbed = grabbedBody is not null;
            Buddy.GrabResistance.SetGrabContext(buddyPartGrabbed, grab.CursorAnchor);
            CareStroke.PhysicsTick(delta);
            ToolReactions.PhysicsTick(delta);
            Reactions.PhysicsTick();
            Buddy.PhysicsTick(
                grabbedBody?.PartId,
                grab.CursorAnchor,
                Pointer.WorldCursor,
                Pointer.HasPointerInput,
                Ropes.HoldsAny(Buddy.Rig.Parts));
            Pipeline.PhysicsTick();
            return;
        }

        _sceneRuntime.PhysicsTick(actor =>
        {
            PuppetPartBody? actorGrabbedBody = actor.OwnsPart(grabbedBody) ? grabbedBody : null;
            return new BuddyActorTickContext(
                Delta: delta,
                GrabbedPart: actorGrabbedBody?.PartId,
                GrabWorldAnchor: grab.CursorAnchor,
                CursorWorldPosition: Pointer.WorldCursor,
                SocialTargetValid: Pointer.HasPointerInput,
                RopeSuspended: Ropes.HoldsAny(actor.Buddy.Rig.Parts));
        });
    }
}
