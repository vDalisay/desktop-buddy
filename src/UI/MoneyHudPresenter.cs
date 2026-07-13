using System;
using System.Globalization;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.UI;

/// <summary>Compact whole-credit balance plus coalesced damage-reward feedback.</summary>
[GlobalClass]
public partial class MoneyHudPresenter : PanelContainer
{
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public Label BalanceLabel { get; set; } = null!;
    [Export] public Label RewardLabel { get; set; } = null!;
    [Export(PropertyHint.Range, "0.1,5,0.1")] public double FeedbackSeconds { get; set; } = 1.0;

    private double _remaining;
    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(BalanceLabel) || !GodotObject.IsInstanceValid(RewardLabel))
            throw new InvalidOperationException("MoneyHudPresenter requires an initialized pipeline and labels.");
        Pipeline.ImpactAccepted += OnImpact;
        Pipeline.RewardFeedbackEmitted += OnFeedback;
        RewardLabel.Visible = false;
        RefreshBalance();
        IsInitialized = true;
    }

    public override void _Process(double delta)
    {
        if (_remaining <= 0) return;
        _remaining -= delta;
        if (_remaining <= 0) RewardLabel.Visible = false;
    }

    public override void _ExitTree()
    {
        if (!GodotObject.IsInstanceValid(Pipeline)) return;
        Pipeline.ImpactAccepted -= OnImpact;
        Pipeline.RewardFeedbackEmitted -= OnFeedback;
    }

    private void OnImpact(AcceptedImpact impact) => RefreshBalance();

    private void OnFeedback(RewardFeedback feedback)
    {
        RewardLabel.Text = "+$" + (feedback.MilliCredits / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
        RewardLabel.Visible = true;
        _remaining = FeedbackSeconds;
    }

    private void RefreshBalance() => BalanceLabel.Text = "$" + Pipeline.BalanceCredits.ToString(CultureInfo.InvariantCulture);
}
