using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Interaction;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Economy;
using DesktopBuddy.Grab;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Interaction;

/// <summary>One fully scored damage event, published after the ledger and mood applied it.</summary>
public readonly record struct AcceptedImpact(
    int InteractionId,
    string ContentId,
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
    string ContentId,
    BuddyPart Part,
    PayoutRegion Region,
    float Impulse,
    float RelativeSpeed,
    Vector2 Point,
    Vector2 Normal,
    double TimeSeconds);

/// <summary>
/// The contact→pain→money/mood pipeline (RAGDOLL §7–§8, ARCHITECTURE §7 steps 7–8,
/// §11). Owns the <b>transient</b> Domain workers — impact router, pain curve, knockout
/// window, care cadence — and runs them on the owning root's fixed tick. Persistent
/// semantic state (mood, harmful history, balance, selected tool, statistics) lives in the
/// injected per-run <see cref="BuddyProgressState"/>, and currency changes go through the
/// injected <see cref="EconomyService"/>: nothing here may outlive or privately own
/// progress, or it would die with the node. Consumes the raw solver contacts each
/// <see cref="PuppetPartBody"/>
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
    private readonly Dictionary<ulong, (int InteractionId, string ContentId)> _untaggedSources = new();

    private ImpactRouter _router = null!;
    private PainCurve _curve = null!;
    private PainKnockoutModel _knockout = null!;
    private BuddyProgressState _progress = null!;
    private EconomyService _economy = null!;
    private CareModel _care = null!;
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

    // Compatibility telemetry: scenarios, the lab panel, and the M3 HUD read progress
    // through the pipeline today. These forward to the injected per-run state; callers
    // migrate to BuddyProgressState/EconomyService as later M4 tasks touch them.
    public long BalanceMilliCredits => _progress.BalanceMilliCredits;
    public long BalanceCredits => _progress.BalanceCredits;
    public float Mood => _progress.Mood;
    public MoodBand MoodBand => _progress.MoodBand;
    public ToolId SelectedTool => _progress.SelectedTool;

    /// <summary>The per-run persistent state this pipeline mutates.</summary>
    public BuddyProgressState Progress => _progress;

    /// <summary>The sole currency/unlock mutator for this run.</summary>
    public EconomyService Economy => _economy;

    public bool IsToolHarmful(string contentId) => _progress.IsContentHarmful(contentId);

    /// <summary>Convenience overload for the tool subset (ARCHITECTURE §5 mapping).</summary>
    public bool IsToolHarmful(ToolId tool) => _progress.IsContentHarmful(ContentIds.ForTool(tool));

    public double PetDistanceProgress => _care.PetDistanceProgress;
    public double PetValidSecondsProgress => _care.PetValidSecondsProgress;
    public double TickleContactSeconds => _care.TickleContactSeconds;
    public TickleDisposition TickleDisposition => _care.TickleDisposition;

    /// <summary>
    /// Validates and returns the pain profile so a composition root can read approved
    /// economy tuning (<c>CashPerPain</c>) before it builds the per-run progress state,
    /// without duplicating the validation error.
    /// </summary>
    public PainConversionProfile RequirePainProfile()
    {
        if (!GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException(
                "InteractionDamageComponent requires a valid pain profile.");
        }

        return Profile;
    }

    /// <param name="progress">The single per-run persistent state owned by the composition root.</param>
    /// <param name="economy">The sole currency/unlock mutator for this run.</param>
    public void Initialize(BuddyProgressState progress, EconomyService economy)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(economy);

        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(Grab))
        {
            throw new InvalidOperationException(
                "InteractionDamageComponent requires an initialized buddy composition and grab tether.");
        }

        if (!GodotObject.IsInstanceValid(CareProfile) || CareProfile.Validate().Count > 0)
        {
            throw new InvalidOperationException(
                "InteractionDamageComponent requires a valid care profile.");
        }

        RequirePainProfile();

        _router = new ImpactRouter(ImpactRouter.DefaultReArmSeconds, Profile.MinimumImpulse);
        _curve = Profile.BuildCurve();
        _knockout = new PainKnockoutModel();
        _progress = progress;
        _economy = economy;
        _care = new CareModel(CareProfile.ToTuning());
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

                if (!TryResolveSource(contact.Collider, out int interactionId, out string contentId))
                {
                    continue;
                }

                var sample = new ContactSample(
                    interactionId,
                    contentId,
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
                    accepted.Value.ContentId,
                    accepted.Value.TargetPart,
                    region,
                    accepted.Value.Impulse,
                    accepted.Value.RelativeVelocity,
                    contact.Point,
                    contact.Normal,
                    now);
                LastEpisode = episode;
                EpisodeAccepted?.Invoke(episode);
                ApplyAcceptedImpact(accepted.Value, contact, region, now);
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

        // Task 5 moves drift onto the LifecycleCoordinator's monotonic clock; until then it
        // stays exactly where M3 had it so this refactor changes no observable behavior.
        _progress.DriftMood(_fixedDelta);

        RewardFeedback? feedback = _economy.PollFeedback(now);
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
            _progress.ApplyCareMood(1.0f);
            CareAwarded?.Invoke(kind);
            CareMoodChanged?.Invoke(kind, 1);
        }

        for (int index = 0; index < negativeAwards; index++)
        {
            CarePenaltyCount++;
            _progress.ApplyCareMood(-1.0f);
            CareMoodChanged?.Invoke(kind, -1);
        }
    }

    /// <summary>Explicit tool pick; Work/Play transitions never route here (M2 invariant).</summary>
    public void SelectTool(ToolId tool)
    {
        RequireInitialized();
        ToolId previous = _progress.SelectedTool;
        if (!_progress.SelectTool(tool))
        {
            return;
        }

        ToolChanged?.Invoke(previous, tool);
    }

    private void ApplyAcceptedImpact(
        in ImpactSample accepted,
        in RawPartContact contact,
        PayoutRegion region,
        double now)
    {
        bool guarded = accepted.ContentId == ContentIds.ToolBoxingGlove &&
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
        // Payout, harmful memory, and statistics move together through the economy service
        // so the balance has exactly one mutator (ARCHITECTURE §11).
        long milli = _economy.AcceptDamage(
            accepted.ContentId,
            pain,
            region,
            acceptance.ConsciousnessAtAcceptance,
            now);
        ScoredImpactCount++;

        GrabState grab = Grab.CurrentGrab;
        bool buddyPartGrabbed = grab.Active && grab.Target is PuppetPartBody;

        var impact = new AcceptedImpact(
            accepted.SourceInteractionId,
            accepted.ContentId,
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
            _progress.RecordKnockout();
            Buddy.SetConsciousness(Consciousness.Unconscious);
            KnockoutStarted?.Invoke(now);
        }
    }

    private bool TryResolveSource(GodotObject? collider, out int interactionId, out string contentId)
    {
        interactionId = 0;
        contentId = ContentIds.LooseObject;
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
            if (!_untaggedSources.TryGetValue(instanceId, out (int InteractionId, string ContentId) mapped))
            {
                mapped = (InteractionIds.Next(), ContentIds.LooseObject);
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
