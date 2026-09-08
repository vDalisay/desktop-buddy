using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Semantic interaction-use and physics-trick observers. These deliberately live beside the main
/// achievement adapter rather than inside gameplay implementations: gameplay exposes what genuinely
/// happened, while achievement policy decides which semantic edges satisfy achievement rules.
/// Damage remains a separate signal so Variety Hour does not collapse into Try Everything Once.
/// </summary>
public partial class AchievementBootstrap
{
    private const float BankShotMinimumHorizontalSpeed = 8.0f;

    private readonly Dictionary<int, float> _baseballPreviousVelocityX = [];
    private readonly HashSet<int> _confirmedBaseballRicochets = [];
    private bool _usageObserversWired;
    private bool _swordWasWielded;
    private int _observedPunchCount;
    private int _observedLaunchCount;

    public override void _EnterTree()
    {
        base._EnterTree();
        if (_usageObserversWired || _sandbox is null)
            return;

        // These are committed semantic edges, not raw mouse/button presses. A dry fire therefore
        // does not count, while a real round that misses Buddy still does.
        _sandbox.CursorGuns.ShotFired += OnUsageGunShotFired;
        _sandbox.CursorTools.SwingReleased += OnUsageSwingReleased;
        _sandbox.Grenades.PinPulled += OnUsageGrenadePinPulled;
        _sandbox.FireSprayer.SprayingChanged += OnUsageSprayingChanged;

        // Subscribe before AchievementBootstrap._Ready wires its ordinary impact handler. This
        // gate removes the old near-wall candidate unless a real horizontal rebound was observed,
        // so the existing accepted-hit path can remain the single qualification point.
        _sandbox.Pipeline.ImpactAccepted += OnBankShotGateImpact;

        // Punch and pullback-launch components already expose monotonic telemetry but no event.
        // Observe their committed counters instead of changing the gameplay APIs solely for an
        // achievement consumer. One routed physics tick can commit at most one of either action.
        _observedPunchCount = _sandbox.CursorTools.PunchCount;
        _observedLaunchCount = _sandbox.Launcher.LaunchCount;
        _swordWasWielded = _sandbox.CursorTools.IsWieldingPointFirst;

        TreeExiting += UnwireUsageObservers;
        _usageObserversWired = true;
    }

    public override void _PhysicsProcess(double _delta)
    {
        if (!_usageObserversWired || !GodotObject.IsInstanceValid(_sandbox))
            return;

        ObserveCommittedPunches();
        ObserveCommittedLaunches();
        ObserveSwordWield();
        ObserveBaseballRicochets();
        EnforceAirBudContactFreeState();
    }

    private void OnUsageGunShotFired(GunProfile profile)
    {
        if (GodotObject.IsInstanceValid(profile) && !string.IsNullOrWhiteSpace(profile.ContentId))
            _progress.RecordContentUse(profile.ContentId);
    }

    private void OnUsageSwingReleased(float _charge, int _epoch)
    {
        string? contentId = _sandbox.CursorTools.ActiveContentId;
        if (!string.IsNullOrWhiteSpace(contentId))
            _progress.RecordContentUse(contentId);
    }

    private void OnUsageGrenadePinPulled(Vector2 _point) =>
        _progress.RecordContentUse(ContentIds.ToolGrenade);

    private void OnUsageSprayingChanged(bool spraying)
    {
        if (spraying)
            _progress.RecordContentUse(ContentIds.ToolFireSprayer);
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
            _progress.RecordContentUse(ContentIds.ToolBoxingGlove);
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
            // Consumable launchables count when Buddy actually consumes them; an unpinned grenade
            // counts when its pin is pulled. The two sports balls are genuinely used by launching.
            if (string.Equals(contentId, ContentIds.ToolBaseball, StringComparison.Ordinal) ||
                string.Equals(contentId, ContentIds.ToolSoccerBall, StringComparison.Ordinal))
            {
                _progress.RecordContentUse(contentId);
            }
        }

        // PullbackLauncher accepts at most one release per routed tick. If this observer ever sees
        // a larger jump after a component reset/reparent, recording the latest committed launch is
        // still sufficient for Variety Hour's monotonic "used at least once" state.
        _observedLaunchCount = current;
    }

    private void ObserveSwordWield()
    {
        bool wielded = _sandbox.CursorTools.IsWieldingPointFirst;
        if (wielded && !_swordWasWielded &&
            string.Equals(_sandbox.CursorTools.ActiveContentId, ContentIds.ToolSword, StringComparison.Ordinal))
        {
            _progress.RecordContentUse(ContentIds.ToolSword);
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
                    body.GlobalPosition.X - body.Radius <=
                    bounds.Position.X + BankShotWallTolerancePixels;
                bool nearRightWall =
                    body.GlobalPosition.X + body.Radius >=
                    bounds.End.X - BankShotWallTolerancePixels;

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

        // Runtime capacity is 24, but interaction IDs are monotonic across respawns. Keep stale
        // historical samples bounded without allocating on the normal physics path.
        if (_baseballPreviousVelocityX.Count > LooseObjectRegistry.Capacity * 4)
        {
            _baseballPreviousVelocityX.Clear();
            _confirmedBaseballRicochets.Clear();
        }
    }

    private void OnBankShotGateImpact(AcceptedImpact impact)
    {
        if (!string.Equals(impact.ContentId, ContentIds.ToolBaseball, StringComparison.Ordinal))
            return;

        int interactionId = impact.InteractionId;
        if (_confirmedBaseballRicochets.Remove(interactionId))
            _baseballsThatTouchedWall.Add(interactionId);
        else
            _baseballsThatTouchedWall.Remove(interactionId);

        _baseballPreviousVelocityX.Remove(interactionId);
    }

    private void EnforceAirBudContactFreeState()
    {
        if (_airborneSeconds <= 0.0 || _coordinator.Store.IsQualified(AchievementIds.AirBud))
            return;

        // The geometric check in the main adapter cheaply rejects ordinary grounded play. Only
        // once that timer is actually accumulating do we pay for collision queries. Any external
        // contact means Buddy is supported/touching something rather than freely airborne.
        foreach (var part in _sandbox.Buddy.Rig.Parts)
        {
            if (part.GetCollidingBodies().Count == 0)
                continue;

            _airborneSeconds = 0.0;
            return;
        }
    }

    private void UnwireUsageObservers()
    {
        TreeExiting -= UnwireUsageObservers;
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
            _sandbox.FireSprayer.SprayingChanged -= OnUsageSprayingChanged;
        if (GodotObject.IsInstanceValid(_sandbox.Pipeline))
            _sandbox.Pipeline.ImpactAccepted -= OnBankShotGateImpact;

        _baseballPreviousVelocityX.Clear();
        _confirmedBaseballRicochets.Clear();
        _usageObserversWired = false;
    }
}
