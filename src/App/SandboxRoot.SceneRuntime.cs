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
    /// First production activation of the Scene runtime seam. While the presentation still owns one
    /// physical Buddy actor, a split run binds that actor to the real active Scene document and its
    /// stable legacy-primary placement. The placeholder remains only for compatibility fixtures that
    /// exercise a Scene-tagged sandbox without a production RunContext.
    /// </summary>
    private void InitializeSceneRuntimeHostIfEnabled()
    {
        if (!ResolveBuildScope().IncludesScenes)
        {
            _sceneRuntime = null;
            return;
        }

        SceneDocument scene;
        BuddyPlacementId placementId = BuddyPlacementId.LegacyPrimary;
        BuddyIdentityId buddyIdentityId = BuddyIdentityId.LegacyPrimary;

        if (_runContext?.SceneProgress is { } sceneProgress)
        {
            SceneProgressBindingRegistry bindings = sceneProgress.CreateActiveBindings();
            SceneBuddyProgressBinding binding = bindings.ForPlacement(BuddyPlacementId.LegacyPrimary);
            scene = sceneProgress.ActiveScene;
            placementId = binding.Placement.PlacementId;
            buddyIdentityId = binding.Placement.BuddyIdentityId;
        }
        else
        {
            // Compatibility-only fallback until every direct Scene-tagged scenario injects a split
            // RunContext. Production Bootstrap supplies SceneProgress before the sandbox enters tree.
            scene = LegacySceneMigrationPolicy.CreateDefaultScene(
                BuddyIdentityId.LegacyPrimary,
                new EnvironmentLayout([]),
                new CanonicalRoomPosition(0.5f, 0.5f));
        }

        var actor = new BuddyActorRuntime(
            placementId,
            buddyIdentityId,
            Buddy,
            Pipeline,
            CareStroke,
            ToolReactions,
            Reactions,
            VisualPresenter);
        _sceneRuntime = new SceneRuntimeHost(scene, [actor]);
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
