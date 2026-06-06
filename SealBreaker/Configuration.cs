using Dalamud.Configuration;
using SealBreaker.Services;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SealBreaker;

[Serializable]
public class NavWaypoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public Vector3 ToVector3() => new(X, Y, Z);

    public static NavWaypoint From(Vector3 v) => new() { X = v.X, Y = v.Y, Z = v.Z };
}

[Serializable]
public class GcShopBuyEntry
{
    public bool Enabled { get; set; } = true;
    public string ItemName { get; set; } = string.Empty;
    public uint ItemId { get; set; }
    /// <summary>Top tabs: Weapons=0, Armor=1, Materiel=2, Materials=3.</summary>
    public int CategoryTab { get; set; } = 3;
    /// <summary>Left rank icons: top=0, middle=1, bottom=2.</summary>
    public int RankTab { get; set; } = 2;
    /// <summary>Fallback 0-based row in the exchange list.</summary>
    public int ListRow { get; set; }
    public int SealCost { get; set; } = 600;
    /// <summary>Target inventory count. When reached, the next enabled entry is tried.</summary>
    public int KeepAmount { get; set; }
    /// <summary>0 = max affordable per purchase (up to 99). 1–99 = fixed batch size.</summary>
    public int BuyQtyPerPurchase { get; set; }
}

[Serializable]
public class GcTownNavSettings
{
    public bool RepairEnabled { get; set; }
    public int RepairThresholdPercent { get; set; } = 50;
    public string MenderName { get; set; } = string.Empty;
    public float MenderX { get; set; }
    public float MenderY { get; set; }
    public float MenderZ { get; set; }

    public bool UseCustomGcNavWaypoints { get; set; }
    public List<NavWaypoint> GcNavWaypoints { get; set; } = [];
    public bool UseCustomGcCorridorWaypoints { get; set; }
    public List<NavWaypoint> GcCorridorWaypoints { get; set; } = [];
    public bool UseCustomRepairNavWaypoints { get; set; }
    public List<NavWaypoint> RepairNavWaypoints { get; set; } = [];
    public bool UseCustomRepairReturnNavWaypoints { get; set; }
    public List<NavWaypoint> RepairReturnNavWaypoints { get; set; } = [];

    /// <summary>GC aetheryte ticket exchange tuning (per town/GC) — not automated yet.</summary>
    public GcPortTicketShopSettings PortTicket { get; set; } = new();

    public Vector3 MenderPosition => new(MenderX, MenderY, MenderZ);
    public bool HasMenderConfigured => !string.IsNullOrWhiteSpace(MenderName);
}

[Serializable]
public class GcPortTicketShopSettings
{
    /// <summary>Not used by the farm yet — tune values here, then hardcode once verified.</summary>
    public bool Enabled { get; set; }

    /// <summary>GC-named ticket in the exchange (e.g. Maelstrom Aetheryte Ticket).</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>0-based row in the exchange list.</summary>
    public int ListRow { get; set; }

    /// <summary>Top tabs: Weapons=0, Armor=1, Materiel=2, Materials=3.</summary>
    public int CategoryTab { get; set; } = 2;

    /// <summary>Left rank icons: top=0, middle=1, bottom=2.</summary>
    public int RankTab { get; set; } = 2;

    public int SealCost { get; set; } = 2000;
    public int BuyQty { get; set; } = 1;
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int RepairProviderSealBreaker = 0;
    public const int RepairProviderAds = 1;

    public const int AdsRepairModeSelf = 0;
    public const int AdsRepairModeNpc = 1;
    public const int AdsRepairModeNpcNoInn = 2;
    public const int AdsRepairModeNpcNoTeleportNoInn = 3;

    public int Version { get; set; } = 19;

    // ── Duty ──────────────────────────────────────────────────
    public int RunsPerCycle { get; set; } = 5;

    /// <summary>0 = AutoDuty, 1 = ADS (AI Duty Solver)</summary>
    public int DutyRunner { get; set; } = 0;
    public uint AdsDutySupportContentFinderConditionId { get; set; }
    public uint AdsDutySupportTerritoryType { get; set; } = DutySupportCatalog.MistwakeTerritoryType;
    public string AdsDutySupportName { get; set; } = DutySupportCatalog.MistwakeName;

    // ── Grand Company ─────────────────────────────────────────
    /// <summary>0=Maelstrom  1=Twin Adder  2=Immortal Flames</summary>
    public int GrandCompanyIndex { get; set; } = 0;

    /// <summary>Per-town nav: 0=Limsa  1=Gridania  2=Ul'dah</summary>
    public GcTownNavSettings[] GcTownNav { get; set; } =
    [
        new()
        {
            RepairEnabled = true,
            MenderName = "Syvinmhas",
            MenderX = 10f,
            MenderY = 44.5f,
            MenderZ = 160f,
        },
        new()
        {
            RepairEnabled = true,
            MenderName = GcNavRoutes.GridaniaRepairName,
            MenderX = GcNavRoutes.GridaniaRepairPos.X,
            MenderY = GcNavRoutes.GridaniaRepairPos.Y,
            MenderZ = GcNavRoutes.GridaniaRepairPos.Z,
        },
        new()
        {
            RepairEnabled = true,
            MenderName = GcNavRoutes.UldahRepairName,
            MenderX = GcNavRoutes.UldahRepairPos.X,
            MenderY = GcNavRoutes.UldahRepairPos.Y,
            MenderZ = GcNavRoutes.UldahRepairPos.Z,
        },
    ];

    // Legacy v3 fields — migrated into GcTownNav[0] on load
    public bool UseCustomMaelstromGcNavWaypoints { get; set; }
    public List<NavWaypoint> MaelstromGcNavWaypoints { get; set; } = [];
    public bool UseCustomMaelstromRepairNavWaypoints { get; set; }
    public List<NavWaypoint> MaelstromRepairNavWaypoints { get; set; } = [];
    public bool RepairEnabled { get; set; } = true;
    public int RepairThresholdPercent { get; set; } = 50;
    /// <summary>0=SealBreaker mender route, 1=ADS repair command.</summary>
    public int RepairProvider { get; set; } = RepairProviderSealBreaker;
    /// <summary>0=self, 1=npc, 2=npc-no-inn, 3=npc-no-teleport-no-inn.</summary>
    public int AdsRepairMode { get; set; } = AdsRepairModeNpcNoTeleportNoInn;

    public int SealCap           { get; set; } = 90000;
    public int SealReserve       { get; set; } = 1500;
    public bool UseGrandCompanyOverride { get; set; } = false;

    // ── Item Filter ───────────────────────────────────────────
    /// <summary>0=Off  1=Blacklist  2=Whitelist</summary>
    public int ListMode { get; set; } = 0;
    public List<uint> FilteredItemIds { get; set; } = new();

    // ── Shop ──────────────────────────────────────────────────
    /// <summary>0 = max per purchase (up to 99). 1–99 = fixed amount each buy.</summary>
    public int DuckboneBuyQty { get; set; } = 0;

    /// <summary>Priority-ordered GC exchange purchases. Each entry buys until KeepAmount, then the next runs.</summary>
    public List<GcShopBuyEntry> GcShopBuyList { get; set; } = [];

    /// <summary>Priority-ordered GC exchange purchases per Grand Company: 0=Limsa/Maelstrom, 1=Gridania/Twin Adder, 2=Ul'dah/Immortal Flames.</summary>
    public List<GcShopBuyEntry>[] GcShopBuyLists { get; set; } = [[], [], []];

    /// <summary>Legacy v5 — migrated into GcTownNav[].PortTicket.</summary>
    public GcPortTicketShopSettings GcPortTicket { get; set; } = new();

    /// <summary>When false, the plugin never auto-closes the GC personnel officer menu unless the farm/test opened it and is still running cleanup.</summary>
    public bool AutoDismissGcOfficerMenu { get; set; } = true;

    // ── Materia extraction ────────────────────────────────────
    public bool AutoExtractMateriaEnabled { get; set; } = false;
    public bool AutoExtractMateriaBetweenRuns { get; set; } = true;
    public bool AutoExtractMateriaAtCycleBoundary { get; set; } = true;

    // ── Desynth EV / stats ────────────────────────────────────
    public int DesynthTargetGil { get; set; } = 20_000_000;
    public int DesynthCyclesPerDay { get; set; } = 2;
    public int MooglePriceAlertThreshold { get; set; } = 50_000;
    public bool MooglePriceAlertEnabled { get; set; } = false;
    public Dictionary<uint, int> DesynthPriceOverrides { get; set; } = new();
    public Dictionary<uint, bool> DesynthPriceOverrideEnabled { get; set; } = new();

    // Legacy v4 duckbone shop fields — ignored after migration (values live in GcShopDefaults)
    public int DuckboneShopRow { get; set; } = 40;
    public string DuckboneItemName { get; set; } = "Duckbone";
    public int DuckboneCategoryTab { get; set; } = 3;
    public int DuckboneRankTab { get; set; } = 2;
    public int DuckboneSealCost { get; set; } = 600;

    // ── Misc ──────────────────────────────────────────────────
    public bool EchoToChat { get; set; } = false;
    public bool ShowWindowBanner { get; set; } = false;

    public GcTownNavSettings TownNav(int gcIdx) =>
        GcTownNav[Math.Clamp(gcIdx, 0, GcTownNav.Length - 1)];

    public void EnsureGcTownNav()
    {
        if (GcTownNav == null || GcTownNav.Length != 3)
        {
            GcTownNav =
            [
                new() { RepairEnabled = true, MenderName = "Syvinmhas", MenderX = 10f, MenderY = 44.5f, MenderZ = 160f },
                new()
                {
                    RepairEnabled = true,
                    MenderName = GcNavRoutes.GridaniaRepairName,
                    MenderX = GcNavRoutes.GridaniaRepairPos.X,
                    MenderY = GcNavRoutes.GridaniaRepairPos.Y,
                    MenderZ = GcNavRoutes.GridaniaRepairPos.Z,
                },
                new()
                {
                    RepairEnabled = true,
                    MenderName = GcNavRoutes.UldahRepairName,
                    MenderX = GcNavRoutes.UldahRepairPos.X,
                    MenderY = GcNavRoutes.UldahRepairPos.Y,
                    MenderZ = GcNavRoutes.UldahRepairPos.Z,
                },
            ];
        }

        EnsurePortTicketDefaults();
        EnsureGcShopBuyLists();
        EnsureDesynthDefaults();

        if (Version >= 19)
            return;

        if (Version < 4)
        {
            var limsa = GcTownNav[0];
            limsa.UseCustomGcNavWaypoints = UseCustomMaelstromGcNavWaypoints;
            if (MaelstromGcNavWaypoints is { Count: > 0 })
                limsa.GcNavWaypoints = MaelstromGcNavWaypoints;
            limsa.UseCustomRepairNavWaypoints = UseCustomMaelstromRepairNavWaypoints;
            if (MaelstromRepairNavWaypoints is { Count: > 0 })
                limsa.RepairNavWaypoints = MaelstromRepairNavWaypoints;
            limsa.RepairEnabled = RepairEnabled;
            limsa.RepairThresholdPercent = RepairThresholdPercent;
            if (string.IsNullOrWhiteSpace(limsa.MenderName))
            {
                limsa.MenderName = "Syvinmhas";
                limsa.MenderX = GcNavRoutes.LimsaRepairMenderPos.X;
                limsa.MenderY = GcNavRoutes.LimsaRepairMenderPos.Y;
                limsa.MenderZ = GcNavRoutes.LimsaRepairMenderPos.Z;
            }
        }

        if (Version == 5 && GcPortTicket != null)
        {
            var active = GcTownNav[Math.Clamp(GrandCompanyIndex, 0, GcTownNav.Length - 1)];
            active.PortTicket = GcPortTicket;
        }

        if (Version < 7)
        {
            var limsa = GcTownNav[0];
            if (limsa.MenderName.Equals("Syvinmhas", StringComparison.OrdinalIgnoreCase)
                && Math.Abs(limsa.MenderX - 11.4f) < 0.01f
                && Math.Abs(limsa.MenderY - 14.4f) < 0.01f
                && Math.Abs(limsa.MenderZ - (-35.78f)) < 0.01f)
            {
                limsa.MenderX = GcNavRoutes.LimsaRepairMenderPos.X;
                limsa.MenderY = GcNavRoutes.LimsaRepairMenderPos.Y;
                limsa.MenderZ = GcNavRoutes.LimsaRepairMenderPos.Z;
            }
        }

        if (Version < 8)
        {
            var limsa = GcTownNav[0];
            if (limsa.UseCustomRepairNavWaypoints && limsa.RepairNavWaypoints.Count < 2)
            {
                limsa.UseCustomRepairNavWaypoints = false;
                limsa.RepairNavWaypoints.Clear();
            }
        }

        if (Version < 9)
        {
            var limsa = GcTownNav[0];
            if (limsa.UseCustomRepairNavWaypoints && limsa.RepairNavWaypoints.Count < 3)
            {
                limsa.UseCustomRepairNavWaypoints = false;
                limsa.RepairNavWaypoints.Clear();
            }
        }

        if (Version < 10)
        {
            GcShopBuyList ??= [];
            if (GcShopBuyList.Count == 0)
            {
                GcShopBuyList.Add(new GcShopBuyEntry
                {
                    Enabled = true,
                    ItemName = GcShopDefaults.DuckboneItemName,
                    ItemId = GcShopDefaults.DuckboneItemId,
                    CategoryTab = GcShopDefaults.DuckboneCategoryTab,
                    RankTab = GcShopDefaults.DuckboneRankTab,
                    ListRow = GcShopDefaults.DuckboneListRow,
                    SealCost = GcShopDefaults.DuckboneSealCost,
                    KeepAmount = 0,
                    BuyQtyPerPurchase = DuckboneBuyQty,
                });
            }
        }

        if (Version < 12)
            EnsureAdsDutySupportDefaults();

        if (Version < 13)
            MigrateLegacyGcShopBuyList();

        EnsureAdditionalTownRepairDefaults();
        if (Version < 15)
            MigrateGlobalRepairSettings();

        Version = 19;
        Save();
    }

    public void EnsureDesynthDefaults()
    {
        DesynthPriceOverrides ??= new Dictionary<uint, int>();
        DesynthPriceOverrideEnabled ??= new Dictionary<uint, bool>();
        var currentDropIds = KingcakeDesynth.Drops.Select(d => d.ItemId).ToHashSet();
        foreach (var key in DesynthPriceOverrides.Keys.Where(k => !currentDropIds.Contains(k)).ToList())
            DesynthPriceOverrides.Remove(key);
        foreach (var key in DesynthPriceOverrideEnabled.Keys.Where(k => !currentDropIds.Contains(k)).ToList())
            DesynthPriceOverrideEnabled.Remove(key);

        foreach (var drop in KingcakeDesynth.Drops)
        {
            if (!DesynthPriceOverrides.ContainsKey(drop.ItemId))
                DesynthPriceOverrides[drop.ItemId] = drop.FallbackPrice;
            if (!DesynthPriceOverrideEnabled.ContainsKey(drop.ItemId))
                DesynthPriceOverrideEnabled[drop.ItemId] = false;
        }
    }

    public void ApplyAutomaticGrandCompanySettings()
    {
        if (UseGrandCompanyOverride)
            return;

        if (!GrandCompanyState.TryGetDetected(out var detectedGcIdx, out _, out var detectedSealCap))
            return;

        var changed = false;
        if (GrandCompanyIndex != detectedGcIdx)
        {
            GrandCompanyIndex = detectedGcIdx;
            changed = true;
        }

        if (SealCap != detectedSealCap)
        {
            SealCap = detectedSealCap;
            changed = true;
        }

        if (changed)
            Save();
    }

    public List<GcShopBuyEntry> GcShopBuyListFor(int gcIdx)
    {
        EnsureGcShopBuyLists();
        return GcShopBuyLists[Math.Clamp(gcIdx, 0, GcShopBuyLists.Length - 1)];
    }

    public IReadOnlyList<GcShopBuyEntry> EnabledGcShopBuyList()
    {
        var list = GcShopBuyListFor(GrandCompanyIndex);
        return list.FindAll(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ItemName));
    }

    private void EnsureGcShopBuyLists()
    {
        GcShopBuyList ??= [];

        if (GcShopBuyLists == null || GcShopBuyLists.Length != 3)
        {
            var old = GcShopBuyLists;
            GcShopBuyLists = [[], [], []];

            if (old != null)
            {
                for (var i = 0; i < Math.Min(old.Length, GcShopBuyLists.Length); i++)
                    GcShopBuyLists[i] = old[i] ?? [];
            }
        }

        for (var i = 0; i < GcShopBuyLists.Length; i++)
            GcShopBuyLists[i] ??= [];
    }

    private void MigrateLegacyGcShopBuyList()
    {
        if (GcShopBuyList.Count == 0)
            return;

        var activeList = GcShopBuyListFor(GrandCompanyIndex);
        if (activeList.Count == 0)
            activeList.AddRange(GcShopBuyList);
    }

    private void EnsureAdditionalTownRepairDefaults()
    {
        for (var i = 1; i < GcTownNav.Length; i++)
        {
            GcTownNav[i].RepairEnabled = true;
            GcTownNav[i].RepairThresholdPercent = Math.Clamp(GcTownNav[i].RepairThresholdPercent, 10, 90);
        }

        var gridania = GcTownNav[1];
        if (string.IsNullOrWhiteSpace(gridania.MenderName))
        {
            gridania.MenderName = GcNavRoutes.GridaniaRepairName;
            gridania.MenderX = GcNavRoutes.GridaniaRepairPos.X;
            gridania.MenderY = GcNavRoutes.GridaniaRepairPos.Y;
            gridania.MenderZ = GcNavRoutes.GridaniaRepairPos.Z;
        }

        var uldah = GcTownNav[2];
        if (string.IsNullOrWhiteSpace(uldah.MenderName))
        {
            uldah.MenderName = GcNavRoutes.UldahRepairName;
            uldah.MenderX = GcNavRoutes.UldahRepairPos.X;
            uldah.MenderY = GcNavRoutes.UldahRepairPos.Y;
            uldah.MenderZ = GcNavRoutes.UldahRepairPos.Z;
        }
    }

    private void MigrateGlobalRepairSettings()
    {
        var active = TownNav(GrandCompanyIndex);
        RepairEnabled = active.RepairEnabled;
        RepairThresholdPercent = Math.Clamp(active.RepairThresholdPercent, 10, 90);
        RepairProvider = Math.Clamp(RepairProvider, RepairProviderSealBreaker, RepairProviderAds);
        AdsRepairMode = Math.Clamp(AdsRepairMode, AdsRepairModeSelf, AdsRepairModeNpcNoTeleportNoInn);
    }

    public string AdsRepairModeCommand() => AdsRepairMode switch
    {
        AdsRepairModeSelf => "self",
        AdsRepairModeNpc => "npc",
        AdsRepairModeNpcNoInn => "npc-no-inn",
        _ => "npc-no-teleport-no-inn",
    };

    private void EnsurePortTicketDefaults()
    {
        string[] defaultNames =
        [
            "Maelstrom Aetheryte Ticket",
            "Twin Adder Aetheryte Ticket",
            "Immortal Flames Aetheryte Ticket",
        ];

        for (var i = 0; i < GcTownNav.Length; i++)
        {
            GcTownNav[i].PortTicket ??= new GcPortTicketShopSettings();
            if (string.IsNullOrWhiteSpace(GcTownNav[i].PortTicket.ItemName))
                GcTownNav[i].PortTicket.ItemName = defaultNames[Math.Clamp(i, 0, defaultNames.Length - 1)];
        }
    }

    private void EnsureAdsDutySupportDefaults()
    {
        if (AdsDutySupportTerritoryType == 0)
            AdsDutySupportTerritoryType = DutySupportCatalog.MistwakeTerritoryType;

        if (string.IsNullOrWhiteSpace(AdsDutySupportName))
            AdsDutySupportName = DutySupportCatalog.MistwakeName;
    }

    public void Save() =>
        Service.PluginInterface.SavePluginConfig(this);
}
