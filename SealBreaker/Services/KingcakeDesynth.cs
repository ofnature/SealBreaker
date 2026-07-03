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

    private static DesynthDrop[]? _resolvedDrops;

    /// <summary>Sheet-resolved drops, cached after the first successful lookup — this is hit every frame by the UI.</summary>
    public static DesynthDrop[] Drops
    {
        get
        {
            if (_resolvedDrops != null)
                return _resolvedDrops;

            if (TryResolveDrops(out var resolved))
            {
                _resolvedDrops = resolved;
                return resolved;
            }

            return BaseDrops;
        }
    }

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

    /// <summary>Single pass over the Item sheet resolving all drops at once. False when Lumina is not ready yet.</summary>
    private static bool TryResolveDrops(out DesynthDrop[] resolved)
    {
        resolved = (DesynthDrop[])BaseDrops.Clone();

        try
        {
            var indexByName = new Dictionary<string, int>(BaseDrops.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < BaseDrops.Length; i++)
                indexByName[BaseDrops[i].Name] = i;

            var remaining = indexByName.Count;
            foreach (var row in Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>())
            {
                if (!indexByName.TryGetValue(row.Name.ExtractText(), out var idx))
                    continue;

                resolved[idx] = resolved[idx] with { ItemId = row.RowId };
                if (--remaining == 0)
                    break;
            }

            return true;
        }
        catch
        {
            // Lumina not ready during startup — keep the known fallback IDs and retry on next access.
            return false;
        }
    }
}
