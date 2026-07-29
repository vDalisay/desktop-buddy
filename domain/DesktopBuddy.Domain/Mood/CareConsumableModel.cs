using System;

namespace DesktopBuddy.Domain.Mood;

/// <summary>
/// Approved tuning for one consumable. Durations count integer routed ticks, never
/// seconds, so a paused laboratory freezes a cooldown exactly (ARCHITECTURE §8).
/// </summary>
/// <param name="MoodGain">Mood granted on successful consumption.</param>
/// <param name="CooldownTicks">Reuse cooldown started only by success (FR-008.10).</param>
public readonly record struct CareConsumableTuning(float MoodGain, int CooldownTicks)
{
    /// <summary>
    /// The M4 laboratory food item: <c>+10</c> mood and a <c>60 s</c> reuse cooldown at
    /// 120 Hz. Provisional — borrowed from FR-008.4 (Meal) because owner decision 4
    /// confirmed the machinery target, not the tuning; M5 replaces it with the catalogue
    /// Meal. See the M4 plan, "Delegated defaults".
    /// </summary>
    public static CareConsumableTuning LabFood => new(10.0f, 7200);
}

/// <summary>Why a consume request was refused.</summary>
public enum ConsumeRejection
{
    None,
    OnCooldown,
    AlreadyConsuming,
    UnknownConsumable,

    /// <summary>
    /// The item would overfill the hunger bar (owner decision 2026-07-29). Not a timer: the
    /// same buddy would accept a smaller portion this instant.
    /// </summary>
    TooFull,
}

/// <summary>The result of a completed or abandoned consume attempt.</summary>
/// <param name="Applied">True only when the consume succeeded and mood was granted.</param>
/// <param name="MoodGain">Mood to apply; <c>0</c> unless <paramref name="Applied"/>.</param>
/// <param name="CooldownTicks">Cooldown started; <c>0</c> unless <paramref name="Applied"/>.</param>
public readonly record struct ConsumeResult(bool Applied, float MoodGain, int CooldownTicks);

/// <summary>
/// Owns consumable reuse cooldowns and the one-success-one-cooldown rule (FR-008.4,
/// FR-008.10). A consume is a two-phase transaction: <see cref="TryBegin"/> issues a token
/// when the item is off cooldown and nothing else is being consumed, and only
/// <see cref="Complete"/> for that exact token grants mood and starts the cooldown. Every
/// other ending — <see cref="Cancel"/> after an accepted impact, a dropped item, a missed
/// bite, or an interrupted activity — starts no cooldown, so a failed meal is never
/// punished by a wait.
///
/// <para>
/// Cooldowns are <b>transient</b>: they are not persisted (FR-015.2 excludes temporary
/// statuses), so a relaunch clears an in-flight cooldown. Recorded as a delegated default
/// in the M4 plan; revisit at M5 when consumables become purchasable.
/// </para>
///
/// <para>
/// Fixed capacity with no per-tick allocation: cooldown slots are a preallocated array
/// scanned linearly, which is faster than a dictionary at this size and allocates nothing
/// on the 120 Hz path (ARCHITECTURE §23).
/// </para>
/// </summary>
public sealed class CareConsumableModel
{
    /// <summary>Distinct consumable IDs that can hold a cooldown at once.</summary>
    public const int Capacity = 8;

    private readonly string[] _cooldownIds = new string[Capacity];
    private readonly int[] _cooldownRemaining = new int[Capacity];

    private string? _activeId;
    private int _activeToken;
    private int _nextToken = 1;

    /// <summary>The consumable currently being eaten, or <c>null</c>.</summary>
    public string? ActiveConsumableId => _activeId;

    /// <summary>True while a consume transaction is open.</summary>
    public bool IsConsuming => _activeId is not null;

    /// <summary>The open transaction's token, or <c>0</c> when idle.</summary>
    public int ActiveToken => _activeToken;

    /// <summary>Routed ticks remaining before <paramref name="contentId"/> may be reused.</summary>
    public int CooldownTicksRemaining(string contentId)
    {
        int slot = FindSlot(contentId);
        return slot < 0 ? 0 : _cooldownRemaining[slot];
    }

    public bool IsOnCooldown(string contentId) => CooldownTicksRemaining(contentId) > 0;

    /// <summary>
    /// Opens a consume transaction. Returns <c>false</c> with a reason when the item is on
    /// cooldown or another consume is already open; the caller must not start choreography.
    /// </summary>
    public bool TryBegin(string contentId, out int token, out ConsumeRejection rejection)
    {
        token = 0;

        if (string.IsNullOrWhiteSpace(contentId))
        {
            rejection = ConsumeRejection.UnknownConsumable;
            return false;
        }

        if (_activeId is not null)
        {
            rejection = ConsumeRejection.AlreadyConsuming;
            return false;
        }

        if (IsOnCooldown(contentId))
        {
            rejection = ConsumeRejection.OnCooldown;
            return false;
        }

        _activeId = contentId;
        _activeToken = _nextToken++;
        token = _activeToken;
        rejection = ConsumeRejection.None;
        return true;
    }

    /// <summary>
    /// Completes the transaction for <paramref name="token"/>: grants mood once and starts
    /// the cooldown once. A stale, duplicate, or unknown token applies nothing — this is
    /// what makes a repeated authoritative bite signal unable to double-pay.
    /// </summary>
    public ConsumeResult Complete(int token, in CareConsumableTuning tuning)
    {
        if (_activeId is null || token == 0 || token != _activeToken)
        {
            return new ConsumeResult(false, 0.0f, 0);
        }

        string contentId = _activeId;
        _activeId = null;
        _activeToken = 0;

        if (tuning.CooldownTicks > 0)
        {
            StartCooldown(contentId, tuning.CooldownTicks);
        }

        return new ConsumeResult(true, tuning.MoodGain, tuning.CooldownTicks);
    }

    /// <summary>
    /// Abandons the transaction for <paramref name="token"/>. Grants nothing and starts no
    /// cooldown (FR-008.10). Safe to call with a stale token.
    /// </summary>
    public ConsumeResult Cancel(int token)
    {
        if (_activeId is not null && token != 0 && token == _activeToken)
        {
            _activeId = null;
            _activeToken = 0;
        }

        return new ConsumeResult(false, 0.0f, 0);
    }

    /// <summary>Advances every cooldown by whole routed ticks.</summary>
    public void Tick(int ticks = 1)
    {
        if (ticks <= 0)
        {
            return;
        }

        for (int index = 0; index < Capacity; index++)
        {
            if (_cooldownRemaining[index] <= 0)
            {
                continue;
            }

            _cooldownRemaining[index] -= ticks;
            if (_cooldownRemaining[index] <= 0)
            {
                _cooldownRemaining[index] = 0;
                _cooldownIds[index] = null!;
            }
        }
    }

    /// <summary>
    /// Clears the open transaction and every cooldown. Used by hard reposition and session
    /// resume, which drop all transient interaction state.
    /// </summary>
    public void Reset()
    {
        _activeId = null;
        _activeToken = 0;
        Array.Clear(_cooldownIds);
        Array.Clear(_cooldownRemaining);
    }

    private void StartCooldown(string contentId, int ticks)
    {
        int slot = FindSlot(contentId);
        if (slot < 0)
        {
            slot = FindFreeSlot();
        }

        if (slot < 0)
        {
            // Capacity is sized above the shipped consumable count; a full table means a
            // programming error, not a gameplay state, so fail loudly rather than
            // silently dropping a cooldown and letting the item be spammed.
            throw new InvalidOperationException(
                $"CareConsumableModel cooldown capacity ({Capacity}) exceeded.");
        }

        _cooldownIds[slot] = contentId;
        _cooldownRemaining[slot] = ticks;
    }

    private int FindSlot(string contentId)
    {
        for (int index = 0; index < Capacity; index++)
        {
            if (_cooldownRemaining[index] > 0 &&
                string.Equals(_cooldownIds[index], contentId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private int FindFreeSlot()
    {
        for (int index = 0; index < Capacity; index++)
        {
            if (_cooldownRemaining[index] <= 0)
            {
                return index;
            }
        }

        return -1;
    }
}
