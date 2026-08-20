using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
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
    private int _pistolSadTicks;
    private int _delightTicks;
    private int _fearTicks;
    private int _petSmileTicks;
    private int _learnedThreatFaceTicks;
    private int _laughTicks;
    private int _annoyedTickleTicks;

    /// <summary>
    /// How long each half of the annoyed-tickle face holds. Short enough to read as a squirm,
    /// long enough not to strobe (owner instruction 2026-08-19).
    /// </summary>
    private const double AnnoyedTickleFaceSwapSeconds = 0.35;

    public event Action<string>? FaceChanged;

    public bool IsInitialized { get; private set; }
    public string CurrentFace { get; private set; } = ":)";
    public float CurrentFear { get; private set; }
    public int PetSmileTicksRemaining => _petSmileTicks;
    public int LearnedThreatFaceTicksRemaining => _learnedThreatFaceTicks;
    public int PistolSadFaceTicksRemaining => _pistolSadTicks;
    public int PistolSadReactionCount { get; private set; }

    /// <summary>
    /// True while the buddy is past its tickle patience. The face alternates between the scowl
    /// and a laugh while this holds, so observers must watch this rather than sample one frame's
    /// emoticon.
    /// </summary>
    public bool IsTickleAnnoyed { get; private set; }

    /// <summary>Ticks left on the clean-catch laugh; non-zero means the buddy is laughing.</summary>
    public int LaughTicksRemaining => _laughTicks;

    /// <summary>Lifetime laughs, so a scenario can assert the reaction actually fired.</summary>
    public int LaughCount { get; private set; }

    /// <summary>Lifetime favourite-colour smiles, so a scenario can assert the reaction fired.</summary>
    public int ColourSmileCount { get; private set; }

    /// <summary>
    /// The buddy reached something in its favourite colour and is pleased about it. Same face
    /// and same duration as the completed-pet smile: it is the same quiet contentment, and a
    /// second smile vocabulary for one more trigger would be noise.
    /// </summary>
    public void PlayColourSmile()
    {
        if (!IsInitialized)
            return;
        _petSmileTicks = SecondsToTicks(Profile.PetCompletionFaceSeconds);
        ColourSmileCount++;
    }

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
        Buddy.ObjectInteraction.FunCatchDelighted += OnFunCatch;
        IsInitialized = true;
        Resolve();
    }

    public void PhysicsTick()
    {
        if (!IsInitialized) return;
        if (_painTicks > 0) _painTicks--;
        if (_pistolSadTicks > 0) _pistolSadTicks--;
        if (_delightTicks > 0) _delightTicks--;
        if (_fearTicks > 0) _fearTicks--;
        if (_petSmileTicks > 0) _petSmileTicks--;
        if (_laughTicks > 0) _laughTicks--;
        if (ToolReaction.IsLearnedGloveThreatActive)
            _learnedThreatFaceTicks = SecondsToTicks(Profile.LearnedThreatFaceTailSeconds);
        else if (_learnedThreatFaceTicks > 0)
            _learnedThreatFaceTicks--;
        Resolve();
    }

    public override void _ExitTree()
    {
        if (!IsInitialized) return;
        if (GodotObject.IsInstanceValid(Pipeline))
        {
            Pipeline.ImpactAccepted -= OnImpact;
            Pipeline.CareAwarded -= OnCare;
        }
        if (GodotObject.IsInstanceValid(Buddy) &&
            GodotObject.IsInstanceValid(Buddy.ObjectInteraction))
        {
            Buddy.ObjectInteraction.FunCatchDelighted -= OnFunCatch;
        }
    }

    private void OnImpact(AcceptedImpact impact)
    {
        if (impact.MoodEffect == ImpactMoodEffectKind.Enjoyment)
        {
            _delightTicks = SecondsToTicks(Profile.DelightFaceSeconds);
            return;
        }

        _painTicks = SecondsToTicks(Profile.PainFaceSeconds);
        _fearTicks = SecondsToTicks(Profile.AcuteFearSeconds);
        if (impact.ContentId == ContentIds.ToolPistol)
        {
            _pistolSadTicks = SecondsToTicks(Profile.PistolSadFaceSeconds);
            PistolSadReactionCount++;
        }
    }

    private void OnCare(CareKind kind)
    {
        if (kind == CareKind.Pet)
            _petSmileTicks = SecondsToTicks(Profile.PetCompletionFaceSeconds);
        else
            _delightTicks = SecondsToTicks(Profile.DelightFaceSeconds);
    }

    /// <summary>
    /// The buddy caught a thrown ball out of the air and still finds catch fun. Interest and
    /// cleanliness are already decided upstream; this only performs the laugh.
    /// </summary>
    private void OnFunCatch()
    {
        _laughTicks = SecondsToTicks(Profile.LaughFaceSeconds);
        LaughCount++;
    }

    private void Resolve()
    {
        float bandFear = Pipeline.MoodBand switch
        {
            MoodBand.Fearful => Profile.FearfulResistance,
            MoodBand.Wary => Profile.WaryResistance,
            _ => 0.0f,
        };
        bool selectedToolFeared = Pipeline.IsToolHarmful(Pipeline.SelectedTool);
        float resolvedFear = Mathf.Max(
            bandFear,
            _fearTicks > 0 || selectedToolFeared ? Profile.AcuteFearResistance : 0.0f);
        CurrentFear = Mathf.Clamp(FearOverride ?? resolvedFear, 0.0f, 1.0f);
        Buddy.GrabResistance.FearLevel = CurrentFear;

        IsTickleAnnoyed = Pipeline.SelectedTool == ToolId.Tickle &&
            CareStroke.TickleDisposition == TickleDisposition.Angry;
        _annoyedTickleTicks = IsTickleAnnoyed ? _annoyedTickleTicks + 1 : 0;

        string resolvedFace = Buddy.CurrentConsciousness == Consciousness.Unconscious ? "x_x" :
            _painTicks > 0 ? ">_<" :
            _pistolSadTicks > 0 ? ":(" :
            IsTickleAnnoyed ? AnnoyedTickleFace() :
            ToolReaction.IsDefending ? ">:(" :
            _fearTicks > 0 || _learnedThreatFaceTicks > 0 ? "o_o" :
            // Above the quieter positives but below pain, anger, and fear: a buddy that gets
            // punched mid-laugh shows the punch.
            _laughTicks > 0 ? "^_^" :
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

        if (!string.Equals(CurrentFace, resolvedFace, StringComparison.Ordinal))
        {
            CurrentFace = resolvedFace;
            FaceChanged?.Invoke(CurrentFace);
        }
        else
        {
            CurrentFace = resolvedFace;
        }

        Buddy.Rig.Head.SetFace(CurrentFace);
    }

    /// <summary>
    /// Past its patience the buddy is annoyed but still ticklish, so it flickers between the
    /// scowl and a laugh instead of holding one glare while it runs.
    /// </summary>
    private string AnnoyedTickleFace()
    {
        int half = Math.Max(1, SecondsToTicks(AnnoyedTickleFaceSwapSeconds));
        return (_annoyedTickleTicks / half) % 2 == 0 ? ">:(" : "^_^";
    }

    private static int SecondsToTicks(double seconds) => seconds <= 0.0
        ? 0
        : Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond, MidpointRounding.AwayFromZero));
}
