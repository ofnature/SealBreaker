using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using System;
using System.Linq;

namespace SealBreaker.Services;

/// <summary>
/// [FC-RANK-STOP] The character's Free Company rank, read from client memory
/// (<see cref="InfoProxyFreeCompany"/>: <c>Id</c> at 0x30, <c>Rank</c> at 0x6B in ClientStructs 7.57), and the
/// milestone rule that decides when the farm should stop. FC exp is not exposed by the proxy, so the
/// rule works on rank alone.
/// <para>
/// "Not in an FC" and "FC data not loaded yet" both read as no rank (<see cref="TryGetFcRank"/> false):
/// the proxy is null, its Id is 0, or its Rank is 0. Neither ever triggers a stop.
/// </para>
/// </summary>
internal static class FcRankMonitor
{
    public const int MinRank = 1;
    public const int MaxRank = 30;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);
    private static DateTime _lastRefreshUtc = DateTime.MinValue;

    /// <summary>The logged-in character's content id (the per-character settings key); 0 when unavailable.</summary>
    public static unsafe ulong CurrentContentId()
    {
        var ui = UIState.Instance();
        return ui != null ? ui->PlayerState.ContentId : 0;
    }

    /// <summary>The FC's rank and id. False when not in an FC or the FC data hasn't loaded.</summary>
    public static unsafe bool TryGetFcRank(out int rank, out ulong fcId)
    {
        rank = 0;
        fcId = 0;

        var proxy = InfoProxyFreeCompany.Instance();
        if (proxy == null || proxy->Id == 0 || proxy->Rank == 0)
            return false;

        rank = proxy->Rank;
        fcId = proxy->Id;
        return true;
    }

    public static bool TryGetFcRank(out int rank) => TryGetFcRank(out rank, out _);

    /// <summary>
    /// Ask the game to refresh the FC data (what opening the FC window does), at most once a minute.
    /// The proxy also refreshes itself on every zone change. Framework thread only.
    /// </summary>
    public static unsafe void RequestRefresh(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastRefreshUtc < RefreshInterval)
            return;

        var proxy = InfoProxyFreeCompany.Instance();
        if (proxy == null || proxy->Id == 0)
            return;

        _lastRefreshUtc = now;
        try
        {
            proxy->RequestData();
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning($"[SealBreaker] FC data refresh failed: {ex.Message}");
        }
    }

    /// <summary>The next rank that would stop the farm, above <paramref name="rank"/>; null when none.</summary>
    public static int? NextMilestone(FcMilestoneSettings settings, int rank)
    {
        if (settings.StopOnEveryRankUp)
            return rank < MaxRank ? rank + 1 : null;

        var next = settings.Ranks.Where(r => r > rank).DefaultIfEmpty(0).Min();
        return next > 0 ? next : null;
    }

    /// <summary>
    /// True when the FC has reached a milestone since the farm started at <paramref name="baselineRank"/>:
    /// any rank above it with "every rank up", else a configured rank above the baseline and at or below
    /// the current rank.
    /// </summary>
    public static bool MilestoneReached(FcMilestoneSettings settings, int baselineRank, int currentRank)
    {
        if (!settings.Enabled || currentRank <= baselineRank)
            return false;

        if (settings.StopOnEveryRankUp)
            return true;

        return settings.Ranks.Any(r => r > baselineRank && r <= currentRank);
    }

    /// <summary>"Rank 12 · next milestone 15" for the UI, or why there is no rank.</summary>
    public static string StatusLine(Configuration cfg)
    {
        if (!TryGetFcRank(out var rank))
            return "No FC, or FC data not loaded yet";

        var settings = cfg.FcMilestonesFor(CurrentContentId());
        var next = NextMilestone(settings, rank);
        return next is { } n ? $"Rank {rank} · next milestone {n}" : $"Rank {rank} · no milestone above it";
    }
}
