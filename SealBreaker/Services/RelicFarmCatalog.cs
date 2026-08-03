using Lumina.Excel.Sheets;
using System;
using System.Linq;

namespace SealBreaker.Services;

internal readonly record struct RelicDungeon(uint TerritoryType, string Name, int TomesPerRun);

internal readonly record struct RelicArcanite(uint ItemId, string Name, int PurchaseId);

/// <summary>
/// Static data for the tomestone relic farm: run a Mathematics dungeon until the tome
/// threshold, then buy Occult Crescent arcanite from the exchange NPC in Phantom Village.
/// IDs verified against the game sheets 2026-08-02; the set is patch-specific by nature.
/// </summary>
internal static class RelicFarmCatalog
{
    public const uint PhantomVillageTerritory = 1278;
    public const uint TomeExchangeNpcId = 1053904; // ENpcResident "Ermina"
    public const int ArcaniteTomeCost = 500;

    /// <summary>Dungeons that award Mathematics, sorted by required ilvl. Tomes per run from the
    /// game's reward tables (level-100 dungeons 80, Yuweyawata 60, Alexandria 50).</summary>
    public static readonly RelicDungeon[] Dungeons =
    [
        new(1199, "Alexandria", 50),
        new(1242, "Yuweyawata Field Station", 60),
        new(1266, "The Underkeep", 80),
        new(1292, "The Meso Terminal", 80),
        new(1314, "Mistwake", 80),
        new(1345, "The Clyteum", 80),
    ];

    /// <summary>PurchaseId is the ShopExchangeCurrency callback row for Ermina's shop.</summary>
    public static readonly RelicArcanite[] Arcanites =
    [
        new(47750, "Arcanite", 0),
        new(46850, "Waxing Arcanite", 1),
        new(50058, "Waning Arcanite", 2),
        new(50977, "Ecliptic Arcanite", 3),
    ];

    public static RelicDungeon SelectedOrDefault(Configuration cfg)
    {
        foreach (var d in Dungeons)
        {
            if (d.TerritoryType == cfg.RelicDungeonTerritory)
                return d;
        }

        return Dungeons[^1]; // The Clyteum
    }

    private static string? _npcName;
    private static string? _villagePlaceName;

    /// <summary>Exchange NPC name from the sheet (localized), for object-table lookup.</summary>
    public static string TomeExchangeNpcName()
    {
        if (_npcName != null)
            return _npcName;

        var name = Service.DataManager.GetExcelSheet<ENpcResident>()
            .GetRowOrDefault(TomeExchangeNpcId)?.Singular.ExtractText();
        _npcName = string.IsNullOrWhiteSpace(name) ? "Ermina" : name;
        return _npcName;
    }

    /// <summary>Localized Phantom Village place name — the Lifestream destination command.</summary>
    public static string PhantomVillagePlaceName()
    {
        if (_villagePlaceName != null)
            return _villagePlaceName;

        var name = Service.DataManager.GetExcelSheet<TerritoryType>()
            .GetRowOrDefault(PhantomVillageTerritory)?.PlaceName.ValueNullable?.Name.ExtractText();
        _villagePlaceName = string.IsNullOrWhiteSpace(name) ? "Phantom Village" : name;
        return _villagePlaceName;
    }

    public static string ArcaniteName(uint itemId) =>
        Arcanites.FirstOrDefault(a => a.ItemId == itemId) is { ItemId: not 0 } a ? a.Name : $"item {itemId}";
}
