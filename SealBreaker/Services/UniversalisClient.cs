using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SealBreaker.Services;

public class MarketItemData
{
    public uint ItemId { get; set; }
    public int LowestPrice { get; set; }
    public int AverageSalePrice { get; set; }
    public DateTime LastUpdated { get; set; }
    public int ListingCount { get; set; }
    public int RecentSalesCount { get; set; }
}

internal static class UniversalisClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private static DateTime _retryAfterUtc = DateTime.MinValue;

    public static DateTime RetryAfterUtc => _retryAfterUtc;

    public static async Task<Dictionary<uint, MarketItemData>?> FetchPricesAsync(uint[] itemIds)
    {
        if (itemIds.Length == 0)
            return [];

        if (DateTime.UtcNow < _retryAfterUtc)
            return null;

        try
        {
            var ids = string.Join(",", itemIds.Distinct());
            using var response = await Http.GetAsync($"https://universalis.app/api/v2/Maduin/{ids}").ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _retryAfterUtc = DateTime.UtcNow.AddSeconds(30);
                return null;
            }

            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            return ParseResponse(doc.RootElement);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Universalis price fetch failed");
            return null;
        }
    }

    public static int GetLowestPrice(MarketItemData data, bool hqOnly = false) => data.LowestPrice;

    public static int GetAverageSalePrice(MarketItemData data, int historyCount = 10) => data.AverageSalePrice;

    private static Dictionary<uint, MarketItemData> ParseResponse(JsonElement root)
    {
        var result = new Dictionary<uint, MarketItemData>();

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in items.EnumerateObject())
            {
                var data = ParseItem(item.Value);
                if (data != null)
                    result[data.ItemId] = data;
            }
            return result;
        }

        var single = ParseItem(root);
        if (single != null)
            result[single.ItemId] = single;

        return result;
    }

    private static MarketItemData? ParseItem(JsonElement item)
    {
        var itemId = GetUInt(item, "itemID");
        if (itemId == 0)
            itemId = GetUInt(item, "itemId");
        if (itemId == 0)
            return null;

        var lowest = 0;
        var listings = 0;
        if (item.TryGetProperty("listings", out var listingArray) && listingArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var listing in listingArray.EnumerateArray())
            {
                var price = GetInt(listing, "pricePerUnit");
                if (price <= 0)
                    continue;

                listings++;
                lowest = lowest == 0 ? price : Math.Min(lowest, price);
            }
        }

        var recentSales = 0;
        var saleTotal = 0L;
        if (item.TryGetProperty("recentHistory", out var historyArray) && historyArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var sale in historyArray.EnumerateArray())
            {
                var price = GetInt(sale, "pricePerUnit");
                if (price <= 0)
                    continue;

                recentSales++;
                saleTotal += price;
                if (recentSales >= 10)
                    break;
            }
        }

        return new MarketItemData
        {
            ItemId = itemId,
            LowestPrice = lowest,
            AverageSalePrice = recentSales == 0 ? 0 : (int)Math.Round(saleTotal / (double)recentSales),
            LastUpdated = DateTime.Now,
            ListingCount = listings,
            RecentSalesCount = recentSales,
        };
    }

    private static uint GetUInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetUInt32(out var result)
            ? result
            : 0;
    }

    private static int GetInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result)
            ? result
            : 0;
    }
}
