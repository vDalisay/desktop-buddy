using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Buddy.Presentation;

/// <summary>
/// Resolves acute reaction above persistent mood, drives the head emoticon, and
/// maps fear/memory state into the existing physical grab-resistance component.
/// </summary>
[GlobalClass]
public partial class BuddyReactionComponent : Node
{
    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public ReactionProfile Profile { get; set; } = null!;
    [Export] public CareStrokeComponent CareStroke { get; set; } = null!;
    [Export] public ToolReactionComponent ToolReaction { get; set; } = null!;

    private int _painTicks;
    private int _delightTicks;
    private int _fearTicks;
    private int _petSmileTicks;
    private int _learnedThreatFaceTicks;

    public bool IsInitialized { get; private set; }
    public string CurrentFace { get; private set; } = ":)";
    public float CurrentFear { get; private set; }
    public int PetSmileTicksRemaining => _petSmileTicks;
    public int LearnedThreatFaceTicksRemaining => _learnedThreatFaceTicks;

    /// <summary>
    /// Development/scenario seam for measuring resistance independently of mood.
    /// Production leaves this null so mood, acute fear, and harmful history own fear.
    /// </summary>
    public float? FearOverride { get; set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0 ||
            !GodotObject.IsInstanceValid(CareStroke) || !CareStroke.IsInitialized ||
            !GodotObject.IsInstanceValid(ToolReaction) || !ToolReaction.IsInitialized)
            throw new InvalidOperationException("BuddyReactionComponent requires buddy, pipeline, and valid reaction tuning.");
        Pipeline.ImpactAccepted += OnImpact;
        Pipeline.CareAwarded += OnCare;
        IsInitialized = true;
        Resolve();
    }

    public void PhysicsTick()
    {
        if (!IsInitialized) return;
        if (_painTicks > 0) _painTicks--;
        if (_delightTicks > 0) _delightTicks--;
        if (_fearTicks > 0) _fearTicks--;
        if (_petSmileTicks > 0) _petSmileTicks--;
        if (ToolReaction.IsLearnedGloveThreatActive)
            _learnedThreatFaceTicks = SecondsToTicks(Profile.LearnedThreatFaceTailSeconds);
        else if (_learnedThreatFaceTicks > 0)
            _learnedThreatFaceTicks--;
        Resolve();
    }

    public override void _ExitTree()
    {
        if (!IsInitialized || !GodotObject.IsInstanceValid(Pipeline)) return;
        Pipeline.ImpactAccepted -= OnImpact;
        Pipeline.CareAwarded -= OnCare;
    }

    private void OnImpact(AcceptedImpact impact)
    {
        _painTicks = SecondsToTicks(Profile.PainFaceSeconds);
        _fearTicks = SecondsToTicks(Profile.AcuteFearSeconds);
    }

    private void OnCare(CareKind kind)
    {
        if (kind == CareKind.Pet)
            _petSmileTicks = SecondsToTicks(Profile.PetCompletionFaceSeconds);
        else
            _delightTicks = SecondsToTicks(Profile.DelightFaceSeconds);
    }

    private void Resolve()
    {
        float bandFear = Pipeline.MoodBand switch
        {
            MoodBand.Fearful => Profile.FearfulResistance,
            MoodBand.Wary => Profile.WaryResistance,
            _ => 0.0f,
        };
        bool selectedToolFeared = Pipeline.IsToolHarmful((int)Pipeline.SelectedTool);
        float resolvedFear = Mathf.Max(
            bandFear,
            _fearTicks > 0 || selectedToolFeared ? Profile.AcuteFearResistance : 0.0f);
        CurrentFear = Mathf.Clamp(FearOverride ?? resolvedFear, 0.0f, 1.0f);
        Buddy.GrabResistance.FearLevel = CurrentFear;

        CurrentFace = Buddy.CurrentConsciousness == Consciousness.Unconscious ? "x_x" :
            _painTicks > 0 ? ">_<" :
            Pipeline.SelectedTool == ToolId.Tickle &&
            CareStroke.TickleDisposition == TickleDisposition.Angry ? ">:(" :
            ToolReaction.IsDefending ? ">:(" :
            _fearTicks > 0 || _learnedThreatFaceTicks > 0 ? "o_o" :
            _petSmileTicks > 0 ? ":)" :
            CareStroke.IsPetRubbing ? ":3" :
            CareStroke.IsTickleContact ? "^_^" :
            _delightTicks > 0 ? "^_^" :
            Pipeline.MoodBand switch
            {
                MoodBand.Fearful => ":(",
                MoodBand.Wary => ":/",
                MoodBand.Content => ":)",
                MoodBand.Delighted => "^_^",
                _ => ":)",
            };
        Buddy.Rig.Head.SetFace(CurrentFace);
    }

    private static int SecondsToTicks(double seconds) => seconds <= 0.0
        ? 0
        : Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond, MidpointRounding.AwayFromZero));
}
