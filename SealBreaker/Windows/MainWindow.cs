using Dalamud.Interface.Windowing;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using SealBreaker.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SealBreaker.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private static readonly Vector4 ColGreen  = new(0.2f, 0.9f, 0.2f, 1f);
    private static readonly Vector4 ColRed    = new(0.9f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 ColYellow = new(0.9f, 0.9f, 0.2f, 1f);
    private static readonly Vector4 ColGray   = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 ColTitle  = new(1.0f, 0.78f, 0.35f, 1f);
    private static readonly Vector2 WindowIconSize = new(40f, 40f);
    private const float HeaderTitleScale = 1.45f;

    private static readonly string[] GcTownTabNames = ["Limsa", "Gridania", "Ul'dah"];
    private static readonly int[] GcTownTopLevelTabOrder = [0, 2, 1];
    private static readonly string[] GcTownFullNames =
    [
        "Maelstrom (zone 128)",
        "Twin Adder (zone 133)",
        "Immortal Flames (zone 130)",
    ];

    private string _newItemIdInput = string.Empty;
    private int _catalogCategoryFilter = 4;
    private string _catalogSearch = string.Empty;
    private int _catalogAddIndex;
    private readonly ISharedImmediateTexture? _windowIcon;

    private static string GetWindowTitle() =>
        $"Seal Breaker v{GetDisplayVersion()}##SealBreaker";

    private static string GetDisplayVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion
            .Split('+')[0];

        return string.IsNullOrWhiteSpace(version)
            ? assembly.GetName().Version?.ToString(3) ?? "1.0.0"
            : version;
    }

    public MainWindow() : base(GetWindowTitle())
    {
        _windowIcon = LoadWindowIcon();

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 520),
            MaximumSize = new Vector2(600, 900),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        WindowName = GetWindowTitle();

        var cfg  = Plugin.Config;
        var ctrl = Plugin.Controller;

        DrawWindowHeaderIcon();

        if (ImGui.BeginTabBar("##sealbreakerTabs"))
        {
            if (ImGui.BeginTabItem("Farm"))
            {
                DrawStatusPanel(ctrl);
                ImGui.Separator();
                DrawStatsPanel(ctrl);
                ImGui.Separator();
                DrawControlButtons(ctrl);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Config"))
            {
                DrawFarmTab(cfg);
                ImGui.Separator();
                DrawConfigTab(cfg);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Buy List"))
            {
                DrawBuyListTab(cfg);
                ImGui.EndTabItem();
            }

            foreach (var gcIdx in GcTownTopLevelTabOrder)
            {
                if (ImGui.BeginTabItem(GcTownTabNames[gcIdx]))
                {
                    DrawGcTownTab(cfg, gcIdx);
                    ImGui.EndTabItem();
                }
            }

            if (ImGui.BeginTabItem("Setup Guide"))
            {
                DrawSetupGuide(cfg);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private static ISharedImmediateTexture? LoadWindowIcon()
    {
        var pluginDirectory = Service.PluginInterface.AssemblyLocation.DirectoryName;
        if (string.IsNullOrWhiteSpace(pluginDirectory))
            return null;

        var iconPath = Path.Combine(pluginDirectory, "icon.png");
        return File.Exists(iconPath)
            ? Service.TextureProvider.GetFromFile(iconPath)
            : null;
    }

    private void DrawWindowHeaderIcon()
    {
        if (_windowIcon != null && _windowIcon.TryGetWrap(out var icon, out _))
        {
            ImGui.Image(icon.Handle, WindowIconSize);
            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 6f);
        }

        ImGui.SetWindowFontScale(HeaderTitleScale);
        ImGui.TextColored(ColTitle, "Seal Breaker");
        ImGui.SetWindowFontScale(1f);
        ImGui.Separator();
    }

    private void DrawFarmTab(Configuration cfg)
    {
        ImGui.TextColored(ColGray, "Farm settings");

        var runs = cfg.RunsPerCycle;
        if (ImGui.SliderInt("Runs per cycle", ref runs, 1, 50))
        { cfg.RunsPerCycle = runs; cfg.Save(); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How many Mistwake runs before heading to the Grand Company.");

        if (ImGui.CollapsingHeader("Item filter"))
            DrawFilterPanel(cfg);

        var echo = cfg.EchoToChat;
        if (ImGui.Checkbox("Echo log to chat", ref echo))
        { cfg.EchoToChat = echo; cfg.Save(); }

        ImGui.Spacing();
        ImGui.TextColored(ColGray, $"GC town routes: {GcTownTabNames[cfg.GrandCompanyIndex]} tab (active GC).");
    }

    private void DrawBuyListTab(Configuration cfg)
    {
        ImGui.TextColored(ColGray, "GC shop buy list");
        ImGui.TextWrapped("Priority order: buy each item until Keep is reached, then move to the next entry.");
        ImGui.TextDisabled("Keep 0 = spend seals on that item until reserve, then try the next entry.");
        ImGui.TextDisabled($"Seal reserve (Config tab): {cfg.SealReserve:N0} — buying stops when seals reach this amount.");
        ImGui.TextDisabled($"Farm uses the {GcTownTabNames[cfg.GrandCompanyIndex]} buy list selected by Grand Company.");

        ImGui.Spacing();
        if (ImGui.BeginTabBar("##gcShopBuyListTabs"))
        {
            for (var i = 0; i < GcTownTabNames.Length; i++)
            {
                if (ImGui.BeginTabItem(GcTownTabNames[i]))
                {
                    ImGui.PushID($"buylist-{i}");
                    var buyList = cfg.GcShopBuyListFor(i);
                    DrawGcShopCatalogPicker(cfg, i, buyList);
                    ImGui.Spacing();
                    DrawGcShopBuyList(cfg, i, buyList);
                    ImGui.PopID();
                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawGcShopCatalogPicker(Configuration cfg, int gcIdx, List<GcShopBuyEntry> buyList)
    {
        GcShopCatalog.EnsureInitialized();

        ImGui.TextColored(ColGray, "GC exchange catalog");
        ImGui.TextDisabled("Loaded from gc_shop_catalog.json when present, otherwise built from game data (GCScripShop sheets).");
        ImGui.TextDisabled("If categories look wrong, click Reload from game data (old JSON exports are ignored).");
        ImGui.TextWrapped("For GC aetheryte tickets: use Add port ticket preset on the matching city tab below.");

        if (ImGui.Button("Reload from game data"))
            GcShopCatalog.RefreshFromGameData();

        ImGui.SameLine();
        if (ImGui.Button("Export to JSON"))
        {
            if (GcShopCatalog.ExportToConfigFile())
                ImGui.OpenPopup("CatalogExported");
        }

        if (ImGui.BeginPopup("CatalogExported"))
        {
            ImGui.TextWrapped($"Saved to:\n{GcShopCatalog.CatalogFilePath}");
            ImGui.EndPopup();
        }

        ImGui.TextDisabled($"Catalog entries: {GcShopCatalog.Entries.Count:N0}");

        var categoryFilters = new[] { "All tabs", "Weapons", "Armor", "Materiel", "Materials" };
        ImGui.SetNextItemWidth(140);
        ImGui.Combo("Shop tab", ref _catalogCategoryFilter, categoryFilters, categoryFilters.Length);

        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##catalogsearch", "Search items...", ref _catalogSearch, 64);

        int? tabFilter = _catalogCategoryFilter == 0 ? null : _catalogCategoryFilter - 1;
        var filtered = GcShopCatalog.GetForGrandCompany(gcIdx, tabFilter, _catalogSearch).ToList();

        if (filtered.Count == 0)
        {
            ImGui.TextColored(ColYellow, "No catalog items match — reload from game data while logged in.");
            return;
        }

        _catalogAddIndex = Math.Clamp(_catalogAddIndex, 0, filtered.Count - 1);
        var labels = filtered.Select(FormatCatalogLabel).ToArray();
        ImGui.SetNextItemWidth(320);
        ImGui.Combo("Add from catalog", ref _catalogAddIndex, labels, labels.Length);

        ImGui.SameLine();
        if (ImGui.Button("Add selected"))
        {
            buyList.Add(GcShopCatalog.CreateBuyEntryFromCatalog(filtered[_catalogAddIndex]));
            cfg.Save();
        }
    }

    private static string FormatCatalogLabel(GcShopCatalogEntry entry) =>
        $"{entry.ItemName} ({entry.SealCost} seals) — {GcShopCatalog.CategoryName(entry.CategoryTab)}";

    private static void DrawConfigTab(Configuration cfg)
    {
        ImGui.TextColored(ColGray, "General configuration");

        var runnerItems = new[] { "AutoDuty", "ADS (AI Duty Solver)" };
        var runner = cfg.DutyRunner;
        if (ImGui.Combo("Duty Runner", ref runner, runnerItems, runnerItems.Length))
        {
            cfg.DutyRunner = runner;
            cfg.Save();
            IpcManager.ResetDutyRunners();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Select which plugin handles dungeon automation.");

        if (cfg.DutyRunner == 1 && ImGui.CollapsingHeader("ADS Duty Support", ImGuiTreeNodeFlags.DefaultOpen))
            DrawAdsDutySupportSection(cfg);

        var gcItems = new[] { "Maelstrom (Limsa)", "Order of the Twin Adder (Gridania)", "Immortal Flames (Ul'dah)" };
        var gcIdx = cfg.GrandCompanyIndex;
        if (ImGui.Combo("Grand Company", ref gcIdx, gcItems, gcItems.Length))
        { cfg.GrandCompanyIndex = gcIdx; cfg.Save(); }

        var sealCap = cfg.SealCap;
        if (ImGui.SliderInt("Seal cap", ref sealCap, 10000, 90000))
        { cfg.SealCap = sealCap; cfg.Save(); }

        var sealRes = cfg.SealReserve;
        if (ImGui.SliderInt("Seal reserve", ref sealRes, 0, 10000))
        { cfg.SealReserve = sealRes; cfg.Save(); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Stop buying from the buy list when seals drop to this amount.");

        var autoDismiss = cfg.AutoDismissGcOfficerMenu;
        if (ImGui.Checkbox("Auto-close GC officer menus", ref autoDismiss))
        { cfg.AutoDismissGcOfficerMenu = autoDismiss; cfg.Save(); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When enabled, SealBreaker closes the personnel officer prompt after delivery/shop automation.\nDisable to talk to the officer manually while the plugin is loaded.");

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Materia extraction"))
            DrawMateriaExtractionSection(cfg);
    }

    private static void DrawAdsDutySupportSection(Configuration cfg)
    {
        DutySupportCatalog.EnsureInitialized();
        var duties = DutySupportCatalog.Duties.ToList();
        if (duties.Count == 0)
        {
            ImGui.TextColored(ColYellow, "No Duty Support dungeons found.");
            return;
        }

        var selectedIndex = DutySupportCatalog.IndexOfSelected(cfg);
        var labels = duties.Select(DutySupportCatalog.FormatLabel).ToArray();
        ImGui.SetNextItemWidth(320);
        if (ImGui.Combo("Duty Support dungeon", ref selectedIndex, labels, labels.Length))
            DutySupportCatalog.ApplySelection(cfg, duties[Math.Clamp(selectedIndex, 0, duties.Count - 1)]);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("SealBreaker queues this Duty Support dungeon, then starts ADS once inside.");

        var selected = DutySupportCatalog.SelectedOrDefault(cfg);
        ImGui.TextDisabled($"Selected: {selected.Name} (territory {selected.TerritoryType}, content finder {selected.ContentFinderConditionId})");
        if (selected.ContentFinderConditionId == 0)
            ImGui.TextColored(ColYellow, "Content Finder ID was not detected from game data; reload in game before queueing.");
    }

    private static void DrawSetupGuide(Configuration cfg)
    {
        ImGui.TextColored(ColGray, "Step 1 — Required Plugins");
        DrawPluginStatus("vnavmesh", IpcManager.VnavAvailable);
        DrawPluginStatus("Lifestream", IpcManager.LifestreamAvailable);
        DrawPluginStatus(cfg.DutyRunner == 0 ? "AutoDuty" : "ADS", cfg.DutyRunner == 0 ? IpcManager.AutoDutyAvailable : IpcManager.AdsAvailable);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(ColGray, "Step 2 — Game Settings");
        ImGui.BulletText(string.Empty);
        ImGui.SameLine();
        ImGui.TextColored(ColYellow, "Disable auto-equip new gear: Character Config → Item Settings → Equip Retrieved Gear → OFF — gear must stay in your bags for Expert Delivery to see it");
        ImGui.BulletText(string.Empty);
        ImGui.SameLine();
        ImGui.TextColored(ColYellow, "Ensure your Grand Company rank is high enough for Expert Delivery (Second Lieutenant or above)");
        ImGui.BulletText(string.Empty);
        ImGui.SameLine();
        ImGui.TextColored(ColYellow, "Make sure your inventory has free space before starting — a full inventory will cause drops to be lost");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(ColGray, "Step 3 — Grand Company Setup");
        ImGui.BulletText("Select your Grand Company in the Configuration tab");
        ImGui.BulletText($"Current seals: {FarmController.GetCurrentSeals():N0} / {cfg.SealCap:N0}");
        ImGui.BulletText("Set Duckbone Shop Row by opening your GC shop, counting rows from 0, and entering that number in Configuration");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(ColGray, "Step 4 — First Run Checklist");
        ImGui.TextColored(ColGray, "✓ Set Runs Per Cycle to 1 for your first test run");
        ImGui.TextColored(ColGray, "✓ Set List Mode to Off so all gear is turned in automatically");
        ImGui.TextColored(ColGray, "✓ Click Start and watch the Farm tab log output");
        ImGui.TextColored(ColGray, "✓ Verify: zones into dungeon → completes run → teleports to GC → delivers gear → buys items → loops");
        ImGui.TextColored(ColGray, "✓ Once confirmed working, increase Runs Per Cycle and configure your item filter");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(ColGray, "Step 5 — Item Filter Setup");
        ImGui.BulletText("Run one cycle with List Mode = Off");
        ImGui.BulletText("Check plugin log for 'DROP: ItemID XXXXX' lines to find IDs of dropped gear");
        ImGui.BulletText("Add any item IDs you want to KEEP to the Protected Item IDs list in Configuration");
        ImGui.BulletText("Switch List Mode to Blacklist — everything except your protected IDs will be turned in");
        ImGui.BulletText("Use Whitelist mode instead if you only want to deliver specific items and keep everything else");
    }

    private static void DrawPluginStatus(string pluginName, bool available)
    {
        var marker = available ? "✓" : "✗";
        var status = available ? "Installed" : "NOT DETECTED — install from your plugin list";
        ImGui.TextColored(available ? ColGreen : ColRed, $"{marker} {pluginName}: {status}");
    }

    private static void DrawMateriaExtractionSection(Configuration cfg)
    {
        ImGui.TextDisabled("Only equipped gear is checked. Armory chest and inventory gear are left alone.");

        var autoExtract = cfg.AutoExtractMateriaEnabled;
        if (ImGui.Checkbox("Auto extract materia from equipped gear", ref autoExtract))
        { cfg.AutoExtractMateriaEnabled = autoExtract; cfg.Save(); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Extracts materia only from fully spiritbound equipped items using the native Materia Extraction window.");

        if (!cfg.AutoExtractMateriaEnabled)
            ImGui.BeginDisabled();

        var extractBetweenRuns = cfg.AutoExtractMateriaBetweenRuns;
        if (ImGui.Checkbox("Check between duty runs", ref extractBetweenRuns))
        { cfg.AutoExtractMateriaBetweenRuns = extractBetweenRuns; cfg.Save(); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("When enabled, checks equipped gear after each completed run before queueing the next run.");

        var extractAtCycleBoundary = cfg.AutoExtractMateriaAtCycleBoundary;
        if (ImGui.Checkbox("Check before each duty cycle", ref extractAtCycleBoundary))
        { cfg.AutoExtractMateriaAtCycleBoundary = extractAtCycleBoundary; cfg.Save(); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("When enabled, checks equipped gear before the pre-duty gear check and run 1 of a new cycle.");

        if (!cfg.AutoExtractMateriaEnabled)
            ImGui.EndDisabled();
    }

    private static void DrawGcTownTab(Configuration cfg, int gcIdx)
    {
        cfg.EnsureGcTownNav();
        var town = cfg.TownNav(gcIdx);

        ImGui.TextColored(ColGray, GcTownFullNames[gcIdx]);
        if (gcIdx == cfg.GrandCompanyIndex)
            ImGui.TextColored(ColGreen, "  ← active Grand Company");
        else
            ImGui.TextColored(ColGray, "  (configure now, switch GC on Config tab when ready)");

        if (ImGui.CollapsingHeader("Repair between runs", ImGuiTreeNodeFlags.DefaultOpen))
            DrawGcTownRepairSection(cfg, town, gcIdx);

        if (ImGui.CollapsingHeader("GC navigation route", ImGuiTreeNodeFlags.DefaultOpen))
            DrawGcRouteSection(cfg, town, gcIdx, GcRouteKind.Approach);

        if (ImGui.CollapsingHeader("GC corridor route"))
            DrawGcRouteSection(cfg, town, gcIdx, GcRouteKind.Corridor);

        if (ImGui.CollapsingHeader("Repair navigation route", ImGuiTreeNodeFlags.DefaultOpen))
            DrawGcRouteSection(cfg, town, gcIdx, GcRouteKind.Repair);
    }

    private enum GcRouteKind { Approach, Corridor, Repair }

    private static void DrawGcTownRepairSection(Configuration cfg, GcTownNavSettings town, int gcIdx)
    {
        var repairEnabled = town.RepairEnabled;
        if (ImGui.Checkbox("Repair between runs", ref repairEnabled))
        {
            town.RepairEnabled = repairEnabled;
            cfg.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Visit the mender in {GcTownFullNames[gcIdx]} when gear drops below threshold.");

        if (!town.RepairEnabled)
            return;

        var repairPct = town.RepairThresholdPercent;
        if (ImGui.SliderInt("Repair below %", ref repairPct, 10, 90))
        {
            town.RepairThresholdPercent = repairPct;
            cfg.Save();
        }

        ImGui.Spacing();
        ImGui.TextColored(ColYellow, "Mender NPC");

        var menderName = town.MenderName;
        if (ImGui.InputText("Mender name", ref menderName, 64))
        {
            town.MenderName = menderName;
            cfg.Save();
        }

        var mx = town.MenderX;
        var my = town.MenderY;
        var mz = town.MenderZ;
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputFloat("Mender X", ref mx, 0.1f, 1f, "%.2f"))
        { town.MenderX = mx; cfg.Save(); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputFloat("Y##mender", ref my, 0.1f, 1f, "%.2f"))
        { town.MenderY = my; cfg.Save(); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputFloat("Z##mender", ref mz, 0.1f, 1f, "%.2f"))
        { town.MenderZ = mz; cfg.Save(); }

        var target = Service.TargetManager.Target;
        if (ImGui.Button("Set mender from target"))
        {
            if (target != null)
            {
                town.MenderName = target.Name.ToString();
                town.MenderX = target.Position.X;
                town.MenderY = target.Position.Y;
                town.MenderZ = target.Position.Z;
                cfg.Save();
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Target the arms/armor mender, then click to fill name and position.");

        if (!town.HasMenderConfigured)
            ImGui.TextColored(ColRed, "Set mender name before repair can run.");
    }

    private static void DrawGcRouteSection(Configuration cfg, GcTownNavSettings town, int gcIdx, GcRouteKind kind)
    {
        var (useCustom, bakedCount, tooltip, idPrefix, waypoints) = kind switch
        {
            GcRouteKind.Approach => (
                town.UseCustomGcNavWaypoints,
                GcNavRoutes.BakedGcApproachCount(gcIdx),
                gcIdx == 0
                    ? "Y=40 supply deck walk (after port-in). Skipped on main deck."
                    : "Walk from city arrival to GC staging area.",
                $"gcnav{gcIdx}",
                town.GcNavWaypoints),
            GcRouteKind.Corridor => (
                town.UseCustomGcCorridorWaypoints,
                GcNavRoutes.BakedGcCorridorCount(gcIdx),
                gcIdx == 0
                    ? "Main deck Aftcastle → GC command corridor."
                    : "Optional second segment (stairs/hallway to officer area).",
                $"gccor{gcIdx}",
                town.GcCorridorWaypoints),
            GcRouteKind.Repair => (
                town.UseCustomRepairNavWaypoints,
                GcNavRoutes.BakedRepairCount(gcIdx),
                "GC area → mender forward, reversed back after repair.",
                $"repair{gcIdx}",
                town.RepairNavWaypoints),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var useCustomLocal = useCustom;
        if (ImGui.Checkbox("Use custom route (override baked-in)", ref useCustomLocal))
        {
            switch (kind)
            {
                case GcRouteKind.Approach:
                    town.UseCustomGcNavWaypoints = useCustomLocal;
                    break;
                case GcRouteKind.Corridor:
                    town.UseCustomGcCorridorWaypoints = useCustomLocal;
                    break;
                case GcRouteKind.Repair:
                    town.UseCustomRepairNavWaypoints = useCustomLocal;
                    break;
            }

            cfg.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        if (!useCustomLocal)
        {
            if (bakedCount == 0)
            {
                ImGui.TextColored(ColYellow, "No baked-in route — enable custom and map steps below.");
                return;
            }

            ImGui.TextColored(ColGray, $"Using baked-in route ({bakedCount} steps).");
            return;
        }

        DrawNavWaypointEditor(cfg, waypoints, idPrefix);
    }

    private static void DrawNavWaypointEditor(Configuration cfg, List<NavWaypoint> waypoints, string idPrefix)
    {
        ImGui.TextColored(ColYellow, "Waypoints (in order):");
        var removeIdx = -1;
        for (var i = 0; i < waypoints.Count; i++)
        {
            var wp = waypoints[i];
            ImGui.PushID($"{idPrefix}{i}");
            ImGui.Text($"{i + 1}.");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70);
            var x = wp.X;
            if (ImGui.InputFloat($"X##{idPrefix}", ref x, 0.1f, 1f, "%.2f"))
            {
                wp.X = x;
                cfg.Save();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70);
            var y = wp.Y;
            if (ImGui.InputFloat($"Y##{idPrefix}", ref y, 0.1f, 1f, "%.2f"))
            {
                wp.Y = y;
                cfg.Save();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70);
            var z = wp.Z;
            if (ImGui.InputFloat($"Z##{idPrefix}", ref z, 0.1f, 1f, "%.2f"))
            {
                wp.Z = z;
                cfg.Save();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton($"X##rm{idPrefix}"))
                removeIdx = i;
            ImGui.PopID();
        }

        if (removeIdx >= 0)
        {
            waypoints.RemoveAt(removeIdx);
            cfg.Save();
        }

        var player = Service.ObjectTable.LocalPlayer;
        ImGui.PushID(idPrefix);
        if (ImGui.Button("Add current position"))
        {
            if (player != null)
            {
                waypoints.Add(NavWaypoint.From(player.Position));
                cfg.Save();
            }
        }
        ImGui.SameLine();
        var target = Service.TargetManager.Target;
        if (ImGui.Button("Add target position"))
        {
            if (target != null)
            {
                waypoints.Add(NavWaypoint.From(target.Position));
                cfg.Save();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            waypoints.Clear();
            cfg.Save();
        }
        ImGui.PopID();

        if (player != null)
            ImGui.TextColored(ColGray, $"Player: {player.Position}");

        if (waypoints.Count == 0)
            ImGui.TextColored(ColRed, "Add at least one waypoint.");
    }

    private void DrawFilterPanel(Configuration cfg)
    {
        var modeItems = new[] { "Off (turn in everything)", "Blacklist (protect listed IDs)", "Whitelist (only deliver listed IDs)" };
        var mode = cfg.ListMode;
        if (ImGui.Combo("List mode", ref mode, modeItems, modeItems.Length))
        { cfg.ListMode = mode; cfg.Save(); }

        if (cfg.ListMode == 0)
        {
            ImGui.TextColored(ColGray, "  All items will be turned in.");
            return;
        }

        var modeLabel = cfg.ListMode == 1
            ? "Protected item IDs (never turn in):"
            : "Whitelist item IDs (only turn in):";
        ImGui.TextColored(ColYellow, modeLabel);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Find item IDs: hover item in inventory, run /xldata items");

        uint? toRemove = null;
        foreach (var id in cfg.FilteredItemIds)
        {
            ImGui.BulletText($"{id}");
            ImGui.SameLine();
            ImGui.PushID((int)id);
            if (ImGui.SmallButton("X")) toRemove = id;
            ImGui.PopID();
        }

        if (toRemove.HasValue)
        { cfg.FilteredItemIds.Remove(toRemove.Value); cfg.Save(); }

        ImGui.SetNextItemWidth(120);
        ImGui.InputText("##newid", ref _newItemIdInput, 16);
        ImGui.SameLine();
        if (ImGui.Button("Add ID"))
        {
            if (uint.TryParse(_newItemIdInput.Trim(), out var newId) && newId > 0)
            {
                if (!cfg.FilteredItemIds.Contains(newId))
                { cfg.FilteredItemIds.Add(newId); cfg.Save(); }
                _newItemIdInput = string.Empty;
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Enter an item ID number and click Add.");
    }

    private static void DrawStatusPanel(FarmController ctrl)
    {
        ImGui.TextColored(ColGray, "Status  ");
        ImGui.SameLine();

        var (label, col) = ctrl.State switch
        {
            FarmController.FarmState.Idle  => ("Idle",    ColGray),
            FarmController.FarmState.Error => ("Error",   ColRed),
            _                              => ("Running", ColGreen),
        };
        ImGui.TextColored(col, label);
        ImGui.TextWrapped(ctrl.StatusMessage);

        if (ctrl.LastError != null)
            ImGui.TextColored(ColRed, $"Last error: {ctrl.LastError}");
    }

    private static void DrawStatsPanel(FarmController ctrl)
    {
        ImGui.TextColored(ColGray, "Statistics");

        var elapsed = ctrl.IsRunning
            ? DateTime.Now - ctrl.StartTime
            : TimeSpan.Zero;
        var duckBonesTotal = FarmController.GetDuckBoneInventoryCount();
        var duckBonesValue = duckBonesTotal * 360;

        ImGui.Columns(2, "stats", false);
        ImGui.Text("Cycles:");       ImGui.NextColumn(); ImGui.Text($"{ctrl.TotalCycles}");      ImGui.NextColumn();
        ImGui.Text("Runs:");         ImGui.NextColumn(); ImGui.Text($"{ctrl.TotalRuns}");        ImGui.NextColumn();
        ImGui.Text("Seals earned:");      ImGui.NextColumn(); ImGui.Text($"{ctrl.TotalSeals:N0}");              ImGui.NextColumn();
        ImGui.Text("Duck Bones Bought:"); ImGui.NextColumn(); ImGui.Text($"{ctrl.TotalDuckbones:N0}");          ImGui.NextColumn();
        ImGui.Text("Duck Bones Total:");  ImGui.NextColumn(); ImGui.Text($"{duckBonesTotal:N0}"); ImGui.NextColumn();
        ImGui.Text("Estimated Duck Bone Value:"); ImGui.NextColumn(); ImGui.Text($"{duckBonesValue:N0}"); ImGui.NextColumn();
        ImGui.Text("Runtime:");           ImGui.NextColumn(); ImGui.Text($"{elapsed:hh\\:mm\\:ss}");             ImGui.NextColumn();
        ImGui.Columns(1);
    }

    private static void DrawGcShopBuyList(Configuration cfg, int gcIdx, List<GcShopBuyEntry> buyList)
    {
        if (ImGui.Button("Add Duck Bones"))
        {
            buyList.Add(GcShopDefaults.CreateDuckboneBuyEntry(gcIdx));
            cfg.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("Add blank entry"))
        {
            buyList.Add(new GcShopBuyEntry());
            cfg.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("Add port ticket preset"))
        {
            buyList.Add(GcShopDefaults.CreatePortTicketBuyEntry(gcIdx));
            cfg.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Uses the {GcTownTabNames[gcIdx]} Grand Company catalog.");

        if (buyList.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(ColYellow, "No buy entries yet — add Duck Bones or a blank entry to get started.");
        }

        for (var i = 0; i < buyList.Count; i++)
        {
            var entry = buyList[i];
            ImGui.PushID(i);
            ImGui.Separator();
            ImGui.Text($"#{i + 1}");

            var enabled = entry.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
            { entry.Enabled = enabled; cfg.Save(); }

            ImGui.SameLine();
            if (i > 0 && ImGui.SmallButton("Up"))
            {
                (buyList[i - 1], buyList[i]) = (buyList[i], buyList[i - 1]);
                cfg.Save();
            }

            ImGui.SameLine();
            if (i < buyList.Count - 1 && ImGui.SmallButton("Down"))
            {
                (buyList[i + 1], buyList[i]) = (buyList[i], buyList[i + 1]);
                cfg.Save();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                buyList.RemoveAt(i);
                cfg.Save();
                ImGui.PopID();
                break;
            }

            DrawEntryCatalogPicker(cfg, entry, gcIdx);

            var name = entry.ItemName;
            if (ImGui.InputText("Item name", ref name, 128))
            { entry.ItemName = name; cfg.Save(); }

            var itemId = (int)entry.ItemId;
            if (ImGui.InputInt("Item ID (0=lookup)", ref itemId))
            { entry.ItemId = (uint)Math.Max(0, itemId); cfg.Save(); }

            if (ImGui.CollapsingHeader("Shop tuning (manual override)"))
            {
                ImGui.TextDisabled("Seal cost, tabs, and list row are auto-resolved from game data when possible.");
                ImGui.Indent();

                var sealCost = entry.SealCost;
                if (ImGui.InputInt("Seal cost", ref sealCost))
                { entry.SealCost = Math.Max(1, sealCost); cfg.Save(); }

                var cat = entry.CategoryTab;
                if (ImGui.InputInt("Category tab", ref cat))
                { entry.CategoryTab = Math.Clamp(cat, 0, 3); cfg.Save(); }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Weapons=0, Armor=1, Materiel=2, Materials=3.");

                var rank = entry.RankTab;
                if (ImGui.InputInt("Rank tab", ref rank))
                { entry.RankTab = Math.Clamp(rank, 0, 2); cfg.Save(); }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Left rank icons: top=0, middle=1, bottom=2.");

                var row = entry.ListRow;
                if (ImGui.InputInt("List row", ref row))
                { entry.ListRow = Math.Max(0, row); cfg.Save(); }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("0-based fallback row if name search fails.");

                ImGui.Unindent();
            }

            var keep = entry.KeepAmount;
            if (ImGui.InputInt("Keep amount", ref keep))
            { entry.KeepAmount = Math.Max(0, keep); cfg.Save(); }

            var qty = entry.BuyQtyPerPurchase;
            if (ImGui.InputInt("Buy qty (0=max)", ref qty))
            { entry.BuyQtyPerPurchase = Math.Clamp(qty, 0, 99); cfg.Save(); }

            var have = FarmController.GetInventoryItemCountPublic(entry);
            ImGui.TextDisabled($"In inventory: {have:N0}");

            ImGui.PopID();
        }
    }

    private static void DrawEntryCatalogPicker(Configuration cfg, GcShopBuyEntry entry, int gcIdx)
    {
        var matches = GcShopCatalog.GetForGrandCompany(gcIdx).ToList();
        if (matches.Count == 0)
            return;

        var matchedIndex = matches.FindIndex(e =>
            (e.ItemId != 0 && e.ItemId == entry.ItemId)
            || e.ItemName.Equals(entry.ItemName, StringComparison.OrdinalIgnoreCase));

        var pickerIndex = matchedIndex >= 0 ? matchedIndex + 1 : 0;
        var labels = new[] { "(keep manual values)" }
            .Concat(matches.Select(FormatCatalogLabel))
            .ToArray();

        if (ImGui.Combo("Pick from catalog", ref pickerIndex, labels, labels.Length) && pickerIndex > 0)
        {
            GcShopCatalog.ApplyToBuyEntry(entry, matches[pickerIndex - 1]);
            cfg.Save();
        }
    }

    private static void DrawExpertDeliveryTestButton(FarmController ctrl)
    {
        var canRun = IpcManager.VnavAvailable
                     && IpcManager.LifestreamAvailable
                     && (!ctrl.IsRunning || ctrl.IsDeliveryTest);

        if (!canRun)
            ImGui.BeginDisabled();

        if (ImGui.Button("Delivery", new Vector2(100, 30)))
            ctrl.StartExpertDeliveryTest();

        if (!canRun)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (!IpcManager.VnavAvailable || !IpcManager.LifestreamAvailable)
                ImGui.SetTooltip("Requires vnavmesh and Lifestream.");
            else if (ctrl.IsRunning && !ctrl.IsDeliveryTest)
                ImGui.SetTooltip("Stop the farm before running a delivery test.");
            else
                ImGui.SetTooltip("Navigate to the personnel officer and run Expert Delivery once.");
        }
    }

    private static void DrawShopTestButton(FarmController ctrl)
    {
        var canRun = IpcManager.VnavAvailable
                     && IpcManager.LifestreamAvailable
                     && Plugin.Config.EnabledGcShopBuyList().Count > 0
                     && (!ctrl.IsRunning || ctrl.IsShopTest);

        if (!canRun)
            ImGui.BeginDisabled();

        if (ImGui.Button("Shop", new Vector2(100, 30)))
            ctrl.StartShopTest();

        if (!canRun)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (!IpcManager.VnavAvailable || !IpcManager.LifestreamAvailable)
                ImGui.SetTooltip("Requires vnavmesh and Lifestream.");
            else if (Plugin.Config.EnabledGcShopBuyList().Count == 0)
                ImGui.SetTooltip("Add at least one enabled buy entry on the Buy List tab.");
            else if (ctrl.IsRunning && !ctrl.IsShopTest)
                ImGui.SetTooltip("Stop the farm before running a shop test.");
            else
                ImGui.SetTooltip("Navigate to the quartermaster and run the GC shop buy list once.");
        }
    }

    private static void DrawRepairTestButton(FarmController ctrl)
    {
        var gcIdx = Plugin.Config.GrandCompanyIndex;
        var town = Plugin.Config.TownNav(gcIdx);
        var hasRoute = GcNavRoutes.HasRepairRoute(Plugin.Config, gcIdx);
        var canRun = IpcManager.VnavAvailable
                     && town.HasMenderConfigured
                     && (!ctrl.IsRunning || ctrl.IsRepairTest);

        if (!canRun)
            ImGui.BeginDisabled();

        if (ImGui.Button("Repair", new Vector2(100, 30)))
            ctrl.StartRepair();

        if (!canRun)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (!IpcManager.VnavAvailable)
                ImGui.SetTooltip("Requires vnavmesh.");
            else if (!town.HasMenderConfigured)
                ImGui.SetTooltip("Configure mender name/position on the GC town tab.");
            else if (ctrl.IsRunning && !ctrl.IsRepairTest)
                ImGui.SetTooltip("Stop the farm before running a repair test.");
            else
            {
                var routeNote = hasRoute
                    ? "Uses the configured repair navigation route."
                    : "No repair route — walks directly to mender position.";
                ImGui.SetTooltip($"Run repair path for {GcTownTabNames[gcIdx]}. {routeNote} Teleports to GC first if needed.");
            }
        }
    }

    private static void DrawExtractTestButton(FarmController ctrl)
    {
        var canRun = !ctrl.IsRunning || ctrl.IsExtractTest;

        if (!canRun)
            ImGui.BeginDisabled();

        if (ImGui.Button("Extract", new Vector2(100, 30)))
            ctrl.StartExtractTest();

        if (!canRun)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (ctrl.IsRunning && !ctrl.IsExtractTest)
                ImGui.SetTooltip("Stop the farm before running an extract test.");
            else
                ImGui.SetTooltip("Open Materia Extraction and extract materia from fully spiritbound equipped gear once.");
        }
    }

    private static void DrawControlButtons(FarmController ctrl)
    {
        var cfg = Plugin.Config;

        if (cfg.DutyRunner == 0)
        {
            if (!IpcManager.AutoDutyPluginLoaded)
                ImGui.TextColored(ColRed, "! AutoDuty plugin not loaded");
            else if (!IpcManager.AutoDutyAvailable)
                ImGui.TextColored(ColYellow, "! AutoDuty loaded — waiting for Run IPC (restart AutoDuty if this persists)");
        }

        if (cfg.DutyRunner == 1)
        {
            if (!IpcManager.AdsPluginLoaded)
                ImGui.TextColored(ColRed, "! ADS plugin not loaded");
            else if (!IpcManager.AdsAvailable)
                ImGui.TextColored(ColYellow, "! ADS loaded — waiting for IPC");
        }
        if (!IpcManager.VnavAvailable)
            ImGui.TextColored(ColRed, "! vnavmesh not detected");
        if (!IpcManager.LifestreamAvailable)
            ImGui.TextColored(ColRed, "! Lifestream not detected");
        else if (!IpcManager.LifestreamMoveAvailable)
            ImGui.TextColored(ColYellow, "! Lifestream.Move unavailable — using vnav fallback");

        ImGui.Spacing();

        var dutyReady = cfg.DutyRunner == 0
            ? IpcManager.AutoDutyAvailable
            : IpcManager.AdsAvailable;

        var allReady = dutyReady
                    && IpcManager.VnavAvailable
                    && IpcManager.LifestreamAvailable;

        if (ctrl.IsRunning)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ColRed);
            if (ImGui.Button("Stop##farm", new Vector2(120, 30))) ctrl.Stop();
            ImGui.PopStyleColor();
        }
        else
        {
            if (!allReady) ImGui.BeginDisabled();
            ImGui.PushStyleColor(ImGuiCol.Button, ColGreen);
            if (ImGui.Button("Start##farm", new Vector2(120, 30))) ctrl.Start();
            ImGui.PopStyleColor();
            if (!allReady) ImGui.EndDisabled();
        }

        ImGui.SameLine();
        DrawExpertDeliveryTestButton(ctrl);
        ImGui.SameLine();
        DrawShopTestButton(ctrl);
        ImGui.SameLine();
        DrawRepairTestButton(ctrl);
        ImGui.SameLine();
        DrawExtractTestButton(ctrl);

        if (ctrl.State == FarmController.FarmState.Error)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear Error", new Vector2(120, 30))) ctrl.Stop();
        }
    }
}
