using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SealBreaker.Services;

public enum TransferKind { Universal, Mapped, Exclusive }

public sealed class TransferCoordDto
{
    public int Cat { get; set; }
    public int Rank { get; set; }
    public int Row { get; set; }
    public int Cost { get; set; }
}

public sealed class TransferItemDto
{
    public string Kind { get; set; } = "universal";
    public uint ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public TransferCoordDto? Coord { get; set; }
    public bool Enabled { get; set; } = true;
    public int Keep { get; set; }
    public int Qty { get; set; }
}

public sealed class TransferSourceDto
{
    public int Gc { get; set; }
    public string GcName { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public string Plugin { get; set; } = string.Empty;
    public long At { get; set; }
}

public sealed class TransferPayload
{
    public int V { get; set; } = BuyListTransfer.FormatVersion;
    public string Type { get; set; } = BuyListTransfer.PayloadType;
    public TransferSourceDto Source { get; set; } = new();
    public List<TransferItemDto> Items { get; set; } = [];
    public int Excluded { get; set; }
}

/// <summary>One buy-list entry classified for export.</summary>
public sealed record ClassifiedEntry(
    GcShopBuyEntry Entry,
    TransferKind Kind,
    GcShopCatalogEntry? CatalogEntry,
    string? CounterpartSummary,
    string? ExcludeReason);

/// <summary>One imported item resolved against the recipient's catalog.</summary>
public sealed record ResolvedImportItem(GcShopBuyEntry Entry, string? MappedFromName);

public sealed class ImportResolution
{
    public List<ResolvedImportItem> Clean { get; } = [];
    public List<ResolvedImportItem> Mapped { get; } = [];
    public List<(string Name, string Reason)> Excluded { get; } = [];

    public IEnumerable<GcShopBuyEntry> AllEntries()
    {
        foreach (var item in Clean) yield return item.Entry;
        foreach (var item in Mapped) yield return item.Entry;
    }
}

public sealed record MergeConflictPair(GcShopBuyEntry Current, GcShopBuyEntry Incoming);

public sealed class MergePlan
{
    public List<GcShopBuyEntry> Added { get; } = [];
    public int SkippedDuplicates { get; set; }
    public List<MergeConflictPair> Conflicts { get; } = [];
}

/// <summary>
/// Buy-list export/import logic. Pure and catalog-injected — every method takes the
/// catalog entry list so unit tests can run without Lumina/game data.
///
/// Counterpart mapping rule: the three GC shops are structurally parallel in the
/// GCScripShop sheets, so the coordinate (CategoryTab, RankTab, ListRow, SealCost)
/// identifies the same "slot" in each Grand Company's exchange. Same ItemId in all
/// three GCs = universal; three different ItemIds in one complete slot = GC-mapped;
/// anything without a complete slot (including seal-cost mismatches) = exclusive.
/// </summary>
public static class BuyListTransfer
{
    public const string Prefix = "SB_EXPORT:";
    public const string PayloadType = "sb-buylist";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Counterpart grouping ──────────────────────────────────

    private static Dictionary<(int Cat, int Rank, int Row, int Cost), GcShopCatalogEntry?[]> BuildGroups(
        IReadOnlyList<GcShopCatalogEntry> catalog)
    {
        var groups = new Dictionary<(int, int, int, int), GcShopCatalogEntry?[]>();
        foreach (var entry in catalog)
        {
            if (entry.GrandCompanyIndex is < 0 or > 2)
                continue;

            var key = (entry.CategoryTab, entry.RankTab, entry.ListRow, entry.SealCost);
            if (!groups.TryGetValue(key, out var slot))
            {
                slot = new GcShopCatalogEntry?[3];
                groups[key] = slot;
            }

            slot[entry.GrandCompanyIndex] ??= entry;
        }

        return groups;
    }

    private static Dictionary<uint, bool[]> BuildItemPresence(IReadOnlyList<GcShopCatalogEntry> catalog)
    {
        var presence = new Dictionary<uint, bool[]>();
        foreach (var entry in catalog)
        {
            if (entry.ItemId == 0 || entry.GrandCompanyIndex is < 0 or > 2)
                continue;

            if (!presence.TryGetValue(entry.ItemId, out var flags))
            {
                flags = new bool[3];
                presence[entry.ItemId] = flags;
            }

            flags[entry.GrandCompanyIndex] = true;
        }

        return presence;
    }

    private static GcShopCatalogEntry? FindCatalogEntry(
        GcShopBuyEntry entry, int gcIdx, IReadOnlyList<GcShopCatalogEntry> catalog)
    {
        GcShopCatalogEntry? byName = null;
        foreach (var candidate in catalog)
        {
            if (candidate.GrandCompanyIndex != gcIdx)
                continue;

            if (entry.ItemId != 0 && candidate.ItemId == entry.ItemId)
                return candidate;

            if (byName == null
                && !string.IsNullOrWhiteSpace(entry.ItemName)
                && candidate.ItemName.Equals(entry.ItemName, StringComparison.OrdinalIgnoreCase))
                byName = candidate;
        }

        return byName;
    }

    // ── Export ────────────────────────────────────────────────

    public static List<ClassifiedEntry> Classify(
        IEnumerable<GcShopBuyEntry> buyList, int sourceGc, IReadOnlyList<GcShopCatalogEntry> catalog)
    {
        var groups = BuildGroups(catalog);
        var presence = BuildItemPresence(catalog);
        var result = new List<ClassifiedEntry>();

        foreach (var entry in buyList)
        {
            var catalogEntry = FindCatalogEntry(entry, sourceGc, catalog);
            if (catalogEntry == null)
            {
                result.Add(new ClassifiedEntry(entry, TransferKind.Exclusive, null, null,
                    "not found in the GC exchange catalog"));
                continue;
            }

            if (presence.TryGetValue(catalogEntry.ItemId, out var flags) && flags[0] && flags[1] && flags[2])
            {
                result.Add(new ClassifiedEntry(entry, TransferKind.Universal, catalogEntry, null, null));
                continue;
            }

            var key = (catalogEntry.CategoryTab, catalogEntry.RankTab, catalogEntry.ListRow, catalogEntry.SealCost);
            if (groups.TryGetValue(key, out var slot)
                && slot[0] != null && slot[1] != null && slot[2] != null)
            {
                var counterparts = new List<string>();
                for (var gc = 0; gc < 3; gc++)
                {
                    if (gc != sourceGc)
                        counterparts.Add(slot[gc]!.ItemName);
                }

                result.Add(new ClassifiedEntry(entry, TransferKind.Mapped, catalogEntry,
                    string.Join(" / ", counterparts), null));
                continue;
            }

            result.Add(new ClassifiedEntry(entry, TransferKind.Exclusive, catalogEntry, null,
                "no counterpart in all three Grand Companies"));
        }

        return result;
    }

    public static string BuildExportString(
        IReadOnlyList<ClassifiedEntry> classified, int sourceGc, string character, string pluginVersion)
    {
        var payload = new TransferPayload
        {
            Source = new TransferSourceDto
            {
                Gc = sourceGc,
                GcName = GrandCompanyState.GrandCompanyName(sourceGc),
                Character = character,
                Plugin = pluginVersion,
                At = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            },
        };

        foreach (var item in classified)
        {
            switch (item.Kind)
            {
                case TransferKind.Universal:
                    payload.Items.Add(new TransferItemDto
                    {
                        Kind = "universal",
                        ItemId = item.CatalogEntry!.ItemId,
                        Name = item.CatalogEntry.ItemName,
                        Enabled = item.Entry.Enabled,
                        Keep = item.Entry.KeepAmount,
                        Qty = item.Entry.BuyQtyPerPurchase,
                    });
                    break;

                case TransferKind.Mapped:
                    payload.Items.Add(new TransferItemDto
                    {
                        Kind = "mapped",
                        Name = item.CatalogEntry!.ItemName,
                        Coord = new TransferCoordDto
                        {
                            Cat = item.CatalogEntry.CategoryTab,
                            Rank = item.CatalogEntry.RankTab,
                            Row = item.CatalogEntry.ListRow,
                            Cost = item.CatalogEntry.SealCost,
                        },
                        Enabled = item.Entry.Enabled,
                        Keep = item.Entry.KeepAmount,
                        Qty = item.Entry.BuyQtyPerPurchase,
                    });
                    break;

                default:
                    payload.Excluded++;
                    break;
            }
        }

        var json = JsonSerializer.Serialize(payload, Json);
        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    // ── Import ────────────────────────────────────────────────

    public static bool TryParse(string? text, out TransferPayload payload, out string error)
    {
        payload = new TransferPayload();

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "clipboard is empty";
            return false;
        }

        text = text.Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = $"missing the {Prefix} prefix";
            return false;
        }

        string json;
        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(text[Prefix.Length..]));
        }
        catch
        {
            error = "the data after the prefix is not valid Base64 (partial copy?)";
            return false;
        }

        TransferPayload? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TransferPayload>(json, Json);
        }
        catch
        {
            error = "the decoded data is not valid JSON";
            return false;
        }

        if (parsed == null || !PayloadType.Equals(parsed.Type, StringComparison.OrdinalIgnoreCase))
        {
            error = "this is a SealBreaker string, but not a buy-list export";
            return false;
        }

        if (parsed.V > FormatVersion)
        {
            error = $"export format v{parsed.V} is newer than this plugin understands (v{FormatVersion}) — update SealBreaker";
            return false;
        }

        parsed.Items ??= [];
        payload = parsed;
        error = string.Empty;
        return true;
    }

    public static ImportResolution Resolve(
        TransferPayload payload, int recipientGc, IReadOnlyList<GcShopCatalogEntry> catalog)
    {
        var groups = BuildGroups(catalog);
        var resolution = new ImportResolution();

        foreach (var item in payload.Items)
        {
            if (string.Equals(item.Kind, "universal", StringComparison.OrdinalIgnoreCase))
            {
                var match = catalog.FirstOrDefault(e =>
                    e.GrandCompanyIndex == recipientGc && e.ItemId != 0 && e.ItemId == item.ItemId);
                if (match == null)
                {
                    resolution.Excluded.Add((item.Name, "not sold by your Grand Company"));
                    continue;
                }

                resolution.Clean.Add(new ResolvedImportItem(CreateEntry(match, item), null));
                continue;
            }

            if (string.Equals(item.Kind, "mapped", StringComparison.OrdinalIgnoreCase) && item.Coord != null)
            {
                var key = (item.Coord.Cat, item.Coord.Rank, item.Coord.Row, item.Coord.Cost);
                if (!groups.TryGetValue(key, out var slot) || slot[recipientGc] == null)
                {
                    resolution.Excluded.Add((item.Name, "no counterpart found in your Grand Company"));
                    continue;
                }

                var target = slot[recipientGc]!;
                resolution.Mapped.Add(new ResolvedImportItem(CreateEntry(target, item), item.Name));
                continue;
            }

            resolution.Excluded.Add((
                string.IsNullOrWhiteSpace(item.Name) ? "(unnamed item)" : item.Name,
                $"unknown item kind '{item.Kind}'"));
        }

        return resolution;
    }

    private static GcShopBuyEntry CreateEntry(GcShopCatalogEntry catalogEntry, TransferItemDto item) =>
        new()
        {
            Enabled = item.Enabled,
            ItemName = catalogEntry.ItemName,
            ItemId = catalogEntry.ItemId,
            SealCost = catalogEntry.SealCost,
            CategoryTab = catalogEntry.CategoryTab,
            RankTab = catalogEntry.RankTab,
            ListRow = catalogEntry.ListRow,
            KeepAmount = Math.Max(0, item.Keep),
            BuyQtyPerPurchase = Math.Clamp(item.Qty, 0, 99),
        };

    // ── Merge / replace ───────────────────────────────────────

    public static MergePlan PlanMerge(IReadOnlyList<GcShopBuyEntry> existing, IEnumerable<GcShopBuyEntry> incoming)
    {
        var plan = new MergePlan();

        foreach (var entry in incoming)
        {
            var current = existing.FirstOrDefault(e => SameItem(e, entry));
            if (current == null)
            {
                plan.Added.Add(entry);
                continue;
            }

            if (current.KeepAmount == entry.KeepAmount
                && current.BuyQtyPerPurchase == entry.BuyQtyPerPurchase
                && current.Enabled == entry.Enabled)
            {
                plan.SkippedDuplicates++;
                continue;
            }

            plan.Conflicts.Add(new MergeConflictPair(current, entry));
        }

        return plan;
    }

    public static void ApplyReplace(List<GcShopBuyEntry> target, IEnumerable<GcShopBuyEntry> incoming)
    {
        target.Clear();
        target.AddRange(incoming);
    }

    /// <summary>Apply a merge plan. useImported[i] pairs with plan.Conflicts[i].</summary>
    public static void ApplyMerge(List<GcShopBuyEntry> target, MergePlan plan, IReadOnlyList<bool> useImported)
    {
        target.AddRange(plan.Added);

        for (var i = 0; i < plan.Conflicts.Count; i++)
        {
            if (i >= useImported.Count || !useImported[i])
                continue;

            var conflict = plan.Conflicts[i];
            var index = target.IndexOf(conflict.Current);
            if (index >= 0)
                target[index] = conflict.Incoming;
        }
    }

    private static bool SameItem(GcShopBuyEntry a, GcShopBuyEntry b)
    {
        if (a.ItemId != 0 && b.ItemId != 0)
            return a.ItemId == b.ItemId;

        return !string.IsNullOrWhiteSpace(a.ItemName)
               && a.ItemName.Equals(b.ItemName, StringComparison.OrdinalIgnoreCase);
    }

    // ── Mapping table dump (validation aid) ───────────────────

    public static string BuildMappingTableMarkdown(IReadOnlyList<GcShopCatalogEntry> catalog)
    {
        var groups = BuildGroups(catalog);
        var presence = BuildItemPresence(catalog);
        var sb = new StringBuilder();
        sb.AppendLine("# GC shop counterpart mapping (generated)");
        sb.AppendLine();
        sb.AppendLine("| Category | Maelstrom | Twin Adder | Immortal Flames | Universal? |");
        sb.AppendLine("|---|---|---|---|---|");

        foreach (var (key, slot) in groups.OrderBy(g => g.Key.Cat).ThenBy(g => g.Key.Rank).ThenBy(g => g.Key.Row))
        {
            var names = new string[3];
            for (var gc = 0; gc < 3; gc++)
                names[gc] = slot[gc]?.ItemName ?? "—";

            var anyId = slot.FirstOrDefault(e => e != null)?.ItemId ?? 0;
            var universal = anyId != 0
                            && presence.TryGetValue(anyId, out var flags)
                            && flags[0] && flags[1] && flags[2];

            var category = $"{GcShopCatalog.CategoryName(key.Cat)} r{key.Rank} row{key.Row} · {key.Cost}s";
            var complete = slot[0] != null && slot[1] != null && slot[2] != null;
            var marker = universal ? "✓ universal" : complete ? "mapped" : "✗ exclusive";
            sb.AppendLine($"| {category} | {names[0]} | {names[1]} | {names[2]} | {marker} |");
        }

        return sb.ToString();
    }
}
