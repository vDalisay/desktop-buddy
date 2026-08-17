using System;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Work;

public partial class WorkCompanionView
{
    private WorkRewardOverlay? _rewardOverlay;

    /// <summary>
    /// Replacement-ready presentation cue fired after settled session credits increase. Subscribers
    /// may play owner-authored UI audio, but the event exposes no economy mutation path.
    /// </summary>
    public event Action<long>? RewardPulseRequested;

    private void EnsureRewardOverlay()
    {
        if (GodotObject.IsInstanceValid(_rewardOverlay))
            return;

        _rewardOverlay = new WorkRewardOverlay(this)
        {
            Name = "WorkRewardOverlay",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(_rewardOverlay);
    }

    /// <summary>
    /// Small CRT-adjacent reward readout. It observes only the amount the coordinator has already
    /// settled; it contains no milestone formulas and has no economy write path.
    /// </summary>
    private sealed partial class WorkRewardOverlay : Control
    {
        private static readonly Vector2 CompositionPosition = new(406.0f, 195.0f);
        private static readonly Vector2 CompositionSize = new(155.0f, 42.0f);
        private const float PulseLifetimeSeconds = 0.85f;

        private readonly WorkCompanionView _owner;
        private WorkCompanionCoordinator? _coordinator;
        private long _settledMilliCredits;
        private long _pulseMilliCredits;
        private float _pulseRemaining;

        public WorkRewardOverlay(WorkCompanionView owner)
        {
            _owner = owner;
            Size = CompositionSize;
        }

        public override void _Process(double delta)
        {
            SyncComposition();
            ResolveCoordinator();
            if (!GodotObject.IsInstanceValid(_coordinator))
                return;

            long settled = Math.Max(0, _coordinator!.SessionSettledMilliCreditsForPresentation);
            if (settled > _settledMilliCredits)
            {
                _pulseMilliCredits = settled - _settledMilliCredits;
                _pulseRemaining = PulseLifetimeSeconds;
                _owner.RewardPulseRequested?.Invoke(_pulseMilliCredits);
            }
            _settledMilliCredits = settled;
            _pulseRemaining = Math.Max(0.0f, _pulseRemaining - (float)Math.Max(0.0, delta));
            QueueRedraw();
        }

        public override void _Draw()
        {
            Color green = new(0.35f, 1.0f, 0.18f, 0.94f);
            Color dim = new(0.31f, 0.78f, 0.22f, 0.88f);
            string earned = $"EARNED +{FormatCredits(_settledMilliCredits)}";
            ThemeDB.FallbackFont.DrawString(
                GetCanvasItem(),
                new Vector2(0.0f, 13.0f),
                earned,
                HorizontalAlignment.Center,
                Size.X,
                10,
                dim);

            if (_pulseRemaining <= 0.0f || _pulseMilliCredits <= 0)
                return;

            float life = Mathf.Clamp(_pulseRemaining / PulseLifetimeSeconds, 0.0f, 1.0f);
            bool allowsMotion = !GodotObject.IsInstanceValid(_owner._sandbox) ||
                Win98MotionPolicy.Allows(_owner._sandbox.Shell.CurrentLocalSettings);
            float rise = allowsMotion ? (1.0f - life) * 7.0f : 0.0f;
            string pulse = $"+{FormatCredits(_pulseMilliCredits)} credits";
            ThemeDB.FallbackFont.DrawString(
                GetCanvasItem(),
                new Vector2(0.0f, 35.0f - rise),
                pulse,
                HorizontalAlignment.Center,
                Size.X,
                11,
                new Color(green.R, green.G, green.B, life));
        }

        private void SyncComposition()
        {
            Position = _owner._compositionOffset + CompositionPosition * _owner._compositionScale;
            Scale = Vector2.One * _owner._compositionScale;
        }

        private void ResolveCoordinator()
        {
            if (GodotObject.IsInstanceValid(_coordinator))
                return;
            _coordinator = GetTree().Root.FindChild(
                nameof(WorkCompanionCoordinator), recursive: true, owned: false) as WorkCompanionCoordinator;
        }

        private static string FormatCredits(long milliCredits)
        {
            double credits = Math.Max(0, milliCredits) / 1000.0;
            return credits.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
