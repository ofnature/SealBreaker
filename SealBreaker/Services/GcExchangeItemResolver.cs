using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System;
using System.Linq;

namespace SealBreaker.Services;

internal readonly struct GcExchangeShopItemInfo
{
    public uint ItemId { get; init; }
    public int SealCost { get; init; }
    public int RankCallback { get; init; }
    public int CategoryCallback { get; init; }
    public int UiCategoryTab { get; init; }
    public uint CategoryRowId { get; init; }
    public int RequiredGrandCompanyRank { get; init; }
    /// <summary>0-based row within the active category list (GCScripShopItem subrow order).</summary>
    public int SheetListRow { get; init; }
}

/// <summary>Resolves GC seal exchange items from Lumina sheets (tabs + list row).</summary>
internal static class GcExchangeItemResolver
{
    public static bool TryResolve(string itemName, uint itemId, int sealCostHint, out GcExchangeShopItemInfo info)
    {
        return TryResolve(itemName, itemId, sealCostHint, filterByPlayerRank: true, out info);
    }

    public static bool TryResolveAnyRank(string itemName, uint itemId, int sealCostHint, out GcExchangeShopItemInfo info)
    {
        return TryResolve(itemName, itemId, sealCostHint, filterByPlayerRank: false, out info);
    }

    private static bool TryResolve(string itemName, uint itemId, int sealCostHint, bool filterByPlayerRank, out GcExchangeShopItemInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(itemName) && itemId == 0)
            return false;

        unsafe
        {
            var gc = PlayerState.Instance()->GrandCompany;
            var gcRank = PlayerState.Instance()->GetGrandCompanyRank();
            var categorySheet = Service.DataManager.GetExcelSheet<GCScripShopCategory>();
            var itemSheet = Service.DataManager.GetSubrowExcelSheet<GCScripShopItem>();
            if (categorySheet == null || itemSheet == null)
                return false;

            var bestScore = -1;
            GCScripShopCategory bestCategory = default;
            GCScripShopItem bestShopItem = default;
            var found = false;

            foreach (var category in categorySheet)
            {
                if (category.GrandCompany.RowId != gc)
                    continue;

                foreach (var shopItems in itemSheet)
                {
                    if (shopItems.RowId != category.RowId)
                        continue;

                    foreach (var shopItem in shopItems)
                    {
                        if (shopItem.CostGCSeals <= 0)
                            continue;
                        if (filterByPlayerRank && gcRank < shopItem.RequiredGrandCompanyRank.RowId)
                            continue;

                        var rowItemId = shopItem.Item.Value.RowId;
                        if (rowItemId == 0)
                            continue;

                        var score = ScoreMatch(itemName, itemId, rowItemId, shopItem.Item.Value.Name.ExtractText());
                        if (score <= 0)
                            continue;
                        if (score < bestScore)
                            continue;
                        if (score == bestScore && !IsBetterTieBreak(itemId, rowItemId, sealCostHint, shopItem.CostGCSeals, bestShopItem))
                            continue;

                        bestScore = score;
                        bestCategory = category;
                        bestShopItem = shopItem;
                        found = true;
                        if (score >= 3)
                            goto resolved;
                    }
                }
            }

            if (!found)
                return false;

            resolved:
            var categoryCallback = (int)bestCategory.SubCategory;
            var uiCategoryTab = GcShopCategoryResolver.ResolveUiTab(bestShopItem.Item.Value, categoryCallback);
            var rankCallback = (int)bestCategory.Tier - 1;
            var sealCost = (int)bestShopItem.CostGCSeals;
            var resolvedItemId = bestShopItem.Item.Value.RowId;
            var requiredRank = (int)bestShopItem.RequiredGrandCompanyRank.RowId;
            var sheetListRow = GcShopCatalog.ComputeCategoryListRowForPlayer(
                bestCategory.RowId, resolvedItemId, sealCost);
            info = new GcExchangeShopItemInfo
            {
                ItemId = resolvedItemId,
                SealCost = sealCost,
                RankCallback = rankCallback,
                CategoryCallback = categoryCallback,
                UiCategoryTab = uiCategoryTab,
                CategoryRowId = bestCategory.RowId,
                RequiredGrandCompanyRank = requiredRank,
                SheetListRow = sheetListRow,
            };
            return true;
        }
    }

    private static int ScoreMatch(string itemName, uint itemId, uint rowItemId, string rowName)
    {
        if (itemId != 0 && rowItemId == itemId)
            return 3;

        if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(rowName))
            return 0;

        if (rowName.Equals(itemName, StringComparison.OrdinalIgnoreCase))
            return 2;

        var searchKey = NormalizeShopNameKey(itemName);
        var rowKey = NormalizeShopNameKey(rowName);
        if (searchKey.Length >= 3 && rowKey.Length >= 3)
        {
            if (rowKey == searchKey)
                return 2;
            if (rowKey.Contains(searchKey, StringComparison.Ordinal) || searchKey.Contains(rowKey, StringComparison.Ordinal))
                return 1;
        }

        return rowName.Contains(itemName, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static string NormalizeShopNameKey(string name)
    {
        var key = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return key.EndsWith('s') && key.Length > 3 ? key[..^1] : key;
    }

    private static bool IsBetterTieBreak(uint itemId, uint rowItemId, int sealCostHint, uint rowSealCost, GCScripShopItem currentBest)
    {
        if (itemId != 0)
        {
            var currentId = currentBest.Item.Value.RowId;
            if (rowItemId == itemId && currentId != itemId)
                return true;
            if (currentId == itemId && rowItemId != itemId)
                return false;
        }

        if (sealCostHint > 0)
        {
            if (rowSealCost == (uint)sealCostHint && currentBest.CostGCSeals != (uint)sealCostHint)
                return true;
            if (currentBest.CostGCSeals == (uint)sealCostHint && rowSealCost != (uint)sealCostHint)
                return false;
        }

        return rowItemId < currentBest.Item.Value.RowId;
    }
}
