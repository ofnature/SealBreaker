namespace SealBreaker.Services;

public readonly record struct DesynthDrop(uint ItemId, string Name, float DropRate, int FallbackPrice);

internal static class KingcakeDesynth
{
    public const uint KingcakeItemId = 13595;
    public const int KingcakeSealsPerPurchase = 5000;

    private static readonly DesynthDrop[] BaseDrops =
    [
        new(8144, "Clear Demimateria III", 0.0196f, 5000),
        new(8150, "Fieldcraft Demimateria III", 0.3137f, 7000),
        new(14, "Fire Cluster", 1.00f, 100),
        new(12881, "Highland Flour", 0.1373f, 140),
        new(13596, "Moogle Miniature", 0.1843f, 100000),
        new(12872, "Okeanis Egg", 0.1451f, 40),
        new(19, "Water Cluster", 1.00f, 90),
        new(12888, "Yak Milk", 0.20f, 1300),
    ];

    public static DesynthDrop[] Drops => BaseDrops
        .Select(drop => drop with { ItemId = ResolveItemId(drop.Name, drop.ItemId) })
        .ToArray();

    public static uint[] MarketItemIds => Drops.Select(d => d.ItemId).ToArray();

    public static DesynthDrop? FindDrop(uint itemId)
    {
        foreach (var drop in Drops)
        {
            if (drop.ItemId == itemId)
                return drop;
        }

        return null;
    }

    private static uint ResolveItemId(string itemName, uint fallbackItemId)
    {
        try
        {
            foreach (var row in Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>())
            {
                if (row.Name.ExtractText().Equals(itemName, StringComparison.OrdinalIgnoreCase))
                    return row.RowId;
            }
        }
        catch
        {
            // Fall back to the known IDs if Lumina is not ready during startup.
        }

        return fallbackItemId;
    }
}
