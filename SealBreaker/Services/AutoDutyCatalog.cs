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
    string ExpansionName,
    uint InstanceContentId,
    uint ContentTypeId = AutoDutyCatalog.ContentTypeDungeon);

/// <summary>Dungeon catalog for the AutoDuty runner — all dungeons, not just Duty Support ones.
/// <see cref="DutiesWithTrials"/> adds trials for the moogle tomestone farm.</summary>
internal static class AutoDutyCatalog
{
    public const uint ContentTypeDungeon = 2;
    public const uint ContentTypeTrial = 4;

    private static List<AutoDutyDuty>? _duties;
    private static List<AutoDutyDuty>? _dutiesWithTrials;

    public static IReadOnlyList<AutoDutyDuty> Duties
    {
        get
        {
            EnsureInitialized();
            return _duties!;
        }
    }

    /// <summary>Dungeons AND trials, same ordering — used only by the moogle farm's duty picker.</summary>
    public static IReadOnlyList<AutoDutyDuty> DutiesWithTrials
    {
        get
        {
            EnsureInitialized();
            return _dutiesWithTrials!;
        }
    }

    public static void EnsureInitialized()
    {
        if (_duties != null)
            return;

        var all = BuildFromGameData();
        EnsureMistwakeFallback(all);
        all = all
            .OrderBy(d => d.Expansion)
            .ThenBy(d => d.RequiredLevel)
            .ThenBy(d => d.RequiredItemLevel)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _dutiesWithTrials = all;
        _duties = all.Where(d => d.ContentTypeId == ContentTypeDungeon).ToList();
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

    /// <summary>The moogle farm's duty, resolved against the trials-included list — never
    /// <see cref="SelectedOrDefault"/>, whose dungeons-only search would fall back to Mistwake.</summary>
    public static AutoDutyDuty MoogleSelectedOrDefault(Configuration cfg)
    {
        EnsureInitialized();

        var selected = _dutiesWithTrials!.FirstOrDefault(d =>
            d.ContentFinderConditionId != 0
            && d.ContentFinderConditionId == cfg.MoogleDutyCfcId);
        if (selected != null)
            return selected;

        selected = _dutiesWithTrials!.FirstOrDefault(d => d.TerritoryType == cfg.MoogleDutyTerritory);
        if (selected != null)
            return selected;

        // The Porta Decumana, then anything at all.
        return _dutiesWithTrials!.FirstOrDefault(d => d.ContentFinderConditionId == 830)
            ?? _dutiesWithTrials![0];
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
        // RequiredItemLevel 0 means the duty has no ilvl gate (typical for ARR), not "unknown".
        var ilvl = duty.RequiredItemLevel > 0 ? $", ilvl {duty.RequiredItemLevel}" : "";
        var support = duty.HasDutySupport ? ", Duty Support" : "";
        return $"{duty.Name} ({level}{ilvl}{support})";
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

                var contentType = condition.ContentType.ValueNullable?.RowId ?? 0;
                if (contentType is not (ContentTypeDungeon or ContentTypeTrial))
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
                    string.IsNullOrWhiteSpace(expansionName) ? $"Expansion {exVersion.RowId}" : expansionName,
                    condition.ContentLinkType == 1 ? condition.Content.RowId : 0,
                    contentType));
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
            "Dawntrail",
            0));
    }

    private static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        name = name.Trim();
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
