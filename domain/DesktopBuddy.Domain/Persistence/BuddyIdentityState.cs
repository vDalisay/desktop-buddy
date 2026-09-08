using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Mood;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>
/// Persistent semantic state owned by one Buddy identity. It contains no wallet, tool inventory,
/// account statistics, Work progress, room state or live ragdoll state, so several instances can
/// coexist safely under one shared <see cref="PlayerProgressState"/>.
/// </summary>
public sealed class BuddyIdentityState
{
    private readonly MoodModel _mood;
    private readonly HungerModel _hunger;
    private readonly FunInterestModel _fun;

    public BuddyIdentityState(in BuddyIdentitySnapshot snapshot)
    {
        if (!snapshot.BuddyIdentityId.IsValid)
            throw new ArgumentException("Buddy identity requires a stable ID.", nameof(snapshot));
        if (snapshot.CharacterId == Guid.Empty)
            throw new ArgumentException("Character ID cannot be the empty GUID.", nameof(snapshot));
        if (snapshot.Revision < 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Buddy revision cannot be negative.");
        ArgumentNullException.ThrowIfNull(snapshot.HarmfulContentIds);

        BuddyIdentityId = snapshot.BuddyIdentityId;
        CharacterId = snapshot.CharacterId;
        Revision = snapshot.Revision;
        Traits = snapshot.Traits;
        _mood = new MoodModel(snapshot.Mood, snapshot.HarmfulContentIds);
        _hunger = new HungerModel(initialFullness: snapshot.Fullness);
        _fun = new FunInterestModel(Traits.Preferences);
        if (snapshot.FunInterest is not null)
        {
            foreach (FunActivityInterest entry in snapshot.FunInterest)
                _fun.RestoreInterest(entry.Activity, entry.Interest, entry.Bored);
        }
    }

    public BuddyIdentityId BuddyIdentityId { get; }
    public long Revision { get; private set; }
    public Guid? CharacterId { get; private set; }
    public BuddyTraits Traits { get; private set; }
    public float Mood => _mood.Mood;
    public MoodBand MoodBand => _mood.Band;
    public IReadOnlyCollection<string> HarmfulContentIds => _mood.HarmfulTools;
    public float Fullness => _hunger.Fullness;
    public float Appetite => _hunger.Appetite;

    public bool IsContentHarmful(string contentId) => _mood.IsToolHarmful(contentId);
    public bool WouldEat(float hungerFill) => _hunger.Accepts(hungerFill);
    public float InterestIn(FunActivityId activity) => _fun.InterestIn(activity);
    public bool IsFun(FunActivityId activity) => _fun.IsFun(activity);

    public BuddyIdentitySnapshot Snapshot()
    {
        string[] harmful = [.. _mood.HarmfulTools];
        Array.Sort(harmful, StringComparer.Ordinal);
        return new BuddyIdentitySnapshot(
            BuddyIdentityId,
            Revision,
            CharacterId,
            _mood.Mood,
            _hunger.Fullness,
            harmful,
            Traits,
            _fun.Snapshot());
    }

    public bool SetCharacter(Guid? characterId)
    {
        if (characterId == Guid.Empty)
            throw new ArgumentException("Character ID cannot be the empty GUID.", nameof(characterId));
        if (CharacterId == characterId)
            return false;
        CharacterId = characterId;
        Touch();
        return true;
    }

    /// <summary>
    /// Applies only the Buddy-owned emotional/memory side of an accepted impact. The caller routes
    /// payout/statistics once through <see cref="PlayerProgressState.AcceptDamageReward"/>.
    /// Returns true only when an upward mood crossing clears harmful memory.
    /// </summary>
    public bool ApplyImpactMood(
        string contentId,
        float pain,
        ImpactMoodEffect moodEffect = default)
    {
        bool trustReset = moodEffect.Kind switch
        {
            ImpactMoodEffectKind.Harm => RegisterHarmAndReturnFalse(contentId, pain),
            ImpactMoodEffectKind.Enjoyment => _mood.ApplyMoodDelta(moodEffect.EnjoymentMoodGain),
            ImpactMoodEffectKind.Annoyance => _mood.ApplyMoodDelta(-MoodModel.MoodLossForPain(pain)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(moodEffect), moodEffect.Kind, "Unknown impact mood effect."),
        };
        Touch();
        return trustReset;
    }

    public bool ApplyCareMood(float delta)
    {
        bool reset = _mood.ApplyMoodDelta(delta);
        Touch();
        return reset;
    }

    public void FillHunger(float amount)
    {
        if (amount <= 0.0f)
            return;
        _hunger.Fill(amount);
        Touch();
    }

    public void DrainHunger(double elapsedSeconds, HungerActivity activity)
    {
        if (elapsedSeconds <= 0.0)
            return;
        _hunger.Drain(elapsedSeconds, activity);
        Touch();
    }

    public FunOutcome EngageFun(FunActivityId activity)
    {
        FunOutcome outcome = _fun.Engage(activity);
        Touch();
        return outcome;
    }

    public void RechargeFun(double elapsedSeconds)
    {
        if (elapsedSeconds <= 0.0)
            return;
        _fun.Recharge(elapsedSeconds);
        Touch();
    }

    public void DriftMood(double elapsedSeconds)
    {
        if (elapsedSeconds <= 0.0)
            return;
        _mood.Drift(elapsedSeconds);
        Touch();
    }

    /// <summary>New Buddy creation only. Existing identities never reroll personality on load.</summary>
    public void SeedTraits(BuddyTraits traits)
    {
        Traits = traits;
        _fun.SetPreferences(traits.Preferences);
        Touch();
    }

    private bool RegisterHarmAndReturnFalse(string contentId, float pain)
    {
        _mood.RegisterHarm(contentId, pain);
        return false;
    }

    private void Touch() => Revision++;
}
