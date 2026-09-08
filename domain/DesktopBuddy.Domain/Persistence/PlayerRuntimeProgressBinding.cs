using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>
/// Compatibility seam for account-global runtime consumers while Desktop Buddy moves from the
/// one-Buddy aggregate to split player/Buddy persistence. It deliberately exposes no mood, hunger,
/// traits, harmful memory or fun state, so UI and entitlement code cannot accidentally depend on a
/// focused Buddy merely to read the wallet or tool ownership.
/// </summary>
public sealed class PlayerRuntimeProgressBinding
{
    private readonly BuddyProgressState? _legacy;
    private readonly PlayerProgressState? _player;

    public PlayerRuntimeProgressBinding(BuddyProgressState legacy)
    {
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
    }

    public PlayerRuntimeProgressBinding(PlayerProgressState player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    public bool IsSplit => _player is not null;
    public BuddyProgressState? LegacyProgress => _legacy;
    public PlayerProgressState? PlayerProgress => _player;
    public long Revision => _player?.Revision ?? RequireLegacy().Revision;
    public long BalanceMilliCredits => _player?.BalanceMilliCredits ?? RequireLegacy().BalanceMilliCredits;
    public long BalanceCredits => _player?.BalanceCredits ?? RequireLegacy().BalanceCredits;
    public ToolId SelectedTool => _player?.SelectedTool ?? RequireLegacy().SelectedTool;
    public string SelectedToolId => ContentIds.ForTool(SelectedTool);
    public ProgressStatistics Statistics => _player?.Statistics ?? RequireLegacy().Statistics;
    public CumulativeTimes Times => _player?.Times ?? RequireLegacy().Times;
    public ProgressExtensionData? Extensions => _player?.Extensions ?? RequireLegacy().Extensions;

    public bool IsUnlocked(string contentId) =>
        _player?.IsUnlocked(contentId) ?? RequireLegacy().IsToolUnlocked(contentId);

    public bool SelectTool(ToolId tool) =>
        _player?.SelectTool(tool) ?? RequireLegacy().SelectTool(tool);

    public bool Unlock(string contentId) =>
        _player?.Unlock(contentId) ?? RequireLegacy().Unlock(contentId);

    public bool SetExtensionValue(string key, string value) =>
        _player?.SetExtensionValue(key, value) ?? RequireLegacy().SetExtensionValue(key, value);

    public void RecordContentUse(string contentId)
    {
        if (_player is not null)
            _player.RecordContentUse(contentId);
        else
            RequireLegacy().RecordContentUse(contentId);
    }

    public void AccrueTime(double runSeconds, double activeSeconds, double hiddenSeconds)
    {
        if (_player is not null)
            _player.AccrueTime(runSeconds, activeSeconds, hiddenSeconds);
        else
            RequireLegacy().AccrueTime(runSeconds, activeSeconds, hiddenSeconds);
    }

    public static implicit operator PlayerRuntimeProgressBinding(BuddyProgressState legacy) => new(legacy);
    public static implicit operator PlayerRuntimeProgressBinding(PlayerProgressState player) => new(player);

    private BuddyProgressState RequireLegacy() =>
        _legacy ?? throw new InvalidOperationException("This player progress binding uses split account state.");
}
