using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Scenes;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Semantic interaction-use and physics-trick observers. Gameplay exposes what actually happened;
/// this adapter translates committed edges into account achievement observations. Raw button/mouse
/// presses never count as use, and these observers never alter the tool/physics implementations.
/// </summary>
public sealed partial class AchievementBootstrap
{
    private const float BankShotMinimumHorizontalSpeed = 8.0f;
    private const float BankShotWallTolerancePixels = 3.0f;
    private const float AirborneFloorClearancePixels = 3.0f;

    private readonly Dictionary<int, float> _baseballPreviousVelocityX = [];
    private readonly HashSet<int> _confirmedBaseballRicochets = [];
    private bool _usageObserversWired;
    private bool _swordWasWielded;
    private bool _burnWasActive;
    private int _observedPunchCount;
    private int _observedLaunchCount;
    private int _observedRopeAttachCount;
    private double _airborneSeconds;
    private Dictionary<BuddyPlacementId, double> _previousAirborneSeconds = [];
    private Dictionary<BuddyPlacementId, double> _airborneActorSeconds = [];

    public override void _EnterTree()
    {
        if (_usageObserversWired || _sandbox is null)
            return;

        _sandbox.CursorGuns.ShotFired += OnUsageGunShotFired;
        _sandbox.CursorTools.SwingReleased += OnUsageSwingReleased;
        _sandbox.Grenades.PinPulled += OnUsageGrenadePinPulled;
        _sandbox.FireSprayer.SprayingChanged += OnUsageSprayingChanged;
        _sandbox.FireSprayer.Ignited += OnIgnited;
        _sandbox.Grab.Grabbed += OnGrabbed;

        _observedPunchCount = _sandbox.CursorTools.PunchCount;
        _observedLaunchCount = _sandbox.Launcher.LaunchCount;
        _observedRopeAttachCount = _sandbox.Ropes.AttachCount;
        _swordWasWielded = _sandbox.CursorTools.IsWieldingPointFirst;

        WireCustomizationObservers();
        TreeExiting += UnwireUsageObservers;
        _usageObserversWired = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_usageObserversWired || !GodotObject.IsInstanceValid(_sandbox))
            return;

        ObserveCommittedPunches();
        ObserveCommittedLaunches();
        ObserveRopeUse();
        ObserveSwordWield();
        ObserveBaseballRicochets();
        ObserveAirborne(delta);
        TickCustomizationObservers(delta);

        if (!_sandbox.FireSprayer.IsBurning)
            _burnWasActive = false;
    }

    private void OnUsageGunShotFired(GunProfile profile)
    {
        if (GodotObject.IsInstanceValid(profile) && !string.IsNullOrWhiteSpace(profile.ContentId))
            _scenes.Player.RecordContentUse(profile.ContentId);
    }

    private void OnUsageSwingReleased(float _charge, int _epoch)
    {
        string? contentId = _sandbox.CursorTools.ActiveContentId;
        if (!string.IsNullOrWhiteSpace(contentId))
            _scenes.Player.RecordContentUse(contentId);
    }

    private void OnUsageGrenadePinPulled(Vector2 _point) =>
        _scenes.Player.RecordContentUse(ContentIds.ToolGrenade);

    private void OnUsageSprayingChanged(bool spraying)
    {
        if (spraying)
            _scenes.Player.RecordContentUse(ContentIds.ToolFireSprayer);
    }

    private void OnCareMoodChanged(DesktopBuddy.Domain.Mood.CareKind kind, int _delta)
    {
        string? contentId = kind switch
        {
            DesktopBuddy.Domain.Mood.CareKind.Pet => ContentIds.ToolPet,
            DesktopBuddy.Domain.Mood.CareKind.Tickle => ContentIds.ToolTickle,
            _ => null,
        };
        if (contentId is not null)
            _scenes.Player.RecordContentUse(contentId);
    }

    private void OnGrabbed(RigidBody2D _target)
    {
        string selected = _scenes.Player.SelectedToolId;
        if (string.Equals(selected, ContentIds.ToolPowerGrab, StringComparison.Ordinal))
            _scenes.Player.RecordContentUse(ContentIds.ToolPowerGrab);
        else if (string.Equals(selected, ContentIds.ToolGrab, StringComparison.Ordinal))
            _scenes.Player.RecordContentUse(ContentIds.ToolGrab);
    }

    private void OnIgnited(Vector2 _point) => _burnWasActive = true;

    private void OnCareItemTaken(LooseObjectBody item)
    {
        if (GodotObject.IsInstanceValid(item) && !string.IsNullOrWhiteSpace(item.SemanticContentId))
            _scenes.Player.RecordContentUse(item.SemanticContentId);

        if (_burnWasActive && GodotObject.IsInstanceValid(item) &&
            GodotObject.IsInstanceValid(item.Profile) && item.Profile!.ClearsHarmfulStatuses)
        {
            _coordinator.RecordFireDrill();
        }

        _burnWasActive = _sandbox.FireSprayer.IsBurning;
    }

    private void ObserveCommittedPunches()
    {
        int current = _sandbox.CursorTools.PunchCount;
        if (current < _observedPunchCount)
        {
            _observedPunchCount = current;
            return;
        }

        while (_observedPunchCount < current)
        {
            _scenes.Player.RecordContentUse(ContentIds.ToolBoxingGlove);
            _observedPunchCount++;
        }
    }

    private void ObserveCommittedLaunches()
    {
        int current = _sandbox.Launcher.LaunchCount;
        if (current < _observedLaunchCount)
        {
            _observedLaunchCount = current;
            return;
        }
        if (_observedLaunchCount >= current)
            return;

        LooseObjectBody? launched = _sandbox.Launcher.LastLaunchedBody;
        if (GodotObject.IsInstanceValid(launched))
        {
            string contentId = launched!.SemanticContentId;
            if (string.Equals(contentId, ContentIds.ToolBaseball, StringComparison.Ordinal) ||
                string.Equals(contentId, ContentIds.ToolSoccerBall, StringComparison.Ordinal))
            {
                _scenes.Player.RecordContentUse(contentId);
            }
        }

        _observedLaunchCount = current;
    }

    private void ObserveRopeUse()
    {
        int current = _sandbox.Ropes.AttachCount;
        if (current < _observedRopeAttachCount)
        {
            _observedRopeAttachCount = current;
            return;
        }

        while (_observedRopeAttachCount < current)
        {
            _scenes.Player.RecordContentUse(ContentIds.ToolRopeSuspender);
            _observedRopeAttachCount++;
        }
    }

    private void ObserveSwordWield()
    {
        bool wielded = _sandbox.CursorTools.IsWieldingPointFirst;
        if (wielded && !_swordWasWielded &&
            string.Equals(_sandbox.CursorTools.ActiveContentId, ContentIds.ToolSword, StringComparison.Ordinal))
        {
            _scenes.Player.RecordContentUse(ContentIds.ToolSword);
        }
        _swordWasWielded = wielded;
    }

    private void ObserveBaseballRicochets()
    {
        if (_coordinator.Store.IsQualified(AchievementIds.BankShot))
            return;

        Rect2 bounds = _sandbox.Boundaries.InnerBounds;
        for (int slot = 0; slot < LooseObjectRegistry.Capacity; slot++)
        {
            LooseObjectBody? body = _sandbox.Objects.BodyAt(slot);
            if (!GodotObject.IsInstanceValid(body) || body!.RuntimeId == 0 ||
                !string.Equals(body.SemanticContentId, ContentIds.ToolBaseball, StringComparison.Ordinal) ||
                !_sandbox.Objects.TryGetSnapshot(body.RuntimeId, out var snapshot) ||
                snapshot.ThrowToken == 0)
            {
                continue;
            }

            int interactionId = body.InteractionId;
            float currentVelocityX = body.LinearVelocity.X;
            if (_baseballPreviousVelocityX.TryGetValue(interactionId, out float previousVelocityX))
            {
                bool nearLeftWall =
                    body.GlobalPosition.X - body.Radius <= bounds.Position.X + BankShotWallTolerancePixels;
                bool nearRightWall =
                    body.GlobalPosition.X + body.Radius >= bounds.End.X - BankShotWallTolerancePixels;

                if (AchievementPhysicsRules.IsSideWallRicochet(
                        previousVelocityX,
                        currentVelocityX,
                        nearLeftWall,
                        nearRightWall,
                        BankShotMinimumHorizontalSpeed))
                {
                    _confirmedBaseballRicochets.Add(interactionId);
                }
            }

            _baseballPreviousVelocityX[interactionId] = currentVelocityX;
        }

        if (_baseballPreviousVelocityX.Count > LooseObjectRegistry.Capacity * 4)
        {
            _baseballPreviousVelocityX.Clear();
            _confirmedBaseballRicochets.Clear();
        }
    }

    private void OnBankShotImpact(AcceptedImpact impact)
    {
        if (!string.Equals(impact.ContentId, ContentIds.ToolBaseball, StringComparison.Ordinal))
            return;

        if (_confirmedBaseballRicochets.Remove(impact.InteractionId))
            _coordinator.RecordBankShot();
        _baseballPreviousVelocityX.Remove(impact.InteractionId);
    }

    private void ObserveAirborne(double delta)
    {
        if (_coordinator.Store.IsQualified(AchievementIds.AirBud))
            return;

        if (_sandbox.Grab.IsGrabbing || !_sandbox.Shell.GameplayInputEnabled ||
            _sandbox.Lifecycle.PauseCoordinator.IsPaused || _sandbox.Lifecycle.IsEditorModeActive)
        {
            _airborneSeconds = 0.0;
            _previousAirborneSeconds.Clear();
            return;
        }

        Rect2 bounds = _sandbox.Boundaries.InnerBounds;
        float floor = bounds.End.Y;
        double step = Math.Max(0.0, delta);
        double longestAirborne = 0.0;
        _airborneActorSeconds.Clear();

        // Any cast member can earn this, and each keeps its own flight timer so two Buddies taking
        // turns in the air cannot add up to one long flight.
        foreach ((BuddyPlacementId placementId, BuddyRoot buddy) in AirborneCandidates())
        {
            bool airborne = true;
            foreach (var part in buddy.Rig.Parts)
            {
                if (part.GlobalPosition.Y + part.Radius >= floor - AirborneFloorClearancePixels ||
                    part.GetCollidingBodies().Count > 0)
                {
                    airborne = false;
                    break;
                }
            }

            if (!airborne)
                continue;
            _previousAirborneSeconds.TryGetValue(placementId, out double carried);
            double seconds = carried + step;
            _airborneActorSeconds[placementId] = seconds;
            longestAirborne = Math.Max(longestAirborne, seconds);
        }

        (_previousAirborneSeconds, _airborneActorSeconds) = (_airborneActorSeconds, _previousAirborneSeconds);
        _airborneSeconds = longestAirborne;
        if (longestAirborne > 0.0)
            _coordinator.RecordAirborneSeconds(longestAirborne);

        IEnumerable<(BuddyPlacementId PlacementId, BuddyRoot Buddy)> AirborneCandidates()
        {
            if (_sandbox.ActiveSceneRuntime is { } runtime && runtime.Actors.Count > 0)
            {
                foreach (BuddyActorRuntime actor in runtime.Actors)
                    yield return (actor.PlacementId, actor.Buddy);
                yield break;
            }
            yield return (default, _sandbox.Buddy);
        }
    }

    private void UnwireUsageObservers()
    {
        TreeExiting -= UnwireUsageObservers;
        UnwireCustomizationObservers();
        if (!_usageObserversWired || !GodotObject.IsInstanceValid(_sandbox))
        {
            _usageObserversWired = false;
            return;
        }

        if (GodotObject.IsInstanceValid(_sandbox.CursorGuns))
            _sandbox.CursorGuns.ShotFired -= OnUsageGunShotFired;
        if (GodotObject.IsInstanceValid(_sandbox.CursorTools))
            _sandbox.CursorTools.SwingReleased -= OnUsageSwingReleased;
        if (GodotObject.IsInstanceValid(_sandbox.Grenades))
            _sandbox.Grenades.PinPulled -= OnUsageGrenadePinPulled;
        if (GodotObject.IsInstanceValid(_sandbox.FireSprayer))
        {
            _sandbox.FireSprayer.SprayingChanged -= OnUsageSprayingChanged;
            _sandbox.FireSprayer.Ignited -= OnIgnited;
        }
        if (GodotObject.IsInstanceValid(_sandbox.Grab))
            _sandbox.Grab.Grabbed -= OnGrabbed;

        _baseballPreviousVelocityX.Clear();
        _confirmedBaseballRicochets.Clear();
        _airborneSeconds = 0.0;
        _previousAirborneSeconds.Clear();
        _usageObserversWired = false;
    }
}
