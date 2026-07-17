using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using SealBreaker.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SealBreaker.Services;

[StructLayout(LayoutKind.Explicit, Size = 152)]
internal unsafe struct GCExpertEntry
{
    [FieldOffset(112)] public int Unk112;
    [FieldOffset(116)] public uint Unk116;
    [FieldOffset(120)] public uint Seals;
    [FieldOffset(132)] public uint ItemID;
    [FieldOffset(136)] public uint Unk136;
    [MarshalAs(UnmanagedType.I1)][FieldOffset(145)] public bool Unk145;
}

public sealed class FarmController : IDisposable
{
    private const int GcSupplyMenuOption = 0;      // "Undertake supply and provisioning missions."
    private const int GcOfficerDismissOption = 3;  // "Nothing." — closes the personnel officer menu
    private const int GcExpertDeliveryTab = 2;     // 3rd horizontal tab: Supply / Provisioning / Expert Delivery
    private const string GcExchangeAddon = "GrandCompanyExchange";
    private const string GcBuyDialogAddon = "ShopExchangeCurrencyDialog";
    private const uint GcExchangeListNodeId = 57;
    private static readonly uint[] GcExchangeRankRadioNodes = [37, 38, 39];
    private static readonly uint[] GcExchangeCategoryRadioNodes = [46, 44, 45, 47];
    private static readonly string[] GcBuyDialogAddons =
        ["ShopExchangeCurrencyDialog", "ShopExchangeDialog", "ShopExchangeCurrency"];
    private static readonly string[] ExpertDeliveryUiAddons =
    [
        "GrandCompanySupplyReward",
        "GrandCompanySupplyList",
        "SelectString",
    ];
    private static readonly string[] SelectYesnoAddons = ["SelectYesno", "SelectYesNo"];
    private static readonly InventoryType[] NormalInventoryTypes =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    private static readonly uint[][] GcZoneIds =
    [
        [128, 129], // Maelstrom — Upper / Lower Limsa
        [132, 133], // Twin Adder — New / Old Gridania
        [130, 131], // Immortal Flames — Steps of Nald / Thal
    ];
    private static readonly uint[] GcOfficerZoneId = [128, 132, 130];
    private static readonly string[] GcCityTeleportCommand =
    [
        "Limsa Lominsa Lower Decks",
        "New Gridania",
        "Ul'dah - Steps of Nald",
    ];
    private static readonly string[] GcSubZoneTeleportCommand =
    [
        "The Aftcastle",
        "New Gridania",
        "Ul'dah - Steps of Nald",
    ];
    /// <summary>Aftcastle crystal on the Y≈40 GC walkway (~-84, 40, -12).</summary>
    private static readonly Vector3 MaelstromAftcastleMainDeck = new(-84.0f, 40.0f, -12.0f);
    private const float MaelstromAftcastleMainDeckRadius = 35f;
    /// <summary>Western Y=40 aethernet arrival for The Aftcastle (~16.81, 40, 71.91).</summary>
    private static readonly Vector3 MaelstromAftcastleLandingPortIn = new(16.810669f, 39.999996f, 71.907616f);
    /// <summary>Within this range of Aftcastle crystal or Y=40 aethernet shard = ported in, don't backtrack on GC route.</summary>
    private const float MaelstromAftcastlePortRadius = 32f;
    private const float MaelstromAftcastleLandingRadius = 18f;
    /// <summary>Eastern Y=40 supply/barracks — needs Lower Decks tp.</summary>
    private const float MaelstromEasternSupplyMinX = 45f;
    /// <summary>First walk target from Aftcastle Y=40 port-in — ramp/walkway junction.</summary>
    private static readonly Vector3 MaelstromAftcastleLandingRamp = new(30.0f, 40.0f, 75.0f);
    /// <summary>Walk off the Y=40 landing platform toward western Aftcastle (stay on walkway — no Y=21 descent).</summary>
    private static readonly Vector3[] MaelstromAftcastleLandingDescent =
    [
        new(30.0f, 40.0f, 75.0f),
        new(14.0f, 40.0f, 62.0f),
        new(10.0f, 40.0f, 45.0f),
        new(6.0f, 40.0f, 28.0f),
        new(1.6f, 39.5f, 16.5f),
        new(11.0f, 40.0f, 13.8f),
        new(-84.0f, 20.65f, -12.0f),
    ];
    /// <summary>Y≈40 only — port-in / east walkway toward Aftcastle crystal (GC officer is west, not east).</summary>
    private static readonly Vector3[] MaelstromAftcastleWalkwayPortToCrystal =
    [
        new(30.0f, 40.0f, 75.0f),
        new(14.0f, 40.0f, 62.0f),
        new(10.0f, 40.0f, 45.0f),
        new(6.0f, 40.0f, 28.0f),
        new(1.6f, 39.5f, 16.5f),
        new(11.0f, 40.0f, 13.8f),
        new(-84.0f, 40.0f, -12.0f),
    ];
    private const int GcLandingPathTimeoutMs = 180_000;
    private const int GcWaypointTimeoutMs = 45_000;
    private const int MappedStepTimeoutMs = 60_000;
    private const float MappedStepArriveRange = 3f;
    private const int MappedStepMsPerWaypoint = 30_000;
    private static readonly Vector3[] MaelstromAftcastleToGcWaypoints =
    [
        new(-79.5f, 40.0f, -13.0f),
        new(-75.0f, 40.0f, -14.5f),
        new(-71.5f, 40.0f, -16.0f),
        new(-70.0f, 40.0f, -17.0f),
        new(-67.8f, 40.0f, -18.1f),
    ];
    private const float GcHubStagingRadius = 8f;
    private const float MaelstromUpperDeckY = 17.5f;
    /// <summary>GC walkway plane — officer, crystal, corridor, and approach all stay at Y≥this until repair descent.</summary>
    private const float MaelstromGcWalkwayMinY = MaelstromZone128Nav.GcWalkwayMinY;
    /// <summary>Officer/shop XZ on the Y≈40 walkway (NPC data coords use Y≈21).</summary>
    private static readonly Vector3 MaelstromGcOfficerStaging = new(93.0f, 40.0f, 74.5f);
    private static readonly Vector3 MaelstromGcOfficerWalkway = new(95.68933f, 40.250282f, 74.54028f);
    private static readonly Vector3 MaelstromGcShopWalkway = new(93.0f, 40.0f, 72.0f);
    private const float GcWalkwayMerchantRadius = 25f;
    private const float GcNpcHintMaxPlanarDist = 30f;
    /// <summary>Legacy Y band label — GC corridor X extent on the Y≈40 walkway.</summary>
    private const float MaelstromCommandCorridorMaxY = 24f;
    private const float MaelstromCommandCorridorMinX = -85f;
    private const float MaelstromCommandCorridorMaxX = -55f;
    private static readonly Vector3[] GcOfficerPos =
    [
        new(95.68933f, 40.250282f, 74.54028f),
        new(-67f, -0.5f, -8f),
        new(-141f, 4f, -106f),
    ];
    /// <summary>Navmesh-safe staging on the Y≈40 walkway before walking to GC NPCs.</summary>
    private static readonly Vector3 MaelstromGcUpperHub = new(93.0f, 40.0f, 74.5f);
    private static readonly Vector3 MaelstromGcLowerHub = new(6.0f, 14.4f, -32.0f);
    private static readonly Vector3[] GcShopPos =
    [
        new(93.0f, 40.0f, 72.0f),
        new(-67f, -0.5f, -8f),
        new(-141f, 4f, -106f),
    ];
    private static readonly string[] GcOfficerName =
        ["Storm Personnel Officer", "Serpent Personnel Officer", "Flame Personnel Officer"];
    private static readonly string[] GcShopName =
        ["Storm Quartermaster", "Serpent Quartermaster", "Flame Quartermaster"];

    private const uint MaelstromRepairZone = 128;
    private static readonly string[] RepairYesnoPromptSnippets =
    [
        "Repair as many of the displayed items",
        "repair as many",
        "displayed items as possible",
        "まとめて修理",
        "Folgendes Material verbrauchen",
        "Réparer tous les objets",
    ];

    public enum FarmState
    {
        Idle, CheckSealSpend, StartDuty, WaitingForDutyStart, WaitingForDutyComplete,
        WaitingForDutyExit, TeleportToGC, WaitingForZone, CheckSubZone, WaitingForSubZone,
        NavigateToOfficer, NavigateToShop,
        NavigateToGcTarget,
        OpenExpertDelivery, ProcessDelivery,
        OpenGCShop, BuyDuckbones,
        NavigateToRepair, OpenRepairNpc, OpenRepairMenu, ProcessRepair, NavigateFromRepair,
        OpenMateriaExtraction, ProcessMateriaExtraction,
        OpenDutySupport, QueueDutySupport,
        CheckGcLoop, CycleComplete, Error,
    }

    public FarmState State         { get; private set; } = FarmState.Idle;
    public bool      IsRunning     { get; private set; }
    /// <summary>Graceful stop: finish the current dungeon run (AutoDuty/ADS untouched mid-run), then stop before the next one.</summary>
    public bool      StopAfterRunRequested { get; private set; }
    public bool      IsRepairTest   => _repairTestMode;
    public bool      IsDeliveryTest => _deliveryTestMode;
    public bool      IsShopTest     => _shopTestMode;
    public bool      IsExtractTest  => _extractTestMode;
    public bool      IsAnyTestMode  => _repairTestMode || _deliveryTestMode || _shopTestMode || _extractTestMode;
    public string    StatusMessage { get; private set; } = "Idle";
    public string?   LastError     { get; private set; }
    public int       TotalCycles    { get; private set; }
    public int       TotalRuns      { get; private set; }
    public int       TotalSeals     { get; private set; }
    public int       TotalDuckbones { get; private set; }
    public DateTime  StartTime      { get; private set; }
    public TimeSpan AverageClearTime => _runClearTimes.Count == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(_runClearTimes.Average(t => t.TotalSeconds));
    public TimeSpan FastestClearTime => _runClearTimes.Count == 0
        ? TimeSpan.Zero
        : _runClearTimes.Min();
    public TimeSpan SlowestClearTime => _runClearTimes.Count == 0
        ? TimeSpan.Zero
        : _runClearTimes.Max();
    public int TotalRunsTracked => _runClearTimes.Count;
    public int RunsThisCycle => _runsThisCycle;

    private int   _runsThisCycle;
    private int   _pendingHandinRow = -1;
    private uint  _pendingHandinItemId;
    private int   _sealsWhenHandinPending;
    private int   _sealsBefore;
    private Task? _currentTask;
    private FarmState _gcNavFinalState;
    private DateTime  _zoneWaitStartTime;
    private DateTime  _subZoneStartTime;
    private uint      _subZoneTargetZone;
    private DateTime  _dutyExitStartTime;
    private DateTime  _currentRunStart;
    private readonly List<TimeSpan> _runClearTimes = new();
    private DateTime? _dutyExitReadyAt;
    private bool      _gcInitialSpend;
    private bool      _deliveryListEmpty;
    private bool      _deliveryBlockedByCap;
    private DateTime? _deliveryListLostAt;
    private DateTime? _pendingHandinSince;
    private bool      _gcShopRankSelected;
    private bool      _gcShopCategorySelected;
    private Vector3   _gcNavDest;
    private string    _gcNavNpcName = string.Empty;
    private FarmState _gcNavNextState;
    private const int GcUiCooldownMs = 100;
    private DateTime _gcActionCooldownUntil;
    private bool _buyAwaitingConfirm;
    private int _buyPendingQty;
    private int _buyLastQty;
    private string _buyLastItemLabel = string.Empty;
    private int _deliverClickedForRow = -1;
    private DateTime? _buyPhaseSince;
    private DateTime? _buyAttemptSince;

    private static readonly string PluginVersion =
        typeof(FarmController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
    private bool _buyQtyDialogSent;
    private int _buyFindItemFailures;
    private const int BuyAttemptTimeoutMs = 20_000;
    private const int BuyPhaseTimeoutMs = 180_000;
    private const int BuyFindItemMaxFailures = 8;
    private bool _cycleCounted;
    private bool _openGcShopRetried;
    private bool _repairAllClicked;
    private bool _repairYesnoLogged;
    private DateTime? _repairPhaseSince;
    private bool _adsRepairAttemptedBeforeDuty;
    private bool _adsLeaveRequestedForFinalRun;
    private DateTime _lastAdsCombatRefreshUtc;
    private bool _deliveryFinishing;
    private bool _expectNpcMenu;
    private bool _automationOwnsGcPersonnelUi;
    private bool _repairTestMode;
    private bool _deliveryTestMode;
    private bool _shopTestMode;
    private bool _extractTestMode;
    private int _buyListIndex;
    private GcShopBuyEntry? _oneShotBuyEntry;
    private bool _oneShotBuyAttemptSent;
    private FarmState _extractReturnState = FarmState.StartDuty;
    private int _materializeCategory;
    private bool _materializeCategoryArmed;
    private bool _materializeAttemptPending;
    private bool _extractAttemptedAny;
    private DateTime? _materializePhaseSince;
    private DateTime _materializeLastActionUtc;
    private int _lastBetweenRunExtractRun = -1;
    private int _lastCycleBoundaryExtractCycle = -1;
    private DutySupportDuty? _pendingDutySupportDuty;
    private DateTime? _dutySupportQueueSince;
    private DateTime _dutySupportLastActionUtc;
    private bool _adsNeedsStartInside;
    private DateTime _autoDutyStoppedAt = DateTime.MinValue;
    private bool _pendingKingcakeDesynthStats;
    private DateTime _kingcakeDesynthStartedUtc;
    private DateTime? _kingcakeDesynthConsumedUtc;
    private int _kingcakeDesynthStartCount;
    private Dictionary<uint, int> _kingcakeDesynthStartDropCounts = new();
    private List<uint> _kingcakeDesynthObservedResultDrops = [];
    private const int DutyExitTimeoutMs = 90_000;
    private string?   _lastStatusLogMessage;
    private DateTime  _lastStatusLogAt;
    private const int StatusLogThrottleMs = 5000;
    private const int GcZoneWaitTimeoutMs = 60_000;
    private const int GcSubZoneWaitTimeoutMs = 30_000;
    private const float GcNpcArriveRange = 5f;
    private const int PostDutySettleMs = 2500;
    private const int RepairPhaseTimeoutMs = 45_000;
    private const int MaterializeGeneralAction = 14;
    private const int EquippedMaterializeCategory = 0;
    private const int LastMaterializeCategory = EquippedMaterializeCategory;
    private const int MaterializeResultWaitMs = 2500;
    private const int MaterializePhaseTimeoutMs = 60_000;
    private const uint MateriaExtractionQuestId = 66174;
    private const ushort FullSpiritbond = 10_000;
    private const int DutySupportQueueTimeoutMs = 120_000;
    private const int DutySupportOpenRetryMs = 2_000;
    private const int DutySupportRegisterRetryMs = 10_000;

    private readonly AddonLifecycleRegistration _addonLifecycle = new();

    public FarmController()
    {
        Service.Framework.Update += OnFrameworkUpdate;
        _addonLifecycle.Register(AddonEvent.PostSetup, "GrandCompanySupplyList", OnSupplyListPostSetup);
    }

    public void Dispose()
    {
        Service.Framework.Update -= OnFrameworkUpdate;
        _addonLifecycle.Dispose();
        IpcManager.VnavStop();
    }

    /// <summary>HaselTweaks-style Expert Deliveries tweak: flip the supply window to the Expert Delivery tab as it opens.
    /// Only active while the farm or a test is running so manual use is untouched once stopped.</summary>
    private unsafe void OnSupplyListPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!Plugin.Config.OpenExpertDeliveryTabDirectly || !IsRunning)
            return;

        var addon = (AddonGrandCompanySupplyList*)args.Addon.Address;
        if (addon == null || addon->SelectedTab == GcExpertDeliveryTab)
            return;

        var atk = (AtkUnitBase*)addon;
        var evt = stackalloc AtkEvent[1];
        *evt = default;
        atk->ReceiveEvent(AtkEventType.ButtonClick, 4, evt);
        Log("Supply window opened — switched straight to Expert Delivery tab");
    }

    public void Start()
    {
        if (IsRunning) return;
        if (!Service.ClientState.IsLoggedIn)
        {
            Log("Cannot start — not logged in.");
            return;
        }

        if (!CanRunWorldAutomation())
        {
            Log("Cannot start — character is zoning, occupied, or unavailable.");
            return;
        }

        IsRunning = true; TotalCycles = 0; TotalRuns = 0; TotalSeals = 0;
        TotalDuckbones = 0; StartTime = DateTime.Now; _runsThisCycle = 0; _cycleCounted = false; LastError = null;
        StopAfterRunRequested = false;
        _currentRunStart = default;
        _runClearTimes.Clear();
        _gcInitialSpend = true; _deliveryListEmpty = false; _deliveryBlockedByCap = false;
        _deliveryFinishing = false; _expectNpcMenu = false;
        _automationOwnsGcPersonnelUi = false;
        _repairTestMode = false; _deliveryTestMode = false; _shopTestMode = false; _extractTestMode = false;
        _oneShotBuyEntry = null;
        _oneShotBuyAttemptSent = false;
        _adsRepairAttemptedBeforeDuty = false;
        _adsLeaveRequestedForFinalRun = false;
        _lastAdsCombatRefreshUtc = DateTime.MinValue;
        _autoDutyStoppedAt = DateTime.MinValue;
        _lastBetweenRunExtractRun = -1; _lastCycleBoundaryExtractCycle = -1;
        ResetMateriaExtractionState();
        ResetDutySupportQueueState();
        Plugin.Config.ApplyAutomaticGrandCompanySettings();
        GotoState(FarmState.CheckSealSpend);
        Log($"SealBreaker v{PluginVersion} started — current seals: {GetCurrentSeals():N0}");
    }

    public void StartExpertDeliveryTest()
    {
        if (IsRunning && !_deliveryTestMode)
        {
            Log("Cannot start delivery test while farm is running — stop the farm first");
            return;
        }

        if (!IpcManager.VnavAvailable)
        {
            SetError("vnavmesh IPC not available");
            return;
        }

        if (!IpcManager.LifestreamAvailable)
        {
            SetError("Lifestream IPC not available");
            return;
        }

        _deliveryTestMode = true;
        _shopTestMode = false;
        _repairTestMode = false;
        _extractTestMode = false;
        _gcInitialSpend = false;
        IsRunning = true;
        LastError = null;
        StartTime = DateTime.Now;
        _currentTask = null;
        _deliveryListEmpty = false;
        _deliveryBlockedByCap = false;
        Log("Expert Delivery test — navigating to personnel officer...");
        BeginGcNavigation(FarmState.OpenExpertDelivery);
    }

    public void StartShopTest()
    {
        if (IsRunning && !_shopTestMode)
        {
            Log("Cannot start shop test while farm is running — stop the farm first");
            return;
        }

        if (!IpcManager.VnavAvailable)
        {
            SetError("vnavmesh IPC not available");
            return;
        }

        if (!IpcManager.LifestreamAvailable)
        {
            SetError("Lifestream IPC not available");
            return;
        }

        if (Plugin.Config.EnabledGcShopBuyList().Count == 0)
        {
            SetError("No enabled GC shop buy entries — add items on the Buy List tab");
            return;
        }

        _shopTestMode = true;
        _deliveryTestMode = false;
        _repairTestMode = false;
        _extractTestMode = false;
        _gcInitialSpend = false;
        _buyListIndex = 0;
        _gcShopRankSelected = false;
        _gcShopCategorySelected = false;
        IsRunning = true;
        LastError = null;
        StartTime = DateTime.Now;
        _currentTask = null;
        Log("GC shop test — navigating to quartermaster...");
        BeginGcNavigation(FarmState.OpenGCShop);
    }

    public void StartKingcakeBuyTest()
    {
        if (IsRunning && !_shopTestMode)
        {
            Log("Cannot start Kingcake buy test while farm is running — stop the farm first");
            return;
        }

        if (!IpcManager.VnavAvailable)
        {
            SetError("vnavmesh IPC not available");
            return;
        }

        if (!IpcManager.LifestreamAvailable)
        {
            SetError("Lifestream IPC not available");
            return;
        }

        _shopTestMode = true;
        _deliveryTestMode = false;
        _repairTestMode = false;
        _extractTestMode = false;
        _gcInitialSpend = false;
        _buyListIndex = 0;
        _gcShopRankSelected = false;
        _gcShopCategorySelected = false;
        _oneShotBuyEntry = new GcShopBuyEntry
        {
            Enabled = true,
            ItemName = "Kingcake",
            ItemId = 13595,
            SealCost = 5000,
            CategoryTab = GcShopCategoryResolver.TabMateriel,
            RankTab = 2,
            ListRow = -1,
            BuyQtyPerPurchase = 1,
        };
        _oneShotBuyAttemptSent = false;
        IsRunning = true;
        LastError = null;
        StartTime = DateTime.Now;
        _currentTask = null;
        Log("One-shot Kingcake buy test — navigating to quartermaster...");
        BeginGcNavigation(FarmState.OpenGCShop);
    }

    public void StartRepair()
    {
        if (IsRunning && !_repairTestMode)
        {
            Log("Cannot start repair test while farm is running — stop the farm first");
            return;
        }

        var gcIdx = Plugin.Config.GrandCompanyIndex;
        var town = Plugin.Config.TownNav(gcIdx);

        if (!IpcManager.VnavAvailable)
        {
            SetError("vnavmesh IPC not available");
            return;
        }

        if (!town.HasMenderConfigured)
        {
            SetError("Mender not configured — set name and position on the GC town tab");
            return;
        }

        _repairTestMode = true;
        _deliveryTestMode = false;
        _shopTestMode = false;
        _extractTestMode = false;
        _repairAllClicked = false;
        _repairYesnoLogged = false;
        _repairPhaseSince = null;
        IsRunning = true;
        LastError = null;
        StartTime = DateTime.Now;
        _currentTask = null;

        var zone = Service.ClientState.TerritoryType;
        var officerZone = GcOfficerZoneId[gcIdx];
        var routeDesc = GcNavRoutes.HasRepairRoute(Plugin.Config, gcIdx)
            ? $"{GcNavRoutes.GetRepairPath(Plugin.Config, gcIdx).Length} waypoint repair route"
            : "direct vnav to mender";

        Log($"Repair test — {town.MenderName}, {routeDesc} (zone {zone})");

        if (zone == officerZone)
            GotoState(FarmState.NavigateToRepair);
        else if (IsInGcAcceptedZone(gcIdx, zone))
            BeginGcNavigation(FarmState.NavigateToRepair);
        else
            BeginGcNavigation(FarmState.NavigateToRepair);
    }

    public void StartExtractTest()
    {
        if (IsRunning && !_extractTestMode)
        {
            Log("Cannot start extract test while farm is running — stop the farm first");
            return;
        }

        if (!CanRunWorldAutomation())
        {
            Log("Cannot start extract test — character is zoning, occupied, or unavailable.");
            return;
        }

        _extractTestMode = true;
        _repairTestMode = false;
        _deliveryTestMode = false;
        _shopTestMode = false;
        _extractReturnState = FarmState.Idle;
        ResetMateriaExtractionState();
        IsRunning = true;
        LastError = null;
        StartTime = DateTime.Now;
        _currentTask = null;

        Log("Materia extraction test — opening materia extraction...");
        GotoState(FarmState.OpenMateriaExtraction);
    }

    /// <summary>Arm/disarm a graceful stop. Never interrupts the duty runner mid-dungeon —
    /// the run completes normally and the farm stops before launching the next one.</summary>
    public void ToggleStopAfterRun()
    {
        if (!IsRunning || IsAnyTestMode)
            return;

        StopAfterRunRequested = !StopAfterRunRequested;
        Log(StopAfterRunRequested
            ? "Stop requested — the current dungeon run will finish, then the farm stops"
            : "Stop-after-run cancelled — the farm keeps looping");
    }

    public void Stop()
    {
        IsRunning = false;
        StopAfterRunRequested = false;
        _repairTestMode = false;
        _deliveryTestMode = false;
        _shopTestMode = false;
        _extractTestMode = false;
        _oneShotBuyEntry = null;
        _oneShotBuyAttemptSent = false;
        _automationOwnsGcPersonnelUi = false;
        ResetMateriaExtractionState();
        ResetDutySupportQueueState();
        _adsLeaveRequestedForFinalRun = false;
        _lastAdsCombatRefreshUtc = DateTime.MinValue;
        CloseMateriaExtractionUi();
        IpcManager.VnavStop();
        IpcManager.LifestreamAbort();
        StopDutyRunner();
        _currentTask = null;
        _expectNpcMenu = false;
        GotoState(FarmState.Idle);
        Log("Farm stopped.");
    }

    private Task<bool> IsNavCancelledAsync() => Task.FromResult(!IsRunning);

    private void OnFrameworkUpdate(IFramework fw)
    {
        if (!Service.ClientState.IsLoggedIn)
        {
            if (IsRunning)
                SetError("Character logged out");
            return;
        }

        PollKingcakeDesynthResult();
        TryDismissStuckOfficerMenuTick();

        if (!IsRunning) return;
        if (_currentTask is { IsCompleted: false }) return;
        if (_currentTask is { IsFaulted: true })
        {
            var ex = _currentTask.Exception?.GetBaseException();
            var msg = ex?.Message ?? "Unknown task error";
            Log($"WARN: Background task failed ({msg}) — attempting to continue");
            _currentTask = null;
            if (State is FarmState.ProcessDelivery or FarmState.CheckGcLoop)
            {
                _deliveryFinishing = false;
                GotoState(FarmState.CheckGcLoop);
            }
            else
            {
                SetError(msg);
            }
            return;
        }
        _currentTask = null;
        try { Tick(); } catch (Exception ex) { SetError(ex.Message); }
    }

    private void Tick()
    {
        var cfg   = Plugin.Config;
        cfg.ApplyAutomaticGrandCompanySettings();
        var gcIdx = cfg.GrandCompanyIndex;

        switch (State)
        {
            case FarmState.CheckSealSpend:
            {
                var seals = GetCurrentSeals();
                Log($"Seal check — current: {seals:N0}, duckbone cost: {GcShopDefaults.DuckboneSealCost + Plugin.Config.SealReserve:N0}");

                if (ShouldEnterGcSpendLoop())
                {
                    if (!IpcManager.VnavAvailable) { SetError("vnavmesh IPC not available"); return; }
                    BeginGcNavigation(FarmState.OpenExpertDelivery);
                }
                else
                {
                    Log($"Seals at {seals:N0} — below spend threshold, starting duties");
                    _gcInitialSpend = false;
                    GotoState(FarmState.StartDuty);
                }
                break;
            }

            case FarmState.StartDuty:
            {
                if (StopAfterRunRequested)
                {
                    Log("Stop-after-run: stopping before the next duty.");
                    Stop();
                    break;
                }

                if (IsMidDutyCycle(cfg))
                {
                    _currentTask = ContinueDutyAsync();
                    break;
                }

                if (TryBeginCycleBoundaryMateriaExtraction())
                    break;

                LogPreDutyGearCheck();
                if (TryBeginRepairBeforeDuty())
                    break;

                _currentTask = StartDutyAsync();
                break;
            }

            case FarmState.WaitingForDutyStart:
                if (InDuty())
                {
                    if (cfg.DutyRunner == 1 && _adsNeedsStartInside)
                    {
                        if (!TryStartAdsInsideDuty())
                        {
                            SetError("ADS failed to start after entering Duty Support duty");
                            return;
                        }
                    }

                    _cycleCounted = false;
                    _currentRunStart = DateTime.Now;
                    _adsRepairAttemptedBeforeDuty = false;
                    _adsLeaveRequestedForFinalRun = false;
                    RefreshAdsCombatAutomationIfNeeded(force: true);
                    Status($"In duty — run {_runsThisCycle + 1}/{cfg.RunsPerCycle}");
                    GotoState(FarmState.WaitingForDutyComplete);
                }
                break;

            case FarmState.WaitingForDutyComplete:
            {
                var runner = cfg.DutyRunner;
                var dutyStopped = runner == 0
                    ? IpcManager.AutoDutyIsStopped()
                    : IpcManager.AdsIsStopped();

                if (runner == 0 && dutyStopped)
                    RecordAutoDutyStopped();

                if (runner == 1 && InDuty())
                {
                    RefreshAdsCombatAutomationIfNeeded();

                    if (dutyStopped && (_runsThisCycle + 1 >= cfg.RunsPerCycle || StopAfterRunRequested))
                    {
                        if (!_adsLeaveRequestedForFinalRun)
                        {
                            _adsLeaveRequestedForFinalRun = IpcManager.AdsLeaveDuty();
                            Log(_adsLeaveRequestedForFinalRun
                                ? "ADS run complete — requested duty leave"
                                : "WARN: ADS run complete but /ads leave could not be sent");
                        }

                        StatusQuiet("ADS run complete — waiting to leave duty...");
                    }
                }

                if (!InDuty() && dutyStopped)
                {
                    if (_currentRunStart != default)
                    {
                        var clearTime = DateTime.Now - _currentRunStart;
                        _runClearTimes.Add(clearTime);
                        _currentRunStart = default;
                        Log($"Run cleared in {clearTime:mm\\:ss} | Avg: {AverageClearTime:mm\\:ss} | Fastest: {FastestClearTime:mm\\:ss}");
                    }

                    _runsThisCycle++; TotalRuns++;
                    Status($"Run {_runsThisCycle}/{cfg.RunsPerCycle} complete (total {TotalRuns})");

                    if (StopAfterRunRequested)
                    {
                        Log($"Run complete — stopping as requested (total runs this session: {TotalRuns}).");
                        Stop();
                        break;
                    }

                    if (_runsThisCycle >= cfg.RunsPerCycle)
                    {
                        _deliveryListEmpty = false;
                        _dutyExitReadyAt   = null;
                        _dutyExitStartTime = DateTime.Now;
                        GotoState(FarmState.WaitingForDutyExit);
                    }
                    else
                    {
                        if (TryBeginBetweenRunMateriaExtraction())
                            break;

                        GotoState(FarmState.StartDuty);
                    }
                }
                break;
            }

            case FarmState.WaitingForDutyExit:
            {
                if (DateTime.Now - _dutyExitStartTime > TimeSpan.FromMilliseconds(DutyExitTimeoutMs))
                {
                    SetError($"Timed out leaving duty (zone {Service.ClientState.TerritoryType})");
                    return;
                }

                if (!CanExecuteGcTeleport())
                {
                    StatusQuiet("Leaving duty — waiting before GC teleport...");
                    _dutyExitReadyAt = null;
                    break;
                }

                if (_dutyExitReadyAt == null)
                {
                    _dutyExitReadyAt = DateTime.Now;
                    Log($"Duty exit ready in zone {Service.ClientState.TerritoryType} — settling {PostDutySettleMs / 1000.0:0.#}s...");
                    Status("Duty complete — settling before teleport...");
                    break;
                }

                if (DateTime.Now - _dutyExitReadyAt.Value >= TimeSpan.FromMilliseconds(PostDutySettleMs))
                {
                    _dutyExitReadyAt = null;
                    BeginGcNavigation(FarmState.OpenExpertDelivery);
                }
                break;
            }

            case FarmState.TeleportToGC:
            {
                var zone = Service.ClientState.TerritoryType;
                if (IsInGcAcceptedZone(gcIdx, zone))
                {
                    GotoState(FarmState.CheckSubZone);
                    break;
                }

                ExecuteLifestreamTeleport(GcCityTeleportCommand[gcIdx]);
                _zoneWaitStartTime = DateTime.Now;
                GotoState(FarmState.WaitingForZone);
                break;
            }

            case FarmState.WaitingForZone:
            {
                if (DateTime.Now - _zoneWaitStartTime > TimeSpan.FromMilliseconds(GcZoneWaitTimeoutMs))
                {
                    SetError($"GC zone teleport timed out (zone {Service.ClientState.TerritoryType})");
                    return;
                }

                if (IpcManager.LifestreamIsBusy() || IsBetweenAreas())
                {
                    StatusQuiet($"Waiting for GC city teleport (zone {Service.ClientState.TerritoryType})...");
                    break;
                }

                if (!IsInGcAcceptedZone(gcIdx, Service.ClientState.TerritoryType))
                {
                    StatusQuiet($"Waiting for GC zone (current {Service.ClientState.TerritoryType})...");
                    break;
                }

                GotoState(FarmState.CheckSubZone);
                break;
            }

            case FarmState.CheckSubZone:
            {
                var zone = Service.ClientState.TerritoryType;
                var officerZone = GcOfficerZoneId[gcIdx];
                if (zone == officerZone)
                {
                    GotoState(GetGcWalkDestination());
                    break;
                }

                ExecuteLifestreamTeleport(GcSubZoneTeleportCommand[gcIdx]);
                _subZoneTargetZone = officerZone;
                _subZoneStartTime = DateTime.Now;
                GotoState(FarmState.WaitingForSubZone);
                break;
            }

            case FarmState.WaitingForSubZone:
            {
                if (DateTime.Now - _subZoneStartTime > TimeSpan.FromMilliseconds(GcSubZoneWaitTimeoutMs))
                {
                    SetError($"GC sub-zone teleport timed out (zone {Service.ClientState.TerritoryType}, target {_subZoneTargetZone})");
                    return;
                }

                if (IpcManager.LifestreamIsBusy() || IsBetweenAreas())
                {
                    StatusQuiet($"Waiting for GC sub-zone {_subZoneTargetZone} (current {Service.ClientState.TerritoryType})...");
                    break;
                }

                if (Service.ClientState.TerritoryType != _subZoneTargetZone)
                    break;

                GotoState(GetGcWalkDestination());
                break;
            }

            case FarmState.NavigateToOfficer:
                if (!IpcManager.VnavAvailable) { SetError("vnavmesh IPC not available"); return; }
                RouteGcNavigation(gcIdx, GcOfficerPos[gcIdx], GcOfficerName[gcIdx], FarmState.OpenExpertDelivery);
                break;

            case FarmState.NavigateToShop:
                if (!IpcManager.VnavAvailable) { SetError("vnavmesh IPC not available"); return; }
                _currentTask = BeginNavigateToShopAsync();
                break;

            case FarmState.NavigateToGcTarget:
                if (!IpcManager.VnavAvailable) { SetError("vnavmesh IPC not available"); return; }
                Status($"Navigating to {_gcNavNpcName}...");
                _currentTask = NavigateAndInteractAsync(_gcNavDest, _gcNavNpcName, _gcNavNextState);
                break;

            case FarmState.OpenExpertDelivery:  _currentTask = OpenExpertDeliveryAsync(); break;
            case FarmState.ProcessDelivery:      ProcessDeliveryTick(); break;

            case FarmState.OpenGCShop:   _currentTask = OpenGCShopAsync(); break;
            case FarmState.BuyDuckbones: BuyDuckbonesTick(); break;

            case FarmState.NavigateToRepair:
            {
                var repairGcIdx = Plugin.Config.GrandCompanyIndex;
                var town = Plugin.Config.TownNav(repairGcIdx);
                if (Service.ClientState.TerritoryType != GcOfficerZoneId[repairGcIdx])
                {
                    var msg = $"Not in GC officer zone {GcOfficerZoneId[repairGcIdx]} (current {Service.ClientState.TerritoryType})";
                    if (_repairTestMode)
                        SetError(msg);
                    else
                    {
                        Log($"Repair skipped — {msg}");
                        GotoState(FarmState.StartDuty);
                    }
                    break;
                }
                if (!_repairTestMode && (!Plugin.Config.RepairEnabled || !town.HasMenderConfigured))
                {
                    Log("Repair skipped — not enabled or mender not configured");
                    GotoState(FarmState.StartDuty);
                    break;
                }
                if (_repairTestMode && !town.HasMenderConfigured)
                {
                    SetError("Mender not configured");
                    break;
                }
                _currentTask = NavigateToRepairAsync();
                break;
            }

            case FarmState.OpenRepairNpc:
            {
                var town = Plugin.Config.TownNav(Plugin.Config.GrandCompanyIndex);
                _currentTask = InteractRepairMenderAsync(town.MenderPosition, town.MenderName);
                break;
            }
            case FarmState.OpenRepairMenu: _currentTask = OpenRepairMenuAsync(); break;
            case FarmState.ProcessRepair:
                IpcManager.VnavStop();
                ProcessRepairTick();
                break;

            case FarmState.NavigateFromRepair:
                _currentTask = NavigateFromRepairAsync();
                break;

            case FarmState.OpenMateriaExtraction:
                OpenMateriaExtractionTick();
                break;

            case FarmState.ProcessMateriaExtraction:
                ProcessMateriaExtractionTick();
                break;

            case FarmState.OpenDutySupport:
                OpenDutySupportTick();
                break;

            case FarmState.QueueDutySupport:
                QueueDutySupportTick();
                break;

            case FarmState.CheckGcLoop:
            {
                var seals = GetCurrentSeals();
                Log($"GC loop check — seals: {seals:N0}, delivery empty: {_deliveryListEmpty}, blocked by cap: {_deliveryBlockedByCap}, can buy: {CanAffordDuckbone()}");

                if (CanAffordDuckbone())
                {
                    _gcShopRankSelected = false;
                    _gcShopCategorySelected = false;
                    _buyListIndex = 0;
                    BeginGcNavigation(FarmState.OpenGCShop);
                }
                else if (!_deliveryListEmpty || _deliveryBlockedByCap)
                {
                    _deliveryBlockedByCap = false;
                    _deliveryListEmpty = false;
                    _pendingHandinRow = -1;
                    BeginGcNavigation(FarmState.OpenExpertDelivery);
                }
                else if (_gcInitialSpend)
                {
                    _gcInitialSpend = false;
                    Log("GC spend complete — starting duty cycle");
                    GotoState(FarmState.StartDuty);
                }
                else
                {
                    GotoState(FarmState.CycleComplete);
                }
                break;
            }

            case FarmState.CycleComplete:
                var elapsed = DateTime.Now - StartTime;
                Log($"Cycle {TotalCycles} complete | Runs:{TotalRuns} Seals:{TotalSeals} Duckbones:{TotalDuckbones} Runtime:{elapsed:hh\\:mm\\:ss}");
                if (TryBeginCycleBoundaryMateriaExtraction())
                    break;
                GotoState(FarmState.StartDuty);
                break;

            case FarmState.Idle:
            case FarmState.Error:
                break;
        }
    }

    // ── Async steps ───────────────────────────────────────────

    private void BeginGcNavigation(FarmState finalState)
    {
        _gcNavFinalState = finalState;
        _openGcShopRetried = false;
        IpcManager.VnavStop();
        GotoState(FarmState.TeleportToGC);
    }

    private FarmState GetGcWalkDestination() => _gcNavFinalState switch
    {
        FarmState.OpenGCShop => FarmState.NavigateToShop,
        FarmState.NavigateToRepair => FarmState.NavigateToRepair,
        _ => FarmState.NavigateToOfficer,
    };

    private void FinishRepairTest()
    {
        _repairTestMode = false;
        _automationOwnsGcPersonnelUi = false;
        IsRunning = false;
        _repairAllClicked = false;
        _repairYesnoLogged = false;
        _repairPhaseSince = null;
        GotoState(FarmState.Idle);
        Log("Repair test finished.");
    }

    private void FinishDeliveryTest()
    {
        _deliveryTestMode = false;
        _deliveryFinishing = false;
        _expectNpcMenu = false;
        if (ShouldAutoDismissGcOfficerMenu())
            Service.Framework.RunOnFrameworkThread(CloseExpertDeliveryUiForce);
        _automationOwnsGcPersonnelUi = false;
        IsRunning = false;
        GotoState(FarmState.Idle);
        Log("Expert Delivery test finished.");
    }

    private void FinishShopTest()
    {
        _shopTestMode = false;
        _automationOwnsGcPersonnelUi = false;
        _oneShotBuyEntry = null;
        _oneShotBuyAttemptSent = false;
        _buyListIndex = 0;
        _gcShopRankSelected = false;
        _gcShopCategorySelected = false;
        ResetBuyAttempt();
        _buyPhaseSince = null;
        _buyFindItemFailures = 0;
        IsRunning = false;
        GotoState(FarmState.Idle);
        Log("GC shop test finished.");
    }

    private static bool IsInGcAcceptedZone(int gcIdx, uint zoneId)
    {
        foreach (var id in GcZoneIds[gcIdx])
        {
            if (zoneId == id)
                return true;
        }

        return false;
    }

    private static void ExecuteLifestreamTeleport(string command)
    {
        if (IpcManager.LifestreamExecute(command))
        {
            Log($"Lifestream zone teleport: {command}");
            return;
        }

        Log($"Lifestream IPC unavailable — /li {command}");
        SendLifestreamChat(command, useTpPrefix: false);
    }

    private async Task BeginNavigateToShopAsync()
    {
        try
        {
            await CloseExpertDeliveryUiAsync();
        }
        catch (Exception ex)
        {
            await LogAsync($"WARN: Pre-shop UI close failed ({ex.Message}) — continuing");
        }

        if (!IsRunning)
            return;

        var gcIdx = Plugin.Config.GrandCompanyIndex;
        RouteGcNavigation(gcIdx, GcShopPos[gcIdx], GcShopName[gcIdx], FarmState.OpenGCShop);
    }

    private void RouteGcNavigation(int gcIdx, Vector3 dest, string npcName, FarmState nextState)
    {
        if (!IpcManager.VnavAvailable) { SetError("vnavmesh IPC not available"); return; }

        _automationOwnsGcPersonnelUi = true;
        CloseExpertDeliveryUiForce();
        _gcNavDest      = dest;
        _gcNavNpcName   = npcName;
        _gcNavNextState = nextState;
        Log($"GC nav — target {npcName} at {dest}");
        GotoState(FarmState.NavigateToGcTarget);
    }

    private const float NpcInteractRange = 3.25f;
    private const float NpcApproachRange = 3.0f;
    private const float RepairNpcApproachRange = 1.6f;
    private const float GenericRepairReturnReconnectRadius = 40f;
    private static readonly Random _rng = new();

    private static int Jitter(int baseMs, int rangeMs = 100)
    {
        lock (_rng)
            return Math.Max(50, baseMs + _rng.Next(-rangeMs / 2, rangeMs / 2));
    }

    private async Task NavigateToRepairAsync()
    {
        if (!IsRunning)
            return;

        var gcIdx = Plugin.Config.GrandCompanyIndex;
        var town = Plugin.Config.TownNav(gcIdx);
        var repairPath = GcNavRoutes.GetRepairPath(Plugin.Config, gcIdx);
        if (repairPath.Length > 0)
        {
            await StatusAsync("Navigating to repair mender (vnav)...");
            await WalkHumanizedRouteAsync(repairPath, reverse: false, "Repair", preventBacktrack: true);

            if (!IsRunning)
                return;

            await InteractRepairMenderAsync(town.MenderPosition, town.MenderName);
            return;
        }

        RouteGcNavigation(gcIdx, town.MenderPosition, town.MenderName, FarmState.OpenRepairNpc);
    }

    private async Task InteractRepairMenderAsync(Vector3 menderPos, string menderName)
    {
        if (!IsRunning)
            return;

        IpcManager.VnavStop();
        await WaitForMovementStopAsync(500);

        if (IsAddonVisible("Repair"))
        {
            await LogAsync("Repair window already open");
            await BeginRepairPhaseAsync();
            return;
        }

        var approachPos = await Service.Framework.RunOnFrameworkThread(() =>
            FindNpcByName(menderName, menderPos, 80f)?.Position ?? menderPos);
        var dist = await GetPlayerDistToAsync(approachPos);
        if (dist.HasValue && dist.Value > RepairNpcApproachRange)
        {
            await LogAsync($"Short vnav to {menderName} ({dist.Value:F1}y away)...");
            await PathfindToPointAsync(approachPos, RepairNpcApproachRange, 30_000);
            IpcManager.VnavStop();
            await WaitForMovementStopAsync(500);
        }

        if (!IsRunning)
            return;

        await LogAsync($"Interacting with {menderName}...");
        await Service.Framework.RunOnFrameworkThread(() => _expectNpcMenu = true);
        await Service.Framework.RunOnFrameworkThread(() => TargetAndInteract(menderName, menderPos));
        await Task.Delay(600);
        IpcManager.VnavStop();

        if (await WaitForAddonVisibleAsync("Repair", 3000))
        {
            await BeginRepairPhaseAsync();
            return;
        }

        if (await WaitForAddonVisibleAsync("SelectString", 3000))
        {
            await Service.Framework.RunOnFrameworkThread(() => SelectSelectStringOption(0));
            await Task.Delay(250);
            if (await WaitForAddonVisibleAsync("Repair", 5000))
            {
                await BeginRepairPhaseAsync();
                return;
            }
        }

        _expectNpcMenu = false;
        await SetErrorAsync($"Could not open repair menu for {menderName}");
    }

    private async Task BeginRepairPhaseAsync()
    {
        IpcManager.VnavStop();
        _expectNpcMenu = false;
        _repairAllClicked = false;
        _repairYesnoLogged = false;
        _repairPhaseSince = DateTime.UtcNow;
        _gcActionCooldownUntil = DateTime.MinValue;
        await StatusAsync("Repairing gear...");
        await GotoStateAsync(FarmState.ProcessRepair);
    }

    private async Task NavigateFromRepairAsync()
    {
        if (!IsRunning)
            return;

        IpcManager.VnavStop();
        await Service.Framework.RunOnFrameworkThread(CloseRepairUi);
        await WaitForMovementStopAsync(300);

        var gcIdx = Plugin.Config.GrandCompanyIndex;
        var returnPath = GcNavRoutes.GetRepairReturnPath(Plugin.Config, gcIdx);

        if (returnPath.Length > 0)
        {
            await StatusAsync("Walking back from mender...");
            await WalkHumanizedRouteAsync(returnPath, reverse: false, "Repair return", preventBacktrack: true);
        }

        if (gcIdx == 0 && IsRunning)
        {
            await LogAsync($"Return vnav → {MaelstromGcUpperHub}...");
            if (!await PathfindToPointAsync(MaelstromGcUpperHub, 6f, GcWaypointTimeoutMs))
                await LogAsync($"WARN: return vnav to GC hub failed or timed out");
            IpcManager.VnavStop();
        }

        if (!IsRunning)
            return;

        if (_repairTestMode)
            await Service.Framework.RunOnFrameworkThread(FinishRepairTest);
        else
            await GotoStateAsync(FarmState.StartDuty);
    }

    private async Task NavigateAndInteractAsync(Vector3 dest, string npcName, FarmState nextState, bool skipStaging = false)
    {
        if (!IsRunning) return;

        if (nextState == FarmState.OpenGCShop)
        {
            await Service.Framework.RunOnFrameworkThread(CloseExpertDeliveryUiForce);
            if (await Service.Framework.RunOnFrameworkThread(IsAnyPersonnelUiOpen))
            {
                await LogAsync("Personnel menu still open — stepping away before quartermaster");
                await BreakNpcInteractionAsync();
            }
        }

        await StatusAsync($"Navigating to {npcName}...");

        if (!skipStaging && !await EnsureAtGcStagingHubAsync(dest, npcName))
        {
            if (IsRunning)
                await SetErrorAsync("GC staging failed — could not reach The Aftcastle / command room");
            return;
        }

        if (!IsRunning) return;

        if (Plugin.Config.GrandCompanyIndex == 0
            && Service.ClientState.TerritoryType == MaelstromRepairZone
            && dest.Y > 18f
            && await Service.Framework.RunOnFrameworkThread(() =>
            {
                var player = Service.ObjectTable.LocalPlayer;
                return player != null
                       && IsOnMaelstromGcWalkway(player.Position)
                       && !IsNearMaelstromGcMerchantArea(player.Position, npcName);
            }))
        {
            await LogAsync($"Still far from {npcName} on walkway — continuing mapped routes...");
            await WalkTowardGcMerchantsAsync(dest, npcName);
            if (!IsRunning)
                return;
        }

        var moveDest = await Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            var walkwayHint = GetMaelstromGcWalkwayHint(dest, npcName);
            var npc = FindMaelstromGcNpc(npcName, dest);
            if (npc != null)
            {
                Log($"Using live NPC position for {npcName}: {npc.Position}");
                return player != null && IsOnMaelstromGcWalkway(player.Position)
                    ? AdaptGcTargetForWalkway(npc.Position, player.Position)
                    : npc.Position;
            }

            Log($"NPC not visible near {walkwayHint} — using walkway coords {walkwayHint}");
            return walkwayHint;
        });

        IpcManager.VnavStop();
        if (Plugin.Config.GrandCompanyIndex == 0
            && Service.ClientState.TerritoryType == MaelstromRepairZone
            && await Service.Framework.RunOnFrameworkThread(() =>
            {
                var player = Service.ObjectTable.LocalPlayer;
                return player != null && IsOnMaelstromGcWalkway(player.Position);
            }))
        {
            await WalkwayApproachNpcAsync(moveDest, npcName, dest);
        }
        else
        {
            await VnavApproachNpcAsync(moveDest, npcName, dest);
        }
        await WaitForMovementStopAsync();

        if (!IsRunning) return;

        const int maxApproachAttempts = 5;
        for (var attempt = 1; attempt <= maxApproachAttempts; attempt++)
        {
            if (!IsRunning) return;

            var npcInfo = await Service.Framework.RunOnFrameworkThread(() =>
            {
                var npc = FindMaelstromGcNpc(npcName, dest) ?? FindNpcByName(npcName, dest);
                if (npc == null) return ((IGameObject?)null, 0f);

                var player = Service.ObjectTable.LocalPlayer;
                if (player == null)
                    return (npc, float.MaxValue);

                var dist = IsOnMaelstromGcWalkway(player.Position)
                    ? NpcPlanarDistance(player.Position, npc.Position)
                    : Vector3.Distance(player.Position, npc.Position);
                return (npc, dist);
            });

            if (npcInfo.Item1 == null)
            {
                await LogNearbyNamedObjectsAsync(npcName, dest);
                await LogAsync($"{npcName} not found yet — approach attempt {attempt}/{maxApproachAttempts}");
                await Task.Delay(Jitter(400));
                continue;
            }

            if (npcInfo.Item2 <= NpcInteractRange)
                break;

            var npcPos = await Service.Framework.RunOnFrameworkThread(() => npcInfo.Item1!.Position);
            await LogAsync($"Moving closer to {npcName} ({npcInfo.Item2:F1}y) attempt {attempt}/{maxApproachAttempts}");
            if (Plugin.Config.GrandCompanyIndex == 0
                && Service.ClientState.TerritoryType == MaelstromRepairZone
                && await Service.Framework.RunOnFrameworkThread(() =>
                {
                    var player = Service.ObjectTable.LocalPlayer;
                    return player != null && IsOnMaelstromGcWalkway(player.Position);
                }))
            {
                var hint = GetMaelstromGcWalkwayHint(dest, npcName);
                var playerPos = await Service.Framework.RunOnFrameworkThread(() => Service.ObjectTable.LocalPlayer!.Position);
                var target = IsPlausibleMaelstromGcNpc(npcName, npcPos)
                    ? AdaptGcTargetForWalkway(npcPos, playerPos)
                    : hint;
                await WalkwayApproachNpcAsync(target, npcName, dest);
            }
            else
            {
                await VnavApproachNpcAsync(npcPos, npcName, dest);
            }
            await WaitForMovementStopAsync();
        }

        if (!IsRunning) return;

        var expectsMenu = nextState is FarmState.OpenExpertDelivery or FarmState.OpenGCShop or FarmState.OpenRepairMenu;

        await Service.Framework.RunOnFrameworkThread(IpcManager.VnavStop);
        await Task.Delay(Jitter(300));

        var npcToInteract = await Service.Framework.RunOnFrameworkThread(() =>
            FindMaelstromGcNpc(npcName, dest) ?? FindNpcByName(npcName, dest));
        if (npcToInteract == null)
        {
            await SetErrorAsync($"Could not find NPC: {npcName}");
            return;
        }

        if (!expectsMenu)
        {
            await Service.Framework.RunOnFrameworkThread(() => SetTargetOnce(npcToInteract));
            await Task.Delay(Jitter(200));
            await Service.Framework.RunOnFrameworkThread(() => Interact(npcToInteract));
            await Task.Delay(Jitter(400));
            await GotoStateAsync(nextState);
            return;
        }

        const int maxMenuAttempts = 3;
        for (var menuAttempt = 1; menuAttempt <= maxMenuAttempts && IsRunning; menuAttempt++)
        {
            // A lingering supply window/agent or the officer talk silently eats the next interaction —
            // make sure the pipeline is genuinely torn down before talking to the NPC.
            if (!await WaitForGcSupplyPipelineClearAsync(6000))
            {
                var blockers = await Service.Framework.RunOnFrameworkThread(DescribeGcSupplyBlockers);
                await LogAsync($"WARN: GC supply UI still busy ({blockers}) — trying {npcName} anyway");
            }

            await Service.Framework.RunOnFrameworkThread(() => _expectNpcMenu = true);
            await Service.Framework.RunOnFrameworkThread(() => SetTargetOnce(npcToInteract));
            await Task.Delay(Jitter(200));

            await Service.Framework.RunOnFrameworkThread(() => Interact(npcToInteract));
            await Task.Delay(Jitter(400));

            if (await WaitForNpcMenuAsync(nextState, 8000))
            {
                await LogAsync($"Opened {npcName} menu");
                await GotoStateAsync(nextState);
                return;
            }

            if (menuAttempt >= maxMenuAttempts)
                break;

            await LogAsync($"WARN: {npcName} menu did not open (attempt {menuAttempt}/{maxMenuAttempts}) — clearing stuck UI and retrying");
            await Service.Framework.RunOnFrameworkThread(CloseExpertDeliveryUiForce);

            if (menuAttempt == 2)
            {
                // Second failure: hard reset — step away to drop any half-open interaction, then walk back.
                await BreakNpcInteractionAsync();
                var npcPos = await Service.Framework.RunOnFrameworkThread(() => npcToInteract.Position);
                await VnavApproachNpcAsync(npcPos, npcName, dest);
                await WaitForMovementStopAsync();
            }

            await Task.Delay(600);
            npcToInteract = await Service.Framework.RunOnFrameworkThread(() =>
                FindMaelstromGcNpc(npcName, dest) ?? FindNpcByName(npcName, dest)) ?? npcToInteract;
        }

        _expectNpcMenu = false;
        await SetErrorAsync($"Menu did not open for {npcName} after {maxMenuAttempts} attempts");
    }

    private async Task WalkwayApproachNpcAsync(Vector3 dest, string npcName, Vector3 dataFallback)
    {
        var walkwayHint = GetMaelstromGcWalkwayHint(dataFallback, npcName);
        var approachDest = dest;

        for (var attempt = 1; attempt <= 3 && IsRunning; attempt++)
        {
            var ctx = await Service.Framework.RunOnFrameworkThread(() =>
            {
                var player = Service.ObjectTable.LocalPlayer;
                if (player == null)
                    return (playerPos: Vector3.Zero, planarToHint: float.MaxValue, npcPos: (Vector3?)null);

                var pos = player.Position;
                var npc = FindMaelstromGcNpc(npcName, dataFallback);
                var planarToHint = RoutePointDistance(pos, walkwayHint);
                return (playerPos: pos, planarToHint, npcPos: npc?.Position);
            });

            if (ctx.planarToHint <= NpcApproachRange + 1f)
            {
                await LogAsync($"Within {ctx.planarToHint:F1}y of {npcName} area on Y≈40 walkway");
                return;
            }

            if (ctx.npcPos.HasValue && IsPlausibleMaelstromGcNpc(npcName, ctx.npcPos.Value))
            {
                approachDest = AdaptGcTargetForWalkway(ctx.npcPos.Value, ctx.playerPos);
                await LogAsync($"Walkway approach to live {npcName} at {approachDest} ({ctx.planarToHint:F0}y from GC room)...");
            }
            else
            {
                approachDest = walkwayHint;
                await LogAsync($"Walkway approach to {npcName} at {approachDest} ({ctx.planarToHint:F0}y away, attempt {attempt}/3)...");
            }

            if (await PathfindToPointAsync(approachDest, NpcApproachRange, MappedStepTimeoutMs))
                continue;

            if (!IsRunning)
                return;

            await Task.Delay(400);
        }

        var finalPlanar = await Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            return player != null ? RoutePointDistance(player.Position, walkwayHint) : float.MaxValue;
        });

        if (finalPlanar <= 10f)
            await LogAsync($"Near {npcName} on walkway ({finalPlanar:F1}y) — proceeding to interact");
        else if (finalPlanar <= 100f)
        {
            await LogAsync($"Lifestream failed — vnav final approach to {npcName} on Y≈40 ({finalPlanar:F0}y)...");
            IpcManager.VnavStop();
            Task<float?> GetDistAsync() =>
                Service.Framework.RunOnFrameworkThread(() => GetNpcDistance(npcName, dataFallback));
            await IpcManager.VnavMoveCloseToAndWaitAsync(
                walkwayHint, NpcApproachRange, false, 60_000, GetDistAsync, IsNavCancelledAsync);
        }
        else
            await LogAsync($"WARN: Still {finalPlanar:F0}y from {npcName} on walkway — will try /target");
    }

    private async Task VnavApproachNpcAsync(Vector3 dest, string npcName, Vector3 fallbackDest)
    {
        Task<float?> GetDistAsync() =>
            Service.Framework.RunOnFrameworkThread(() => GetNpcDistance(npcName, fallbackDest));

        await LogAsync($"Pathfinding to {npcName}...");
        var moved = await IpcManager.VnavMoveCloseToAndWaitAsync(
            dest, NpcApproachRange, false, 120_000, GetDistAsync, IsNavCancelledAsync);
        if (!moved && IsRunning)
            await LogAsync($"WARN: vnavmesh could not approach {npcName}");
    }

    private static Vector3 GetMaelstromStagingHub(Vector3 finalDest) =>
        finalDest.Y > 18f ? MaelstromGcUpperHub : MaelstromGcLowerHub;

    private static bool NeedsMaelstromUpperDeckLift(Vector3 dest)
    {
        if (Plugin.Config.GrandCompanyIndex != 0)
            return false;
        if (Service.ClientState.TerritoryType != MaelstromRepairZone)
            return false;
        if (dest.Y <= 18f)
            return false;

        var player = Service.ObjectTable.LocalPlayer;
        return player != null && player.Position.Y < MaelstromUpperDeckY;
    }

    private static bool IsOnMaelstromGcWalkway(Vector3 pos) => pos.Y >= MaelstromGcWalkwayMinY;

    private static bool IsOnMaelstromLowerDeck(Vector3 pos) => pos.Y < MaelstromGcWalkwayMinY;

    /// <summary>Eastern Y=40 walkway (port-in → barracks approach leg).</summary>
    private static bool IsOnMaelstromSupplyDeck(Vector3 pos) => IsOnMaelstromEasternWalkway(pos);

    private static bool IsOnMaelstromEasternWalkway(Vector3 pos) =>
        IsOnMaelstromGcWalkway(pos) && pos.X >= MaelstromEasternSupplyMinX;

    private static bool IsOnMaelstromEasternSupplyDeck(Vector3 pos) => IsOnMaelstromEasternWalkway(pos);

    private static bool IsNearAftcastleWestWalkway(Vector3 pos) =>
        IsOnMaelstromGcWalkway(pos) && pos.X <= MaelstromCommandCorridorMaxX;

    private static float RoutePointDistance(Vector3 pos, Vector3 point) =>
        MaelstromZone128Nav.RouteDistance(pos, point);

    private static float NpcPlanarDistance(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static Vector3 AdaptGcTargetForWalkway(Vector3 target, Vector3 playerPos) =>
        new(target.X, playerPos.Y, target.Z);

    private static Vector3 GetMaelstromGcWalkwayHint(Vector3 dataCoords, string npcName)
    {
        if (npcName.Contains("Personnel Officer", StringComparison.OrdinalIgnoreCase))
            return MaelstromGcOfficerWalkway;
        if (npcName.Contains("Quartermaster", StringComparison.OrdinalIgnoreCase))
            return MaelstromGcShopWalkway;

        return new Vector3(dataCoords.X, MaelstromGcWalkwayMinY, dataCoords.Z);
    }

    private static Vector3 ToWalkwayHint(Vector3 hint) =>
        new(hint.X, MathF.Max(hint.Y, MaelstromGcWalkwayMinY), hint.Z);

    private static bool IsNearMaelstromGcMerchantArea(Vector3 pos, string npcName)
    {
        var hint = GetMaelstromGcWalkwayHint(Vector3.Zero, npcName);
        return RoutePointDistance(pos, hint) <= GcWalkwayMerchantRadius;
    }

    private static bool IsPlausibleMaelstromGcNpc(string npcName, Vector3 npcPos)
    {
        var hint = GetMaelstromGcWalkwayHint(Vector3.Zero, npcName);
        return NpcPlanarDistance(npcPos, hint) <= GcNpcHintMaxPlanarDist;
    }

    private static IGameObject? FindMaelstromGcNpc(string name, Vector3 dataFallback, float maxPlanarFromPlayer = 80f)
    {
        var hint = ToWalkwayHint(GetMaelstromGcWalkwayHint(dataFallback, name));
        return FindNpcByName(name, hint, maxPlanarFromPlayer);
    }

    private static bool IsOnMaelstromAftcastleLanding(Vector3 pos) =>
        IsNearAftcastleAethernetLanding(pos);

    private static bool IsNearAftcastleAethernetLanding(Vector3 pos) =>
        IsOnMaelstromGcWalkway(pos)
        && Vector3.Distance(pos, MaelstromAftcastleLandingPortIn) <= MaelstromAftcastlePortRadius;

    private static bool IsNearAftcastleMainDeckCrystal(Vector3 pos) =>
        RoutePointDistance(pos, MaelstromAftcastleMainDeck) <= MaelstromAftcastlePortRadius;

    private static bool IsNearAftcastlePort(Vector3 pos) =>
        IsNearAftcastleAethernetLanding(pos) || IsNearAftcastleMainDeckCrystal(pos);

    private static bool IsOnMaelstromMainUpperDeck(Vector3 pos) =>
        IsNearAftcastleWestWalkway(pos) || IsInMaelstromCommandCorridor(pos);

    private static bool IsNearAftcastleMainDeck(Vector3 pos) =>
        IsNearAftcastleMainDeckCrystal(pos);

    private static bool IsAtAftcastleForGcWalk(Vector3 pos) =>
        IsNearAftcastleAethernetLanding(pos)
        || IsNearAftcastleMainDeckCrystal(pos)
        || IsNearAftcastleWestWalkway(pos);

    private static bool IsInMaelstromCommandCorridor(Vector3 pos) =>
        IsNearAftcastleWestWalkway(pos)
        && pos.X >= MaelstromCommandCorridorMinX;

    private static bool NeedsMaelstromUpperGcWalk(Vector3 dest)
    {
        if (Plugin.Config.GrandCompanyIndex != 0 || dest.Y <= 18f)
            return false;

        var player = Service.ObjectTable.LocalPlayer;
        if (player == null)
            return true;

        var pos = player.Position;
        if (IsOnMaelstromEasternSupplyDeck(pos))
            return true;

        return !IsInMaelstromCommandCorridor(pos) && !IsAtAftcastleForGcWalk(pos);
    }

    private static bool IsOnMaelstromSupplyDeck() =>
        Service.ObjectTable.LocalPlayer is { } player && IsOnMaelstromSupplyDeck(player.Position);

    private static bool NeedsGcStagingHub(Vector3 finalDest)
    {
        if (Plugin.Config.GrandCompanyIndex != 0)
            return false;
        if (Service.ClientState.TerritoryType != MaelstromRepairZone)
            return false;

        var player = Service.ObjectTable.LocalPlayer;
        if (player == null)
            return false;

        var hub = GetMaelstromStagingHub(finalDest);
        var pos = player.Position;

        if (finalDest.Y > 18f)
        {
            if (IsInMaelstromCommandCorridor(pos) && Vector3.Distance(pos, finalDest) <= 25f)
                return false;

            if (IsInMaelstromCommandCorridor(pos) && Vector3.Distance(pos, hub) <= GcHubStagingRadius + 2f)
                return false;

            if (IsNearAftcastleMainDeckCrystal(pos))
                return false;
        }

        return Vector3.Distance(pos, hub) > GcHubStagingRadius;
    }

    private async Task<bool> EnsureAtGcStagingHubAsync(Vector3 dest, string npcName)
    {
        if (!IsRunning)
            return false;

        var gcIdx = Plugin.Config.GrandCompanyIndex;

        if (gcIdx == 0 && Service.ClientState.TerritoryType == MaelstromRepairZone)
            return await EnsureMaelstromGcStagingHubAsync(dest, npcName);

        if (GcNavRoutes.HasGcApproachRoute(Plugin.Config, gcIdx)
            || GcNavRoutes.HasGcCorridorRoute(Plugin.Config, gcIdx))
        {
            await PathfindGenericGcMappedAsync(gcIdx, dest, npcName);
            return IsRunning;
        }

        return true;
    }

    private async Task PathfindGenericGcMappedAsync(int gcIdx, Vector3 dest, string npcName)
    {
        if (await TryReconnectFromGenericRepairReturnAsync(gcIdx, dest, npcName))
            return;

        var approach = GcNavRoutes.GetGcApproachPath(Plugin.Config, gcIdx);
        if (approach.Length > 0)
        {
            await LogAsync($"GC approach route ({approach.Length} steps, humanized)...");
            await WalkHumanizedRouteAsync(approach, reverse: false, "GC", preventBacktrack: true);
        }

        if (!IsRunning)
            return;

        var corridor = GcNavRoutes.GetGcCorridorPath(Plugin.Config, gcIdx);
        if (corridor.Length > 0)
        {
            await LogAsync($"GC corridor route ({corridor.Length} steps, humanized)...");
            await WalkHumanizedRouteAsync(corridor, reverse: false, "GC corridor");
        }

        if (IsRunning && GcNavRoutes.HasGcApproachRoute(Plugin.Config, gcIdx))
            await ApproachGcTargetAfterUpperRouteAsync(dest, npcName);
    }

    private async Task<bool> TryReconnectFromGenericRepairReturnAsync(int gcIdx, Vector3 dest, string npcName)
    {
        if (gcIdx == 0 || !GcNavRoutes.HasRepairReturnRoute(Plugin.Config, gcIdx))
            return false;

        var ctx = await Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            if (player == null)
                return (useReturn: false, returnDist: float.MaxValue, approachDist: float.MaxValue);

            var pos = player.Position;
            var returnPath = GcNavRoutes.GetRepairReturnPath(Plugin.Config, gcIdx);
            var approach = GcNavRoutes.GetGcApproachPath(Plugin.Config, gcIdx);
            var returnDist = FindNearestRouteDistance(returnPath, pos);
            var approachDist = FindNearestRouteDistance(approach, pos);
            var useReturn = returnPath.Length > 0
                            && returnDist <= GenericRepairReturnReconnectRadius
                            && returnDist + 5f < approachDist;
            return (useReturn, returnDist, approachDist);
        });

        if (!ctx.useReturn)
            return false;

        var returnRoute = GcNavRoutes.GetRepairReturnPath(Plugin.Config, gcIdx);
        await LogAsync(
            $"GC reconnect — near repair return route ({ctx.returnDist:F0}y, approach {ctx.approachDist:F0}y); walking back toward GC...");
        await WalkHumanizedRouteAsync(returnRoute, reverse: false, "GC repair return", preventBacktrack: true);
        if (!IsRunning)
            return true;

        await ApproachGcTargetAfterUpperRouteAsync(dest, npcName);
        return true;
    }

    private async Task<bool> EnsureMaelstromGcStagingHubAsync(Vector3 dest, string npcName)
    {
        if (!IsRunning)
            return false;

        if (await Service.Framework.RunOnFrameworkThread(() => NeedsMaelstromUpperDeckLift(dest)))
        {
            await LogAsync("Lower deck — vnav to upper GC walkway...");
            if (!await PathfindToPointAsync(MaelstromGcUpperHub, 6f, 90_000) && IsRunning)
                await LogAsync("WARN: vnav to upper walkway timed out");
        }

        if (!IsRunning)
            return false;

        if (dest.Y > 18f)
        {
            var needsAftcastleRoute = await Service.Framework.RunOnFrameworkThread(() =>
            {
                var player = Service.ObjectTable.LocalPlayer;
                if (player == null)
                    return true;

                var pos = player.Position;
                if (MaelstromZone128Nav.IsNearMappedOrMerchant(pos, Plugin.Config, GcOfficerPos[0], GcShopPos[0]))
                    return false;
                if (IsInMaelstromCommandCorridor(pos))
                    return false;
                if (IsAtAftcastleForGcWalk(pos))
                    return false;

                return IsOnMaelstromEasternSupplyDeck(pos);
            });

            if (needsAftcastleRoute)
            {
                IpcManager.VnavStop();
                if (!await RouteToAftcastleAsync())
                    return false;
            }
        }

        if (!IsRunning)
            return false;

        if (dest.Y > 18f)
        {
            await EnsureMaelstromZone128GcRouteAsync(dest, npcName);
            return IsRunning;
        }

        await EnsureMaelstromZone128MenderRouteAsync(dest, npcName);
        return IsRunning;
    }

    /// <summary>Zone 128 upper GC — on mapped route or reconnect to closest waypoint, then walk toward merchants.</summary>
    private async Task EnsureMaelstromZone128GcRouteAsync(Vector3 dest, string npcName)
    {
        var cfg = Plugin.Config;
        var ctx = await Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            if (player == null)
                return (pos: Vector3.Zero, onRoute: false, hit: (MaelstromZone128Nav.RouteHit?)null);

            var pos = player.Position;
            var onRoute = MaelstromZone128Nav.IsNearMappedOrMerchant(
                pos, cfg, GcOfficerPos[0], GcShopPos[0]);
            var hit = MaelstromZone128Nav.FindClosestMappedPoint(pos, cfg, 0);
            return (pos, onRoute, hit);
        });

        if (ctx.onRoute)
        {
            await LogAsync("Zone 128 — within 32y of mapped route or GC merchant");
        }
        else if (ctx.hit.HasValue)
        {
            var hit = ctx.hit.Value;
            await LogAsync(
                $"Zone 128 — {hit.Distance:F0}y off route, walking to nearest mapped point ({hit.Segment} step {hit.Index + 1}) {hit.Point}...");
            await WalkHumanizedRouteAsync([hit.Point], reverse: false, "GC reconnect");
            if (!IsRunning)
                return;
        }
        else
        {
            await LogAsync("WARN: Zone 128 — no mapped routes; falling back to direct GC approach");
            await ApproachGcTargetAfterUpperRouteAsync(dest, npcName);
            return;
        }

        await WalkTowardGcMerchantsAsync(dest, npcName);
    }

    /// <summary>Zone 128 lower deck — reconnect to repair route then walk toward mender.</summary>
    private async Task EnsureMaelstromZone128MenderRouteAsync(Vector3 dest, string npcName)
    {
        var cfg = Plugin.Config;
        var town = cfg.TownNav(0);
        var menderPos = town.MenderPosition;

        var ctx = await Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            if (player == null)
                return (onRoute: false, hit: (MaelstromZone128Nav.RouteHit?)null);

            var pos = player.Position;
            var onRoute = Vector3.Distance(pos, menderPos) <= MaelstromZone128Nav.AnchorRadius
                        || Vector3.Distance(pos, dest) <= MaelstromZone128Nav.AnchorRadius;

            if (!onRoute && GcNavRoutes.HasRepairRoute(cfg, 0))
            {
                foreach (var point in GcNavRoutes.GetRepairPath(cfg, 0))
                {
                    if (Vector3.Distance(pos, point) <= MaelstromZone128Nav.AnchorRadius)
                    {
                        onRoute = true;
                        break;
                    }
                }
            }

            var hit = MaelstromZone128Nav.FindClosestSegmentPoint(
                pos, MaelstromZone128Nav.Segment.Repair, cfg, 0);
            return (onRoute, hit);
        });

        if (!ctx.onRoute && ctx.hit.HasValue)
        {
            var hit = ctx.hit.Value;
            await LogAsync(
                $"Zone 128 — {hit.Distance:F0}y off repair route, walking to nearest point {hit.Point}...");
            await WalkHumanizedRouteAsync([hit.Point], reverse: false, "Repair reconnect");
            if (!IsRunning)
                return;
        }

        if (GcNavRoutes.HasRepairRoute(cfg, 0))
        {
            var repair = GcNavRoutes.GetRepairPath(cfg, 0);
            var startIdx = await Service.Framework.RunOnFrameworkThread(() =>
            {
                var player = Service.ObjectTable.LocalPlayer;
                if (player == null)
                    return 0;
                var hit = MaelstromZone128Nav.FindClosestSegmentPoint(
                    player.Position, MaelstromZone128Nav.Segment.Repair, cfg, 0);
                return hit?.Index ?? 0;
            });

            var remaining = MaelstromZone128Nav.SliceFromIndex(repair, startIdx);
            if (remaining.Length > 0)
                await WalkHumanizedRouteAsync(remaining, reverse: false, "Repair", preventBacktrack: true);
        }
    }

    /// <summary>From current position on/near mapped routes, walk forward on the Y≈40 walkway toward GC merchants.</summary>
    private async Task WalkTowardGcMerchantsAsync(Vector3 dest, string npcName)
    {
        if (!IsRunning)
            return;

        var cfg = Plugin.Config;
        var ctx = await Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            if (player == null)
                return (onWalkway: false, nearCrystal: false, nearPort: false, nearWest: false, nearEast: false, nearMerchant: false,
                    closest: (MaelstromZone128Nav.RouteHit?)null);

            var pos = player.Position;
            var closest = MaelstromZone128Nav.FindClosestMappedPoint(pos, cfg, 0);
            var nearMerchant = IsNearMaelstromGcMerchantArea(pos, npcName);

            return (
                onWalkway: IsOnMaelstromGcWalkway(pos),
                nearCrystal: IsNearAftcastleMainDeckCrystal(pos),
                nearPort: IsNearAftcastleAethernetLanding(pos),
                nearWest: IsNearAftcastleWestWalkway(pos),
                nearEast: IsOnMaelstromEasternWalkway(pos),
                nearMerchant,
                closest);
        });

        if (ctx.nearMerchant)
        {
            await LogAsync($"Near GC merchant area — finishing approach to {npcName}");
            await ApproachGcTargetAfterUpperRouteAsync(dest, npcName);
            return;
        }

        if (!ctx.onWalkway)
        {
            await LogAsync("WARN: Not on Y≈40 GC walkway — trying direct approach to merchant");
            await ApproachGcTargetAfterUpperRouteAsync(dest, npcName);
            return;
        }

        var approach = GcNavRoutes.GetGcApproachPath(cfg, 0);
        var merchantHint = GetMaelstromGcWalkwayHint(dest, npcName);
        var easternMerchant = merchantHint.X >= MaelstromEasternSupplyMinX;

        if (easternMerchant && approach.Length > 0)
        {
            if (ctx.nearCrystal || (ctx.nearWest && !ctx.nearEast))
            {
                var needsStaging = await Service.Framework.RunOnFrameworkThread(() =>
                {
                    var player = Service.ObjectTable.LocalPlayer;
                    return player != null
                           && RoutePointDistance(player.Position, MaelstromGcOfficerStaging) > GcWalkwayMerchantRadius;
                });

                if (needsStaging)
                {
                    await LogAsync("Aftcastle crystal — vnav east on Y≈40 to GC staging (93, 40, 74.5)...");
                    await VnavWalkwayStagingAsync(MaelstromGcOfficerStaging, npcName, dest);
                    if (!IsRunning)
                        return;
                }
            }

            var startIdx = ctx.closest?.Segment == MaelstromZone128Nav.Segment.Approach
                ? ctx.closest.Value.Index
                : await Service.Framework.RunOnFrameworkThread(() =>
                {
                    var player = Service.ObjectTable.LocalPlayer;
                    return player != null
                        ? FindGcApproachStartIndex(approach, player.Position)
                        : 0;
                });

            var slice = MaelstromZone128Nav.SliceFromIndex(approach, startIdx);
            if (slice.Length > 0)
            {
                await LogAsync($"Walking mapped approach route ({slice.Length} steps) east on Y≈40 toward {npcName}...");
                await WalkHumanizedRouteAsync(slice, reverse: false, "GC upper", preventBacktrack: true);
                if (!IsRunning)
                    return;
            }
        }
        else if (GcNavRoutes.HasGcCorridorRoute(cfg, 0))
        {
            var corridor = GcNavRoutes.GetGcCorridorPath(cfg, 0);
            var startIdx = await Service.Framework.RunOnFrameworkThread(() =>
            {
                var player = Service.ObjectTable.LocalPlayer;
                if (player == null)
                    return 0;

                var pos = player.Position;
                var hit = MaelstromZone128Nav.FindClosestSegmentPoint(
                    pos, MaelstromZone128Nav.Segment.Corridor, cfg, 0);
                return hit?.Index ?? FindCorridorRouteStartIndex(corridor, pos);
            });

            var slice = MaelstromZone128Nav.SliceFromIndex(corridor, startIdx);
            if (slice.Length > 0)
            {
                await LogAsync($"Walking mapped corridor ({slice.Length} steps) on Y≈40 walkway...");
                await WalkHumanizedRouteAsync(slice, reverse: false, "GC corridor", preventBacktrack: true);
            }
        }

        var closeEnough = await Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            return player != null && IsNearMaelstromGcMerchantArea(player.Position, npcName);
        });

        if (closeEnough)
            await ApproachGcTargetAfterUpperRouteAsync(dest, npcName);
        else
            await LogAsync($"WARN: Mapped routes done but still far from {npcName} — Lifestream may have failed on Y≈40");
    }

    private async Task VnavWalkwayStagingAsync(Vector3 staging, string npcName, Vector3 dataFallback)
    {
        IpcManager.VnavStop();
        Task<float?> GetDistAsync() =>
            Service.Framework.RunOnFrameworkThread(() =>
            {
                var player = Service.ObjectTable.LocalPlayer;
                if (player == null)
                    return (float?)null;
                return RoutePointDistance(player.Position, staging);
            });

        var moved = await IpcManager.VnavMoveCloseToAndWaitAsync(
            staging, 4f, false, 120_000, GetDistAsync, IsNavCancelledAsync);
        if (!moved && IsRunning)
            await LogAsync($"WARN: vnav to GC staging {staging} failed — will try mapped route");
    }

    /// <summary>Final steps on the Y≈40 walkway to officer/shop — never vnav to Y≈21 data coords.</summary>
    private async Task ApproachGcTargetAfterUpperRouteAsync(Vector3 dest, string npcName)
    {
        if (!IsRunning)
            return;

        var onWalkway = await Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            return player != null && IsOnMaelstromGcWalkway(player.Position);
        });

        if (onWalkway)
        {
            var walkwayHint = GetMaelstromGcWalkwayHint(dest, npcName);
            await LogAsync($"Final walkway approach to {npcName} near {walkwayHint}...");
            await WalkwayApproachNpcAsync(walkwayHint, npcName, dest);
            return;
        }

        var target = await Service.Framework.RunOnFrameworkThread(() =>
        {
            var npc = FindNpcByName(npcName, dest, 80f);
            if (npc != null)
            {
                Log($"Route complete — using live {npcName} position {npc.Position}");
                return npc.Position;
            }

            Log($"Route complete — navigating to {npcName} at {dest}");
            return dest;
        });

        await LogAsync($"Final vnav approach to {npcName}...");
        IpcManager.VnavStop();
        Task<float?> GetDistAsync() =>
            Service.Framework.RunOnFrameworkThread(() => GetNpcDistance(npcName, dest));
        await IpcManager.VnavMoveCloseToAndWaitAsync(
            target, NpcApproachRange, false, 120_000, GetDistAsync, IsNavCancelledAsync);
    }

    private static bool IsNearGcApproachRouteEnd(Vector3 pos)
    {
        var route = GcNavRoutes.GetGcApproachPath(Plugin.Config, 0);
        if (route.Length == 0)
            return false;

        return Vector3.Distance(pos, route[^1]) <= MaelstromAftcastlePortRadius;
    }

    private static float GetMappedStepArriveRange(string label, int stepIndex, int totalSteps)
    {
        if (!label.Contains("GC upper", StringComparison.OrdinalIgnoreCase))
            return MappedStepArriveRange;

        if (stepIndex >= totalSteps - 1)
            return MaelstromAftcastlePortRadius;

        return MappedStepArriveRange + 2f;
    }

    /// <summary>Walk mapped route waypoints via vnavmesh.</summary>
    private async Task WalkHumanizedRouteAsync(
        IReadOnlyList<Vector3> points,
        bool reverse,
        string label,
        bool preventBacktrack = false)
    {
        var steps = GcNavRoutes.OrderRoute(points, reverse);
        if (steps.Length == 0)
            return;

        var startIdx = await Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            if (player == null)
                return 0;

            return FindRouteStartForLabel(steps, player.Position, label, preventBacktrack);
        });

        if (startIdx > 0)
            await LogAsync($"{label}: starting at step {startIdx + 1}/{steps.Length} (skipped {startIdx} earlier step(s))");

        if (startIdx >= steps.Length)
        {
            await LogAsync($"{label}: already past all mapped steps");
            return;
        }

        var remaining = await BuildRemainingRouteAsync(steps, startIdx, label);
        if (remaining.Count == 0)
            return;

        await WalkRouteStepsAsync(remaining, steps, startIdx, label);
    }

    private async Task<List<Vector3>> BuildRemainingRouteAsync(Vector3[] steps, int startIdx, string label)
    {
        var remaining = new List<Vector3>();
        for (var i = startIdx; i < steps.Length; i++)
        {
            if (!IsRunning)
                break;

            var stepArrive = GetMappedStepArriveRange(label, i, steps.Length);
            var dist = await GetPlayerDistToAsync(steps[i]);
            if (remaining.Count == 0 && dist.HasValue && dist.Value <= stepArrive)
                continue;

            remaining.Add(steps[i]);
        }

        return remaining;
    }

    private async Task WalkRouteStepsAsync(
        IReadOnlyList<Vector3> remaining,
        Vector3[] allSteps,
        int startIdx,
        string label)
    {
        for (var r = 0; r < remaining.Count; r++)
        {
            if (!IsRunning)
                return;

            var point = remaining[r];
            var stepArrive = GetMappedStepArriveRange(label, Math.Min(startIdx + r, allSteps.Length - 1), allSteps.Length);
            var dist = await GetPlayerDistToAsync(point);
            if (dist.HasValue && dist.Value <= stepArrive)
                continue;

            var isLastStep = r == remaining.Count - 1;

            await LogAsync($"{label} step {r + 1}/{remaining.Count}...");

            if (await PathfindToPointAsync(point, stepArrive, MappedStepTimeoutMs))
                continue;

            if (!IsRunning)
                return;

            dist = await GetPlayerDistToAsync(point);
            if (dist.HasValue && dist.Value <= stepArrive)
                continue;

            var onWalkway = await Service.Framework.RunOnFrameworkThread(() =>
            {
                var player = Service.ObjectTable.LocalPlayer;
                return player != null && IsOnMaelstromGcWalkway(player.Position);
            });

            if (onWalkway && (label.Contains("GC upper", StringComparison.OrdinalIgnoreCase)
                              || label.Contains("GC west", StringComparison.OrdinalIgnoreCase)
                              || label.Contains("corridor", StringComparison.OrdinalIgnoreCase)))
            {
                await LogAsync($"WARN: Lifestream failed on Y≈40 walkway ({label}) — skipping vnav");
                continue;
            }

            await LogAsync($"WARN: Lifestream step failed — vnav fallback");
            await WalkWaypointAsync(point, 3f, label, GcWaypointTimeoutMs);

            if (isLastStep && label.Contains("GC upper", StringComparison.OrdinalIgnoreCase) && IsRunning)
                await LogAsync($"{label} last step done — ready for GC personnel handoff");
        }
    }

    /// <summary>When resuming mid-route, skip waypoints we've already reached.</summary>
    private static int FindRouteStartIndex(Vector3[] steps, Vector3 pos)
    {
        var start = 0;
        for (var i = 0; i < steps.Length; i++)
        {
            if (RoutePointDistance(pos, steps[i]) <= MappedStepArriveRange)
                start = i + 1;
        }

        return Math.Min(start, steps.Length);
    }

    private static int FindRouteStartForLabel(Vector3[] steps, Vector3 pos, string label, bool preventBacktrack)
    {
        if (label.Contains("GC upper", StringComparison.OrdinalIgnoreCase)
            || label.Contains("GC west", StringComparison.OrdinalIgnoreCase))
        {
            var easternRoute = steps.Length > 0 && steps[^1].X >= MaelstromEasternSupplyMinX;
            if (!easternRoute && (IsNearAftcastleMainDeckCrystal(pos) || IsNearAftcastleWestWalkway(pos)))
                return steps.Length;

            if (preventBacktrack || IsNearAftcastleAethernetLanding(pos))
                return label.Contains("GC west", StringComparison.OrdinalIgnoreCase)
                    ? FindNearestRouteIndex(steps, pos)
                    : FindGcApproachStartIndex(steps, pos);

            return FindNearestRouteIndex(steps, pos);
        }

        if (label.Contains("corridor", StringComparison.OrdinalIgnoreCase))
            return FindCorridorRouteStartIndex(steps, pos);

        if (preventBacktrack)
            return FindNearestRouteIndex(steps, pos);

        return FindRouteStartIndex(steps, pos);
    }

    private static int FindCorridorRouteStartIndex(Vector3[] steps, Vector3 pos)
    {
        for (var i = 0; i < steps.Length; i++)
        {
            if (RoutePointDistance(pos, steps[i]) <= MappedStepArriveRange)
                return Math.Min(i + 1, steps.Length);
        }

        return FindNearestRouteIndex(steps, pos);
    }

    /// <summary>At Aftcastle port or on Y=40 route — never walk backward to earlier waypoints.</summary>
    private static int FindGcApproachStartIndex(Vector3[] route, Vector3 playerPos)
    {
        var easternRoute = route.Length > 0 && route[^1].X >= MaelstromEasternSupplyMinX;
        if (!easternRoute
            && (IsNearAftcastleMainDeckCrystal(playerPos) || IsNearAftcastleWestWalkway(playerPos)))
            return route.Length;

        var start = FindNearestRouteIndex(route, playerPos);

        if (IsNearAftcastleAethernetLanding(playerPos))
        {
            var firstNearPort = FindFirstWaypointNearPoint(route, MaelstromAftcastleLandingPortIn, MaelstromAftcastlePortRadius);
            start = Math.Max(start, firstNearPort);
        }

        return Math.Min(start, route.Length);
    }

    private static int FindFirstWaypointNearPoint(Vector3[] route, Vector3 anchor, float radius)
    {
        for (var i = 0; i < route.Length; i++)
        {
            if (Vector3.Distance(route[i], anchor) <= radius)
                return i;
        }

        return 0;
    }

    private static int FindNearestRouteIndex(Vector3[] steps, Vector3 pos)
    {
        var best = 0;
        var bestDist = float.MaxValue;
        for (var i = 0; i < steps.Length; i++)
        {
            var d = RoutePointDistance(pos, steps[i]);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return best;
    }

    private static float FindNearestRouteDistance(IReadOnlyList<Vector3> steps, Vector3 pos)
    {
        if (steps.Count == 0)
            return float.MaxValue;

        var bestDist = float.MaxValue;
        foreach (var step in steps)
            bestDist = Math.Min(bestDist, RoutePointDistance(pos, step));

        return bestDist;
    }

    private async Task WalkMappedRouteAsync(Vector3[] points) =>
        await WalkHumanizedRouteAsync(points, reverse: false, "GC");

    private async Task WalkWaypointChainAsync(Vector3[] points, string label, int timeoutMs)
    {
        foreach (var point in points)
        {
            if (!IsRunning)
                return;

            if (await Service.Framework.RunOnFrameworkThread(() =>
                {
                    var player = Service.ObjectTable.LocalPlayer;
                    return player != null && IsInMaelstromCommandCorridor(player.Position)
                           && Vector3.Distance(player.Position, MaelstromGcUpperHub) <= GcHubStagingRadius + 2f;
                }))
            {
                await LogAsync("Reached GC command corridor — stopping waypoint chain");
                return;
            }

            var dist = await GetPlayerDistToAsync(point);
            if (dist.HasValue && dist.Value <= 4f)
                continue;

            await WalkWaypointAsync(point, 3f, label, timeoutMs);
        }
    }

    private async Task WalkWaypointAsync(Vector3 point, float range, string label, int timeoutMs)
    {
        await LogAsync($"{label} {point}...");
        if (!await PathfindToPointAsync(point, range, timeoutMs))
            await LogAsync($"WARN: {label} {point} failed or timed out ({timeoutMs / 1000}s)");
        await WaitForMovementStopAsync();
    }

    private async Task<bool> RouteToAftcastleAsync()
    {
        var atAftcastle = await Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            return player != null && HasReachedAftcastleArrival(player.Position);
        });

        if (atAftcastle)
        {
            await LogAsync("Already at/near The Aftcastle — walking to GC");
            return true;
        }

        await LogAsync("Not at Aftcastle walkway — vnav approach...");
        foreach (var point in MaelstromAftcastleWalkwayPortToCrystal)
        {
            if (!IsRunning)
                return false;

            if (!await PathfindToPointAsync(point, 5f, GcWaypointTimeoutMs))
                await LogAsync($"WARN: walkway step {point} failed or timed out");
        }

        return await Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            return player != null && HasReachedAftcastleArrival(player.Position);
        });
    }

    private static bool HasReachedAftcastleArrival(Vector3 pos) =>
        IsAtAftcastleForGcWalk(pos) || IsInMaelstromCommandCorridor(pos);

    private async Task<bool> PathfindToPointAsync(Vector3 point, float range, int timeoutMs = 120_000)
    {
        Task<float?> DistAsync() => GetPlayerDistToAsync(point);
        return await IpcManager.VnavMoveCloseToAndWaitAsync(
            point, range, false, timeoutMs, DistAsync, IsNavCancelledAsync);
    }

    private Task<float?> GetPlayerDistToAsync(Vector3 target) =>
        Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            return player != null ? Vector3.Distance(player.Position, target) : (float?)null;
        });

    private async Task<bool> WaitForUpperDeckAsync(int timeoutMs)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < deadline)
        {
            if (!IsRunning)
                return false;

            var ready = await Service.Framework.RunOnFrameworkThread(() =>
            {
                if (IpcManager.LifestreamIsBusy() || IsBetweenAreas())
                    return false;

                var player = Service.ObjectTable.LocalPlayer;
                if (player == null)
                    return false;

                return player.Position.Y >= MaelstromGcWalkwayMinY
                       || IsNearAftcastleWestWalkway(player.Position);
            });

            if (ready)
                return true;

            await Task.Delay(200);
        }

        return false;
    }

    private static float? GetNpcDistance(string npcName, Vector3? nearHint)
    {
        var npc = nearHint.HasValue && Service.ClientState.TerritoryType == MaelstromRepairZone
            ? FindMaelstromGcNpc(npcName, nearHint.Value) ?? FindNpcByName(npcName, nearHint, 80f)
            : FindNpcByName(npcName, nearHint, 80f);
        var player = Service.ObjectTable.LocalPlayer;
        if (npc == null || player == null)
            return null;

        return IsOnMaelstromGcWalkway(player.Position)
            ? NpcPlanarDistance(player.Position, npc.Position)
            : Vector3.Distance(player.Position, npc.Position);
    }

    private async Task WaitForMovementStopAsync(int timeoutMs = 2000)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        Vector3? lastPos = null;

        while (DateTime.Now < deadline)
        {
            if (!IsRunning)
                return;

            var pos = await Service.Framework.RunOnFrameworkThread(() =>
                Service.ObjectTable.LocalPlayer?.Position);

            if (pos.HasValue && lastPos.HasValue && Vector3.Distance(pos.Value, lastPos.Value) < 0.05f)
            {
                await Task.Delay(150);
                return;
            }

            lastPos = pos;
            await Task.Delay(100);
        }
    }

    private Task LogNearbyNamedObjectsAsync(string npcName, Vector3 dest) =>
        Service.Framework.RunOnFrameworkThread(() =>
        {
            var player = Service.ObjectTable.LocalPlayer;
            var nearby = Service.ObjectTable
                .Where(o => !string.IsNullOrWhiteSpace(o.Name.TextValue))
                .Select(o => new
                {
                    Name = o.Name.TextValue,
                    Kind = o.ObjectKind,
                    Dist = player != null ? Vector3.Distance(player.Position, o.Position) : float.MaxValue,
                    HintDist = Vector3.Distance(o.Position, dest),
                })
                .Where(x => x.Dist <= 25f || x.HintDist <= 25f)
                .OrderBy(x => x.Dist)
                .Take(20)
                .Select(x => $"{x.Name} ({x.Kind}) {x.Dist:F1}y")
                .ToList();

            if (nearby.Count > 0)
                Log($"Nearby ({npcName} missing): {string.Join(", ", nearby)}");
        });

    private async Task OpenExpertDeliveryAsync()
    {
        _expectNpcMenu = true;
        await Service.Framework.RunOnFrameworkThread(CloseStuckConfirmDialogs);

        if (!await WaitForAddonVisibleAsync("SelectString", 5000))
        {
            _expectNpcMenu = false;
            await SetErrorAsync("Officer menu did not open");
            return;
        }

        await Service.Framework.RunOnFrameworkThread(() => SelectSelectStringOption(GcSupplyMenuOption));
        await Task.Delay(250);

        if (!await WaitForAddonVisibleAsync("GrandCompanySupplyList", 8000))
        {
            _expectNpcMenu = false;
            await Service.Framework.RunOnFrameworkThread(() => CloseAddonSafe("SelectString"));
            await SetErrorAsync("Supply list did not open");
            return;
        }

        _expectNpcMenu = false;

        if (!await Service.Framework.RunOnFrameworkThread(IsExpertDeliveryTabActive))
        {
            await Service.Framework.RunOnFrameworkThread(SelectExpertDeliveryTab);
            await Task.Delay(250);
        }
        else
        {
            await LogAsync("Supply list already open on Expert Delivery tab");
        }

        if (!await WaitForExpertDeliveryTabAsync(5000))
        {
            await Service.Framework.RunOnFrameworkThread(() =>
            {
                CloseAddonSafe("GrandCompanySupplyList");
                CloseAddonSafe("SelectString");
            });
            await SetErrorAsync("Expert Delivery tab did not open");
            return;
        }

        _pendingHandinRow = -1;
        _pendingHandinItemId = 0;
        _deliveryListEmpty = false;
        _deliveryBlockedByCap = false;
        _deliveryListLostAt = null;
        _pendingHandinSince = null;
        _gcActionCooldownUntil = DateTime.MinValue;
        _deliverClickedForRow = -1;
        _sealsBefore = await GetCurrentSealsAsync();
        await StatusAsync("Expert Delivery open — processing items...");
        await GotoStateAsync(FarmState.ProcessDelivery);
    }

    private static unsafe void CloseStuckConfirmDialogs()
    {
        foreach (var name in SelectYesnoAddons)
        {
            if (IsAddonVisible(name))
                CloseAddonSafe(name);
        }
    }

    private static unsafe int ResolveSelectStringDismissOption(AddonSelectString* addon)
    {
        var count = addon->PopupMenu.PopupMenu.EntryCount;
        if (count <= 0)
            return GcOfficerDismissOption;

        var names = addon->PopupMenu.PopupMenu.EntryNames;
        if (names != null)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                var text = names[i].ToString();
                if (text.Contains("Nothing", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Leave", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Cancel", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Farewell", StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return count - 1;
    }

    private static unsafe void TrySelectStringPopupOption(AddonSelectString* addon, int optionIndex, bool logOptions = false)
    {
        var popup = addon->PopupMenu.PopupMenu;
        var count = popup.EntryCount;
        var index = optionIndex;
        if (count > 0 && (index < 0 || index >= count))
            index = count - 1;

        if (logOptions && count > 0)
        {
            var names = popup.EntryNames;
            var labels = new List<string>(count);
            for (var i = 0; i < count; i++)
                labels.Add(names != null ? names[i].ToString() : $"#{i}");
            Log($"SelectString [{string.Join(" | ", labels)}] — selecting {index}");
        }

        var list = popup.List;
        if (list != null && index >= 0 && index < list->ListLength)
        {
            list->ScrollToItem((short)index);
            list->UpdateListItems();
            list->SelectItem(index, dispatchEvent: false);
            list->SetItemHighlightedState(index, highlighted: true, triggerUpdate: true);
            list->DispatchItemEvent(index, AtkEventType.ListItemRollOver);
            list->DispatchItemEvent(index, AtkEventType.ListItemClick);
            list->SelectItem(index, dispatchEvent: true);
            list->DispatchItemEvent(index, AtkEventType.ListItemSelect);
        }

        SendCallback("SelectString", true, index);
        SendCallback("SelectString", false, index);
        addon->FireCallbackInt(index);
    }

    private static unsafe void SelectSelectStringOption(int optionIndex)
    {
        var addon = Service.GameGui.GetAddonByName<AddonSelectString>("SelectString");
        if (addon == null || !addon->IsVisible)
            return;

        TrySelectStringPopupOption(addon, optionIndex);
    }

    private static int _gcOfficerDismissLogCounter;

    private bool ShouldAutoDismissGcOfficerMenu() =>
        Plugin.Config.AutoDismissGcOfficerMenu && _automationOwnsGcPersonnelUi;

    private void TryDismissStuckOfficerMenuTick()
    {
        if (!IsRunning || !ShouldAutoDismissGcOfficerMenu())
            return;

        if (!GcActionReady())
            return;

        if (_expectNpcMenu)
            return;

        if (State is FarmState.OpenExpertDelivery or FarmState.OpenRepairMenu or FarmState.OpenGCShop
            or FarmState.NavigateToGcTarget or FarmState.NavigateToOfficer or FarmState.NavigateToShop
            or FarmState.NavigateToRepair or FarmState.TeleportToGC or FarmState.WaitingForZone
            or FarmState.CheckSubZone or FarmState.WaitingForSubZone)
            return;

        if (!_deliveryFinishing && !IsGcOfficerMenuOpen())
            return;

        if (_deliveryFinishing)
        {
            if (IsAddonVisible("SelectString") && !IsAddonOpen("GrandCompanySupplyList"))
                TryDismissGcOfficerMenu();
        }
        else if (IsGcOfficerMenuOpen())
        {
            TryDismissGcOfficerMenu();
        }

        ThrottleGcAction(250);
    }

    private void ProcessDeliveryTick()
    {
        if (!GcActionReady())
            return;

        if (_deliveryFinishing)
        {
            if (ShouldAutoDismissGcOfficerMenu()
                && IsAddonVisible("SelectString")
                && !IsAddonOpen("GrandCompanySupplyList"))
            {
                TryDismissGcOfficerMenu();
                ThrottleGcAction(250);
            }
            return;
        }

        var cfg = Plugin.Config;

        if (HandleSupplyRewardDialog())
            return;

        if (GetCurrentSeals() >= cfg.SealCap - 100)
        {
            Log("Seal cap reached — heading to shop to spend seals");
            _deliveryBlockedByCap = true;
            CloseExpertDeliveryUiForce();
            FinishDelivery();
            return;
        }

        if (IsSelectYesnoVisible())
        {
            ClickSelectYesno();
            ThrottleGcAction(300);
            TryAcknowledgePendingHandin();
            if (_pendingHandinRow >= 0 || !GcActionReady())
                return;
        }

        if (_pendingHandinRow >= 0)
        {
            if (TryAcknowledgePendingHandin())
            {
                _deliverClickedForRow = -1;
                ThrottleGcAction(50);
            }
            else if (_pendingHandinSince == null)
            {
                _pendingHandinSince = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - _pendingHandinSince.Value > TimeSpan.FromSeconds(20))
            {
                Log($"WARN: Handin row {_pendingHandinRow} timed out — retrying");
                _pendingHandinRow = -1;
                _pendingHandinItemId = 0;
                _pendingHandinSince = null;
                _deliverClickedForRow = -1;
            }

            if (_pendingHandinRow >= 0)
                return;
        }

        if (!IsAddonOpen("GrandCompanySupplyList"))
        {
            if (IsSupplyRewardVisible())
            {
                HandleSupplyRewardDialog();
                return;
            }

            if (ShouldAutoDismissGcOfficerMenu() && IsAddonVisible("SelectString"))
            {
                TryDismissGcOfficerMenu();
                ThrottleGcAction(300);
                return;
            }

            if (_deliveryListLostAt == null)
            {
                _deliveryListLostAt = DateTime.UtcNow;
                Status("Waiting for delivery list...");
                return;
            }

            if (DateTime.UtcNow - _deliveryListLostAt.Value < TimeSpan.FromSeconds(8))
                return;

            Log("Delivery list closed — ending delivery phase");
            CloseExpertDeliveryUiForce();
            FinishDelivery();
            return;
        }

        _deliveryListLostAt = null;

        if (!IsSupplyListReady())
        {
            if (IsSupplyRewardVisible())
            {
                HandleSupplyRewardDialog();
                return;
            }
            return;
        }

        var next = FindNextHandinRow();
        if (next == null)
        {
            if (IsSupplyRewardVisible())
            {
                HandleSupplyRewardDialog();
                return;
            }

            _deliveryBlockedByCap = IsDeliveryBlockedByCap();
            _deliveryListEmpty = !_deliveryBlockedByCap;
            CloseExpertDeliveryUiForce();

            if (_deliveryBlockedByCap)
                Log("No more items fit under seal cap — spending seals on duckbones");
            else
                Log("No deliverable items remaining");

            FinishDelivery();
            return;
        }

        var (row, itemId, seals) = next.Value;
        _pendingHandinRow = row;
        _pendingHandinItemId = itemId;
        _deliverClickedForRow = -1;
        _sealsWhenHandinPending = GetCurrentSeals();
        _pendingHandinSince = DateTime.UtcNow;
        Log($"Handing in item {itemId} (row {row}, {seals} seals) | Seals: {_sealsWhenHandinPending:N0}/{cfg.SealCap:N0}");
        InvokeSupplyHandin(row);
        ThrottleGcAction();
    }

    private bool TryAcknowledgePendingHandin()
    {
        if (_pendingHandinRow < 0)
            return false;

        var curSeals = GetCurrentSeals();
        if (curSeals <= _sealsWhenHandinPending)
            return false;

        Log($"Delivered item | +{curSeals - _sealsWhenHandinPending} seals | Seals: {curSeals:N0}");
        _pendingHandinRow = -1;
        _pendingHandinItemId = 0;
        _pendingHandinSince = null;
        _deliverClickedForRow = -1;
        return true;
    }

    /// <summary>Handle GrandCompanySupplyReward — click Deliver once per handin, then wait.</summary>
    private bool HandleSupplyRewardDialog()
    {
        if (!IsSupplyRewardVisible())
        {
            if (_deliverClickedForRow >= 0 && _pendingHandinRow >= 0)
                TryAcknowledgePendingHandin();
            return false;
        }

        if (_pendingHandinRow < 0)
            return true;

        if (_deliverClickedForRow == _pendingHandinRow)
            return true;

        if (!TryConfirmSupplyReward())
            return true;

        Log("Confirming expert delivery reward...");
        _deliverClickedForRow = _pendingHandinRow;
        ThrottleGcAction(400);
        return true;
    }

    private void FinishDelivery()
    {
        if (_deliveryFinishing)
            return;

        _deliveryFinishing = true;

        var seals = GetCurrentSeals();
        var gained = seals - _sealsBefore;
        TotalSeals += Math.Max(0, gained);
        Log($"Delivery phase done | +{gained} seals | Now: {seals:N0} | blocked by cap: {_deliveryBlockedByCap} | list empty: {_deliveryListEmpty}");

        _deliveryListLostAt = null;
        _pendingHandinSince = null;
        _pendingHandinRow = -1;
        _deliverClickedForRow = -1;
        _currentTask = FinishDeliveryAsync();
    }

    private async Task FinishDeliveryAsync()
    {
        try
        {
            if (ShouldAutoDismissGcOfficerMenu())
            {
                await CloseExpertDeliveryUiAsync();
                await EnsureGcOfficerMenuDismissedAsync();
                _automationOwnsGcPersonnelUi = false;
            }
        }
        catch (Exception ex)
        {
            await LogAsync($"WARN: Expert Delivery close failed ({ex.Message}) — continuing");
        }

        if (!IsRunning)
            return;

        _deliveryFinishing = false;

        if (_deliveryTestMode)
        {
            FinishDeliveryTest();
            return;
        }

        if (_deliveryBlockedByCap && ShouldSpendSealsOnShop())
        {
            _gcShopRankSelected = false;
            _gcShopCategorySelected = false;
            _buyAwaitingConfirm = false;
            _buyListIndex = 0;
            BeginGcNavigation(FarmState.OpenGCShop);
        }
        else
        {
            await GotoStateAsync(FarmState.CheckGcLoop);
        }
    }

    private async Task CloseExpertDeliveryUiAsync()
    {
        const int maxAttempts = 32;
        var stableClosed = 0;

        for (var attempt = 1; attempt <= maxAttempts && IsRunning; attempt++)
        {
            bool anyOpen;
            try
            {
                anyOpen = await Service.Framework.RunOnFrameworkThread(() =>
                {
                    CloseExpertDeliveryUiForce();
                    return IsAnyExpertDeliveryUiOpen() || IsGcSupplyPipelineBusy();
                });
            }
            catch (Exception ex)
            {
                await LogAsync($"WARN: Expert Delivery close attempt {attempt} failed ({ex.Message})");
                anyOpen = true;
            }

            if (!anyOpen)
            {
                stableClosed++;
                if (stableClosed >= 2)
                {
                    if (attempt > 1)
                        await LogAsync("Expert Delivery UI closed");
                    break;
                }

                await Task.Delay(200);
                continue;
            }

            stableClosed = 0;

            if (attempt == 1)
                await LogAsync("Closing Expert Delivery UI...");
            else if (attempt % 4 == 0)
            {
                var blockers = await Service.Framework.RunOnFrameworkThread(DescribeGcSupplyBlockers);
                await LogAsync($"Still waiting for Expert Delivery UI to close ({attempt}/{maxAttempts}) — {blockers}");
            }

            await Task.Delay(250);
        }
    }

    private async Task EnsureGcOfficerMenuDismissedAsync()
    {
        const int maxAttempts = 24;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (!IsRunning && !IsGcOfficerMenuOpen())
                return;

            var stillOpen = await Service.Framework.RunOnFrameworkThread(() =>
            {
                CloseStuckConfirmDialogs();
                TryDismissGcOfficerMenu(forceLog: _deliveryFinishing || _deliveryTestMode);
                if (!IsGcOfficerMenuOpen())
                    return false;

                CloseAddonSafe("SelectString");
                return IsGcOfficerMenuOpen();
            });

            if (!stillOpen)
            {
                await Task.Delay(150);
                if (!await Service.Framework.RunOnFrameworkThread(IsGcOfficerMenuOpen))
                    return;
            }

            if (attempt == 1 && stillOpen)
                await LogAsync("Closing GC personnel officer menu...");

            await Task.Delay(200);
        }

        if (!await Service.Framework.RunOnFrameworkThread(IsGcOfficerMenuOpen))
            return;

        await LogAsync("GC personnel menu still open — breaking NPC interaction");
        await BreakNpcInteractionAsync();
        await Task.Delay(400);

        await Service.Framework.RunOnFrameworkThread(() =>
        {
            CloseExpertDeliveryUiForce();
            CloseAddonSafe("SelectString");
        });
    }

    private static bool IsGcOfficerMenuOpen()
    {
        if (!IsAddonVisible("SelectString"))
            return false;

        return !IsAddonVisible("GrandCompanySupplyList")
               && !IsAddonVisible("GrandCompanySupplyReward");
    }

    private static bool IsAnyPersonnelUiOpen()
    {
        if (IsGcOfficerMenuOpen())
            return true;

        if (IsAnyExpertDeliveryUiOpen())
            return true;

        if (IsGcSupplyPipelineBusy())
            return true;

        return IsAddonVisible("GrandCompanySupplyReward")
            || IsAddonVisible("GrandCompanySupplyList");
    }

    /// <summary>True while the GC supply UI can still eat a fresh NPC interaction. Visibility alone lies:
    /// an addon stays allocated (invisible) mid-teardown, the supply agent can outlive the window, and the
    /// officer talk keeps the player occupied — any of these makes the next quartermaster interact fail silently.</summary>
    private static unsafe bool IsGcSupplyPipelineBusy()
    {
        if (Service.GameGui.GetAddonByName<AtkUnitBase>("GrandCompanySupplyList") != null)
            return true;

        if (Service.GameGui.GetAddonByName<AtkUnitBase>("GrandCompanySupplyReward") != null)
            return true;

        var agent = AgentGrandCompanySupply.Instance();
        if (agent != null && agent->IsAgentActive())
            return true;

        // Occupied only counts as a supply blocker when no other automation-owned event window
        // explains it — the GC exchange/repair/officer menus set the same flags while legitimately open.
        if (!Service.Condition[ConditionFlag.OccupiedInEvent]
            && !Service.Condition[ConditionFlag.OccupiedInQuestEvent])
            return false;

        return !IsAddonVisible(GcExchangeAddon)
            && !IsAddonVisible("Repair")
            && !IsAddonVisible("SelectString");
    }

    private static unsafe string DescribeGcSupplyBlockers()
    {
        var parts = new List<string>();
        if (Service.GameGui.GetAddonByName<AtkUnitBase>("GrandCompanySupplyList") != null)
            parts.Add("SupplyList allocated");
        if (Service.GameGui.GetAddonByName<AtkUnitBase>("GrandCompanySupplyReward") != null)
            parts.Add("SupplyReward allocated");
        var agent = AgentGrandCompanySupply.Instance();
        if (agent != null && agent->IsAgentActive())
            parts.Add("supply agent active");
        if (Service.Condition[ConditionFlag.OccupiedInEvent])
            parts.Add("OccupiedInEvent");
        if (Service.Condition[ConditionFlag.OccupiedInQuestEvent])
            parts.Add("OccupiedInQuestEvent");
        return parts.Count == 0 ? "clear" : string.Join(", ", parts);
    }

    /// <summary>Force-close and wait until the supply pipeline is fully torn down. False on timeout.</summary>
    private async Task<bool> WaitForGcSupplyPipelineClearAsync(int timeoutMs)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < deadline && IsRunning)
        {
            var busy = await Service.Framework.RunOnFrameworkThread(() =>
            {
                if (!IsGcSupplyPipelineBusy() && !IsAnyExpertDeliveryUiOpen())
                    return false;

                CloseExpertDeliveryUiForce();
                return true;
            });

            if (!busy)
                return true;

            await Task.Delay(200);
        }

        return false;
    }

    private async Task BreakNpcInteractionAsync()
    {
        var gcIdx = Plugin.Config.GrandCompanyIndex;

        await Service.Framework.RunOnFrameworkThread(() =>
        {
            TryClearAgentContextMenu();
            TryDismissGcOfficerMenu();
            TryHideGrandCompanySupplyAgent();
            Service.TargetManager.Target = null;
        });

        var pos = await Service.Framework.RunOnFrameworkThread(PlayerPos);
        if (!pos.HasValue || !IpcManager.VnavAvailable)
            return;

        var officer = GcOfficerPos[gcIdx];
        var delta = pos.Value - officer;
        if (delta.LengthSquared() < 0.25f)
            delta = new Vector3(6f, 0f, 0f);
        var away = pos.Value + Vector3.Normalize(delta) * 6f;

        await IpcManager.VnavMoveToAsync(away, false);
        await Task.Delay(800);
        await Service.Framework.RunOnFrameworkThread(IpcManager.VnavStop);
        await Task.Delay(400);
        _expectNpcMenu = false;
    }

    private static bool IsAnyExpertDeliveryUiOpen()
    {
        foreach (var name in ExpertDeliveryUiAddons)
        {
            if (IsAddonVisible(name))
                return true;
        }

        return IsSelectYesnoVisible();
    }

    private async Task StartDutyAsync()
    {
        await PrepareForDutyLaunchAsync();
        if (!IsRunning)
            return;

        var cfg = Plugin.Config;
        // Launch attempts retry every tick (e.g. AutoDuty post-stop cooldown) — count the
        // cycle once per boundary, not once per attempt.
        if (!_cycleCounted)
        {
            TotalCycles++;
            _runsThisCycle = 0;
            _cycleCounted = true;
        }

        await StatusAsync($"Cycle {TotalCycles}: starting run 1/{cfg.RunsPerCycle}");

        if (!await LaunchDutyRunnerAsync())
            return;

        await GotoStateAsync(FarmState.WaitingForDutyStart);
    }

    private async Task ContinueDutyAsync()
    {
        await PrepareForDutyLaunchAsync();
        if (!IsRunning)
            return;

        var cfg = Plugin.Config;
        await StatusAsync($"Cycle {TotalCycles}: starting run {_runsThisCycle + 1}/{cfg.RunsPerCycle}");

        if (!await LaunchDutyRunnerAsync())
            return;

        await GotoStateAsync(FarmState.WaitingForDutyStart);
    }

    private async Task PrepareForDutyLaunchAsync()
    {
        try
        {
            await CloseExpertDeliveryUiAsync();
        }
        catch (Exception ex)
        {
            await LogAsync($"WARN: Pre-duty UI close failed ({ex.Message}) — continuing");
        }
    }

    private async Task<bool> LaunchDutyRunnerAsync()
    {
        var runner = Plugin.Config.DutyRunner;
        if (runner == 0 && !IpcManager.AutoDutyAvailable)
        {
            var msg = IpcManager.AutoDutyPluginLoaded
                ? "AutoDuty is loaded but Run IPC is not ready — restart AutoDuty or reload plugins"
                : "AutoDuty plugin is not loaded";
            await SetErrorAsync(msg);
            return false;
        }

        if (runner == 1 && !IpcManager.AdsAvailable)
        {
            await SetErrorAsync("ADS IPC not available — install/update McVaxius ADS");
            return false;
        }

        if (runner == 0)
        {
            _adsNeedsStartInside = false;
            if (!TryPrepareAutoDutyRun())
                return false;

            var autoDutyDuty = AutoDutyCatalog.SelectedOrDefault(Plugin.Config);
            if (!IpcManager.AutoDutyContentHasPath(autoDutyDuty.TerritoryType))
            {
                await SetErrorAsync($"AutoDuty has no path for {autoDutyDuty.Name} (territory {autoDutyDuty.TerritoryType}) — pick another duty or update AutoDuty");
                return false;
            }

            var modeValue = Plugin.Config.AutoDutyModeConfigValue();
            if (modeValue != null && !IpcManager.AutoDutySetConfig("dutyModeEnum", modeValue))
                await LogAsync($"WARN: Could not set AutoDuty duty mode to {modeValue} — using AutoDuty's current setting");

            if (!IpcManager.AutoDutyRun(autoDutyDuty.TerritoryType, 1))
            {
                await SetErrorAsync("AutoDuty.Run IPC failed");
                return false;
            }

            await LogAsync($"AutoDuty run started: {autoDutyDuty.Name}{(modeValue != null ? $" ({modeValue})" : string.Empty)}");
            _autoDutyStoppedAt = DateTime.MinValue;
            return true;
        }

        if (InDuty())
        {
            if (!TryStartAdsInsideDuty())
            {
                await SetErrorAsync("ADS failed to start inside duty");
                return false;
            }

            return true;
        }

        var adsOutsideQueued = TryStartAdsOutsideDuty();
        var duty = DutySupportCatalog.SelectedOrDefault(Plugin.Config);
        if (duty.ContentFinderConditionId == 0)
        {
            await SetErrorAsync($"Duty Support content finder ID for {duty.Name} was not detected — reload in game and reselect the duty");
            return false;
        }

        _pendingDutySupportDuty = duty;
        _adsNeedsStartInside = !adsOutsideQueued;
        _dutySupportQueueSince = DateTime.UtcNow;
        _dutySupportLastActionUtc = DateTime.MinValue;
        await LogAsync(adsOutsideQueued
            ? $"Queueing Duty Support: {duty.Name} (ADS outside ownership queued)"
            : $"Queueing Duty Support: {duty.Name} (ADS will start inside after zoning)");
        await GotoStateAsync(FarmState.OpenDutySupport);
        return false;
    }

    private bool TryPrepareAutoDutyRun()
    {
        if (!IpcManager.AutoDutyIsStopped())
        {
            _autoDutyStoppedAt = DateTime.MinValue;
            StatusQuiet("Waiting for AutoDuty to finish previous state...");
            return false;
        }

        RecordAutoDutyStopped();

        var cooldownRemaining = TimeSpan.FromSeconds(2) - (DateTime.Now - _autoDutyStoppedAt);
        if (cooldownRemaining > TimeSpan.Zero)
        {
            StatusQuiet($"Waiting for AutoDuty cooldown ({cooldownRemaining.TotalSeconds:0.0}s)...");
            return false;
        }

        return true;
    }

    private void RecordAutoDutyStopped()
    {
        if (_autoDutyStoppedAt == DateTime.MinValue)
            _autoDutyStoppedAt = DateTime.Now;
    }

    private unsafe void OpenDutySupportTick()
    {
        if (InDuty())
        {
            GotoState(FarmState.WaitingForDutyStart);
            return;
        }

        _pendingDutySupportDuty ??= DutySupportCatalog.SelectedOrDefault(Plugin.Config);
        var duty = _pendingDutySupportDuty;
        if (duty == null)
        {
            SetError("No Duty Support duty selected");
            return;
        }

        if (duty.ContentFinderConditionId == 0)
        {
            SetError($"Duty Support content finder ID for {duty.Name} was not detected");
            return;
        }

        if (DutySupportQueueTimedOut())
            return;

        var agent = AgentDawnStory.Instance();
        if (agent != null && agent->IsAddonReady())
        {
            GotoState(FarmState.QueueDutySupport);
            return;
        }

        if (!CanRunWorldAutomation())
        {
            StatusQuiet("Waiting to open Duty Support...");
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _dutySupportLastActionUtc < TimeSpan.FromMilliseconds(DutySupportOpenRetryMs))
            return;

        var atk = RaptureAtkModule.Instance();
        if (atk == null)
        {
            SetError("RaptureAtkModule unavailable — cannot open Duty Support");
            return;
        }

        if (!IsDutySupportMainCommandEnabled())
        {
            StatusQuiet("Duty Support command is not available yet...");
            _dutySupportLastActionUtc = now;
            return;
        }

        atk->OpenDawnStory(duty.ContentFinderConditionId);
        _dutySupportLastActionUtc = now;
        Status($"Opening Duty Support: {duty.Name}...");
    }

    private unsafe void QueueDutySupportTick()
    {
        if (InDuty())
        {
            GotoState(FarmState.WaitingForDutyStart);
            return;
        }

        _pendingDutySupportDuty ??= DutySupportCatalog.SelectedOrDefault(Plugin.Config);
        var duty = _pendingDutySupportDuty;
        if (duty == null)
        {
            SetError("No Duty Support duty selected");
            return;
        }

        if (TryConfirmDutySupportQueue())
            return;

        if (DutySupportQueueTimedOut())
            return;

        var agent = AgentDawnStory.Instance();
        if (agent == null || !agent->IsAddonReady())
        {
            GotoState(FarmState.OpenDutySupport);
            return;
        }

        if (Service.Condition[ConditionFlag.Occupied] || Service.Condition[ConditionFlag.Casting])
        {
            StatusQuiet("Duty Support is busy — waiting...");
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _dutySupportLastActionUtc < TimeSpan.FromMilliseconds(DutySupportRegisterRetryMs))
            return;

        agent->RegisterForDuty();
        _dutySupportLastActionUtc = now;
        Status($"Registering for Duty Support: {duty.Name}...");
    }

    private bool DutySupportQueueTimedOut()
    {
        _dutySupportQueueSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _dutySupportQueueSince.Value <= TimeSpan.FromMilliseconds(DutySupportQueueTimeoutMs))
            return false;

        SetError("Timed out queueing Duty Support duty");
        return true;
    }

    private bool TryConfirmDutySupportQueue()
    {
        if (!IsAddonVisible("ContentsFinderConfirm"))
            return false;

        var now = DateTime.UtcNow;
        if (now - _dutySupportLastActionUtc < TimeSpan.FromMilliseconds(GcUiCooldownMs))
            return true;

        SendCallback("ContentsFinderConfirm", true, 8);
        _dutySupportLastActionUtc = now;
        StatusQuiet("Confirming Duty Support queue...");
        return true;
    }

    private static unsafe bool IsDutySupportMainCommandEnabled()
    {
        var agentHud = AgentHUD.Instance();
        return agentHud == null || agentHud->IsMainCommandEnabled(91);
    }

    private bool TryStartAdsInsideDuty()
    {
        var started = IpcManager.AdsStartDutyFromInside()
                      || IpcManager.AdsResumeDutyFromInside()
                      || IpcManager.AdsStartInsideCommand();
        if (!started)
            return false;

        _adsNeedsStartInside = false;
        ResetDutySupportQueueState(clearAdsStartPending: false);
        RefreshAdsCombatAutomationIfNeeded(force: true);
        return true;
    }

    private bool TryStartAdsOutsideDuty()
    {
        var started = IpcManager.AdsStartDutyFromOutside() || IpcManager.AdsStartOutsideCommand();
        if (started)
            RefreshAdsCombatAutomationIfNeeded(force: true);

        return started;
    }

    private void RefreshAdsCombatAutomationIfNeeded(bool force = false)
    {
        if (Plugin.Config.DutyRunner != 1)
            return;

        var now = DateTime.UtcNow;
        if (!force && now - _lastAdsCombatRefreshUtc < TimeSpan.FromSeconds(15))
            return;

        _lastAdsCombatRefreshUtc = now;
        IpcManager.RefreshAdsCombatAutomation();
    }

    private void ResetDutySupportQueueState(bool clearAdsStartPending = true)
    {
        _pendingDutySupportDuty = null;
        _dutySupportQueueSince = null;
        _dutySupportLastActionUtc = DateTime.MinValue;
        if (clearAdsStartPending)
            _adsNeedsStartInside = false;
    }

    private bool IsMidDutyCycle(Configuration cfg) =>
        _runsThisCycle > 0 && _runsThisCycle < cfg.RunsPerCycle;

    private bool TryBeginBetweenRunMateriaExtraction()
    {
        var cfg = Plugin.Config;
        if (!cfg.AutoExtractMateriaEnabled || !cfg.AutoExtractMateriaBetweenRuns)
            return false;

        if (_lastBetweenRunExtractRun == TotalRuns)
            return false;

        _lastBetweenRunExtractRun = TotalRuns;
        return TryBeginMateriaExtraction(FarmState.StartDuty, "between duty runs");
    }

    private bool TryBeginCycleBoundaryMateriaExtraction()
    {
        var cfg = Plugin.Config;
        if (!cfg.AutoExtractMateriaEnabled || !cfg.AutoExtractMateriaAtCycleBoundary)
            return false;

        if (_lastCycleBoundaryExtractCycle == TotalCycles)
            return false;

        _lastCycleBoundaryExtractCycle = TotalCycles;
        return TryBeginMateriaExtraction(FarmState.StartDuty, "before duty cycle");
    }

    private bool TryBeginMateriaExtraction(FarmState returnState, string context)
    {
        if (InDuty() || IsBetweenAreas() || IpcManager.LifestreamIsBusy())
            return false;

        ResetMateriaExtractionState();
        _extractReturnState = returnState;
        Log($"Materia extraction check — {context}");
        GotoState(FarmState.OpenMateriaExtraction);
        return true;
    }

    private void LogPreDutyGearCheck()
    {
        var cfg = Plugin.Config;
        var town = cfg.TownNav(cfg.GrandCompanyIndex);
        var condition = GetMinEquippedConditionPercent();
        if (!cfg.RepairEnabled)
        {
            Log($"Pre-duty gear check — lowest condition {condition}% (repair disabled)");
            return;
        }

        var provider = cfg.RepairProvider == Configuration.RepairProviderAds ? "ADS" : "SealBreaker";
        var menderNote = cfg.RepairProvider == Configuration.RepairProviderSealBreaker && !town.HasMenderConfigured
            ? " — SealBreaker mender not configured"
            : string.Empty;
        Log($"Pre-duty gear check — lowest condition {condition}% (repair below {cfg.RepairThresholdPercent}%, provider {provider}){menderNote}");
    }

    private void StopDutyRunner()
    {
        if (Plugin.Config.DutyRunner == 0)
        {
            if (!IpcManager.AutoDutyIsStopped())
                IpcManager.AutoDutyStop();
            else
                RecordAutoDutyStopped();
        }
        else
        {
            IpcManager.AdsStop();
        }
    }

    private bool GcActionReady() => DateTime.UtcNow >= _gcActionCooldownUntil;

    private void ThrottleGcAction(int ms = GcUiCooldownMs) =>
        _gcActionCooldownUntil = DateTime.UtcNow.AddMilliseconds(ms);

    private async Task OpenRepairMenuAsync()
    {
        IpcManager.VnavStop();
        _expectNpcMenu = true;

        if (await WaitForAddonVisibleAsync("Repair", 1500))
        {
            await LogAsync("Repair window already open — skipping NPC dialog");
        }
        else if (await WaitForAddonVisibleAsync("SelectString", 5000))
        {
            await Service.Framework.RunOnFrameworkThread(() => SelectSelectStringOption(0));
            await Task.Delay(250);

            if (!await WaitForAddonVisibleAsync("Repair", 8000))
            {
                _expectNpcMenu = false;
                await Service.Framework.RunOnFrameworkThread(() =>
                {
                    CloseAddonSafe("SelectString");
                    CloseAddonSafe("Repair");
                });
                await SetErrorAsync("Repair window did not open");
                return;
            }
        }
        else
        {
            _expectNpcMenu = false;
            await SetErrorAsync($"{Plugin.Config.TownNav(Plugin.Config.GrandCompanyIndex).MenderName} menu did not open");
            return;
        }

        await BeginRepairPhaseAsync();
    }

    private void ProcessRepairTick()
    {
        IpcManager.VnavStop();

        if (!GcActionReady())
            return;

        _repairPhaseSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _repairPhaseSince.Value > TimeSpan.FromMilliseconds(RepairPhaseTimeoutMs))
        {
            Log("WARN: Repair timed out — continuing duties");
            FinishRepair();
            return;
        }

        var yesnoOpen = IsSelectYesnoOpen();
        var repairOpen = IsAddonOpen("Repair");

        if (IsSelectYesnoVisible())
        {
            if (yesnoOpen && (_repairAllClicked || IsRepairYesnoPrompt()))
            {
                ClickSelectYesno(forceEnableYes: true);
                if (!_repairYesnoLogged)
                {
                    Log("Confirming repair...");
                    _repairYesnoLogged = true;
                }
            }

            ThrottleGcAction(300);
            return;
        }

        if (_repairYesnoLogged)
        {
            CloseRepairUi();
            if (!IsAddonVisible("Repair") && !IsSelectYesnoVisible())
            {
                Log($"Repair complete | Lowest condition: {GetMinEquippedConditionPercent()}%");
                FinishRepair();
            }
            else
                ThrottleGcAction(250);

            return;
        }

        if (repairOpen)
        {
            if (!_repairAllClicked && !NeedsRepair())
            {
                Log($"No repair needed | Lowest condition: {GetMinEquippedConditionPercent()}%");
                FinishRepair();
                return;
            }

            if (TryClickRepairAll())
            {
                if (!_repairAllClicked)
                {
                    Log("Repairing all equipped gear...");
                    _repairAllClicked = true;
                }

                ThrottleGcAction(250);
            }

            return;
        }

        if (_repairAllClicked || !NeedsRepair())
        {
            Log($"Repair complete | Lowest condition: {GetMinEquippedConditionPercent()}%");
            FinishRepair();
        }
    }

    private void FinishRepair()
    {
        CloseRepairUi();

        _repairAllClicked = false;
        _repairYesnoLogged = false;
        _repairPhaseSince = null;

        if (GcNavRoutes.HasRepairRoute(Plugin.Config, Plugin.Config.GrandCompanyIndex))
            GotoState(FarmState.NavigateFromRepair);
        else if (_repairTestMode)
            FinishRepairTest();
        else
            GotoState(FarmState.StartDuty);
    }

    private void OpenMateriaExtractionTick()
    {
        if (!CanStartMateriaExtraction(out var skipReason))
        {
            FinishMateriaExtraction(skipReason);
            return;
        }

        _materializePhaseSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _materializePhaseSince.Value > TimeSpan.FromMilliseconds(MaterializePhaseTimeoutMs))
        {
            FinishMateriaExtraction("Materia extraction timed out.");
            return;
        }

        if (IsAddonVisible("Materialize"))
        {
            GotoState(FarmState.ProcessMateriaExtraction);
            return;
        }

        var extractableEquipped = CountFullySpiritboundEquippedItems();
        if (extractableEquipped == 0)
        {
            FinishMateriaExtraction(_extractAttemptedAny
                ? "Materia extraction finished."
                : "No fully spiritbound equipped gear was found.");
            return;
        }

        if (DateTime.UtcNow - _materializeLastActionUtc < TimeSpan.FromMilliseconds(GcUiCooldownMs))
            return;

        if (TryUseMateriaExtractionAction())
        {
            _materializeLastActionUtc = DateTime.UtcNow;
            Status($"Opening materia extraction for {extractableEquipped} equipped item(s)...");
        }
        else
        {
            FinishMateriaExtraction("Could not open materia extraction.");
        }
    }

    private unsafe void ProcessMateriaExtractionTick()
    {
        if (!CanStartMateriaExtraction(out var skipReason))
        {
            FinishMateriaExtraction(skipReason);
            return;
        }

        _materializePhaseSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _materializePhaseSince.Value > TimeSpan.FromMilliseconds(MaterializePhaseTimeoutMs))
        {
            FinishMateriaExtraction("Materia extraction timed out.");
            return;
        }

        var now = DateTime.UtcNow;
        if (IsMateriaExtractionBusy())
        {
            _materializeLastActionUtc = now;
            StatusQuiet("Materia extraction busy — waiting...");
            return;
        }

        var materializeDialog = GetVisibleMaterializeDialog();
        if (materializeDialog != null)
        {
            if (now - _materializeLastActionUtc >= TimeSpan.FromMilliseconds(GcUiCooldownMs))
            {
                if (materializeDialog->YesButton == null || !materializeDialog->YesButton->IsEnabled)
                    return;

                ClickComponentButton(materializeDialog->YesButton, (AtkUnitBase*)materializeDialog);
                _extractAttemptedAny = true;
                _materializeCategoryArmed = false;
                _materializeAttemptPending = false;
                _materializeLastActionUtc = now;
                Status("Confirming materia extraction; will recheck equipped gear...");
            }

            return;
        }

        if (!IsAddonVisible("Materialize"))
        {
            if (_materializeCategory > LastMaterializeCategory)
            {
                FinishMateriaExtraction(_extractAttemptedAny
                    ? "Materia extraction finished."
                    : "No extractable materia was found.");
                return;
            }

            if (now - _materializeLastActionUtc >= TimeSpan.FromMilliseconds(GcUiCooldownMs)
                && TryUseMateriaExtractionAction())
            {
                _materializeLastActionUtc = now;
                Status("Opening materia extraction...");
            }

            return;
        }

        if (_materializeCategory > LastMaterializeCategory)
        {
            var remaining = CountFullySpiritboundEquippedItems();
            if (remaining > 0)
            {
                _materializeCategory = EquippedMaterializeCategory;
                _materializeCategoryArmed = false;
                _materializeAttemptPending = false;
                Status($"Equipped gear still has {remaining} fully spiritbound item(s); retrying extraction...");
                return;
            }

            FinishMateriaExtraction(_extractAttemptedAny
                ? "Materia extraction finished."
                : "No fully spiritbound equipped gear was found.");
            return;
        }

        if (!_materializeCategoryArmed)
        {
            if (now - _materializeLastActionUtc >= TimeSpan.FromMilliseconds(GcUiCooldownMs))
            {
                SendCallback("Materialize", false, 1, _materializeCategory);
                _materializeCategoryArmed = true;
                _materializeAttemptPending = false;
                _materializeLastActionUtc = now;
                Status("Switching materia extraction to equipped gear...");
            }

            return;
        }

        if (!_materializeAttemptPending)
        {
            if (now - _materializeLastActionUtc >= TimeSpan.FromMilliseconds(GcUiCooldownMs))
            {
                SendCallback("Materialize", true, 2, 0);
                _materializeAttemptPending = true;
                _materializeLastActionUtc = now;
                Status("Trying materia extraction from equipped gear...");
            }

            return;
        }

        if (now - _materializeLastActionUtc < TimeSpan.FromMilliseconds(MaterializeResultWaitMs))
        {
            StatusQuiet("Waiting for equipped materia extraction result...");
            return;
        }

        var remainingEquipped = CountFullySpiritboundEquippedItems();
        if (remainingEquipped > 0)
        {
            _materializeCategoryArmed = false;
            _materializeAttemptPending = false;
            Status($"Equipped gear still has {remainingEquipped} fully spiritbound item(s); retrying extraction...");
            return;
        }

        _materializeCategory++;
        _materializeCategoryArmed = false;
        _materializeAttemptPending = false;
        Status("No further fully spiritbound equipped gear remains.");
    }

    private void FinishMateriaExtraction(string message)
    {
        CloseMateriaExtractionUi();
        var returnState = _extractReturnState;
        var wasTest = _extractTestMode;
        ResetMateriaExtractionState();

        Log(message);

        if (wasTest)
        {
            _extractTestMode = false;
            IsRunning = false;
            GotoState(FarmState.Idle);
            return;
        }

        GotoState(returnState);
    }

    private void ResetMateriaExtractionState()
    {
        _materializeCategory = EquippedMaterializeCategory;
        _materializeCategoryArmed = false;
        _materializeAttemptPending = false;
        _extractAttemptedAny = false;
        _materializePhaseSince = null;
        _materializeLastActionUtc = DateTime.MinValue;
        _extractReturnState = FarmState.StartDuty;
    }

    private static unsafe bool CanStartMateriaExtraction(out string skipReason)
    {
        skipReason = string.Empty;

        if (InDuty())
        {
            skipReason = "Materia extraction skipped — character is in duty.";
            return false;
        }

        if (IsBetweenAreas() || IpcManager.LifestreamIsBusy())
        {
            skipReason = "Materia extraction skipped — character is zoning.";
            return false;
        }

        if (!QuestManager.IsQuestComplete(MateriaExtractionQuestId))
        {
            skipReason = "Materia extraction skipped — Materia Extraction is not unlocked.";
            return false;
        }

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
        {
            skipReason = "Materia extraction skipped — inventory manager unavailable.";
            return false;
        }

        if (inventoryManager->GetEmptySlotsInBag() < 1)
        {
            skipReason = "Materia extraction skipped — at least one empty inventory slot is required.";
            return false;
        }

        return true;
    }

    private static bool IsMateriaExtractionBusy() =>
        Service.Condition[ConditionFlag.Occupied]
        || Service.Condition[ConditionFlag.OccupiedInCutSceneEvent]
        || Service.Condition[ConditionFlag.Casting];

    private static unsafe int CountFullySpiritboundEquippedItems()
    {
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return 0;

        var equipment = inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipment == null)
            return 0;

        var count = 0;
        for (var i = 0; i < equipment->Size; i++)
        {
            var item = equipment->GetInventorySlot(i);
            if (item == null || item->ItemId == 0)
                continue;

            if (item->GetSpiritbondOrCollectability() >= FullSpiritbond)
                count++;
        }

        return count;
    }

    private static unsafe AddonMaterializeDialog* GetVisibleMaterializeDialog()
    {
        var addon = Service.GameGui.GetAddonByName<AddonMaterializeDialog>("MaterializeDialog");
        return addon != null && addon->IsVisible && addon->IsReady ? addon : null;
    }

    private static unsafe bool TryUseMateriaExtractionAction()
    {
        var actionManager = ActionManager.Instance();
        return actionManager != null
               && actionManager->UseAction(ActionType.GeneralAction, MaterializeGeneralAction);
    }

    private static void CloseMateriaExtractionUi()
    {
        CloseAddonSafe("MaterializeDialog");
        CloseAddonSafe("Materialize");
    }

    private static unsafe void CloseRepairUi()
    {
        foreach (var name in SelectYesnoAddons)
            CloseAddonSafe(name);

        CloseAddonSafe("Repair");
        CloseAddonSafe("SelectString");

        var agentModule = AgentModule.Instance();
        if (agentModule == null)
            return;

        var agent = agentModule->GetAgentByInternalId(AgentId.Repair);
        if (agent != null)
            agent->Hide();
    }

    private bool TryBeginRepairBeforeDuty()
    {
        if (InDuty() || IsMidDutyCycle(Plugin.Config))
            return false;

        if (!ShouldRepairBetweenRuns())
            return false;

        var cfg = Plugin.Config;
        var condition = GetMinEquippedConditionPercent();
        if (cfg.RepairProvider == Configuration.RepairProviderAds)
        {
            if (_adsRepairAttemptedBeforeDuty)
            {
                Log($"ADS repair already attempted this duty launch — starting duty with gear at {condition}%");
                return false;
            }

            _adsRepairAttemptedBeforeDuty = true;
            _currentTask = RunAdsRepairBeforeDutyAsync(condition);
            return true;
        }

        var gcIdx = Plugin.Config.GrandCompanyIndex;
        var town = Plugin.Config.TownNav(gcIdx);
        Log($"Gear condition {condition}% < {cfg.RepairThresholdPercent}% — heading to {town.MenderName} before duty");

        if (Service.ClientState.TerritoryType == GcOfficerZoneId[gcIdx])
            GotoState(FarmState.NavigateToRepair);
        else
            BeginGcNavigation(FarmState.NavigateToRepair);

        return true;
    }

    private async Task RunAdsRepairBeforeDutyAsync(int condition)
    {
        var mode = Plugin.Config.AdsRepairModeCommand();
        Log($"Gear condition {condition}% < {Plugin.Config.RepairThresholdPercent}% — starting ADS repair ({mode}) before duty");
        if (!IpcManager.AdsStartRepair(mode))
        {
            await LogAsync("ADS repair skipped — ADS is not loaded");
            await GotoStateAsync(FarmState.StartDuty);
            return;
        }

        var deadline = DateTime.Now.AddMinutes(3);
        while (IsRunning && DateTime.Now < deadline)
        {
            if (!NeedsRepair())
            {
                await LogAsync($"ADS repair complete | Lowest condition: {GetMinEquippedConditionPercent()}%");
                await GotoStateAsync(FarmState.StartDuty);
                return;
            }

            await Task.Delay(1000);
        }

        await LogAsync($"WARN: ADS repair did not finish before timeout | Lowest condition: {GetMinEquippedConditionPercent()}%");
        await GotoStateAsync(FarmState.StartDuty);
    }

    private static bool ShouldRepairBetweenRuns()
    {
        var cfg = Plugin.Config;
        var gcIdx = cfg.GrandCompanyIndex;
        var town = cfg.TownNav(gcIdx);
        if (!cfg.RepairEnabled)
            return false;

        if (cfg.RepairProvider == Configuration.RepairProviderSealBreaker && !town.HasMenderConfigured)
            return false;

        return GetMinEquippedConditionPercent() < cfg.RepairThresholdPercent;
    }

    private static bool NeedsRepair()
    {
        return GetMinEquippedConditionPercent() < Plugin.Config.RepairThresholdPercent;
    }

    private static unsafe int GetMinEquippedConditionPercent()
    {
        var equipment = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipment == null)
            return 100;

        var minCondition = ushort.MaxValue;
        for (var i = 0; i < equipment->Size; i++)
        {
            var item = equipment->GetInventorySlot(i);
            if (item == null || item->ItemId == 0)
                continue;

            if (item->Condition < minCondition)
                minCondition = item->Condition;
        }

        if (minCondition == ushort.MaxValue)
            return 100;

        return (int)Math.Ceiling(minCondition / 300.0);
    }

    private static unsafe bool TryClickRepairAll()
    {
        var addon = Service.GameGui.GetAddonByName<AddonRepair>("Repair");
        if (addon == null || !addon->IsVisible || !addon->IsReady)
            return false;

        var button = addon->RepairAllButton;
        if (button == null || !button->IsEnabled)
            return false;

        ClickAddonButtonViaReceiveEvent(button, (AtkUnitBase*)addon);
        return true;
    }

    private static unsafe bool IsRepairYesnoPrompt()
    {
        foreach (var name in SelectYesnoAddons)
        {
            var addon = Service.GameGui.GetAddonByName<AddonSelectYesno>(name);
            if (addon == null || !addon->IsVisible || addon->PromptText == null)
                continue;

            var text = addon->PromptText->NodeText.ToString();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            foreach (var snippet in RepairYesnoPromptSnippets)
            {
                if (text.Contains(snippet, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private async Task OpenGCShopAsync()
    {
        try
        {
            await CloseExpertDeliveryUiAsync();
        }
        catch (Exception ex)
        {
            await LogAsync($"WARN: Pre-exchange UI close failed ({ex.Message}) — continuing");
        }

        if (!IsRunning)
            return;

        _expectNpcMenu = true;
        if (await Service.Framework.RunOnFrameworkThread(() => IsAddonVisible(GcExchangeAddon)))
        {
            await LogAsync("GC exchange already open");
        }
        else if (await Service.Framework.RunOnFrameworkThread(IsAnyPersonnelUiOpen))
        {
            await LogAsync("Personnel menu still open — closing before GC exchange");
            await Service.Framework.RunOnFrameworkThread(CloseExpertDeliveryUiForce);
            await Task.Delay(400);
            if (await Service.Framework.RunOnFrameworkThread(IsAnyPersonnelUiOpen))
                await BreakNpcInteractionAsync();
        }
        else if (await WaitForAddonVisibleAsync("SelectString", 3000))
        {
            await Service.Framework.RunOnFrameworkThread(() => SendCallback("SelectString", true, 0));
            await Task.Delay(250);
        }

        if (!await WaitForAddonVisibleAsync(GcExchangeAddon, 8000))
        {
            _expectNpcMenu = false;
            await Service.Framework.RunOnFrameworkThread(() => CloseAddonSafe("SelectString"));

            if (!_openGcShopRetried && IsRunning)
            {
                _openGcShopRetried = true;
                await LogAsync("WARN: GC exchange did not open — clearing stuck UI and re-approaching the quartermaster");
                await WaitForGcSupplyPipelineClearAsync(8000);
                await BreakNpcInteractionAsync();
                await GotoStateAsync(FarmState.NavigateToShop);
                return;
            }

            await SetErrorAsync("GC exchange did not open");
            return;
        }

        _expectNpcMenu = false;
        _openGcShopRetried = false;

        await Service.Framework.RunOnFrameworkThread(() => CloseAddonSafe("SelectString"));
        _gcActionCooldownUntil = DateTime.MinValue;
        _buyAwaitingConfirm = false;
        _buyPhaseSince = DateTime.UtcNow;
        _buyAttemptSince = null;
        _buyQtyDialogSent = false;
        _buyFindItemFailures = 0;
        await StatusAsync("GC exchange open — buying GC shop list...");
        _buyListIndex = 0;
        await GotoStateAsync(FarmState.BuyDuckbones);
    }

    private void BuyDuckbonesTick()
    {
        if (!GcActionReady())
            return;

        _buyPhaseSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _buyPhaseSince.Value > TimeSpan.FromMilliseconds(BuyPhaseTimeoutMs))
        {
            Log("WARN: GC shop buying phase timed out — closing exchange");
            FinishBuying();
            return;
        }

        var entry = GetActiveShopBuyEntry();
        if (entry == null)
        {
            Log($"Seals at {GetCurrentSeals():N0} — buy list targets met or unaffordable");
            FinishBuying();
            return;
        }

        if (IsSelectYesnoVisible())
        {
            ClickSelectYesno();
            if (_buyAwaitingConfirm)
            {
                if (ResolveShopItemId(entry) == ResolveDuckBoneItemId())
                    TotalDuckbones += _buyLastQty;
                Log($"Bought {_buyLastQty}x '{_buyLastItemLabel}' | Seals: {GetCurrentSeals()}");
                ResetBuyAttempt();
                if (_oneShotBuyEntry != null)
                {
                    Log("One-shot Kingcake buy test complete.");
                    FinishBuying();
                    return;
                }
            }
            ThrottleGcAction(300);

            if (!NeedsMoreOfShopEntry(entry))
                AdvanceShopBuyEntry();
            return;
        }

        var buyDialog = GetVisibleBuyDialogName();
        if (buyDialog != null && _buyAwaitingConfirm)
        {
            _buyAttemptSince ??= DateTime.UtcNow;

            if (!_buyQtyDialogSent)
            {
                SendCallback(buyDialog, true, 0, _buyPendingQty, 0);
                _buyQtyDialogSent = true;
                ThrottleGcAction(300);
                return;
            }

            if (BuyAttemptTimedOut())
            {
                Log("WARN: GC shop quantity dialog timed out — closing exchange dialogs");
                CloseBuyConfirmDialogs();
                ResetBuyAttempt();
                if (_oneShotBuyEntry != null)
                {
                    FinishBuying();
                    return;
                }
                if (!NeedsMoreOfShopEntry(entry))
                    AdvanceShopBuyEntry();
            }
            return;
        }

        if (_buyAwaitingConfirm)
        {
            _buyAttemptSince ??= DateTime.UtcNow;
            if (BuyAttemptTimedOut())
            {
                Log("WARN: GC shop purchase confirm timed out — resetting");
                CloseBuyConfirmDialogs();
                ResetBuyAttempt();
                if (_oneShotBuyEntry != null)
                {
                    FinishBuying();
                    return;
                }
                if (!NeedsMoreOfShopEntry(entry))
                    AdvanceShopBuyEntry();
            }
            return;
        }

        if (!IsAddonVisible(GcExchangeAddon))
        {
            FinishBuying(false);
            return;
        }

        ResolveGcExchangeTabs(
            entry.CategoryTab, entry.RankTab, entry.ItemName, ResolveShopItemId(entry),
            out var rankCallback, out var categoryCallback, out var uiRankTab, out var uiCategoryTab, out var sheetTabsResolved);

        if (!_gcShopRankSelected)
        {
            SelectGcExchangeRankOnly(rankCallback, uiRankTab);
            _gcShopRankSelected = true;
            _gcShopCategorySelected = false;
            Log($"Selected GC exchange rank tab {uiRankTab} for '{entry.ItemName}'");
            ThrottleGcAction(700);
            return;
        }

        if (!_gcShopCategorySelected)
        {
            SelectGcExchangeCategoryOnly(categoryCallback, uiCategoryTab, sheetTabsResolved);
            _gcShopCategorySelected = true;
            Log($"Selected GC exchange category tab {uiCategoryTab} for '{entry.ItemName}'");
            ThrottleGcAction(700);
            return;
        }

        var resolvedItemId = ResolveShopItemId(entry);
        var expectedSealCost = entry.SealCost;
        var sheetListRow = -1;
        var sheetResolved = GcExchangeItemResolver.TryResolve(
            entry.ItemName, resolvedItemId, entry.SealCost, out var shopInfo);
        if (sheetResolved)
        {
            if (shopInfo.ItemId != 0)
                resolvedItemId = shopInfo.ItemId;
            if (shopInfo.SealCost > 0)
                expectedSealCost = shopInfo.SealCost;
            sheetListRow = shopInfo.SheetListRow;
            Log($"Resolved '{entry.ItemName}' from sheets — item {resolvedItemId}, cost {expectedSealCost}, rank callback {shopInfo.RankCallback}, category callback {shopInfo.CategoryCallback}, UI tab {shopInfo.UiCategoryTab}, row {sheetListRow}");
        }

        var maxAffordable = GetMaxAffordableForEntry(entry, expectedSealCost);
        if (maxAffordable < 1)
        {
            Log($"Cannot afford '{entry.ItemName}' ({expectedSealCost} seals) — trying next buy entry");
            AdvanceShopBuyEntry();
            return;
        }

        var qty = entry.BuyQtyPerPurchase <= 0
            ? (int)Math.Min(maxAffordable, 99)
            : (int)Math.Min(Math.Clamp(entry.BuyQtyPerPurchase, 1, 99), maxAffordable);

        if (entry.KeepAmount > 0)
        {
            var have = GetInventoryItemCount(ResolveShopItemId(entry));
            qty = (int)Math.Min(qty, Math.Max(0, entry.KeepAmount - have));
            if (qty < 1)
            {
                AdvanceShopBuyEntry();
                return;
            }
        }

        var (row, itemLabel, rowSource) = FindGcExchangeItemRow(
            entry.ItemName, resolvedItemId, expectedSealCost, sheetListRow, entry.ListRow);

        if (row < 0)
        {
            if (_oneShotBuyEntry != null)
            {
                Log("WARN: One-shot Kingcake buy test could not find Kingcake — closing exchange");
                FinishBuying();
                return;
            }

            _buyFindItemFailures++;
            if (_buyFindItemFailures >= BuyFindItemMaxFailures)
            {
                Log($"WARN: Could not find '{entry.ItemName}' after {_buyFindItemFailures} tries — closing exchange");
                FinishBuying();
                return;
            }

            Log($"WARN: Could not find '{entry.ItemName}' in list — will retry ({_buyFindItemFailures}/{BuyFindItemMaxFailures})");
            _gcShopRankSelected = false;
            _gcShopCategorySelected = false;
            ThrottleGcAction(300);
            return;
        }

        if (!ExchangeRowLabelMatches(entry.ItemName, itemLabel))
        {
            Log($"WARN: Row {row} shows '{itemLabel}', expected '{entry.ItemName}' — refusing purchase ({rowSource})");
            _buyFindItemFailures++;
            _gcShopRankSelected = false;
            _gcShopCategorySelected = false;
            ThrottleGcAction(300);
            return;
        }

        _buyFindItemFailures = 0;
        if (_oneShotBuyEntry != null)
        {
            if (!TryOpenGcExchangeBuyDialog(row, qty))
            {
                Log($"WARN: One-shot Kingcake buy test could not open buy dialog for row {row}");
                FinishBuying();
                return;
            }
        }
        else if (!TryOpenGcExchangeBuyDialog(row, qty))
        {
            Log($"WARN: Could not open buy dialog for '{itemLabel}' at row {row}");
            _buyFindItemFailures++;
            _gcShopRankSelected = false;
            _gcShopCategorySelected = false;
            ThrottleGcAction(300);
            return;
        }

        _buyAwaitingConfirm = true;
        _oneShotBuyAttemptSent = _oneShotBuyEntry != null;
        _buyPendingQty = qty;
        _buyLastQty = qty;
        _buyLastItemLabel = itemLabel;
        _buyAttemptSince = DateTime.UtcNow;
        _buyQtyDialogSent = false;
        Log($"Buying '{itemLabel}' at list index {row} ({rowSource}), qty {qty}, {expectedSealCost} seals each (keep {entry.KeepAmount}, have {GetInventoryItemCount(resolvedItemId)})...");
        ThrottleGcAction(300);
    }

    private GcShopBuyEntry? GetActiveShopBuyEntry()
    {
        if (_oneShotBuyEntry != null)
            return _oneShotBuyAttemptSent && !_buyAwaitingConfirm ? null : _oneShotBuyEntry;

        var list = Plugin.Config.EnabledGcShopBuyList();
        while (_buyListIndex < list.Count)
        {
            var entry = list[_buyListIndex];
            if (!CanBuyShopEntryForCurrentRank(entry, out var rankSkipReason))
            {
                Log(rankSkipReason);
                _buyListIndex++;
                _gcShopRankSelected = false;
                _gcShopCategorySelected = false;
                _buyFindItemFailures = 0;
                continue;
            }

            if (NeedsMoreOfShopEntry(entry))
                return entry;

            _buyListIndex++;
            _gcShopRankSelected = false;
            _gcShopCategorySelected = false;
            _buyFindItemFailures = 0;
        }

        return null;
    }

    private static unsafe bool CanBuyShopEntryForCurrentRank(GcShopBuyEntry entry, out string skipReason)
    {
        skipReason = string.Empty;
        var itemId = ResolveShopItemId(entry);
        if (!GcExchangeItemResolver.TryResolveAnyRank(entry.ItemName, itemId, entry.SealCost, out var info))
            return true;

        var playerRank = (int)PlayerState.Instance()->GetGrandCompanyRank();
        if (playerRank >= info.RequiredGrandCompanyRank)
            return true;

        skipReason = $"Skipping '{entry.ItemName}' — requires GC rank {GrandCompanyState.RankName(info.RequiredGrandCompanyRank)}";
        return false;
    }

    private void AdvanceShopBuyEntry()
    {
        _buyListIndex++;
        _gcShopRankSelected = false;
        _gcShopCategorySelected = false;
        _buyFindItemFailures = 0;
        ResetBuyAttempt();
    }

    private static bool NeedsMoreOfShopEntry(GcShopBuyEntry entry)
    {
        if (!CanAffordShopEntry(entry))
            return false;

        if (entry.KeepAmount <= 0)
            return true;

        return GetInventoryItemCount(ResolveShopItemId(entry)) < entry.KeepAmount;
    }

    private static int ResolveShopSealCost(GcShopBuyEntry entry)
    {
        if (GcExchangeItemResolver.TryResolve(entry.ItemName, ResolveShopItemId(entry), entry.SealCost, out var info)
            && info.SealCost > 0)
            return info.SealCost;

        return Math.Max(1, entry.SealCost);
    }

    private static bool CanAffordShopEntry(GcShopBuyEntry entry) =>
        GetCurrentSeals() >= Plugin.Config.SealReserve + ResolveShopSealCost(entry);

    private static int GetMaxAffordableForEntry(GcShopBuyEntry entry, int sealCostOverride = 0)
    {
        var sealCost = sealCostOverride > 0 ? sealCostOverride : ResolveShopSealCost(entry);
        var cur = GetCurrentSeals();
        return (cur - Plugin.Config.SealReserve) / Math.Max(1, sealCost);
    }

    private static bool ShouldSpendSealsOnShop()
    {
        foreach (var entry in Plugin.Config.EnabledGcShopBuyList())
        {
            if (NeedsMoreOfShopEntry(entry))
                return true;
        }

        return false;
    }

    private static bool CanAffordDuckbone() => ShouldSpendSealsOnShop();

    public static int GetKingcakeInventoryCount() => GetInventoryItemCount(KingcakeDesynth.KingcakeItemId);

    public bool TryStartKingcakeDesynth(out string message)
    {
        if (IsRunning)
        {
            message = "Stop the farm before desynthing Kingcakes.";
            return false;
        }

        var count = GetKingcakeInventoryCount();
        if (count <= 0)
        {
            message = "No Kingcakes found in inventory.";
            return false;
        }

        var startDropCounts = SnapshotKingcakeDropCounts();
        if (!TryDesynthFirstKingcake())
        {
            message = "Could not start Kingcake desynth safely. Open your inventory and make sure a Kingcake is in a normal inventory slot.";
            return false;
        }

        _pendingKingcakeDesynthStats = true;
        _kingcakeDesynthStartedUtc = DateTime.UtcNow;
        _kingcakeDesynthConsumedUtc = null;
        _kingcakeDesynthStartCount = count;
        _kingcakeDesynthStartDropCounts = startDropCounts;
        _kingcakeDesynthObservedResultDrops.Clear();
        message = $"Started Kingcake desynth for item {KingcakeDesynth.KingcakeItemId}. Confirm the in-game prompt if one appears.";
        return true;
    }

    private static int GetInventoryItemCount(uint itemId)
    {
        if (itemId == 0)
            return 0;

        unsafe
        {
            var inv = InventoryManager.Instance();
            return (int)(inv->GetInventoryItemCount(itemId) + inv->GetInventoryItemCount(itemId, true));
        }
    }

    private static unsafe bool TryDesynthFirstKingcake()
    {
        var agent = AgentSalvage.Instance();
        var inv = InventoryManager.Instance();
        if (agent == null || inv == null)
            return false;

        foreach (var inventoryType in NormalInventoryTypes)
        {
            var container = inv->GetInventoryContainer(inventoryType);
            if (container == null)
                continue;

            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item == null || item->ItemId != KingcakeDesynth.KingcakeItemId)
                    continue;

                agent->SalvageItem(item);
                return true;
            }
        }

        return false;
    }

    private static List<uint> ExpandSalvageResults(Span<SalvageResult> results)
    {
        var drops = new List<uint>();
        foreach (var result in results)
        {
            if (result.ItemId == 0 || result.Quantity <= 0)
                continue;

            var drop = KingcakeDesynth.FindDrop(result.ItemId);
            if (drop == null)
                continue;

            for (var i = 0; i < result.Quantity; i++)
                drops.Add(result.ItemId);
        }

        return drops;
    }

    private unsafe void PollKingcakeDesynthResult()
    {
        if (!_pendingKingcakeDesynthStats)
            return;

        if (DateTime.UtcNow - _kingcakeDesynthStartedUtc > TimeSpan.FromSeconds(90))
        {
            _pendingKingcakeDesynthStats = false;
            Log("Kingcake desynth result tracking timed out.");
            return;
        }

        var agent = AgentSalvage.Instance();
        var drops = agent != null && agent->IsSalvageResultAddonOpen
            ? ExpandSalvageResults(agent->DesynthResults)
            : [];

        if (drops.Count > 0)
            _kingcakeDesynthObservedResultDrops = drops;

        var currentCount = GetKingcakeInventoryCount();
        if (currentCount >= _kingcakeDesynthStartCount)
            return;

        _kingcakeDesynthConsumedUtc ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _kingcakeDesynthConsumedUtc.Value < TimeSpan.FromSeconds(5))
            return;

        var inventoryDrops = GetKingcakeDropInventoryDeltas();
        var mergedDrops = MergeDropObservations(_kingcakeDesynthObservedResultDrops, inventoryDrops);
        RecordPendingKingcakeDesynth(mergedDrops);
        Log(mergedDrops.Count > 0
            ? $"Recorded Kingcake desynth stats from merged results ({mergedDrops.Count} drop item(s))."
            : "Recorded Kingcake desynth stats from inventory consumption; drop result was not available.");
    }

    private void RecordPendingKingcakeDesynth(IEnumerable<uint> drops)
    {
        var dropList = drops.ToArray();
        DesynthTracker.RecordKingcakeDesynth(dropList);
        _pendingKingcakeDesynthStats = false;
        _kingcakeDesynthConsumedUtc = null;
        _kingcakeDesynthStartCount = 0;
        _kingcakeDesynthStartDropCounts.Clear();
        _kingcakeDesynthObservedResultDrops.Clear();
        Log($"Recorded Kingcake desynth stats ({dropList.Length} drop item(s)).");
    }

    private static List<uint> MergeDropObservations(IReadOnlyCollection<uint> resultDrops, IReadOnlyCollection<uint> inventoryDrops)
    {
        var merged = new List<uint>();
        foreach (var drop in KingcakeDesynth.Drops)
        {
            var resultCount = resultDrops.Count(itemId => itemId == drop.ItemId);
            var inventoryCount = inventoryDrops.Count(itemId => itemId == drop.ItemId);
            for (var i = 0; i < Math.Max(resultCount, inventoryCount); i++)
                merged.Add(drop.ItemId);
        }

        return merged;
    }

    private static Dictionary<uint, int> SnapshotKingcakeDropCounts()
    {
        var counts = new Dictionary<uint, int>();
        foreach (var drop in KingcakeDesynth.Drops)
            counts[drop.ItemId] = GetInventoryItemCount(drop.ItemId);
        return counts;
    }

    private List<uint> GetKingcakeDropInventoryDeltas()
    {
        var drops = new List<uint>();
        foreach (var drop in KingcakeDesynth.Drops)
        {
            var before = _kingcakeDesynthStartDropCounts.GetValueOrDefault(drop.ItemId);
            var after = GetInventoryItemCount(drop.ItemId);
            for (var i = 0; i < Math.Max(0, after - before); i++)
                drops.Add(drop.ItemId);
        }

        return drops;
    }

    private static uint ResolveShopItemId(GcShopBuyEntry entry)
    {
        if (entry.ItemId != 0)
            return entry.ItemId;

        var entryKey = NormalizeShopSearchKey(entry.ItemName);
        if (entryKey.EndsWith('s') && entryKey.Length > 3)
            entryKey = entryKey[..^1];

        foreach (var row in Service.DataManager.GetExcelSheet<Item>())
        {
            var rowName = row.Name.ExtractText();
            var rowKey = NormalizeShopSearchKey(rowName);
            if (rowName.Equals(entry.ItemName, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(entryKey) && entryKey == rowKey))
                return row.RowId;
        }

        return 0;
    }

    private bool BuyAttemptTimedOut() =>
        _buyAttemptSince != null
        && DateTime.UtcNow - _buyAttemptSince.Value > TimeSpan.FromMilliseconds(BuyAttemptTimeoutMs);

    private void ResetBuyAttempt()
    {
        _buyAwaitingConfirm = false;
        _buyAttemptSince = null;
        _buyQtyDialogSent = false;
        _buyLastItemLabel = string.Empty;
    }

    private static void CloseBuyConfirmDialogs()
    {
        foreach (var name in GcBuyDialogAddons)
            CloseAddonSafe(name);
        foreach (var name in SelectYesnoAddons)
            CloseAddonSafe(name);
    }

    private void FinishBuying(bool logDone = true)
    {
        if (logDone)
            Log($"Done buying GC shop list | Seals: {GetCurrentSeals():N0} | Total bought this session: {TotalDuckbones}");

        CloseBuyConfirmDialogs();
        CloseAddonSafe(GcExchangeAddon);
        CloseAddonSafe("SelectString");
        ResetBuyAttempt();
        _buyPhaseSince = null;
        _buyFindItemFailures = 0;
        _gcShopRankSelected = false;
        _gcShopCategorySelected = false;
        _buyListIndex = 0;
        _pendingHandinRow = -1;

        if (_shopTestMode)
        {
            FinishShopTest();
            return;
        }

        GotoState(FarmState.CheckGcLoop);
    }

    private static string? GetVisibleBuyDialogName()
    {
        foreach (var name in GcBuyDialogAddons)
        {
            if (IsAddonVisible(name))
                return name;
        }
        return null;
    }

    private static void ResolveGcExchangeTabs(
        int categoryTab,
        int rankTab,
        string itemName,
        uint itemId,
        out int rankCallback,
        out int categoryCallback,
        out int uiRankTab,
        out int uiCategoryTab,
        out bool sheetResolved)
    {
        rankCallback = rankTab;
        categoryCallback = categoryTab;
        uiRankTab = rankTab;
        uiCategoryTab = categoryTab;
        sheetResolved = GcExchangeItemResolver.TryResolve(itemName, itemId, 0, out var shopInfo);

        if (sheetResolved)
        {
            rankCallback = shopInfo.RankCallback;
            categoryCallback = shopInfo.CategoryCallback;
            uiRankTab = shopInfo.RankCallback;
            uiCategoryTab = shopInfo.UiCategoryTab;
            Log($"GC exchange tabs from sheet — rank {uiRankTab}, UI category {uiCategoryTab} (callbacks rank {rankCallback}, category {categoryCallback})");
        }
        else
            Log($"GC exchange tabs from config — rank {rankTab}, category UI tab {categoryTab} (sheet lookup missed '{itemName}')");
    }

    private static unsafe void SelectGcExchangeRankOnly(int rankCallback, int uiRankTab)
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>(GcExchangeAddon);
        if (addon == null || !addon->IsVisible)
            return;

        SelectGcExchangeRank(addon, uiRankTab);
        SendCallback(GcExchangeAddon, true, 1, rankCallback);
    }

    private static unsafe void SelectGcExchangeCategoryOnly(int categoryCallback, int uiCategoryTab, bool sheetResolved)
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>(GcExchangeAddon);
        if (addon == null || !addon->IsVisible)
            return;

        SelectGcExchangeCategory(addon, uiCategoryTab, categoryCallback, sendCategoryCallback: sheetResolved);
    }

    private static bool ExchangeRowLabelMatches(string searchName, string label) =>
        ShopItemNamesMatch(searchName, label);

    private static bool ShopItemNamesMatch(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
            return false;

        if (expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
            return true;

        if (ExchangeLabelMatches(actual, expected) || ExchangeLabelMatches(expected, actual))
            return true;

        var a = NormalizeShopSearchKey(expected);
        var b = NormalizeShopSearchKey(actual);
        if (a.Length >= 3 && b.Length >= 3 && (a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)))
            return true;

        return ContainsAllWordTokens(expected, "duck", "bone")
               && ContainsAllWordTokens(actual, "duck", "bone");
    }

    private static string NormalizeShopSearchKey(string name) =>
        new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool ContainsAllWordTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        return true;
    }

    private static IEnumerable<string> ExpandShopSearchNames(string searchName, uint itemId)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in EnumerateShopSearchNames(searchName, itemId))
        {
            if (seen.Add(name))
                yield return name;
        }
    }

    private static IEnumerable<string> EnumerateShopSearchNames(string searchName, uint itemId)
    {
        if (!string.IsNullOrWhiteSpace(searchName))
            yield return searchName.Trim();

        if (itemId != 0)
        {
            var item = Service.DataManager.GetExcelSheet<Item>()?.GetRow(itemId);
            if (item != null)
            {
                var luminaName = item.Value.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(luminaName))
                    yield return luminaName;
            }
        }

        if (itemId == GcShopDefaults.DuckboneItemId)
        {
            yield return GcShopDefaults.DuckboneItemName;
            yield return "Duckbone";
        }
    }

    private static bool ExchangeLabelMatches(string label, string searchName)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(searchName))
            return false;

        if (label.Equals(searchName, StringComparison.OrdinalIgnoreCase))
            return true;

        var idx = label.LastIndexOf(searchName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;

        var suffix = label[(idx + searchName.Length)..].TrimEnd('\0');
        if (!string.IsNullOrEmpty(suffix) && suffix.Any(char.IsLetterOrDigit))
            return false;

        var prefix = label[..idx];
        var prefixLetters = new string(prefix.Where(char.IsLetter).ToArray());
        return prefixLetters.Length <= 1;
    }

    private static unsafe string ReadExchangeListLabel(AtkComponentList* list, int index)
    {
        if (list == null || index < 0 || index >= list->ListLength)
            return string.Empty;

        var raw = list->GetItemLabel(index).ToString() ?? string.Empty;
        var nul = raw.IndexOf('\0');
        if (nul >= 0)
            raw = raw[..nul];

        return NormalizeExchangeListLabel(raw);
    }

    private static unsafe string ReadExchangeRowLabel(AtkUnitBase* addon, AtkComponentList* list, int row)
    {
        var fromList = ReadExchangeListLabel(list, row);
        if (IsReadableExchangeLabel(fromList))
            return fromList;

        var fromAtk = ReadExchangeRowNameFromAtk(addon, row);
        return IsReadableExchangeLabel(fromAtk) ? fromAtk : fromList.Length >= fromAtk.Length ? fromList : fromAtk;
    }

    private static unsafe string ReadExchangeRowNameFromAtk(AtkUnitBase* addon, int row)
    {
        var offset = 17 + row;
        if (addon == null || offset >= addon->AtkValuesCount)
            return string.Empty;

        return ReadAtkValueDisplayString(ref addon->AtkValues[offset]);
    }

    private static bool IsReadableExchangeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return false;

        var readable = label.Count(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c is '\'' or '-' or '.' or '’');
        return readable >= Math.Min(4, label.Length) && label.Any(char.IsLetter);
    }

    private static unsafe string ReadAtkValueDisplayString(ref AtkValue value)
    {
        try
        {
            var type = value.Type & (AtkValueType)0xF;
            var text = type switch
            {
                AtkValueType.String or AtkValueType.ManagedString =>
                    value.GetValueAsString() ?? string.Empty,
                AtkValueType.WideString when value.WideString != null =>
                    Marshal.PtrToStringUni((nint)value.WideString) ?? string.Empty,
                _ => IsAtkStringType(value.Type) ? value.GetValueAsString() ?? string.Empty : string.Empty,
            };

            return NormalizeExchangeListLabel(text);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeExchangeListLabel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var bestStart = 0;
        var bestLen = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (!char.IsLetter(raw[i]))
                continue;

            var len = 0;
            while (i + len < raw.Length && IsExchangeItemNameChar(raw[i + len]))
                len++;

            if (len > bestLen)
            {
                bestLen = len;
                bestStart = i;
            }
        }

        return bestLen >= 4 ? raw[bestStart..].Trim() : raw.Trim();
    }

    private static bool IsExchangeItemNameChar(char c) =>
        char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c is '\'' or '-' or '.' or '’';

    private static unsafe void SelectGcExchangeRank(AtkUnitBase* addon, int rankTab)
    {
        rankTab = Math.Clamp(rankTab, 0, GcExchangeRankRadioNodes.Length - 1);

        for (var i = 0; i < GcExchangeRankRadioNodes.Length; i++)
        {
            var node = addon->GetNodeById(GcExchangeRankRadioNodes[i]);
            if (node == null) continue;

            var radio = node->GetAsAtkComponentRadioButton();
            if (radio == null) continue;

            var selected = i == rankTab;
            radio->IsSelected = selected;
            radio->IsChecked = selected;
            if (selected)
                ClickGcExchangeRadioButton(radio, addon, i);
        }
    }

    private static unsafe void SelectGcExchangeCategory(AtkUnitBase* addon, int categoryTab, int categoryCallback, bool sendCategoryCallback)
    {
        categoryTab = Math.Clamp(categoryTab, 0, GcExchangeCategoryRadioNodes.Length - 1);

        if (sendCategoryCallback)
            SendCallback(GcExchangeAddon, true, 2, categoryCallback);

        for (var i = 0; i < GcExchangeCategoryRadioNodes.Length; i++)
        {
            var node = addon->GetNodeById(GcExchangeCategoryRadioNodes[i]);
            if (node == null) continue;

            var radio = node->GetAsAtkComponentRadioButton();
            if (radio == null) continue;

            var selected = i == categoryTab;
            radio->IsSelected = selected;
            radio->IsChecked = selected;
            if (selected)
                ClickGcExchangeRadioButton(radio, addon, i);
        }
    }

    private static unsafe void ClickGcExchangeRadioButton(AtkComponentRadioButton* radio, AtkUnitBase* addon, int eventParam = 0)
    {
        var component = (AtkComponentBase*)radio;
        var evt = stackalloc AtkEvent[1];
        var evtData = stackalloc AtkEventData[1];
        *evt = default;
        *evtData = default;

        evt->Node = (AtkResNode*)component->OwnerNode;
        evt->Target = (AtkEventTarget*)addon;
        evt->Listener = (AtkEventListener*)component;
        component->ReceiveEvent(AtkEventType.ButtonClick, eventParam, evt, evtData);
    }

    private static unsafe AtkComponentList* GetGcExchangeList(AtkUnitBase* addon)
    {
        var node = addon->GetNodeById(GcExchangeListNodeId);
        return node == null ? null : node->GetAsAtkComponentList();
    }

    private static unsafe void HighlightGcExchangeRow(int row)
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>(GcExchangeAddon);
        if (addon == null || !addon->IsVisible) return;

        var list = GetGcExchangeList(addon);
        if (list != null && row >= 0)
        {
            list->ScrollToItem((short)row);
            list->UpdateListItems();
            if (row < list->ListLength)
            {
                list->SelectItem(row, dispatchEvent: false);
                list->SetItemHighlightedState(row, highlighted: true, triggerUpdate: true);
            }
        }

        SendCallback(GcExchangeAddon, true, 0, row);
    }

    private static unsafe bool TryOpenGcExchangeBuyDialog(int row, int qty)
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>(GcExchangeAddon);
        if (addon == null || !addon->IsVisible || row < 0)
            return false;

        var list = GetGcExchangeList(addon);
        if (list == null)
            return false;

        list->ScrollToItem((short)row);
        list->UpdateListItems();
        list->SelectItem(row, dispatchEvent: false);
        list->SetItemHighlightedState(row, highlighted: true, triggerUpdate: true);
        list->SelectItem(row, dispatchEvent: true);
        SendCallback(GcExchangeAddon, false, 0, row, Math.Max(1, qty), 0, true, false);
        return true;
    }

    private static unsafe (int Row, string ItemLabel, string Source) FindGcExchangeItemRow(
        string searchName, uint itemId, int expectedSealCost, int sheetListRow, int fallbackRow)
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>(GcExchangeAddon);
        if (addon == null || !addon->IsVisible)
            return (-1, searchName, string.Empty);

        var list = GetGcExchangeList(addon);
        if (list == null || list->ListLength <= 0)
            return (-1, searchName, string.Empty);

        var listLength = list->ListLength;
        var atkItemCount = addon->AtkValuesCount > 1 ? (int)Math.Min(addon->AtkValues[1].UInt, 100u) : 0;
        var rowCount = Math.Max(listLength, atkItemCount);
        var searchNames = ExpandShopSearchNames(searchName, itemId).ToList();

        foreach (var candidate in searchNames)
        {
            for (var i = 0; i < atkItemCount; i++)
            {
                var label = ReadExchangeRowNameFromAtk(addon, i);
                if (ShopItemNamesMatch(candidate, label))
                    return FinalizeExchangeRowMatch(addon, list, i, label, "atk name");
            }
        }

        foreach (var candidate in searchNames)
        {
            for (var i = 0; i < rowCount; i++)
            {
                var label = ReadExchangeRowLabelAfterScroll(addon, list, i);
                if (ShopItemNamesMatch(candidate, label))
                    return FinalizeExchangeRowMatch(addon, list, i, label, "list scroll");
            }
        }

        var atkMatch = FindExchangeRowInAtkStrings(addon, searchNames, rowCount);
        if (atkMatch.Row >= 0)
            return FinalizeExchangeRowMatch(addon, list, atkMatch.Row, atkMatch.ItemLabel, atkMatch.Source);

        foreach (var candidate in searchNames)
        {
            if (sheetListRow >= 0 && sheetListRow < rowCount)
            {
                var sheetLabel = ReadExchangeRowLabelAfterScroll(addon, list, sheetListRow);
                if (ShopItemNamesMatch(candidate, sheetLabel))
                    return FinalizeExchangeRowMatch(addon, list, sheetListRow, sheetLabel, "sheet row");
            }

            if (fallbackRow >= 0 && fallbackRow < rowCount)
            {
                var fallbackLabel = ReadExchangeRowLabelAfterScroll(addon, list, fallbackRow);
                if (ShopItemNamesMatch(candidate, fallbackLabel))
                    return FinalizeExchangeRowMatch(addon, list, fallbackRow, fallbackLabel, "manual row");
            }
        }

        if (expectedSealCost > 0)
        {
            var probeRow = sheetListRow >= 0 ? sheetListRow : fallbackRow;
            var probe = probeRow >= 0 && probeRow < rowCount
                ? ReadExchangeRowLabelAfterScroll(addon, list, probeRow)
                : string.Empty;
            var tailStart = Math.Max(0, listLength - 3);
            var tail = BuildExchangeListSample(addon, list, tailStart, listLength);
            var sample = BuildExchangeListSample(addon, list, 0, Math.Min(listLength, 8));
            Log($"WARN: No list row matched '{searchName}' ({expectedSealCost} seals) — sheet row {sheetListRow} shows '{probe}', list length {listLength}. Head: {sample} | Tail: {tail}");
        }

        return (-1, searchName, string.Empty);
    }

    private static unsafe (int Row, string ItemLabel, string Source) FinalizeExchangeRowMatch(
        AtkUnitBase* addon, AtkComponentList* list, int row, string matchedLabel, string source)
    {
        var scrolledLabel = ReadExchangeRowLabelAfterScroll(addon, list, row);
        var itemLabel = IsReadableExchangeLabel(scrolledLabel) ? scrolledLabel : matchedLabel;
        return (row, itemLabel, source);
    }

    private static unsafe (int Row, string ItemLabel, string Source) FindExchangeRowInAtkStrings(
        AtkUnitBase* addon, IReadOnlyList<string> searchNames, int rowCount)
    {
        var valueCount = addon->AtkValuesCount;
        for (var offset = 17; offset < valueCount; offset++)
        {
            ref var val = ref addon->AtkValues[offset];
            if (val.Type is AtkValueType.Undefined or AtkValueType.Null)
                continue;

            var label = ReadAtkValueDisplayString(ref val);
            if (string.IsNullOrWhiteSpace(label))
                continue;

            foreach (var candidate in searchNames)
            {
                if (!ShopItemNamesMatch(candidate, label))
                    continue;

                var row = offset - 17;
                if (row >= 0 && row < rowCount)
                    return (row, label, "atk scan");

                break;
            }
        }

        return (-1, string.Empty, string.Empty);
    }

    private static unsafe string ReadExchangeRowLabelAfterScroll(AtkUnitBase* addon, AtkComponentList* list, int row)
    {
        if (list != null && row >= 0)
        {
            list->ScrollToItem((short)row);
            list->UpdateListItems();
        }

        return ReadExchangeRowLabel(addon, list, row);
    }

    private static unsafe string BuildExchangeListSample(AtkUnitBase* addon, AtkComponentList* list, int start, int end)
    {
        var parts = new List<string>(Math.Max(0, end - start));
        for (var i = start; i < end; i++)
        {
            var label = ReadExchangeRowLabelAfterScroll(addon, list, i);
            parts.Add(string.IsNullOrWhiteSpace(label) ? $"[{i}]?" : $"[{i}]{label}");
        }

        return string.Join("; ", parts);
    }

    private static unsafe bool IsSelectYesnoOpen()
    {
        foreach (var name in SelectYesnoAddons)
        {
            if (IsAddonOpen(name))
                return true;
        }
        return false;
    }

    private static unsafe bool IsSelectYesnoVisible()
    {
        foreach (var name in SelectYesnoAddons)
        {
            if (IsAddonVisible(name))
                return true;
        }
        return false;
    }

    private static unsafe void ClickSelectYesno(bool forceEnableYes = false)
    {
        foreach (var name in SelectYesnoAddons)
        {
            var addon = Service.GameGui.GetAddonByName<AddonSelectYesno>(name);
            if (addon == null || !addon->IsVisible || !addon->IsReady)
                continue;

            var atk = (AtkUnitBase*)addon;
            var yes = addon->YesButton;
            if (yes != null)
            {
                if (forceEnableYes && !yes->IsEnabled)
                    ForceEnableComponentButton(yes);

                ClickAddonButtonViaReceiveEvent(yes, atk);
            }

            SendCallback(name, true, 0);
            return;
        }
    }

    private static unsafe void ForceEnableComponentButton(AtkComponentButton* button)
    {
        var ownerNode = button->AtkComponentBase.OwnerNode;
        if (ownerNode == null)
            return;

        var flagsPtr = (ushort*)&ownerNode->AtkResNode.NodeFlags;
        *flagsPtr &= unchecked((ushort)~(1 << 5));
    }

    private static unsafe void ClickComponentButton(AtkComponentButton* button, AtkUnitBase* addon)
    {
        var component = (AtkComponentBase*)button;
        var evt = stackalloc AtkEvent[1];
        var evtData = stackalloc AtkEventData[1];
        *evt = default;
        *evtData = default;

        evt->Node = (AtkResNode*)component->OwnerNode;
        evt->Target = (AtkEventTarget*)addon;
        evt->Listener = (AtkEventListener*)component;
        component->ReceiveEvent(AtkEventType.ButtonClick, 0, evt, evtData);
    }

    /// <summary>AutoDuty/ECommons-style button click — routes through the addon's native ReceiveEvent.</summary>
    private static unsafe void ClickAddonButtonViaReceiveEvent(AtkComponentButton* button, AtkUnitBase* addon)
    {
        if (button == null || addon == null)
            return;

        var ownerNode = button->AtkComponentBase.OwnerNode;
        if (ownerNode == null)
        {
            ClickComponentButton(button, addon);
            return;
        }

        var btnRes = ownerNode->AtkResNode;
        if (btnRes.AtkEventManager.Event == null)
        {
            ClickComponentButton(button, addon);
            return;
        }

        var evt = (AtkEvent*)btnRes.AtkEventManager.Event;
        addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, btnRes.AtkEventManager.Event);
    }

    private static Task<int> GetCurrentSealsAsync() =>
        Service.Framework.RunOnFrameworkThread(GetCurrentSeals);

    private static Task<bool> CanAffordDuckboneAsync() =>
        Service.Framework.RunOnFrameworkThread(CanAffordDuckbone);

    private Task StatusAsync(string msg) =>
        Service.Framework.RunOnFrameworkThread(() => Status(msg));

    private Task LogAsync(string msg) =>
        IsRunning
            ? Service.Framework.RunOnFrameworkThread(() => Log(msg))
            : Task.CompletedTask;

    private Task SetErrorAsync(string msg) =>
        Service.Framework.RunOnFrameworkThread(() => SetError(msg));

    private Task GotoStateAsync(FarmState next) =>
        Service.Framework.RunOnFrameworkThread(() => GotoState(next));

    // ── Game helpers ──────────────────────────────────────────

    private static bool IsInGcCityZone(int gcIdx, uint zoneId)
    {
        foreach (var id in GcZoneIds[gcIdx])
        {
            if (zoneId == id)
                return true;
        }
        return false;
    }

    private static bool IsInOfficerZone(int gcIdx, uint zoneId) => zoneId == GcOfficerZoneId[gcIdx];

    /// <summary>
    /// Dalamud 15.0.2+: do not use IsClientIdle; gate on login and condition flags instead.
    /// </summary>
    private static bool CanRunWorldAutomation()
    {
        if (!Service.ClientState.IsLoggedIn)
            return false;

        if (IsBetweenAreas()
            || Service.Condition[ConditionFlag.Occupied]
            || Service.Condition[ConditionFlag.OccupiedInCutSceneEvent]
            || Service.Condition[ConditionFlag.Casting])
            return false;

        var player = Service.ObjectTable.LocalPlayer;
        return player != null && !player.IsDead;
    }

    private static bool CanExecuteGcTeleport()
    {
        if (InDuty() || !CanRunWorldAutomation())
            return false;

        return true;
    }

    private static bool IsAtkStringType(AtkValueType type) =>
        type is AtkValueType.String or AtkValueType.ManagedString
        || type.ToString() is "ConstString" or "String8";

    private static string ReadAtkValueString(ref AtkValue value) =>
        ReadAtkValueDisplayString(ref value);

    private static void SendLifestreamChat(string command, bool useTpPrefix)
    {
        var liCommand = useTpPrefix ? $"/li tp {command}" : $"/li {command}";
        SendChatCommand(liCommand);
    }

    private static unsafe void SendChatCommand(string command)
    {
        var uiModule = UIModule.Instance();
        if (uiModule == null)
        {
            Service.PluginLog.Error("[SealBreaker] UIModule unavailable — cannot send chat command");
            return;
        }

        var message = stackalloc Utf8String[1];
        message->SetString(command);
        uiModule->ProcessChatBoxEntry(message, nint.Zero, false);
        Log($"Sent chat command: {command}");
    }

    private static bool InDuty()        => Service.Condition[ConditionFlag.BoundByDuty];
    private static bool IsBetweenAreas() => Service.Condition[ConditionFlag.BetweenAreas];

    private static Vector3? PlayerPos()
    {
        var player = Service.ObjectTable.LocalPlayer;
        return player?.Position;
    }

    private static bool ShouldEnterGcSpendLoop()
    {
        var cfg = Plugin.Config;
        var seals = GetCurrentSeals();
        return ShouldSpendSealsOnShop() || seals > cfg.SealReserve;
    }

    public static int GetCurrentSeals()
    {
        unsafe
        {
            var id = Plugin.Config.GrandCompanyIndex switch { 0 => 20u, 1 => 21u, 2 => 22u, _ => 20u };
            return (int)InventoryManager.Instance()->GetInventoryItemCount(id);
        }
    }

    public static int GetDuckBoneInventoryCount() =>
        GetInventoryItemCount(ResolveDuckBoneItemId());

    public static int GetInventoryItemCountPublic(GcShopBuyEntry entry) =>
        GetInventoryItemCount(ResolveShopItemId(entry));

    private static uint _duckBoneItemId;

    private static uint ResolveDuckBoneItemId()
    {
        if (_duckBoneItemId != 0)
            return _duckBoneItemId;

        foreach (var row in Service.DataManager.GetExcelSheet<Item>())
        {
            var name = row.Name.ExtractText();
            if (name.Equals(GcShopDefaults.DuckboneItemName, StringComparison.OrdinalIgnoreCase)
                || name.Contains("duck bone", StringComparison.OrdinalIgnoreCase))
            {
                _duckBoneItemId = row.RowId;
                break;
            }
        }

        if (_duckBoneItemId == 0)
            _duckBoneItemId = GcShopDefaults.DuckboneItemId;

        return _duckBoneItemId;
    }

    private static bool ShouldTurnIn(uint itemId)
    {
        var cfg = Plugin.Config;
        if (cfg.ListMode == 0) return true;
        var inList = cfg.FilteredItemIds.Contains(itemId);
        return cfg.ListMode switch { 1 => !inList, 2 => inList, _ => true };
    }

    private static async Task<bool> WaitForNpcMenuAsync(FarmState nextState, int timeoutMs)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < deadline)
        {
            var ready = await Service.Framework.RunOnFrameworkThread(() => nextState switch
            {
                FarmState.OpenExpertDelivery => IsAddonVisible("SelectString"),
                FarmState.OpenGCShop => IsAddonVisible(GcExchangeAddon) || IsAddonVisible("SelectString"),
                FarmState.OpenRepairMenu => IsAddonVisible("SelectString") || IsAddonVisible("Repair"),
                _ => false,
            });
            if (ready) return true;
            await Task.Delay(100);
        }
        return false;
    }

    private static async Task<bool> WaitForExpertDeliveryTabAsync(int timeoutMs)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < deadline)
        {
            if (await Service.Framework.RunOnFrameworkThread(IsExpertDeliveryTabActive))
                return true;
            await Task.Delay(100);
        }
        return false;
    }

    private static unsafe bool IsExpertDeliveryTabActive()
    {
        var addon = Service.GameGui.GetAddonByName<AddonGrandCompanySupplyList>("GrandCompanySupplyList");
        return addon != null && addon->IsVisible && addon->SelectedTab == GcExpertDeliveryTab;
    }

    private static unsafe void SelectExpertDeliveryTab()
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>("GrandCompanySupplyList");
        if (addon == null || !addon->IsVisible)
            return;

        var node = addon->GetNodeById((uint)(11 + GcExpertDeliveryTab));
        if (node == null)
            return;

        var radio = node->GetAsAtkComponentRadioButton();
        if (radio == null)
            return;

        ClickGcExchangeRadioButton(radio, addon, GcExpertDeliveryTab);
    }

    private static unsafe void CloseDeliveryUi() => CloseExpertDeliveryUiForce();

    private static unsafe void CloseExpertDeliveryUiForce()
    {
        try
        {
            CloseStuckConfirmDialogs();
            TryDismissSupplyRewardDialog();
            TryCloseAddonThoroughly("GrandCompanySupplyList");
            TryHideGrandCompanySupplyAgent();
            TryDismissGcOfficerMenu();

            if (IsAddonVisible("SelectString"))
                TryCloseAddonThoroughly("SelectString");

            TryClearAgentContextMenu();
            Service.TargetManager.Target = null;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "[SealBreaker] CloseExpertDeliveryUiForce failed");
        }
    }

    private static unsafe void TryCloseSupplyListForDismiss()
    {
        var addon = Service.GameGui.GetAddonByName<AddonGrandCompanySupplyList>("GrandCompanySupplyList");
        if (addon == null || !addon->IsVisible)
            return;

        var atk = (AtkUnitBase*)addon;
        TryClickAddonCloseButton(atk);
        addon->FireCloseCallback();
        addon->Close(true);
        addon->Hide(true, true, 0);
        TryCloseAddonViaAtkModule(atk);
    }

    private static unsafe void TryClearAgentContextMenu()
    {
        var agentModule = AgentModule.Instance();
        if (agentModule == null)
            return;

        var agent = (AgentContext*)agentModule->GetAgentByInternalId(AgentId.Context);
        if (agent != null)
            agent->ClearMenu();
    }

    private static unsafe void TryHideGrandCompanySupplyAgent()
    {
        var agent = AgentGrandCompanySupply.Instance();
        if (agent != null)
        {
            agent->HideAddon();
            agent->Hide();
        }

        var agentModule = AgentModule.Instance();
        if (agentModule != null)
        {
            var agentIf = agentModule->GetAgentByInternalId(AgentId.GrandCompanySupply);
            if (agentIf != null)
            {
                agentIf->HideAddon();
                agentIf->Hide();
            }
        }
    }

    private static unsafe void TryDismissSupplyRewardDialog()
    {
        var addon = Service.GameGui.GetAddonByName<AddonGrandCompanySupplyReward>("GrandCompanySupplyReward");
        if (addon == null || !addon->IsVisible)
            return;

        var atkAddon = (AtkUnitBase*)addon;
        if (addon->CancelButton != null && addon->CancelButton->IsEnabled)
        {
            ClickComponentButton(addon->CancelButton, atkAddon);
            SendCallback("GrandCompanySupplyReward", true, 1);
        }

        addon->FireCloseCallback();
        addon->Close(true);
        addon->Hide(true, true, 0);
        TryCloseAddonViaAtkModule(atkAddon);
    }

    private static unsafe void TryDismissGcOfficerMenu(bool forceLog = false)
    {
        if (IsAddonVisible("GrandCompanySupplyReward"))
            TryDismissSupplyRewardDialog();

        if (IsAddonVisible("GrandCompanySupplyList"))
            TryCloseSupplyListForDismiss();

        var addon = Service.GameGui.GetAddonByName<AddonSelectString>("SelectString");
        if (addon == null || !addon->IsVisible)
            return;

        var index = ResolveSelectStringDismissOption(addon);
        var shouldLog = forceLog || _gcOfficerDismissLogCounter++ % 8 == 0;
        TrySelectStringPopupOption(addon, index, logOptions: shouldLog);
        TryClearAgentContextMenu();

        if (!IsAddonVisible("SelectString"))
            return;

        CloseAddonSafe("SelectString");

        if (!IsAddonVisible("SelectString"))
            return;

        var atk = (AtkUnitBase*)addon;
        atk->FireCloseCallback();
        TryCloseAddonViaAtkModule(atk);
    }

    private static unsafe void TryCloseAddonThoroughly(string name)
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>(name);
        if (addon == null || !addon->IsVisible)
            return;

        TryClickAddonCloseButton(addon);

        switch (name)
        {
            case "GrandCompanySupplyList":
            {
                var typed = (AddonGrandCompanySupplyList*)addon;
                typed->FireCloseCallback();
                typed->Close(true);
                typed->Hide(true, true, 0);
                break;
            }
            case "GrandCompanySupplyReward":
            {
                var typed = (AddonGrandCompanySupplyReward*)addon;
                typed->FireCloseCallback();
                typed->Close(true);
                typed->Hide(true, true, 0);
                break;
            }
            default:
                addon->Close(true);
                addon->Hide(true, true, 0);
                break;
        }

        TryCloseAddonViaAtkModule(addon);
        CloseAddonSafe(name);
    }

    private static unsafe void TryClickAddonCloseButton(AtkUnitBase* addon)
    {
        foreach (var nodeId in new uint[] { 2u, 3u, 41u, 42u })
        {
            var button = addon->GetComponentButtonById(nodeId);
            if (button == null || !button->IsEnabled)
                continue;

            ClickComponentButton(button, addon);
            return;
        }
    }

    private static unsafe void TryCloseAddonViaAtkModule(AtkUnitBase* addon)
    {
        if (addon == null || !addon->IsVisible)
            return;

        var uiModule = UIModule.Instance();
        if (uiModule == null)
            return;

        var atkModule = (AtkModule*)uiModule->GetRaptureAtkModule();
        if (atkModule != null)
            atkModule->CloseAddon(addon->Id);
    }

    private static unsafe void TryCloseSupplyRewardAddon()
    {
        TryDismissSupplyRewardDialog();
    }

    private static unsafe void TryCloseSupplyListAddon()
    {
        TryCloseAddonThoroughly("GrandCompanySupplyList");
    }

    private static unsafe bool IsSupplyRewardVisible()
    {
        var addon = Service.GameGui.GetAddonByName<AddonGrandCompanySupplyReward>("GrandCompanySupplyReward");
        return addon != null && addon->IsVisible;
    }

    private static unsafe bool TryConfirmSupplyReward()
    {
        var addon = Service.GameGui.GetAddonByName<AddonGrandCompanySupplyReward>("GrandCompanySupplyReward");
        if (addon == null || !addon->IsVisible)
            return false;

        var button = addon->GetComponentButtonById(38);
        if (button == null)
            button = addon->DeliverButton;
        if (button == null || !button->IsEnabled)
            return false;

        var atkAddon = (AtkUnitBase*)addon;
        ClickComponentButton(button, atkAddon);
        SendCallback("GrandCompanySupplyReward", true, 0);
        return true;
    }

    private static unsafe bool IsSupplyListReady()
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>("GrandCompanySupplyList");
        if (addon == null || !addon->IsVisible || !addon->IsReady)
            return false;

        if (addon->AtkValuesCount < 7)
            return false;

        var loading = addon->AtkValues[0].UInt;
        if (loading != 2)
            return false;

        var listNode = addon->GetNodeById(24);
        return listNode != null && listNode->IsVisible();
    }

    private static unsafe int GetSupplyListItemCount()
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>("GrandCompanySupplyList");
        if (addon == null || !addon->IsVisible || addon->AtkValuesCount < 7)
            return 0;

        return (int)addon->AtkValues[6].UInt;
    }

    private static unsafe List<(uint ItemID, uint Seals)> GetHandinItems()
    {
        var ret = new List<(uint ItemID, uint Seals)>();
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>("GrandCompanySupplyList");
        if (addon == null || !IsSupplyListReady())
            return ret;

        var count = GetSupplyListItemCount();
        if (count <= 0)
            return ret;

        var ptr = (GCExpertEntry*)*(nint*)((nint)addon + 648);
        for (var i = 0; i < count; i++)
        {
            var entry = ptr[i];
            if (entry.ItemID == 0)
                continue;
            ret.Add((entry.ItemID, entry.Seals));
        }

        return ret;
    }

    private static unsafe (int Row, uint ItemID, uint Seals)? FindNextHandinRow()
    {
        var cfg = Plugin.Config;
        var sealsRemaining = cfg.SealCap - GetCurrentSeals();
        var items = GetHandinItems();
        if (items.Count == 0)
            return null;

        for (var i = 0; i < items.Count; i++)
        {
            var (itemId, seals) = items[i];
            if (!ShouldTurnIn(itemId))
                continue;
            if (!HasItemInInventory(itemId))
                continue;
            if (sealsRemaining <= (int)seals)
                continue;

            return (i, itemId, seals);
        }

        return null;
    }

    private static unsafe bool IsDeliveryBlockedByCap()
    {
        var cfg = Plugin.Config;
        var sealsRemaining = cfg.SealCap - GetCurrentSeals();
        var items = GetHandinItems();
        if (items.Count == 0)
            return false;

        var hasOwned = false;
        foreach (var (itemId, seals) in items)
        {
            if (!ShouldTurnIn(itemId) || !HasItemInInventory(itemId))
                continue;

            hasOwned = true;
            if (sealsRemaining > seals)
                return false;
        }

        return hasOwned;
    }

    private static unsafe bool HasItemInInventory(uint itemId)
    {
        var inv = InventoryManager.Instance();
        return inv->GetInventoryItemCount(itemId) + inv->GetInventoryItemCount(itemId, true) > 0;
    }

    private static unsafe void InvokeSupplyHandin(int row)
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>("GrandCompanySupplyList");
        if (addon == null || !addon->IsVisible)
            return;

        SendCallback("GrandCompanySupplyList", true, 1, row, 0);
    }

    private static async Task<bool> WaitForAddonVisibleAsync(string name, int timeoutMs)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < deadline)
        {
            if (await Service.Framework.RunOnFrameworkThread(() => IsAddonVisible(name)))
                return true;
            await Task.Delay(100);
        }
        return false;
    }

    private static async Task<bool> WaitForAddonAsync(string name, int timeoutMs)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < deadline)
        {
            if (await Service.Framework.RunOnFrameworkThread(() => IsAddonOpen(name)))
                return true;
            await Task.Delay(100);
        }
        return false;
    }

    private static unsafe bool IsAddonVisible(string name)
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>(name);
        return addon != null && addon->IsVisible;
    }

    private static unsafe bool IsAddonOpen(string name)
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>(name);
        return addon != null && addon->IsVisible && addon->IsReady;
    }

    private static unsafe void SendCallback(string name, bool update, params object[] args)
    {
        var addon = Service.GameGui.GetAddonByName<AtkUnitBase>(name);
        if (addon == null || !addon->IsVisible) return;

        var values = new AtkValue[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            values[i] = args[i] switch
            {
                int n  => new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int,  Int  = n },
                uint n => new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt, UInt = n },
                bool b => new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Bool, Byte = (byte)(b ? 1 : 0) },
                _      => new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int,  Int  = 0 },
            };
        }
        fixed (AtkValue* ptr = values)
            addon->FireCallback((uint)args.Length, ptr, update);
    }

    private static unsafe void CloseAddonSafe(string name)
    {
        try
        {
            var addon = Service.GameGui.GetAddonByName<AtkUnitBase>(name);
            if (addon == null || !addon->IsVisible)
                return;

            var cancel = new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int, Int = -1 };
            addon->FireCallback(1, &cancel, true);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, $"[SealBreaker] CloseAddonSafe({name}) failed");
        }
    }

    private static IGameObject? FindNpcByName(string name, Vector3? nearHint = null, float maxDistFromHint = 50f)
    {
        var player = Service.ObjectTable.LocalPlayer;
        var playerPos = player?.Position;

        var matches = Service.ObjectTable
            .Where(o => !string.IsNullOrWhiteSpace(o.Name.TextValue)
                && o.Name.TextValue.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            return null;

        if (playerPos.HasValue
            && Service.ClientState.TerritoryType == MaelstromRepairZone
            && IsOnMaelstromGcWalkway(playerPos.Value)
            && nearHint.HasValue)
        {
            var hint = ToWalkwayHint(nearHint.Value);
            var valid = matches
                .Where(o => NpcPlanarDistance(o.Position, hint) <= GcNpcHintMaxPlanarDist)
                .OrderBy(o => NpcPlanarDistance(playerPos.Value, o.Position))
                .FirstOrDefault();
            if (valid != null)
                return valid;

            return null;
        }

        if (nearHint.HasValue)
        {
            var nearHintMatch = matches
                .Where(o => Vector3.Distance(o.Position, nearHint.Value) <= maxDistFromHint
                            || (playerPos.HasValue
                                && IsOnMaelstromGcWalkway(playerPos.Value)
                                && NpcPlanarDistance(o.Position, nearHint.Value) <= maxDistFromHint))
                .OrderBy(o => playerPos.HasValue
                    ? Vector3.Distance(playerPos.Value, o.Position)
                    : Vector3.Distance(o.Position, nearHint.Value))
                .FirstOrDefault();
            if (nearHintMatch != null)
                return nearHintMatch;
        }

        if (playerPos.HasValue)
        {
            var nearPlayer = matches
                .Where(o => Vector3.Distance(o.Position, playerPos.Value) <= 15f
                            || NpcPlanarDistance(o.Position, playerPos.Value) <= 15f)
                .OrderBy(o => Vector3.Distance(o.Position, playerPos.Value))
                .FirstOrDefault();
            if (nearPlayer != null)
                return nearPlayer;
        }

        return null;
    }

    private static void TargetAndInteract(string npcName, Vector3? nearHint = null)
    {
        var obj = FindNpcByName(npcName, nearHint);
        if (obj == null)
        {
            SendChatCommand($"/target \"{npcName}\"");
            obj = Service.TargetManager.Target;
            if (obj == null || !obj.Name.TextValue.Equals(npcName, StringComparison.OrdinalIgnoreCase))
            {
                Service.PluginLog.Warning($"[SealBreaker] TargetAndInteract: {npcName} not found");
                return;
            }
            Log($"Targeted {npcName} via /target");
        }

        TargetAndInteract(obj);
    }

    private static void TargetAndInteract(IGameObject obj)
    {
        SetTargetOnce(obj);
        Interact(obj);
    }

    private static void SetTargetOnce(IGameObject obj)
    {
        if (Service.TargetManager.Target?.GameObjectId != obj.GameObjectId)
            Service.TargetManager.Target = obj;
    }

    private static void Interact(IGameObject obj)
    {
        unsafe
        {
            var go = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
            if (go == null) return;
            FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance()->InteractWithObject(go);
        }
    }

    private void GotoState(FarmState next)
    {
        if (next == State)
            return;

        var zone = Service.ClientState.TerritoryType;
        Log($"State: {State} -> {next} (zone {zone})");
        State = next;
        _lastStatusLogMessage = null;
    }

    /// <summary>Updates UI status without writing to log/chat.</summary>
    private void StatusQuiet(string msg) => StatusMessage = msg;

    private void Status(string msg)
    {
        StatusMessage = msg;
        var now = DateTime.Now;
        if (msg == _lastStatusLogMessage
            && now - _lastStatusLogAt < TimeSpan.FromMilliseconds(StatusLogThrottleMs))
            return;

        _lastStatusLogMessage = msg;
        _lastStatusLogAt = now;
        Log(msg);
    }

    private void SetError(string msg)
    {
        _expectNpcMenu = false;
        _automationOwnsGcPersonnelUi = false;
        _repairTestMode = false;
        _deliveryTestMode = false;
        _shopTestMode = false;
        _extractTestMode = false;
        ResetMateriaExtractionState();
        ResetDutySupportQueueState();
        CloseMateriaExtractionUi();
        LastError = msg; StatusMessage = $"ERROR: {msg}"; State = FarmState.Error; IsRunning = false;
        AddLogEntry(LogSeverity.Error, msg);
        Service.PluginLog.Error($"[SealBreaker] {msg}");
        Service.ChatGui.PrintError($"[SealBreaker] {msg}");
    }

    private static void Log(string msg)
    {
        AddLogEntry(
            msg.StartsWith("WARN", StringComparison.OrdinalIgnoreCase) ? LogSeverity.Warning : LogSeverity.Info,
            msg);
        Service.PluginLog.Information($"[SealBreaker] {msg}");
        if (Plugin.Config.EchoToChat) Service.ChatGui.Print($"[SealBreaker] {msg}");
    }

    // ── In-memory log for the Log tab ─────────────────────────

    public enum LogSeverity { Info, Warning, Error }
    public readonly record struct FarmLogEntry(DateTime Time, LogSeverity Severity, string Message);

    private const int LogBufferMax = 500;
    private static readonly object LogLock = new();
    private static readonly List<FarmLogEntry> LogBuffer = new();

    private static void AddLogEntry(LogSeverity severity, string msg)
    {
        lock (LogLock)
        {
            LogBuffer.Add(new FarmLogEntry(DateTime.Now, severity, msg));
            if (LogBuffer.Count > LogBufferMax)
                LogBuffer.RemoveRange(0, LogBuffer.Count - LogBufferMax);
        }
    }

    public static FarmLogEntry[] GetLogSnapshot()
    {
        lock (LogLock)
            return LogBuffer.ToArray();
    }

    public static void ClearLogBuffer()
    {
        lock (LogLock)
            LogBuffer.Clear();
    }
}
