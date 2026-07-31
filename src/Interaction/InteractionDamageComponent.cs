using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Autonomy;
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
    double TimeSeconds,

    /// <summary>
    /// Identity of the charged swing that landed this, or <c>0</c> for every
    /// other kind of contact. Copied from the immutable context the source
    /// carried into the contact rather than read back from controller state.
    /// </summary>
    int SwingEpoch,

    /// <summary>Charge the swing was released with, <c>0..1</c>; <c>0</c> when not a charged swing.</summary>
    float SwingCharge,

    /// <summary>
    /// Routed tick on which the swing was committed, or <c>0</c> for every
    /// non-home-run contact. This travels with the solver sample so later
    /// hit-lag and observation-grace logic never consults mutable tool state.
    /// </summary>
    long SwingReleasedTick,

    /// <summary>The semantic mood response chosen for this accepted physical impact.</summary>
    ImpactMoodEffectKind MoodEffect,

    /// <summary>One-based hit number in the current Nerf barrage, or zero for other sources.</summary>
    int NerfHitNumber);

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
    private NerfMoodToleranceModel _nerfMood = null!;
    private BuddyProgressState _progress = null!;
    private EconomyService _economy = null!;
    private CareModel _care = null!;
    private double _fixedDelta;
    private long _ticks;
    private bool _knockoutDrivenUnconscious;
    private int _claimedSwingSource = -1;
    private int _claimedSwingEpoch = -1;

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
    public int NerfHitsInCurrentBarrage => _nerfMood.HitsInCurrentBarrage;
    public bool NerfIsAnnoyed => _nerfMood.IsAnnoyed;

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
        _nerfMood = new NerfMoodToleranceModel();
        _progress = progress;
        _economy = economy;
        _care = new CareModel(CareProfile.ToTuning());
        _fixedDelta = 1.0 / Engine.PhysicsTicksPerSecond;

        // Centralized hard reposition releases contacts and restores a safe pose:
        // transient interaction state clears, persistent mood/history survive (§5, §8.1).
        Buddy.Recovery.HardRecovered += OnHardRecovered;
        Buddy.Recovery.SessionResumed += OnSessionResumed;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (IsInitialized && GodotObject.IsInstanceValid(Buddy) && GodotObject.IsInstanceValid(Buddy.Recovery))
        {
            Buddy.Recovery.HardRecovered -= OnHardRecovered;
            Buddy.Recovery.SessionResumed -= OnSessionResumed;
        }
    }

    /// <summary>Called only from the owning root's routed fixed tick.</summary>
    public void PhysicsTick()
    {
        RequireInitialized();
        _ticks++;
        double now = NowSeconds;
        _nerfMood.Update(now);

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

                if (!TryResolveSource(
                        contact.Collider,
                        out int interactionId,
                        out string contentId))
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
                ApplyAcceptedImpact(
                    accepted.Value,
                    contact,
                    region,
                    contact.SwingContext,
                    now);
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

    /// <summary>
    /// Which novelty meter a kind of care spends, or <c>null</c> for care that carries none.
    /// </summary>
    private static FunActivityId? FunActivityFor(CareKind kind) => kind switch
    {
        CareKind.Pet => FunActivityId.Pet,
        CareKind.Tickle => FunActivityId.Tickle,
        _ => null,
    };

    private void ApplyCareMood(CareKind kind, int positiveAwards, int negativeAwards)
    {
        for (int index = 0; index < positiveAwards; index++)
        {
            CareAwardCount++;
            _progress.ApplyCareMood(1.0f);
            // Attention still counts as care however often it is repeated — the mood grant is
            // unconditional — but the buddy only visibly lights up while it still finds this
            // kind of attention novel (owner instruction 2026-07-27). Interest recharges with
            // time, so coming back later gets the reaction again.
            if (FunActivityFor(kind) is not FunActivityId activity ||
                _progress.EngageFun(activity).WasFun)
            {
                CareAwarded?.Invoke(kind);
            }

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
        SwingImpactContext? swing,
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

        // The swing gate sits here, after the curve has produced positive pain,
        // because this is the one point where "cannot score, pay, change mood, or
        // trigger hit lag" is all still enforceable at once. Sitting after the
        // zero-pain return also means a graze naturally fails to consume an
        // attack with no extra branch. It admits or rejects; it never scales.
        if (!AdmitSwingImpact(accepted.SourceInteractionId, swing))
        {
            return;
        }

        ImpactMoodEffect moodEffect = ImpactMoodEffect.Harm;
        int nerfHitNumber = 0;
        if (accepted.ContentId == ContentIds.ToolNerfBlaster)
        {
            NerfMoodHit nerfHit = _nerfMood.RegisterHit(now);
            moodEffect = nerfHit.MoodEffect;
            nerfHitNumber = nerfHit.HitNumber;
        }

        PainAcceptance acceptance = _knockout.RegisterPain(pain, now);
        // Payout, harmful memory, and statistics move together through the economy service
        // so the balance has exactly one mutator (ARCHITECTURE §11).
        long milli = _economy.AcceptDamage(
            accepted.ContentId,
            pain,
            region,
            acceptance.ConsciousnessAtAcceptance,
            now,
            moodEffect);
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
            now,
            swing is { Mode: SwingImpactMode.HomeRun } homeRun ? homeRun.SwingEpoch : 0,
            swing is { Mode: SwingImpactMode.HomeRun } charged ? charged.ReleasedCharge : 0.0f,
            swing is { Mode: SwingImpactMode.HomeRun } released ? released.ReleasedTick : 0L,
            moodEffect.Kind,
            nerfHitNumber);
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

    /// <summary>
    /// Scores one blast sample against one buddy part, as a sibling of
    /// <see cref="ApplyAcceptedImpact"/> rather than a parallel pipeline (M5 Task 6 plan
    /// §4.2/§2.2). An explosion produces no solver contact, so there is no
    /// <c>RawPartContact</c> to route and no episode to de-duplicate — but everything
    /// downstream of the contact is the same machinery: the shared curve, its zero-pain
    /// floor, the knockout window, the payout, harmful memory, and the
    /// <see cref="ImpactAccepted"/> event with a world-space hit point for the future gore
    /// consumer.
    ///
    /// <para><paramref name="equivalentImpulse"/> is the blast's strength at this part
    /// after distance falloff, in the same units the solver reports. The blast is an
    /// <b>impulse source</b>, exactly like a collision; the curve still owns impulse→pain,
    /// so the no-per-tool-multiplier rule holds unchanged.</para>
    /// </summary>
    /// <param name="sourceInteractionId">
    /// Identity of the exploding body, so a consumer can tell two grenades apart.
    /// </param>
    /// <returns>The pain scored, or <c>0</c> when the sample fell under the curve floor.</returns>
    public float ApplyBlastImpulse(
        int sourceInteractionId,
        string contentId,
        BuddyPart part,
        float equivalentImpulse,
        Vector2 worldPoint)
    {
        RequireInitialized();
        if (!float.IsFinite(equivalentImpulse) || equivalentImpulse <= 0.0f)
        {
            return 0.0f;
        }

        float pain = _curve.PainFor(equivalentImpulse);
        if (pain <= 0.0f)
        {
            // The same floor a graze meets: a part on the edge of the blast is shoved
            // but must not pay, must not mark the grenade harmful, and must not enter
            // the knockout window.
            return 0.0f;
        }

        PayoutRegion region = PayoutRegions.Of(part);
        PainAcceptance acceptance = _knockout.RegisterPain(pain, NowSeconds);
        long milli = _economy.AcceptDamage(
            contentId,
            pain,
            region,
            acceptance.ConsciousnessAtAcceptance,
            NowSeconds,
            ImpactMoodEffect.Harm);
        ScoredImpactCount++;

        GrabState grab = Grab.CurrentGrab;
        var impact = new AcceptedImpact(
            sourceInteractionId,
            contentId,
            part,
            region,
            equivalentImpulse,
            equivalentImpulse,
            // A blast has no closing speed of its own — nothing travelled into the part.
            0.0f,
            worldPoint,
            Vector2.Zero,
            pain,
            milli,
            acceptance.ConsciousnessAtAcceptance,
            Guarded: false,
            grab.Active && grab.Target is PuppetPartBody,
            acceptance.KnockoutTriggered,
            NowSeconds,
            SwingEpoch: 0,
            SwingCharge: 0.0f,
            SwingReleasedTick: 0L,
            ImpactMoodEffectKind.Harm,
            NerfHitNumber: 0);
        LastImpact = impact;
        if (part == BuddyPart.Head)
            Buddy.ActiveDrive.NotifyHeadDisturbed();
        Buddy.InterruptBehaviorActivity();
        ImpactAccepted?.Invoke(impact);

        if (acceptance.KnockoutTriggered)
        {
            _knockoutDrivenUnconscious = true;
            _progress.RecordKnockout();
            Buddy.SetConsciousness(Consciousness.Unconscious);
            KnockoutStarted?.Invoke(NowSeconds);
        }

        return pain;
    }

    /// <summary>
    /// Whether a contact from a swing-capable source may be scored at all, and
    /// whether it spends that swing. A source that carries no swing context —
    /// every loose object, projectile, and non-swing tool — bypasses this
    /// entirely and is scored exactly as it always was.
    ///
    /// The claim is keyed on the source instance as well as the epoch, so a tool
    /// that despawned and respawned can never inherit an earlier body's spent
    /// swing.
    /// </summary>
    private bool AdmitSwingImpact(
        int sourceInteractionId,
        SwingImpactContext? swing)
    {
        // Non-swing sources carry no swing context and bypass this policy
        // entirely. Loose objects, projectiles, and room contacts therefore keep
        // the exact admission behavior they had before charged tools existed.
        if (swing is null)
        {
            return true;
        }

        SwingImpactContext context = swing.Value;
        bool alreadyClaimed =
            _claimedSwingSource == sourceInteractionId &&
            _claimedSwingEpoch == context.SwingEpoch;
        SwingImpactAdmissionResult admission = SwingImpactAdmission.Evaluate(
            context.Mode, context.SwingEpoch, alreadyClaimed, scoredPain: true);
        if (!admission.Admitted)
        {
            return false;
        }

        if (admission.ClaimsEpoch)
        {
            // One home run per swing: the bat may keep crossing shoulders and
            // arms, but the attack has been spent and they cannot score again.
            _claimedSwingSource = sourceInteractionId;
            _claimedSwingEpoch = context.SwingEpoch;
        }

        return true;
    }

    private bool TryResolveSource(
        GodotObject? collider,
        out int interactionId,
        out string contentId)
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
        ResetTransientState();
    }

    private void OnSessionResumed()
    {
        ResetTransientState();
        Buddy.SetConsciousness(Consciousness.Conscious);
    }

    private void ResetTransientState()
    {
        _router.Reset();
        _knockout.Reset();
        _care.Reset();
        _nerfMood.Reset();
        _knockoutDrivenUnconscious = false;
        _claimedSwingSource = -1;
        _claimedSwingEpoch = -1;
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
