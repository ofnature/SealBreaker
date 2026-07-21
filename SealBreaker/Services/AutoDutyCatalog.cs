using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SealBreaker.Services;

internal sealed record AutoDutyDuty(
    uint ContentFinderConditionId,
    uint TerritoryType,
    string Name,
    byte RequiredLevel,
    uint RequiredItemLevel,
    bool HasDutySupport,
    uint Expansion,
    string ExpansionName);

/// <summary>Dungeon catalog for the AutoDuty runner — all dungeons, not just Duty Support ones.</summary>
internal static class AutoDutyCatalog
{
    private static List<AutoDutyDuty>? _duties;

    public static IReadOnlyList<AutoDutyDuty> Duties
    {
        get
        {
            EnsureInitialized();
            return _duties!;
        }
    }

    public static void EnsureInitialized()
    {
        if (_duties != null)
            return;

        _duties = BuildFromGameData();
        EnsureMistwakeFallback(_duties);
        _duties = _duties
            .OrderBy(d => d.Expansion)
            .ThenBy(d => d.RequiredLevel)
            .ThenBy(d => d.RequiredItemLevel)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static AutoDutyDuty SelectedOrDefault(Configuration cfg)
    {
        EnsureInitialized();

        var selected = _duties!.FirstOrDefault(d =>
            d.ContentFinderConditionId != 0
            && d.ContentFinderConditionId == cfg.AutoDutyContentFinderConditionId);
        if (selected != null)
            return selected;

        selected = _duties!.FirstOrDefault(d => d.TerritoryType == cfg.AutoDutyTerritoryType);
        if (selected != null)
            return selected;

        return _duties!.First(d => d.TerritoryType == DutySupportCatalog.MistwakeTerritoryType);
    }

    public static int IndexOfSelected(Configuration cfg)
    {
        var selected = SelectedOrDefault(cfg);
        var index = Duties.ToList().FindIndex(d =>
            d.ContentFinderConditionId == selected.ContentFinderConditionId
            && d.TerritoryType == selected.TerritoryType);
        return Math.Max(0, index);
    }

    public static void ApplySelection(Configuration cfg, AutoDutyDuty duty)
    {
        cfg.AutoDutyContentFinderConditionId = duty.ContentFinderConditionId;
        cfg.AutoDutyTerritoryType = duty.TerritoryType;
        cfg.AutoDutyDutyName = duty.Name;
        cfg.Save();
    }

    public static string FormatLabel(AutoDutyDuty duty)
    {
        var level = duty.RequiredLevel > 0 ? $"Lv {duty.RequiredLevel}" : "Lv ?";
        var ilvl = duty.RequiredItemLevel > 0 ? $"ilvl {duty.RequiredItemLevel}" : "ilvl ?";
        var support = duty.HasDutySupport ? ", Duty Support" : "";
        return $"{duty.Name} ({level}, {ilvl}{support})";
    }

    private static List<AutoDutyDuty> BuildFromGameData()
    {
        var result = new List<AutoDutyDuty>();

        try
        {
            var conditions = Service.DataManager.GetExcelSheet<ContentFinderCondition>();
            if (conditions == null)
                return result;

            var dutySupportIds = DutySupportCatalog.Duties
                .Where(d => d.ContentFinderConditionId != 0)
                .Select(d => d.ContentFinderConditionId)
                .ToHashSet();

            foreach (var condition in conditions)
            {
                if (condition.Name.ExtractText() is not { Length: > 0 } name)
                    continue;

                if (condition.ContentType.ValueNullable?.RowId != 2)
                    continue;

                var territory = condition.TerritoryType.ValueNullable;
                if (territory == null)
                    continue;

                var exVersion = territory.Value.ExVersion;
                var expansionName = exVersion.ValueNullable?.Name.ExtractText();
                result.Add(new AutoDutyDuty(
                    condition.RowId,
                    territory.Value.RowId,
                    CleanName(name),
                    condition.ClassJobLevelRequired,
                    condition.ItemLevelRequired,
                    dutySupportIds.Contains(condition.RowId),
                    exVersion.RowId,
                    string.IsNullOrWhiteSpace(expansionName) ? $"Expansion {exVersion.RowId}" : expansionName));
            }
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "[SealBreaker] Failed to build AutoDuty dungeon catalog from game data");
        }

        return result;
    }

    private static void EnsureMistwakeFallback(List<AutoDutyDuty> duties)
    {
        if (duties.Any(d => d.TerritoryType == DutySupportCatalog.MistwakeTerritoryType))
            return;

        duties.Add(new AutoDutyDuty(
            0,
            DutySupportCatalog.MistwakeTerritoryType,
            DutySupportCatalog.MistwakeName,
            100,
            690,
            true,
            5,
            "Dawntrail"));
    }

    private static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        name = name.Trim();
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
