using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using DesktopBuddy.Scenes;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.App;

public partial class SandboxRoot
{
    private const string SceneBuddyPackedScenePath = "res://scenes/buddy/puppet.tscn";

    private readonly List<Node> _sceneSpawnedActorNodes = [];
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
    /// Production composition of the active Scene's complete Buddy roster. The authored sandbox
    /// Buddy remains the first actor so existing singular presentation/UI consumers keep a stable
    /// compatibility target while every later placement receives its own physics, damage, care,
    /// reaction, visual and persistent Buddy binding. All actors share the same account economy,
    /// room objects, grab tether and physical cursor-tool services and are ticked by the one routed
    /// SandboxRoot fixed tick through <see cref="SceneRuntimeHost"/>.
    ///
    /// Reserved LegacyPrimary IDs are deliberately absent from this path. They are migration IDs,
    /// not runtime identity. Active Scene document order is the only actor ordering authority.
    /// </summary>
    private void InitializeSceneRuntimeHostIfEnabled()
    {
        if (!ResolveBuildScope().IncludesScenes)
        {
            _sceneRuntime = null;
            return;
        }

        if (_runContext?.SceneProgress is not { } sceneProgress)
        {
            // Compatibility-only fallback for direct Scene-tagged development fixtures. Production
            // Bootstrap always supplies split SceneProgress before the sandbox enters the tree.
            SceneDocument fallback = LegacySceneMigrationPolicy.CreateDefaultScene(
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
            _sceneRuntime = new SceneRuntimeHost(fallback, [actor]);
            return;
        }

        SceneProgressBindingRegistry bindings = sceneProgress.CreateActiveBindings();
        if (bindings.Count == 0)
        {
            throw new InvalidOperationException(
                "The current Sandbox compatibility surface requires at least one Buddy placement in an active Scene.");
        }

        var actors = new List<BuddyActorRuntime>(bindings.Count);
        for (int index = 0; index < bindings.OrderedBindings.Count; index++)
        {
            SceneBuddyProgressBinding binding = bindings.OrderedBindings[index];
            BuddyActorRuntime actor = index == 0
                ? BindAuthoredSceneActor(binding)
                : ComposeAdditionalSceneActor(binding, index);
            actors.Add(actor);
        }

        _sceneRuntime = new SceneRuntimeHost(bindings, actors);
        Boundaries.LayoutApplied += OnSceneActorLayoutApplied;
    }

    private BuddyActorRuntime BindAuthoredSceneActor(SceneBuddyProgressBinding binding)
    {
        // Pipeline/Buddy external state was initialized earlier in SandboxRoot._Ready through
        // RunContext.ActiveBuddyProgress, which resolves this same first ordered placement.
        if (!ReferenceEquals(Pipeline.ProgressBinding.SplitProgress, binding.Progress.SplitProgress))
        {
            throw new InvalidOperationException(
                "Authored Scene Buddy progress does not match the active Scene's first placement.");
        }

        ApplyScenePlacement(Buddy, binding.Placement.Position, resetInitializedRig: true);
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

    private BuddyActorRuntime ComposeAdditionalSceneActor(
        SceneBuddyProgressBinding binding,
        int rosterIndex)
    {
        PackedScene packed = GD.Load<PackedScene>(SceneBuddyPackedScenePath)
            ?? throw new InvalidOperationException(
                $"Missing reusable Buddy actor scene at {SceneBuddyPackedScenePath}.");
        BuddyRoot buddy = packed.Instantiate<BuddyRoot>();
        string suffix = $"{rosterIndex}_{binding.Placement.PlacementId.ToString()[..8]}";
        buddy.Name = $"SceneBuddy_{suffix}";

        Vector2 world = ScenePlacementWorldPosition(binding.Placement.Position);
        buddy.Position = world;
        // Recovery initializes inside BuddyRoot._Ready, so author its per-actor safe state before
        // entering the tree. This prevents two Buddies from sharing the legacy centre recovery pose.
        buddy.Recovery.SafeBounds = Boundaries.InnerBounds;
        buddy.Recovery.SafePoseOrigin = world;
        AddChild(buddy);
        TrackSpawnedSceneActorNode(buddy);

        if (!buddy.IsInitialized)
            throw new InvalidOperationException("Spawned Scene Buddy did not initialize its reusable puppet composition.");

        buddy.Arbiter.Initialize(binding.Progress);
        buddy.ObjectInteraction.Initialize(
            Objects,
            binding.Progress,
            buddy.Arbiter.SocialTuning);
        buddy.AutonomousMotion.SetWalkableBounds(Boundaries.InnerBounds);

        var damage = new InteractionDamageComponent
        {
            Name = $"SceneDamage_{suffix}",
            Buddy = buddy,
            Grab = Grab,
            Profile = Pipeline.Profile,
            CareProfile = Pipeline.CareProfile,
        };
        AddChild(damage);
        TrackSpawnedSceneActorNode(damage);
        damage.Initialize(binding.Progress, Economy);

        var care = new CareStrokeComponent
        {
            Name = $"SceneCare_{suffix}",
            Pipeline = damage,
            Profile = CareStroke.Profile,
        };
        AddChild(care);
        TrackSpawnedSceneActorNode(care);
        care.Initialize();

        var toolReaction = new ToolReactionComponent
        {
            Name = $"SceneToolReaction_{suffix}",
            Buddy = buddy,
            Pipeline = damage,
            CareStroke = care,
            CursorTools = CursorTools,
            Profile = ToolReactions.Profile,
        };
        AddChild(toolReaction);
        TrackSpawnedSceneActorNode(toolReaction);
        toolReaction.Initialize();

        var reaction = new BuddyReactionComponent
        {
            Name = $"SceneReaction_{suffix}",
            Buddy = buddy,
            Pipeline = damage,
            Profile = Reactions.Profile,
            CareStroke = care,
            ToolReaction = toolReaction,
        };
        AddChild(reaction);
        TrackSpawnedSceneActorNode(reaction);
        reaction.Initialize();

        // The secondary actor owns a real 3D rig presenter. The authored first actor keeps the
        // richer singular expression/face chain until those components are extracted into the
        // actor bundle in a later packet; basic body presentation is already independent here.
        var visual = new BuddyVisualPresenter
        {
            Name = $"SceneVisual_{suffix}",
            Buddy = buddy,
            Profile = VisualPresenter.Profile,
            Visible = VisualPresenter.Visible,
        };
        AddChild(visual);
        TrackSpawnedSceneActorNode(visual);
        visual.Initialize();

        return new BuddyActorRuntime(
            binding.Placement.PlacementId,
            binding.Placement.BuddyIdentityId,
            buddy,
            damage,
            care,
            toolReaction,
            reaction,
            visual);
    }

    private void TrackSpawnedSceneActorNode(Node node) => _sceneSpawnedActorNodes.Add(node);

    private void ApplyScenePlacement(
        BuddyRoot buddy,
        CanonicalRoomPosition position,
        bool resetInitializedRig)
    {
        Vector2 world = ScenePlacementWorldPosition(position);
        buddy.Recovery.SafeBounds = Boundaries.InnerBounds;
        buddy.Recovery.SafePoseOrigin = world;
        buddy.AutonomousMotion.SetWalkableBounds(Boundaries.InnerBounds);
        if (resetInitializedRig)
            buddy.Rig.ResetToSafePose(world);
        else
            buddy.Position = world;
    }

    private Vector2 ScenePlacementWorldPosition(CanonicalRoomPosition position)
    {
        Rect2 bounds = Boundaries.InnerBounds;
        return bounds.Position + new Vector2(
            bounds.Size.X * position.X,
            bounds.Size.Y * position.Y);
    }

    private void OnSceneActorLayoutApplied(RoomLayout _layout, Rect2 innerBounds)
    {
        if (_sceneRuntime is null)
            return;

        foreach (BuddyActorRuntime actor in _sceneRuntime.Actors)
        {
            actor.Buddy.Recovery.SafeBounds = innerBounds;
            actor.Buddy.AutonomousMotion.SetWalkableBounds(innerBounds);
        }
    }

    private void SyncSharedCareInputToSceneActors()
    {
        if (_sceneRuntime is null || _sceneRuntime.Actors.Count <= 1)
            return;

        // Hardware input continues to enter through the existing singular care component. Mirror
        // only that raw held/cursor state to every other actor's independent geometry/model; each
        // CareStroke then decides contact against its own Buddy rig during the routed actor tick.
        for (int index = 1; index < _sceneRuntime.Actors.Count; index++)
        {
            CareStrokeComponent care = _sceneRuntime.Actors[index].CareStroke;
            care.SetStroke(CareStroke.IsHeld, CareStroke.Cursor);
            care.SetWiggle(CareStroke.IsWiggling);
        }
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

        SyncSharedCareInputToSceneActors();
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
