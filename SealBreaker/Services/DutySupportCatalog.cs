using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SealBreaker.Services;

internal sealed record DutySupportDuty(
    uint ContentFinderConditionId,
    uint TerritoryType,
    string Name,
    byte RequiredLevel,
    uint RequiredItemLevel,
    uint Expansion,
    string ExpansionName);

internal static class DutySupportCatalog
{
    public const uint MistwakeTerritoryType = 1314;
    public const string MistwakeName = "Mistwake";

    private static List<DutySupportDuty>? _duties;

    public static IReadOnlyList<DutySupportDuty> Duties
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

    public static DutySupportDuty SelectedOrDefault(Configuration cfg)
    {
        EnsureInitialized();

        var selected = _duties!.FirstOrDefault(d =>
            d.ContentFinderConditionId != 0
            && d.ContentFinderConditionId == cfg.AdsDutySupportContentFinderConditionId);
        if (selected != null)
            return selected;

        selected = _duties!.FirstOrDefault(d => d.TerritoryType == cfg.AdsDutySupportTerritoryType);
        if (selected != null)
            return selected;

        return _duties!.First(d => d.TerritoryType == MistwakeTerritoryType);
    }

    public static int IndexOfSelected(Configuration cfg)
    {
        var selected = SelectedOrDefault(cfg);
        var index = Duties.ToList().FindIndex(d =>
            d.ContentFinderConditionId == selected.ContentFinderConditionId
            && d.TerritoryType == selected.TerritoryType);
        return Math.Max(0, index);
    }

    public static void ApplySelection(Configuration cfg, DutySupportDuty duty)
    {
        cfg.AdsDutySupportContentFinderConditionId = duty.ContentFinderConditionId;
        cfg.AdsDutySupportTerritoryType = duty.TerritoryType;
        cfg.AdsDutySupportName = duty.Name;
        cfg.Save();
    }

    public static string FormatLabel(DutySupportDuty duty)
    {
        var level = duty.RequiredLevel > 0 ? $"Lv {duty.RequiredLevel}" : "Lv ?";
        var ilvl = duty.RequiredItemLevel > 0 ? $"ilvl {duty.RequiredItemLevel}" : "ilvl ?";
        return $"{duty.Name} ({level}, {ilvl})";
    }

    private static List<DutySupportDuty> BuildFromGameData()
    {
        var result = new List<DutySupportDuty>();

        try
        {
            var conditions = Service.DataManager.GetExcelSheet<ContentFinderCondition>();
            var dawnContents = Service.DataManager.GetExcelSheet<DawnContent>();
            var dawnParticipable = Service.DataManager.GetSubrowExcelSheet<DawnContentParticipable>();
            if (conditions == null || dawnContents == null || dawnParticipable == null)
                return result;

            foreach (var condition in conditions)
            {
                if (condition.Name.ExtractText() is not { Length: > 0 } name)
                    continue;

                if (condition.ContentType.ValueNullable?.RowId != 2)
                    continue;

                var territory = condition.TerritoryType.ValueNullable;
                if (territory == null)
                    continue;

                var dawnContent = dawnContents.FirstOrDefault(d =>
                    d.Content.ValueNullable?.RowId == condition.RowId);
                if (dawnContent.RowId == 0)
                    continue;

                if (dawnParticipable.GetSubrowCount(dawnContent.RowId) <= 1)
                    continue;

                var exVersion = territory.Value.ExVersion;
                var expansionName = exVersion.ValueNullable?.Name.ExtractText();
                result.Add(new DutySupportDuty(
                    condition.RowId,
                    territory.Value.RowId,
                    CleanName(name),
                    condition.ClassJobLevelRequired,
                    condition.ItemLevelRequired,
                    exVersion.RowId,
                    string.IsNullOrWhiteSpace(expansionName) ? $"Expansion {exVersion.RowId}" : expansionName));
            }
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "[SealBreaker] Failed to build Duty Support catalog from game data");
        }

        return result;
    }

    private static void EnsureMistwakeFallback(List<DutySupportDuty> duties)
    {
        if (duties.Any(d => d.TerritoryType == MistwakeTerritoryType))
            return;

        duties.Add(new DutySupportDuty(
            0,
            MistwakeTerritoryType,
            MistwakeName,
            100,
            690,
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
