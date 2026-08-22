using System;
using System.Globalization;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Economy;
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
    private EconomyService _economy = null!;
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// Whether an editor owns the screen. The balance and the reward burst belong to the play
    /// screen; Buddy Studio and Paint Buddy have their own money readouts, and a "+$1" from a
    /// hit landed before the window opened was still floating over the Studio afterwards
    /// (owner report 2026-08-21).
    ///
    /// <para>A flag the presenter itself honours, rather than the host reaching in and setting
    /// <c>Visible</c>: that lookup ran by node name from the scene root and quietly did nothing
    /// when it missed, and a feedback burst arriving later turned its label back on regardless.</para>
    /// </summary>
    public static bool SuppressedByEditor { get; set; }

    /// <summary>
    /// Whether the Win98 shell has taken the readout over. Its command bar mirrors this
    /// presenter's balance and reward into the menu strip and hides this panel — but this
    /// presenter set its own <c>Visible</c> every frame and turned itself straight back on, so
    /// the old floating counter kept showing under the menu bar (owner report 2026-08-22).
    /// The value still lives here; only the panel is retired.
    /// </summary>
    public static bool SuppressedByShell { get; set; }

    public void Initialize(EconomyService economy)
    {
        if (!GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(BalanceLabel) || !GodotObject.IsInstanceValid(RewardLabel))
            throw new InvalidOperationException("MoneyHudPresenter requires an initialized pipeline and labels.");
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        Pipeline.ImpactAccepted += OnImpact;
        Pipeline.RewardFeedbackEmitted += OnFeedback;
        _economy.BalanceChanged += OnBalanceChanged;
        RewardLabel.Visible = false;
        RefreshBalance();
        IsInitialized = true;
    }

    public override void _Process(double delta)
    {
        Visible = !SuppressedByEditor && !SuppressedByShell;
        if (_remaining <= 0) return;
        _remaining -= delta;
        if (_remaining <= 0) RewardLabel.Visible = false;
    }

    public override void _ExitTree()
    {
        // The economy service outlives every node, so its unsubscribe must not be
        // gated on the pipeline node still being valid.
        if (_economy is not null)
            _economy.BalanceChanged -= OnBalanceChanged;
        if (!GodotObject.IsInstanceValid(Pipeline)) return;
        Pipeline.ImpactAccepted -= OnImpact;
        Pipeline.RewardFeedbackEmitted -= OnFeedback;
    }

    private void OnImpact(AcceptedImpact impact) => RefreshBalance();
    private void OnBalanceChanged(long _) => RefreshBalance();

    private void OnFeedback(RewardFeedback feedback)
    {
        if (SuppressedByEditor)
            return;

        // Keep sub-credit precision in the ledger so economy pacing does not change, but never
        // expose a decimal damage reward to the player. The visible burst is the ceiling of the
        // coalesced reward: +$0.01..+$1.00 reads +$1, +$1.01..+$2.00 reads +$2, etc.
        long wholeCredits = feedback.MilliCredits <= 0
            ? 0
            : (long)Math.Ceiling(feedback.MilliCredits / 1000.0);
        RewardLabel.Text = "+$" + wholeCredits.ToString(CultureInfo.InvariantCulture);
        RewardLabel.Visible = true;
        _remaining = FeedbackSeconds;
    }

    private void RefreshBalance() => BalanceLabel.Text = "$" + Pipeline.BalanceCredits.ToString(CultureInfo.InvariantCulture);
}