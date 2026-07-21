using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Interaction;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Grab;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Interaction;

/// <summary>One fully scored damage event, published after the ledger and mood applied it.</summary>
public readonly record struct AcceptedImpact(
    int InteractionId,
    int ContentId,
    BuddyPart Part,
    PayoutRegion Region,
    float RawImpulse,
    float Impulse,
    float RelativeSpeed,
    Vector2 Point,
    Vector2 Normal,
    float Pain,
    long MilliCredits,
    DamageConsciousness ConsciousnessAtAcceptance,
    bool Guarded,
    bool IsBuddyGrabbed,
    bool KnockoutTriggered,
    double TimeSeconds);

/// <summary>
/// One contact episode accepted by the router before the empirical pain curve.
/// Zero-pain episodes remain visible in laboratory telemetry so threshold tuning
/// can prove resting contacts do not print money or poison harmful memory.
/// </summary>
public readonly record struct AcceptedContactEpisode(
    int InteractionId,
    int ContentId,
    BuddyPart Part,
    PayoutRegion Region,
    float Impulse,
    float RelativeSpeed,
    Vector2 Point,
    Vector2 Normal,
    double TimeSeconds);

/// <summary>
/// The contact→pain→money/mood pipeline (RAGDOLL §7–§8, ARCHITECTURE §7 steps 7–8,
/// §11). Owns the Domain workers — impact router, pain curve, knockout window,
/// reward ledger, mood/care models, tool selection — and runs them on the owning
/// root's fixed tick. Consumes the raw solver contacts each <see cref="PuppetPartBody"/>
/// buffered during the previous physics step (one-tick trail accepted per §23),
/// resolves source attribution, and applies accepted events in spec order: the
/// payout multiplier uses consciousness <b>at acceptance time</b>, harm marks the
/// tool harmful only when pain is positive, and a triggered knockout drives
/// <see cref="BuddyRoot.SetConsciousness"/> for exactly the fixed 4 s window.
/// Simulation time is derived from the routed integer tick count at 120 Hz —
/// pausing the laboratory freezes it, and no float accumulates.
/// </summary>
[GlobalClass]
public partial class InteractionDamageComponent : Node
{
    private readonly Dictionary<ulong, (int InteractionId, int ContentId)> _untaggedSources = new();

    private ImpactRouter _router = null!;
    private PainCurve _curve = null!;
    private PainKnockoutModel _knockout = null!;
    private RewardLedger _ledger = null!;
    private MoodModel _mood = null!;
    private CareModel _care = null!;
    private ToolSelection _tools = null!;
    private double _fixedDelta;
    private long _ticks;
    private bool _knockoutDrivenUnconscious;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
    [Export] public PainConversionProfile Profile { get; set; } = null!;
    [Export] public CareInteractionProfile CareProfile { get; set; } = null!;

    public event Action<AcceptedContactEpisode>? EpisodeAccepted;
    public event Action<AcceptedImpact>? ImpactAccepted;
    public event Action<double>? KnockoutStarted;
    public event Action<double>? KnockoutEnded;
    public event Action<RewardFeedback>? RewardFeedbackEmitted;
    public event Action<CareKind>? CareAwarded;
    public event Action<CareKind, int>? CareMoodChanged;
    public event Action<ToolId, ToolId>? ToolChanged;

    public bool IsInitialized { get; private set; }
    public double NowSeconds => _ticks * _fixedDelta;

    // Pipeline telemetry consumed by scenarios, the lab panel, and the M3 HUD.
    public long RawContactCount { get; private set; }
    public long AcceptedEpisodeCount { get; private set; }
    public long ScoredImpactCount { get; private set; }
    public long FeedbackCount { get; private set; }
    public long CareAwardCount { get; private set; }
    public long CarePenaltyCount { get; private set; }
    public float MaxRawImpulse { get; private set; }
    public AcceptedImpact LastImpact { get; private set; }
    public AcceptedContactEpisode LastEpisode { get; private set; }
    public RewardFeedback LastFeedback { get; private set; }
    public PainKnockoutState LastKnockoutState { get; private set; }

    public int KnockoutCount => _knockout.KnockoutCount;
    public long BalanceMilliCredits => _ledger.BalanceMilliCredits;
    public long BalanceCredits => _ledger.BalanceCredits;
    public float Mood => _mood.Mood;
    public MoodBand MoodBand => _mood.Band;
    public ToolId SelectedTool => _tools.Selected;

    public bool IsToolHarmful(int contentId) => _mood.IsToolHarmful(contentId);

    public double PetDistanceProgress => _care.PetDistanceProgress;
    public double PetValidSecondsProgress => _care.PetValidSecondsProgress;
    public double TickleContactSeconds => _care.TickleContactSeconds;
    public TickleDisposition TickleDisposition => _care.TickleDisposition;

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(Grab))
        {
            throw new InvalidOperationException(
                "InteractionDamageComponent requires an initialized buddy composition and grab tether.");
        }

        if (!GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0 ||
            !GodotObject.IsInstanceValid(CareProfile) || CareProfile.Validate().Count > 0)
        {
            throw new InvalidOperationException(
                "InteractionDamageComponent requires valid pain and care profiles.");
        }

        _router = new ImpactRouter(ImpactRouter.DefaultReArmSeconds, Profile.MinimumImpulse);
        _curve = Profile.BuildCurve();
        _knockout = new PainKnockoutModel();
        _ledger = new RewardLedger(Profile.CashPerPain);
        _mood = new MoodModel();
        _care = new CareModel(CareProfile.ToTuning());
        _tools = new ToolSelection();
        _fixedDelta = 1.0 / Engine.PhysicsTicksPerSecond;

        // Centralized hard reposition releases contacts and restores a safe pose:
        // transient interaction state clears, persistent mood/history survive (§5, §8.1).
        Buddy.Recovery.HardRecovered += OnHardRecovered;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (IsInitialized && GodotObject.IsInstanceValid(Buddy) && GodotObject.IsInstanceValid(Buddy.Recovery))
        {
            Buddy.Recovery.HardRecovered -= OnHardRecovered;
        }
    }

    /// <summary>Called only from the owning root's routed fixed tick.</summary>
    public void PhysicsTick()
    {
        RequireInitialized();
        _ticks++;
        double now = NowSeconds;

        IReadOnlyList<PuppetPartBody> parts = Buddy.Rig.Parts;
        for (int partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            PuppetPartBody part = parts[partIndex];
            int contactCount = part.PendingContactCount;
            for (int index = 0; index < contactCount; index++)
            {
                RawPartContact contact = part.GetPendingContact(index);
                RawContactCount++;
                if (contact.Impulse > MaxRawImpulse)
                {
                    MaxRawImpulse = contact.Impulse;
                }

                if (!TryResolveSource(contact.Collider, out int interactionId, out int contentId))
                {
                    continue;
                }

                var sample = new ContactSample(
                    interactionId,
                    (BuddyPart)(int)part.PartId,
                    contact.Impulse,
                    contact.RelativeSpeed,
                    now);
                ImpactSample? accepted = _router.Offer(sample);
                if (accepted is null)
                {
                    continue;
                }

                AcceptedEpisodeCount++;
                PayoutRegion region = PayoutRegions.Of(accepted.Value.TargetPart);
                var episode = new AcceptedContactEpisode(
                    accepted.Value.SourceInteractionId,
                    contentId,
                    accepted.Value.TargetPart,
                    region,
                    accepted.Value.Impulse,
                    accepted.Value.RelativeVelocity,
                    contact.Point,
                    contact.Normal,
                    now);
                LastEpisode = episode;
                EpisodeAccepted?.Invoke(episode);
                ApplyAcceptedImpact(accepted.Value, contact, contentId, region, now);
            }

            part.ClearPendingContacts();
        }

        PainKnockoutState state = _knockout.Update(now);
        LastKnockoutState = state;
        if (_knockoutDrivenUnconscious && !state.KnockoutActive)
        {
            _knockoutDrivenUnconscious = false;
            Buddy.SetConsciousness(Consciousness.Conscious);
            KnockoutEnded?.Invoke(now);
        }

        _mood.Drift(_fixedDelta);

        RewardFeedback? feedback = _ledger.PollFeedback(now);
        if (feedback is RewardFeedback burst)
        {
            LastFeedback = burst;
            FeedbackCount++;
            RewardFeedbackEmitted?.Invoke(burst);
        }
    }

    /// <summary>
    /// Adds weighted Pet distance plus valid-contact time and applies completion mood.
    /// </summary>
    public PetCareResult AccumulatePet(
        double travelledDistance,
        bool favoriteSpot,
        double validContactSeconds)
    {
        RequireInitialized();
        PetCareResult result = _care.AccumulatePet(
            travelledDistance,
            favoriteSpot,
            validContactSeconds);
        ApplyCareMood(CareKind.Pet, result.PositiveMoodAwards, 0);
        return result;
    }

    /// <summary>Advances friendly/angry Tickle contact and its no-contact cooldown.</summary>
    public TickleCareResult TickTickle(bool validContact, double elapsedSeconds)
    {
        RequireInitialized();
        TickleCareResult result = _care.TickTickle(validContact, elapsedSeconds);
        ApplyCareMood(CareKind.Tickle, result.PositiveMoodAwards, result.NegativeMoodAwards);
        return result;
    }

    private void ApplyCareMood(CareKind kind, int positiveAwards, int negativeAwards)
    {
        for (int index = 0; index < positiveAwards; index++)
        {
            CareAwardCount++;
            _mood.ApplyMoodDelta(1.0f);
            CareAwarded?.Invoke(kind);
            CareMoodChanged?.Invoke(kind, 1);
        }

        for (int index = 0; index < negativeAwards; index++)
        {
            CarePenaltyCount++;
            _mood.ApplyMoodDelta(-1.0f);
            CareMoodChanged?.Invoke(kind, -1);
        }
    }

    /// <summary>Explicit tool pick; Work/Play transitions never route here (M2 invariant).</summary>
    public void SelectTool(ToolId tool)
    {
        RequireInitialized();
        ToolId previous = _tools.Selected;
        if (previous == tool)
        {
            return;
        }

        _tools.Select(tool);
        ToolChanged?.Invoke(previous, tool);
    }

    private void ApplyAcceptedImpact(
        in ImpactSample accepted,
        in RawPartContact contact,
        int contentId,
        PayoutRegion region,
        double now)
    {
        bool guarded = contentId == (int)ToolId.BoxingGlove &&
                       accepted.TargetPart is BuddyPart.LeftHand or BuddyPart.RightHand &&
                       Buddy.CurrentDriveIntent.GuardActive;
        float effectiveImpulse = guarded
            ? accepted.Impulse * Buddy.CurrentDriveIntent.GuardAbsorption
            : accepted.Impulse;
        float pain = _curve.PainFor(effectiveImpulse);
        if (pain <= 0.0f)
        {
            // Above the episode threshold but at/below the curve floor: a valid
            // contact episode that scores nothing — it must not pay, must not
            // mark the source harmful, and must not enter the knockout window.
            return;
        }

        PainAcceptance acceptance = _knockout.RegisterPain(pain, now);
        long milli = _ledger.Accept(pain, region, acceptance.ConsciousnessAtAcceptance, now);
        _mood.RegisterHarm(contentId, pain);
        ScoredImpactCount++;

        GrabState grab = Grab.CurrentGrab;
        bool buddyPartGrabbed = grab.Active && grab.Target is PuppetPartBody;

        var impact = new AcceptedImpact(
            accepted.SourceInteractionId,
            contentId,
            accepted.TargetPart,
            region,
            accepted.Impulse,
            effectiveImpulse,
            accepted.RelativeVelocity,
            contact.Point,
            contact.Normal,
            pain,
            milli,
            acceptance.ConsciousnessAtAcceptance,
            guarded,
            buddyPartGrabbed,
            acceptance.KnockoutTriggered,
            now);
        LastImpact = impact;
        if (accepted.TargetPart == BuddyPart.Head)
            Buddy.ActiveDrive.NotifyHeadDisturbed();
        Buddy.InterruptBehaviorActivity();
        ImpactAccepted?.Invoke(impact);

        if (acceptance.KnockoutTriggered)
        {
            _knockoutDrivenUnconscious = true;
            Buddy.SetConsciousness(Consciousness.Unconscious);
            KnockoutStarted?.Invoke(now);
        }
    }

    private bool TryResolveSource(GodotObject? collider, out int interactionId, out int contentId)
    {
        interactionId = 0;
        contentId = 0;
        if (collider is null || !GodotObject.IsInstanceValid(collider))
        {
            return false;
        }

        if (collider is IImpactSource source)
        {
            interactionId = source.InteractionId;
            contentId = source.ContentId;
            return true;
        }

        if (collider is CollisionObject2D body)
        {
            // Untagged physical bodies (e.g. scenario props) attribute to the
            // generic loose-object source, one stable interaction ID per instance.
            ulong instanceId = body.GetInstanceId();
            if (!_untaggedSources.TryGetValue(instanceId, out (int InteractionId, int ContentId) mapped))
            {
                mapped = (InteractionIds.Next(), ImpactContent.LooseObject);
                _untaggedSources[instanceId] = mapped;
            }

            interactionId = mapped.InteractionId;
            contentId = mapped.ContentId;
            return true;
        }

        return false;
    }

    private void OnHardRecovered(HardRecoveryReason reason)
    {
        _router.Reset();
        _knockout.Reset();
        _care.Reset();
        _knockoutDrivenUnconscious = false;
        // Persistent mood and harmful history intentionally survive (§5): hard
        // reposition is a fail-safe, not a trust event.
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("InteractionDamageComponent used before initialization.");
        }
    }
}
