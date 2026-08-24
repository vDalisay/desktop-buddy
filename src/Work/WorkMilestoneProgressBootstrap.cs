using System;
using System.Collections.Generic;
using System.Globalization;
using DesktopBuddy.Domain.Work;
using Godot;

namespace DesktopBuddy.Work;

/// <summary>
/// Adds one deliberately small progress line to the Work CRT. The companion remains the HUD:
/// this presenter reads the coordinator's already-authoritative session/lifetime counters and
/// milestone claims, and never owns rewards or persistence itself.
/// </summary>
public partial class WorkMilestoneProgressBootstrap : Node
{
    private const double RefreshIntervalSeconds = 0.10;
    private static readonly WorkMilestoneCatalogue Catalogue = WorkMilestoneDefaults.Create();

    private WorkCompanionCoordinator? _coordinator;
    private WorkCompanionView? _view;
    private Label? _label;
    private double _untilRefresh;
    private string _lastText = string.Empty;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _untilRefresh -= Math.Max(0.0, delta);
        if (_untilRefresh > 0.0)
            return;
        _untilRefresh = RefreshIntervalSeconds;

        ResolveRuntime();
        if (!GodotObject.IsInstanceValid(_coordinator) || !_coordinator!.IsActive ||
            _coordinator.Session is null || !GodotObject.IsInstanceValid(_view))
        {
            RemoveLabel();
            return;
        }

        EnsureLabel();
        if (!GodotObject.IsInstanceValid(_label))
            return;

        string text = BuildProgressText(_coordinator, _view!.ShowLifetime);
        if (string.Equals(text, _lastText, StringComparison.Ordinal))
            return;
        _lastText = text;
        _label!.Text = text;
    }

    private void ResolveRuntime()
    {
        if (!GodotObject.IsInstanceValid(_coordinator))
            _coordinator = GetTree().Root.FindChild(nameof(WorkCompanionCoordinator), true, false) as WorkCompanionCoordinator;
        if (!GodotObject.IsInstanceValid(_view))
            _view = GetTree().Root.FindChild(nameof(WorkCompanionView), true, false) as WorkCompanionView;
    }

    private void EnsureLabel()
    {
        if (GodotObject.IsInstanceValid(_label))
            return;
        if (!GodotObject.IsInstanceValid(_view) ||
            _view!.FindChild("WorkCompanionRoot", true, false) is not Control root)
            return;

        _label = root.FindChild("WorkMilestoneProgress", false, false) as Label;
        if (GodotObject.IsInstanceValid(_label))
            return;

        _label = new Label
        {
            Name = "WorkMilestoneProgress",
            Position = new Vector2(420, 104),
            Size = new Vector2(117, 13),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipText = true,
            TooltipText = "Progress toward the next Work milestone.",
        };
        _label.AddThemeFontSizeOverride("font_size", 7);
        _label.AddThemeColorOverride("font_color", new Color(0.31f, 0.78f, 0.22f, 0.92f));
        root.AddChild(_label);
    }

    private void RemoveLabel()
    {
        if (GodotObject.IsInstanceValid(_label))
            _label!.QueueFree();
        _label = null;
        _view = null;
        _lastText = string.Empty;
    }

    private static string BuildProgressText(WorkCompanionCoordinator coordinator, bool lifetime)
    {
        WorkSessionState session = coordinator.Session!;
        WorkCounterSnapshot sessionCounters = session.Counters;
        WorkCounterSnapshot lifetimeCounters = coordinator.Progress.Lifetime;
        IReadOnlyCollection<string> sessionClaims = session.EarnedRepeatPerSessionMilestoneIds;
        IReadOnlyCollection<string> lifetimeClaims = coordinator.Progress.ClaimedLifetimeMilestoneIds;

        WorkMilestoneScope wantedScope = lifetime
            ? WorkMilestoneScope.Lifetime
            : WorkMilestoneScope.CurrentSession;
        foreach (WorkMilestoneDefinition definition in Catalogue.Definitions)
        {
            if (!definition.Visible || definition.Scope != wantedScope)
                continue;

            bool claimed = definition.RepeatPolicy switch
            {
                WorkMilestoneRepeatPolicy.OnceLifetime => Contains(lifetimeClaims, definition.Id),
                WorkMilestoneRepeatPolicy.RepeatPerSession => Contains(sessionClaims, definition.Id),
                _ => false,
            };
            if (claimed)
                continue;

            WorkCounterSnapshot counters = lifetime ? lifetimeCounters : sessionCounters;
            long current = counters.Value(definition.CounterKind);
            if (current >= definition.Threshold)
                continue;

            long rewardCredits = definition.RewardMilliCredits / 1000;
            return $"{CounterTag(definition.CounterKind)} {Compact(current)}/{Compact(definition.Threshold)} +{rewardCredits}C";
        }

        return "MILESTONES DONE";
    }

    private static bool Contains(IEnumerable<string> ids, string wanted)
    {
        foreach (string id in ids)
            if (string.Equals(id, wanted, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static string CounterTag(WorkCounterKind kind) => kind switch
    {
        WorkCounterKind.KeyboardPresses => "KEY",
        WorkCounterKind.MouseClicks => "CLK",
        _ => "ACT",
    };

    private static string Compact(long value)
    {
        value = Math.Max(0, value);
        if (value >= 1_000_000 && value % 1_000_000 == 0)
            return $"{value / 1_000_000}M";
        if (value >= 1_000 && value % 1_000 == 0)
            return $"{value / 1_000}K";
        if (value >= 1_000_000)
            return (value / 1_000_000d).ToString("0.#M", CultureInfo.InvariantCulture);
        if (value >= 1_000)
            return (value / 1_000d).ToString("0.#K", CultureInfo.InvariantCulture);
        return value.ToString(CultureInfo.InvariantCulture);
    }
}