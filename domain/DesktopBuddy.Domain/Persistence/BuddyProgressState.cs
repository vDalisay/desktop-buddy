using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>Lifetime counters that persist across runs (FR-015.1 statistics).</summary>
public readonly record struct ProgressStatistics(
    long ScoredImpacts,
    long Knockouts,
    long CareAwards,
    long TrustResets,
    long EarnedMilliCredits,
    long SuccessfulCatches = 0,
    long TotalPainMilli = 0,
    long BestOneSecondMilliCredits = 0,
    long BestThreeSecondMilliCredits = 0,
    long BestTenSecondMilliCredits = 0,
    float HighestMood = 0.0f,
    float LowestMood = 0.0f,
    IReadOnlyDictionary<string, long>? ToolUses = null,
    IReadOnlyDictionary<string, long>? ToolPainMilli = null);

/// <summary>Cumulative wall-time accounting, in seconds (FR-015.1, FR-016.8).</summary>
public readonly record struct CumulativeTimes(
    double RunSeconds,
    double ActiveSeconds,
    double HiddenSeconds);

/// <summary>An immutable read of everything that persists, for snapshotting into a save DTO.</summary>
public readonly record struct ProgressSnapshot(
    long Revision,
    long BalanceMilliCredits,
    string SelectedToolId,
    IReadOnlyList<string> UnlockedToolIds,
    float Mood,
    IReadOnlyList<string> HarmfulContentIds,
    BuddyTraits Traits,
    ProgressStatistics Statistics,
    CumulativeTimes Times,
    ProgressExtensionData? Extensions = null,
    /// <summary>Remaining novelty per fun activity; taste itself rides on Traits.</summary>
    IReadOnlyList<FunActivityInterest>? FunInterest = null,
    /// <summary>Hidden appetite, in points of the hunger bar.</summary>
    float Fullness = 0.0f);

/// <summary>
/// Forward-compatible data retained but never activated by this build.
/// Unknown selected/content IDs survive a load/save cycle here.
/// </summary>
public sealed record ProgressExtensionData(
    string? UnknownSelectedToolId = null,
    IReadOnlyList<string>? UnknownContentIds = null,
    IReadOnlyDictionary<string, string>? Values = null);

/// <summary>Why the semantic state changed, for low-frequency subscribers.</summary>
public enum ProgressChange
{
    ToolSelected,
    ToolUnlocked,
    ContentPurchased,
    BalanceChanged,
    TrustReset,
    CareApplied,
}

/// <summary>
/// The single per-run owner of persistent semantic state (ARCHITECTURE §12, FR-015.1).
/// One instance is composed per run by the composition root and injected into the workers
/// that need it; nothing that owns persistent state may be constructed inside a Godot node,
/// or the state dies with the node.
///
/// It holds the <see cref="MoodModel"/> (mood + harmful history), the
/// <see cref="RewardLedger"/> (balance), tool selection, unlocks, traits, statistics, and
/// cumulative times. It holds <b>no</b> live simulation state: pose, velocities, loose
/// objects, pain window, knockout, activity, grab, and cooldowns stay transient and are
/// never persisted (FR-015.2).
///
/// <para>
/// Currency and unlocks are mutated only through the runtime economy service
/// (ARCHITECTURE §11); the <c>Accept*</c>/<c>Deposit</c>/<c>Unlock</c> members here are its
/// implementation, not a second entry point for gameplay code.
/// </para>
///
/// <para>
/// <see cref="Revision"/> increments on every persistent mutation and is what the save
/// coordinator's dirty tracking compares. Mood drift is a persistent mutation, so a
/// running game bumps the revision continuously — that is why <see cref="Changed"/> fires
/// only for discrete semantic events and never for drift.
/// </para>
/// </summary>
public sealed class BuddyProgressState
{
    private readonly MoodModel _mood;
    private readonly HungerModel _hunger;
    private readonly RewardLedger _ledger;
    private readonly ToolSelection _tools = new();
    private readonly HashSet<string> _unlockedTools = new(StringComparer.Ordinal);
    private readonly FunInterestModel _fun;

    private long _scoredImpacts;
    private long _knockouts;
    private long _careAwards;
    private long _trustResets;
    private long _earnedMilliCredits;
    private long _successfulCatches;
    private long _totalPainMilli;
    private long _bestOneSecondMilliCredits;
    private long _bestThreeSecondMilliCredits;
    private long _bestTenSecondMilliCredits;
    private float _highestMood;
    private float _lowestMood;
    private readonly Dictionary<string, long> _toolUses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _toolPainMilli = new(StringComparer.Ordinal);

    /// <param name="cashPerPain">Approved shared economy coefficient (FR-012.5).</param>
    /// <param name="initialMood">Persisted mood, or <c>0</c> for a new save.</param>
    /// <param name="harmfulContentIds">Persisted harmful history, or <c>null</c> for a new save.</param>
    /// <param name="unlockedToolIds">
    /// Persisted unlocks. <c>null</c> seeds the FR-013.1 new-save set: Grab, Pet, Tickle,
    /// and Boxing Glove available immediately.
    /// </param>
    public BuddyProgressState(
        double cashPerPain,
        float initialMood = 0.0f,
        IEnumerable<string>? harmfulContentIds = null,
        IEnumerable<string>? unlockedToolIds = null,
        BuddyTraits? traits = null,
        ProgressStatistics statistics = default,
        CumulativeTimes times = default,
        long revision = 0,
        long initialBalanceMilliCredits = 0,
        string? selectedToolId = null,
        ProgressExtensionData? extensions = null,
        IEnumerable<FunActivityInterest>? funInterest = null,
        float initialFullness = 0.0f)
    {
        _mood = new MoodModel(initialMood, harmfulContentIds);
        _hunger = new HungerModel(initialFullness: initialFullness);
        _ledger = new RewardLedger(cashPerPain, initialBalanceMilliCredits);
        Traits = traits ?? BuddyTraits.Default;
        Revision = revision;
        Extensions = extensions;

        // Tastes ride on the traits, so the interest model is always constructed from the
        // personality this save was created with. A new save starts at full novelty.
        _fun = new FunInterestModel(Traits.Preferences);
        if (funInterest is not null)
        {
            foreach (FunActivityInterest entry in funInterest)
            {
                _fun.RestoreInterest(entry.Activity, entry.Interest, entry.Bored);
            }
        }

        if (unlockedToolIds is null)
        {
            // FR-013.1: a new save has all four launch-subset tools available. The set is
            // declared once in CataloguePolicy so seeding and the catalogue cannot drift.
            foreach (string id in CataloguePolicy.NewSaveUnlockedContentIds)
            {
                _unlockedTools.Add(id);
            }
        }
        else
        {
            foreach (string id in unlockedToolIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _unlockedTools.Add(id);
                }
            }

            // Grab is never purchasable and never absent (RAGDOLL §9).
            _unlockedTools.Add(ContentIds.ToolGrab);
        }

        if (ContentIds.TryParseTool(selectedToolId, out ToolId selected) &&
            _unlockedTools.Contains(ContentIds.ForTool(selected)))
        {
            _tools.Select(selected);
        }

        _scoredImpacts = statistics.ScoredImpacts;
        _knockouts = statistics.Knockouts;
        _careAwards = statistics.CareAwards;
        _trustResets = statistics.TrustResets;
        _earnedMilliCredits = statistics.EarnedMilliCredits;
        _successfulCatches = statistics.SuccessfulCatches;
        _totalPainMilli = statistics.TotalPainMilli;
        _bestOneSecondMilliCredits = statistics.BestOneSecondMilliCredits;
        _bestThreeSecondMilliCredits = statistics.BestThreeSecondMilliCredits;
        _bestTenSecondMilliCredits = statistics.BestTenSecondMilliCredits;
        _highestMood = Math.Max(statistics.HighestMood, _mood.Mood);
        _lowestMood = Math.Min(statistics.LowestMood, _mood.Mood);
        if (statistics.ToolUses is not null)
        {
            foreach ((string id, long count) in statistics.ToolUses)
                _toolUses[id] = count;
        }
        if (statistics.ToolPainMilli is not null)
        {
            foreach ((string id, long pain) in statistics.ToolPainMilli)
                _toolPainMilli[id] = pain;
        }
        Times = times;
    }

    /// <summary>Discrete semantic changes only — never per-tick drift (see class remarks).</summary>
    public event Action<ProgressChange>? Changed;

    public long Revision { get; private set; }

    public float Mood => _mood.Mood;
    public MoodBand MoodBand => _mood.Band;
    public IReadOnlyCollection<string> HarmfulContentIds => _mood.HarmfulTools;
    public bool IsContentHarmful(string contentId) => _mood.IsToolHarmful(contentId);

    /// <summary>Hidden appetite: how full the buddy is, in points (owner decision 2026-07-29).</summary>
    public float Fullness => _hunger.Fullness;

    /// <summary>Room left in the bar — the largest item the buddy would accept right now.</summary>
    public float Appetite => _hunger.Appetite;

    /// <summary>
    /// Whether the buddy would eat an item of this size. The rule is arithmetic: it fits or
    /// it does not, so a nearly full buddy still takes a snack but refuses a banquet.
    /// </summary>
    public bool WouldEat(float hungerFill) => _hunger.Accepts(hungerFill);

    /// <summary>Fills the bar after a successful consume.</summary>
    public void FillHunger(float amount)
    {
        if (amount <= 0.0f)
            return;

        _hunger.Fill(amount);
        Touch();
    }

    /// <summary>
    /// Burns appetite over an elapsed span at the rate for what the buddy is doing. Fires no
    /// <see cref="Changed"/> event, for the same reason mood drift does not: it runs every
    /// tick and the save coordinator already coalesces on <see cref="Revision"/>.
    /// </summary>
    public void DrainHunger(double elapsedSeconds, HungerActivity activity)
    {
        if (elapsedSeconds <= 0.0)
            return;

        _hunger.Drain(elapsedSeconds, activity);
        Touch();
    }

    public long BalanceMilliCredits => _ledger.BalanceMilliCredits;
    public long BalanceCredits => _ledger.BalanceCredits;

    public ToolId SelectedTool => _tools.Selected;
    public string SelectedToolId => ContentIds.ForTool(_tools.Selected);
    public bool IsToolUnlocked(string contentId) => _unlockedTools.Contains(contentId);

    public BuddyTraits Traits { get; private set; }
    public CumulativeTimes Times { get; private set; }
    public ProgressExtensionData? Extensions { get; private set; }

    public ProgressStatistics Statistics => new(
        _scoredImpacts,
        _knockouts,
        _careAwards,
        _trustResets,
        _earnedMilliCredits,
        _successfulCatches,
        _totalPainMilli,
        _bestOneSecondMilliCredits,
        _bestThreeSecondMilliCredits,
        _bestTenSecondMilliCredits,
        _highestMood,
        _lowestMood,
        new Dictionary<string, long>(_toolUses, StringComparer.Ordinal),
        new Dictionary<string, long>(_toolPainMilli, StringComparer.Ordinal));

    /// <summary>An immutable read of all persistent state for the save writer.</summary>
    public ProgressSnapshot Snapshot()
    {
        var harmful = new string[_mood.HarmfulTools.Count];
        int index = 0;
        foreach (string id in _mood.HarmfulTools)
        {
            harmful[index++] = id;
        }

        var unlocks = new string[_unlockedTools.Count];
        index = 0;
        foreach (string id in _unlockedTools)
        {
            unlocks[index++] = id;
        }

        Array.Sort(harmful, StringComparer.Ordinal);
        Array.Sort(unlocks, StringComparer.Ordinal);

        return new ProgressSnapshot(
            Revision,
            _ledger.BalanceMilliCredits,
            SelectedToolId,
            unlocks,
            _mood.Mood,
            harmful,
            Traits,
            Statistics,
            Times,
            Extensions,
            _fun.Snapshot(),
            _hunger.Fullness);
    }

    /// <summary>
    /// Explicit tool pick. Locked tools and unchanged selections are rejected without
    /// mutating persistence.
    /// </summary>
    public bool SelectTool(ToolId tool)
    {
        string contentId = ContentIds.ForTool(tool);
        if (_tools.Selected == tool || !_unlockedTools.Contains(contentId))
        {
            return false;
        }

        _tools.Select(tool);
        if (Extensions?.UnknownSelectedToolId is not null)
        {
            Extensions = Extensions with { UnknownSelectedToolId = null };
        }
        Touch();
        Changed?.Invoke(ProgressChange.ToolSelected);
        return true;
    }

    /// <summary>Records a permanent unlock. Returns <c>false</c> when already unlocked.</summary>
    public bool Unlock(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            throw new ArgumentException("An unlock requires a stable content ID.", nameof(contentId));
        }

        if (!_unlockedTools.Add(contentId))
        {
            return false;
        }

        Touch();
        Changed?.Invoke(ProgressChange.ToolUnlocked);
        return true;
    }

    /// <summary>
    /// Atomically buys one catalogue entry. The <b>catalogue</b> resolves purchasability
    /// and the authoritative price — there is deliberately no caller-supplied price, so a
    /// shop button cannot name its own number (ARCHITECTURE §11). Every failure path
    /// leaves revision, balance, and ownership untouched.
    /// </summary>
    public PurchaseResult Purchase(string contentId, ToolCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        catalogue.TryGet(contentId, out CatalogueEntry entry);
        long price = entry.PriceMilliCredits;
        PurchaseStatus status = CataloguePolicy.EvaluatePurchase(
            catalogue,
            contentId,
            _unlockedTools.Contains(contentId ?? string.Empty),
            _ledger.BalanceMilliCredits);

        if (status != PurchaseStatus.Purchased)
        {
            return new PurchaseResult(
                status,
                contentId ?? string.Empty,
                price,
                _ledger.BalanceMilliCredits);
        }

        if (!_ledger.TrySpend(price))
        {
            // The policy already compared the balance; this is the ledger's own last word,
            // and it must still leave nothing half-applied.
            return new PurchaseResult(
                PurchaseStatus.InsufficientFunds,
                contentId!,
                price,
                _ledger.BalanceMilliCredits);
        }

        _unlockedTools.Add(contentId!);
        Touch();
        Changed?.Invoke(ProgressChange.ContentPurchased);
        return new PurchaseResult(
            PurchaseStatus.Purchased,
            contentId!,
            price,
            _ledger.BalanceMilliCredits);
    }

    /// <summary>
    /// Applies one accepted damage event: payout, harmful memory, and statistics in spec
    /// order. Returns the milli-credits awarded.
    /// </summary>
    public long AcceptDamage(
        string contentId,
        float pain,
        PayoutRegion region,
        DamageConsciousness consciousness,
        double now)
    {
        long milli = _ledger.Accept(pain, region, consciousness, now);
        _mood.RegisterHarm(contentId, pain);
        UpdateMoodExtrema();
        _scoredImpacts++;
        _earnedMilliCredits += milli;
        _totalPainMilli += (long)Math.Round(
            pain * 1000.0f,
            MidpointRounding.AwayFromZero);
        _toolPainMilli.TryGetValue(contentId, out long priorPain);
        _toolPainMilli[contentId] = priorPain + (long)Math.Round(
            pain * 1000.0f,
            MidpointRounding.AwayFromZero);
        Touch();
        if (milli != 0)
        {
            Changed?.Invoke(ProgressChange.BalanceChanged);
        }

        return milli;
    }

    /// <summary>Deposits already-earned credits (passive income). Emits no <c>+$</c> burst.</summary>
    public void Deposit(long milliCredits)
    {
        if (milliCredits <= 0)
        {
            return;
        }

        _ledger.Deposit(milliCredits);
        _earnedMilliCredits += milliCredits;
        Touch();
        Changed?.Invoke(ProgressChange.BalanceChanged);
    }

    /// <summary>Applies a care mood delta and reports whether it fired the trust reset.</summary>
    public bool ApplyCareMood(float delta)
    {
        bool reset = _mood.ApplyMoodDelta(delta);
        if (delta > 0.0f)
        {
            _careAwards++;
        }

        if (reset)
        {
            _trustResets++;
        }

        UpdateMoodExtrema();
        Touch();
        Changed?.Invoke(reset ? ProgressChange.TrustReset : ProgressChange.CareApplied);
        return reset;
    }

    /// <summary>Records that a knockout began (statistics only; the window stays transient).</summary>
    public void RecordKnockout()
    {
        _knockouts++;
        Touch();
    }

    /// <summary>Records the once-per-throw care-bearing catch statistic.</summary>
    public void RecordSuccessfulCatch()
    {
        _successfulCatches++;
        Touch();
    }

    /// <summary>Remaining novelty in one activity, <c>0–100</c>.</summary>
    public float InterestIn(FunActivityId activity) => _fun.InterestIn(activity);

    /// <summary>Whether doing this right now would still be fun for this buddy.</summary>
    public bool IsFun(FunActivityId activity) => _fun.IsFun(activity);

    /// <summary>
    /// Spends one engagement's worth of interest and reports whether the buddy enjoyed it.
    /// The caller decides what enjoyment looks like — a laugh, a mood grant, nothing.
    /// </summary>
    public FunOutcome EngageFun(FunActivityId activity)
    {
        FunOutcome outcome = _fun.Engage(activity);
        Touch();
        return outcome;
    }

    /// <summary>
    /// Recovers interest in every fun activity over a monotonic elapsed span. Fires no
    /// <see cref="Changed"/> event for the same reason mood drift does not: it runs
    /// continuously and the save coordinator already coalesces on <see cref="Revision"/>.
    /// </summary>
    public void RechargeFun(double elapsedSeconds)
    {
        if (elapsedSeconds <= 0.0)
        {
            return;
        }

        _fun.Recharge(elapsedSeconds);
        Touch();
    }

    /// <summary>Records one semantic use for a known content/tool ID.</summary>
    public void RecordContentUse(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            throw new ArgumentException("A content use requires a stable ID.", nameof(contentId));
        _toolUses.TryGetValue(contentId, out long prior);
        _toolUses[contentId] = prior + 1;
        Touch();
    }

    /// <summary>
    /// Drifts mood toward neutral over a monotonic elapsed span. Fires no
    /// <see cref="Changed"/> event: at runtime this is called continuously, and the save
    /// coordinator already coalesces on <see cref="Revision"/>.
    /// </summary>
    public void DriftMood(double elapsedSeconds)
    {
        if (elapsedSeconds <= 0.0)
        {
            return;
        }

        _mood.Drift(elapsedSeconds);
        UpdateMoodExtrema();
        Touch();
    }

    /// <summary>Accrues cumulative time. Foreground and hidden spans are counted separately.</summary>
    public void AccrueTime(double runSeconds, double activeSeconds, double hiddenSeconds)
    {
        if (runSeconds <= 0.0 && activeSeconds <= 0.0 && hiddenSeconds <= 0.0)
        {
            return;
        }

        Times = new CumulativeTimes(
            Times.RunSeconds + Math.Max(0.0, runSeconds),
            Times.ActiveSeconds + Math.Max(0.0, activeSeconds),
            Times.HiddenSeconds + Math.Max(0.0, hiddenSeconds));
        Touch();
    }

    /// <summary>Assigns freshly sampled traits. Only new-save creation may call this.</summary>
    public void SeedTraits(BuddyTraits traits)
    {
        Traits = traits;
        // Tastes are part of the personality, so re-seeding the traits re-tastes the buddy.
        _fun.SetPreferences(traits.Preferences);
        Touch();
    }

    /// <summary>Returns a completed coalesced reward burst, or <c>null</c>.</summary>
    public RewardFeedback? PollRewardFeedback(double now) => _ledger.PollFeedback(now);

    private void UpdateMoodExtrema()
    {
        _highestMood = Math.Max(_highestMood, _mood.Mood);
        _lowestMood = Math.Min(_lowestMood, _mood.Mood);
    }

    private void Touch() => Revision++;
}
