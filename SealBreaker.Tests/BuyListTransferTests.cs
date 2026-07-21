using SealBreaker;
using SealBreaker.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SealBreaker.Tests;

public class BuyListTransferTests
{
    // Synthetic three-GC catalog mirroring the parallel GCScripShop structure.
    // Item ids/names are fixture data, not claims about game data.
    private static GcShopCatalogEntry Entry(int gc, uint id, string name, int cost, int cat, int rank, int row) =>
        new()
        {
            GrandCompanyIndex = gc,
            ItemId = id,
            ItemName = name,
            SealCost = cost,
            CategoryTab = cat,
            RankTab = rank,
            ListRow = row,
        };

    private static List<GcShopCatalogEntry> Catalog() =>
    [
        // Universal: same item id in all three GCs.
        Entry(0, 10119, "Duck Bones", 600, 3, 2, 0),
        Entry(1, 10119, "Duck Bones", 600, 3, 2, 0),
        Entry(2, 10119, "Duck Bones", 600, 3, 2, 0),

        // GC-mapped triplet: same slot, three different item ids.
        Entry(0, 901, "Maelstrom Aetheryte Ticket", 2000, 2, 2, 1),
        Entry(1, 902, "Twin Adder Aetheryte Ticket", 2000, 2, 2, 1),
        Entry(2, 903, "Immortal Flames Aetheryte Ticket", 2000, 2, 2, 1),

        // Exclusive: exists only in Maelstrom.
        Entry(0, 555, "Storm Exclusive Roll", 4000, 2, 1, 5),

        // Cost mismatch at the same slot: incomplete groups, must be exclusive.
        Entry(0, 700, "Storm Odd Item", 500, 0, 0, 0),
        Entry(1, 701, "Serpent Odd Item", 600, 0, 0, 0),
        Entry(2, 702, "Flame Odd Item", 700, 0, 0, 0),
    ];

    private static GcShopBuyEntry Buy(uint id, string name, int keep = 0, int qty = 0, bool enabled = true) =>
        new() { ItemId = id, ItemName = name, KeepAmount = keep, BuyQtyPerPurchase = qty, Enabled = enabled, SealCost = 1 };

    // ── Classification ────────────────────────────────────────

    [Fact]
    public void UniversalItem_ClassifiesAsUniversal()
    {
        var result = BuyListTransfer.Classify([Buy(10119, "Duck Bones")], 0, Catalog());
        Assert.Equal(TransferKind.Universal, Assert.Single(result).Kind);
    }

    [Fact]
    public void GcSpecificItem_ClassifiesAsMapped_WithCounterpartNames()
    {
        var result = BuyListTransfer.Classify([Buy(901, "Maelstrom Aetheryte Ticket")], 0, Catalog());
        var item = Assert.Single(result);
        Assert.Equal(TransferKind.Mapped, item.Kind);
        Assert.Contains("Twin Adder Aetheryte Ticket", item.CounterpartSummary);
        Assert.Contains("Immortal Flames Aetheryte Ticket", item.CounterpartSummary);
    }

    [Fact]
    public void ExclusiveItem_ClassifiesAsExclusive()
    {
        var result = BuyListTransfer.Classify([Buy(555, "Storm Exclusive Roll")], 0, Catalog());
        Assert.Equal(TransferKind.Exclusive, Assert.Single(result).Kind);
    }

    [Fact]
    public void CostMismatchAcrossGcs_ClassifiesAsExclusive()
    {
        var result = BuyListTransfer.Classify([Buy(700, "Storm Odd Item")], 0, Catalog());
        Assert.Equal(TransferKind.Exclusive, Assert.Single(result).Kind);
    }

    [Fact]
    public void UnknownItem_ClassifiesAsExclusive()
    {
        var result = BuyListTransfer.Classify([Buy(31337, "Not A Shop Item")], 0, Catalog());
        var item = Assert.Single(result);
        Assert.Equal(TransferKind.Exclusive, item.Kind);
        Assert.Contains("not found", item.ExcludeReason);
    }

    // ── Export payload ────────────────────────────────────────

    [Fact]
    public void ExclusiveItem_IsExcludedFromExportPayload()
    {
        var classified = BuyListTransfer.Classify(
            [Buy(10119, "Duck Bones"), Buy(555, "Storm Exclusive Roll")], 0, Catalog());
        var text = BuyListTransfer.BuildExportString(classified, 0, "Test@World", "1.0");

        Assert.True(BuyListTransfer.TryParse(text, out var payload, out _));
        Assert.Single(payload.Items);
        Assert.Equal("Duck Bones", payload.Items[0].Name);
        Assert.Equal(1, payload.Excluded);
    }

    // ── Parsing / validation ──────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an export at all")]
    [InlineData("SB_EXPORT:!!!not-base64!!!")]
    public void InvalidStrings_FailParse(string? input)
    {
        Assert.False(BuyListTransfer.TryParse(input, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void WrongPayloadType_FailsParse()
    {
        var json = "{\"v\":1,\"type\":\"something-else\",\"items\":[]}";
        var text = BuyListTransfer.Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        Assert.False(BuyListTransfer.TryParse(text, out _, out _));
    }

    [Fact]
    public void UnknownJsonFields_AreTolerated()
    {
        var json = "{\"v\":1,\"type\":\"sb-buylist\",\"futureField\":42,\"items\":[{\"kind\":\"universal\",\"itemId\":10119,\"name\":\"Duck Bones\",\"mystery\":true,\"keep\":5,\"qty\":0,\"enabled\":true}]}";
        var text = BuyListTransfer.Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        Assert.True(BuyListTransfer.TryParse(text, out var payload, out _));
        Assert.Equal(5, payload.Items.Single().Keep);
    }

    [Fact]
    public void NewerFormatVersion_FailsParseWithUpdateHint()
    {
        var json = "{\"v\":99,\"type\":\"sb-buylist\",\"items\":[]}";
        var text = BuyListTransfer.Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        Assert.False(BuyListTransfer.TryParse(text, out _, out var error));
        Assert.Contains("update", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Resolution ────────────────────────────────────────────

    [Theory]
    [InlineData(0, "Maelstrom Aetheryte Ticket")]
    [InlineData(1, "Twin Adder Aetheryte Ticket")]
    [InlineData(2, "Immortal Flames Aetheryte Ticket")]
    public void MappedItem_ResolvesToRecipientGcCounterpart(int recipientGc, string expectedName)
    {
        var classified = BuyListTransfer.Classify([Buy(901, "Maelstrom Aetheryte Ticket", keep: 7)], 0, Catalog());
        var text = BuyListTransfer.BuildExportString(classified, 0, "Test@World", "1.0");
        Assert.True(BuyListTransfer.TryParse(text, out var payload, out _));

        var resolution = BuyListTransfer.Resolve(payload, recipientGc, Catalog());
        var mapped = Assert.Single(resolution.Mapped);
        Assert.Equal(expectedName, mapped.Entry.ItemName);
        Assert.Equal(7, mapped.Entry.KeepAmount);
        Assert.Empty(resolution.Excluded);
    }

    [Fact]
    public void UniversalItem_ResolvesByItemId_ForOtherGc()
    {
        var classified = BuyListTransfer.Classify([Buy(10119, "Duck Bones", keep: 3, qty: 16)], 0, Catalog());
        var text = BuyListTransfer.BuildExportString(classified, 0, "Test@World", "1.0");
        Assert.True(BuyListTransfer.TryParse(text, out var payload, out _));

        var resolution = BuyListTransfer.Resolve(payload, 2, Catalog());
        var clean = Assert.Single(resolution.Clean);
        Assert.Equal(10119u, clean.Entry.ItemId);
        Assert.Equal(3, clean.Entry.KeepAmount);
        Assert.Equal(16, clean.Entry.BuyQtyPerPurchase);
    }

    [Fact]
    public void UniversalItem_MissingFromRecipientCatalog_IsExcluded()
    {
        var payload = new TransferPayload
        {
            Items = [new TransferItemDto { Kind = "universal", ItemId = 424242, Name = "Ghost Item" }],
        };

        var resolution = BuyListTransfer.Resolve(payload, 1, Catalog());
        Assert.Empty(resolution.Clean);
        var (name, reason) = Assert.Single(resolution.Excluded);
        Assert.Equal("Ghost Item", name);
        Assert.Contains("not sold", reason);
    }

    // ── Merge / replace ───────────────────────────────────────

    [Fact]
    public void Merge_SkipsIdenticalDuplicates()
    {
        var existing = new List<GcShopBuyEntry> { Buy(10119, "Duck Bones", keep: 3, qty: 16) };
        var incoming = new List<GcShopBuyEntry> { Buy(10119, "Duck Bones", keep: 3, qty: 16) };

        var plan = BuyListTransfer.PlanMerge(existing, incoming);
        Assert.Empty(plan.Added);
        Assert.Empty(plan.Conflicts);
        Assert.Equal(1, plan.SkippedDuplicates);
    }

    [Fact]
    public void Merge_DifferingSettings_BecomeConflicts_AndChoiceApplies()
    {
        var existing = new List<GcShopBuyEntry> { Buy(10119, "Duck Bones", keep: 0) };
        var incoming = new List<GcShopBuyEntry> { Buy(10119, "Duck Bones", keep: 50), Buy(901, "Maelstrom Aetheryte Ticket", keep: 10) };

        var plan = BuyListTransfer.PlanMerge(existing, incoming);
        Assert.Single(plan.Added);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(0, conflict.Current.KeepAmount);
        Assert.Equal(50, conflict.Incoming.KeepAmount);

        BuyListTransfer.ApplyMerge(existing, plan, [true]);
        Assert.Equal(2, existing.Count);
        Assert.Equal(50, existing.First(e => e.ItemId == 10119).KeepAmount);
    }

    [Fact]
    public void Merge_KeepCurrentChoice_LeavesExistingEntry()
    {
        var existing = new List<GcShopBuyEntry> { Buy(10119, "Duck Bones", keep: 0) };
        var incoming = new List<GcShopBuyEntry> { Buy(10119, "Duck Bones", keep: 50) };

        var plan = BuyListTransfer.PlanMerge(existing, incoming);
        BuyListTransfer.ApplyMerge(existing, plan, [false]);

        Assert.Equal(0, Assert.Single(existing).KeepAmount);
    }

    [Fact]
    public void Replace_WipesExistingList()
    {
        var existing = new List<GcShopBuyEntry> { Buy(555, "Storm Exclusive Roll"), Buy(10119, "Duck Bones") };
        var incoming = new List<GcShopBuyEntry> { Buy(901, "Maelstrom Aetheryte Ticket") };

        BuyListTransfer.ApplyReplace(existing, incoming);

        Assert.Equal("Maelstrom Aetheryte Ticket", Assert.Single(existing).ItemName);
    }

    // ── Round trip ────────────────────────────────────────────

    [Fact]
    public void RoundTrip_SameGc_ProducesIdenticalTransferableList()
    {
        var original = new List<GcShopBuyEntry>
        {
            Buy(10119, "Duck Bones", keep: 3, qty: 16),
            Buy(901, "Maelstrom Aetheryte Ticket", keep: 10, qty: 1),
        };

        var classified = BuyListTransfer.Classify(original, 0, Catalog());
        var text = BuyListTransfer.BuildExportString(classified, 0, "Test@World", "1.0");
        Assert.True(BuyListTransfer.TryParse(text, out var payload, out _));

        var resolution = BuyListTransfer.Resolve(payload, 0, Catalog());
        var target = new List<GcShopBuyEntry>();
        BuyListTransfer.ApplyReplace(target, resolution.AllEntries());

        Assert.Equal(original.Count, target.Count);
        foreach (var expected in original)
        {
            var actual = target.Single(e => e.ItemId == expected.ItemId);
            Assert.Equal(expected.ItemName, actual.ItemName);
            Assert.Equal(expected.KeepAmount, actual.KeepAmount);
            Assert.Equal(expected.BuyQtyPerPurchase, actual.BuyQtyPerPurchase);
            Assert.Equal(expected.Enabled, actual.Enabled);
        }
    }
}
