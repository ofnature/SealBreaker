using System;
using System.Linq;

namespace SealBreaker.Services;

/// <summary>
/// Shared "best dungeon I can actually run" logic — used by the Duty page's Auto-pick
/// button and by leveling mode before each duty launch. Highest RequiredLevel wins,
/// then highest RequiredItemLevel; every gate the game enforces is applied first.
/// </summary>
internal static class DutyAutoPicker
{
    public static AutoDutyDuty? PickBestAutoDuty(Configuration cfg, int level, int ilvl, Func<uint, bool> hasPath)
    {
        if (level <= 0)
            return null;

        AutoDutyCatalog.EnsureInitialized();
        var wantsNpcParty = cfg.AutoDutyDutyMode is Configuration.AutoDutyModeSupport or Configuration.AutoDutyModeTrust;

        var ordered = AutoDutyCatalog.Duties
            .Where(d => d.RequiredLevel > 0 && d.RequiredLevel <= level)
            .Where(d => d.RequiredItemLevel == 0 || ilvl <= 0 || d.RequiredItemLevel <= (uint)ilvl)
            .Where(d => !wantsNpcParty || d.HasDutySupport)
            .OrderByDescending(d => d.RequiredLevel)
            .ThenByDescending(d => d.RequiredItemLevel)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

        // Path check last and lazily — it can be an IPC call per territory.
        foreach (var duty in ordered)
        {
            if (hasPath(duty.TerritoryType))
                return duty;
        }

        return null;
    }

    public static DutySupportDuty? PickBestAds(int level, int ilvl)
    {
        if (level <= 0)
            return null;

        DutySupportCatalog.EnsureInitialized();
        return DutySupportCatalog.Duties
            .Where(d => d.ContentFinderConditionId != 0)
            .Where(d => d.RequiredLevel > 0 && d.RequiredLevel <= level)
            .Where(d => d.RequiredItemLevel == 0 || ilvl <= 0 || d.RequiredItemLevel <= (uint)ilvl)
            .OrderByDescending(d => d.RequiredLevel)
            .ThenByDescending(d => d.RequiredItemLevel)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
