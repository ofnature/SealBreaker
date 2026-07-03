using Dalamud.Interface.Windowing;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ManagedFontAtlas;
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
    private static readonly Vector4 ColTitleGlow = new(0.0f, 0.95f, 0.95f, 0.35f);
    private const string BannerTitleFontName = "CinzelDecorative-Bold.ttf";

    private static readonly string[] GcTownTabNames = ["Limsa", "Gridania", "Ul'dah"];
    private static readonly int[] GcTownTopLevelTabOrder = [0, 2, 1];
    private static readonly string[] RepairProviderItems = ["Seal Breaker", "ADS"];
    private static readonly string[] AdsRepairModeItems =
    [
        "self",
        "npc",
        "npc-no-inn",
        "npc-no-teleport-no-inn",
    ];
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
    private Dictionary<uint, MarketItemData>? _desynthPrices;
    private Task<Dictionary<uint, MarketItemData>?>? _desynthPriceTask;
    private DateTime? _desynthPricesUpdatedAt;
    private string? _desynthPriceError;
    private string _desynthStatus = "Idle";
    private bool _confirmResetDesynthStats;
    private string? _desynthExportStatus;
    private readonly IFontHandle? _bannerTitleFont;

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
        _bannerTitleFont = CreateBannerTitleFont();

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 520),
            MaximumSize = new Vector2(1000, 1200),
        };
    }

    public void Dispose() => _bannerTitleFont?.Dispose();

    public override void Draw()
    {
        WindowName = GetWindowTitle();

        var cfg  = Plugin.Config;
        var ctrl = Plugin.Controller;
        cfg.ApplyAutomaticGrandCompanySettings();

        using var theme = UiTheme.Begin();

        if (cfg.ShowWindowBanner)
            DrawPluginBanner();

        if (ImGui.BeginTabBar("##sealbreakerTabs"))
        {
            if (ImGui.BeginTabItem("Farm"))
            {
                ImGui.Spacing();
                DrawStatusPanel(ctrl);
                DrawStatsPanel(ctrl);
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

            if (ImGui.BeginTabItem("Desynth"))
            {
                DrawDesynthTab(cfg);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Stats"))
            {
                DrawStatsTab(cfg);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("GC Towns"))
            {
                ImGui.Spacing();
                if (ImGui.BeginTabBar("##gcTownTabs"))
                {
                    foreach (var gcIdx in GcTownTopLevelTabOrder)
                    {
                        if (ImGui.BeginTabItem(GcTownTabNames[gcIdx]))
                        {
                            DrawGcTownTab(cfg, gcIdx);
                            ImGui.EndTabItem();
                        }
                    }

                    ImGui.EndTabBar();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Setup Guide"))
            {
                DrawSetupGuide(cfg);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawPluginBanner()
    {
        if (Plugin.PluginBanner == null || !Plugin.PluginBanner.TryGetWrap(out var banner, out _))
            return;

        var availWidth = ImGui.GetContentRegionAvail().X;
        if (availWidth <= 0 || banner.Width <= 0)
            return;

        var bannerHeight = availWidth * (banner.Height / (float)banner.Width);
        var bannerSize = new Vector2(availWidth, bannerHeight);
        var bannerPos = ImGui.GetCursorScreenPos();

        ImGui.Image(banner.Handle, bannerSize);

        using var font = _bannerTitleFont?.Push();
        DrawBannerTitleText(bannerPos, bannerSize);

        ImGui.Spacing();
    }

    private static IFontHandle? CreateBannerTitleFont()
    {
        var pluginDirectory = Service.PluginInterface.AssemblyLocation.DirectoryName;
        if (string.IsNullOrWhiteSpace(pluginDirectory))
            return null;

        var fontPath = Path.Combine(pluginDirectory, BannerTitleFontName);
        if (!File.Exists(fontPath))
            return null;

        return Service.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var config = new SafeFontConfig
            {
                SizePx = UiBuilder.DefaultFontSizePx * 2.15f,
            };

            tk.Font = tk.AddFontFromFile(fontPath, config);
        }));
    }

    private static void DrawBannerTitleText(Vector2 bannerPos, Vector2 bannerSize)
    {
        const string text = "Seal Breaker";
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var textSize = ImGui.CalcTextSize(text);
        var textPos = new Vector2(
            bannerPos.X + (bannerSize.X - textSize.X) / 2f,
            bannerPos.Y + (bannerSize.Y - textSize.Y) / 2f);

        var drawList = ImGui.GetWindowDrawList();

        for (var dx = -2; dx <= 2; dx += 2)
        {
            for (var dy = -2; dy <= 2; dy += 2)
            {
                if (dx == 0 && dy == 0)
                    continue;

                drawList.AddText(font, fontSize,
                    new Vector2(textPos.X + dx, textPos.Y + dy),
                    ImGui.ColorConvertFloat4ToU32(ColTitleGlow),
                    text);
            }
        }

        drawList.AddText(font, fontSize,
            new Vector2(textPos.X + 3f, textPos.Y + 3f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.8f)),
            text);
        drawList.AddText(font, fontSize,
            textPos,
            ImGui.ColorConvertFloat4ToU32(ColTitle),
            text);
    }

    private void DrawFarmTab(Configuration cfg)
    {
        UiTheme.SectionTitle("Farm settings");

        var runs = cfg.RunsPerCycle;
        if (ImGui.SliderInt("Runs per cycle", ref runs, 1, 50))
        { cfg.RunsPerCycle = runs; cfg.Save(); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How many duty runs before heading to the Grand Company.");

        if (ImGui.CollapsingHeader("Item filter"))
            DrawFilterPanel(cfg);

        var echo = cfg.EchoToChat;
        if (ImGui.Checkbox("Echo log to chat", ref echo))
        { cfg.EchoToChat = echo; cfg.Save(); }

        ImGui.Spacing();
        ImGui.TextColored(ColGray, $"GC town routes: GC Towns tab → {GcTownTabNames[cfg.GrandCompanyIndex]} (active GC).");
    }

    private void DrawBuyListTab(Configuration cfg)
    {
        UiTheme.SectionTitle("GC shop buy list");
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

    private static bool _desynthDefaultsEnsured;

    private static void EnsureDesynthDefaultsOnce(Configuration cfg)
    {
        if (_desynthDefaultsEnsured)
            return;

        cfg.EnsureDesynthDefaults();
        _desynthDefaultsEnsured = true;
    }

    private void DrawDesynthTab(Configuration cfg)
    {
        EnsureDesynthDefaultsOnce(cfg);
        CompleteDesynthPriceFetch();

        UiTheme.SectionTitle("Kingcake desynthesis EV");
        ImGui.TextDisabled("Prices: Universalis public API, Maduin. Offline/fallback values are used when live prices are missing.");

        ImGui.Spacing();
        DrawDesynthPriceFetchSection();
        ImGui.Separator();
        DrawKingcakeDesynthButton();
        ImGui.Separator();
        DrawDesynthDropTable(cfg);
        ImGui.Separator();
        DrawDesynthProjections(cfg);
        ImGui.Separator();
        DrawDesynthAlertsAndOverrides(cfg);
    }

    private void DrawDesynthPriceFetchSection()
    {
        var loading = _desynthPriceTask is { IsCompleted: false };
        if (loading)
            ImGui.BeginDisabled();

        if (ImGui.Button("Refresh Maduin prices"))
        {
            _desynthPriceError = null;
            _desynthPriceTask = UniversalisClient.FetchPricesAsync(KingcakeDesynth.MarketItemIds);
        }

        if (loading)
            ImGui.EndDisabled();

        ImGui.SameLine();
        if (loading)
            ImGui.TextColored(ColYellow, "Loading...");
        else if (_desynthPricesUpdatedAt.HasValue)
            ImGui.TextDisabled($"Last updated: {_desynthPricesUpdatedAt.Value:g}");
        else
            ImGui.TextDisabled("Not fetched this session.");

        if (!string.IsNullOrWhiteSpace(_desynthPriceError))
            ImGui.TextColored(ColYellow, _desynthPriceError);

        if (DateTime.UtcNow < UniversalisClient.RetryAfterUtc)
            ImGui.TextColored(ColYellow, "Universalis rate limit hit; wait briefly before refreshing.");
    }

    private void CompleteDesynthPriceFetch()
    {
        if (_desynthPriceTask is not { IsCompleted: true } task)
            return;

        _desynthPriceTask = null;
        if (task.IsFaulted)
        {
            _desynthPriceError = "Price fetch failed; using fallback/manual prices.";
            return;
        }

        _desynthPrices = task.Result;
        if (_desynthPrices == null || _desynthPrices.Count == 0)
        {
            _desynthPriceError = "No live prices returned; using fallback/manual prices.";
            return;
        }

        _desynthPricesUpdatedAt = DateTime.Now;
        _desynthPriceError = null;
    }

    private void DrawKingcakeDesynthButton()
    {
        var count = FarmController.GetKingcakeInventoryCount();
        ImGui.TextColored(ColGray, $"Kingcakes in inventory: {count:N0}");
        if (count <= 0 || Plugin.Controller.IsRunning)
            ImGui.BeginDisabled();

        if (ImGui.Button("Desynth Kingcake only"))
        {
            Plugin.Controller.TryStartKingcakeDesynth(out var message);
            _desynthStatus = message;
        }

        if (count <= 0 || Plugin.Controller.IsRunning)
            ImGui.EndDisabled();

        if (Plugin.Controller.IsRunning)
            ImGui.TextColored(ColYellow, "Stop the farm before desynthing.");

        ImGui.TextWrapped(_desynthStatus);
    }

    private void DrawDesynthDropTable(Configuration cfg)
    {
        var ev = CalculateKingcakeEv(cfg);
        ImGui.TextColored(ColGray, $"Expected value per Kingcake: {ev:N0} gil");
        ImGui.TextDisabled($"Seal EV: {(ev / KingcakeDesynth.KingcakeSealsPerPurchase):N2} gil per seal");

        if (!ImGui.BeginTable("##kingcakeDrops", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Drop %");
        ImGui.TableSetupColumn("Price");
        ImGui.TableSetupColumn("EV");
        ImGui.TableSetupColumn("Source");
        ImGui.TableHeadersRow();

        foreach (var drop in KingcakeDesynth.Drops)
        {
            var price = ResolveDesynthPrice(cfg, drop, out var source);
            var contribution = price * drop.DropRate;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(drop.Name);
            ImGui.TableNextColumn();
            ImGui.Text($"{drop.DropRate:P2}");
            ImGui.TableNextColumn();
            ImGui.Text($"{price:N0}");
            ImGui.TableNextColumn();
            ImGui.Text($"{contribution:N0}");
            ImGui.TableNextColumn();
            ImGui.TextDisabled(source);
        }

        ImGui.EndTable();
    }

    private void DrawDesynthProjections(Configuration cfg)
    {
        var targetGil = cfg.DesynthTargetGil;
        ImGui.SetNextItemWidth(140);
        if (ImGui.InputInt("Target gil", ref targetGil, 100000, 1000000))
        {
            cfg.DesynthTargetGil = Math.Max(0, targetGil);
            cfg.Save();
        }

        var cycles = cfg.DesynthCyclesPerDay;
        ImGui.SetNextItemWidth(140);
        if (ImGui.InputInt("Cycles per day", ref cycles, 1, 5))
        {
            cfg.DesynthCyclesPerDay = Math.Max(1, cycles);
            cfg.Save();
        }

        var ev = CalculateKingcakeEv(cfg);
        var currentSeals = FarmController.GetCurrentSeals();
        var currentKingcakes = currentSeals / KingcakeDesynth.KingcakeSealsPerPurchase;
        var currentCycleGil = currentKingcakes * ev;
        var targetKingcakes = ev <= 0 ? 0 : (int)Math.Ceiling(cfg.DesynthTargetGil / ev);
        var targetSeals = targetKingcakes * KingcakeDesynth.KingcakeSealsPerPurchase;

        ImGui.TextDisabled($"Current seals can buy about {currentKingcakes:N0} Kingcake(s), EV {currentCycleGil:N0} gil.");
        ImGui.TextDisabled($"Target needs about {targetKingcakes:N0} Kingcake(s), or {targetSeals:N0} seals.");
        ImGui.TextDisabled($"Daily projected EV at {cfg.DesynthCyclesPerDay:N0} cycle(s): {(currentCycleGil * cfg.DesynthCyclesPerDay):N0} gil.");
    }

    private void DrawDesynthAlertsAndOverrides(Configuration cfg)
    {
        if (ImGui.CollapsingHeader("Price alerts", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var enabled = cfg.MooglePriceAlertEnabled;
            if (ImGui.Checkbox("Alert when Moogle Miniature is below threshold", ref enabled))
            {
                cfg.MooglePriceAlertEnabled = enabled;
                cfg.Save();
            }

            var threshold = cfg.MooglePriceAlertThreshold;
            ImGui.SetNextItemWidth(140);
            if (ImGui.InputInt("Moogle Miniature threshold", ref threshold, 1000, 10000))
            {
                cfg.MooglePriceAlertThreshold = Math.Max(0, threshold);
                cfg.Save();
            }

            var moogle = KingcakeDesynth.Drops.FirstOrDefault(d => d.Name == "Moogle Miniature");
            if (moogle.ItemId != 0)
            {
                var price = ResolveDesynthPrice(cfg, moogle, out _);
                if (cfg.MooglePriceAlertEnabled && price > 0 && price < cfg.MooglePriceAlertThreshold)
                    ImGui.TextColored(ColYellow, $"Moogle Miniature is below threshold: {price:N0} gil.");
            }
        }

        if (!ImGui.CollapsingHeader("Manual price overrides"))
            return;

        ImGui.TextDisabled("Enable an override to use that price instead of live Universalis/fallback data.");
        foreach (var drop in KingcakeDesynth.Drops)
        {
            ImGui.PushID($"override-{drop.ItemId}");
            var enabled = cfg.DesynthPriceOverrideEnabled.GetValueOrDefault(drop.ItemId);
            if (ImGui.Checkbox("##enabled", ref enabled))
            {
                cfg.DesynthPriceOverrideEnabled[drop.ItemId] = enabled;
                cfg.Save();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(130);
            var price = cfg.DesynthPriceOverrides.GetValueOrDefault(drop.ItemId, drop.FallbackPrice);
            if (ImGui.InputInt(drop.Name, ref price, 100, 1000))
            {
                cfg.DesynthPriceOverrides[drop.ItemId] = Math.Max(0, price);
                cfg.Save();
            }
            ImGui.PopID();
        }
    }

    private void DrawStatsTab(Configuration cfg)
    {
        EnsureDesynthDefaultsOnce(cfg);
        var stats = DesynthTracker.Stats;
        var ev = CalculateKingcakeEv(cfg);

        UiTheme.SectionTitle("Kingcake desynth statistics");
        ImGui.TextDisabled("Stats are persisted to DesynthStats.json in the SealBreaker plugin config folder.");
        ImGui.Text($"Total Kingcakes desynthed: {stats.TotalKingcakesDesynthed:N0}");
        ImGui.Text($"Observed total value: {CalculateObservedDesynthValue(cfg):N0} gil");
        ImGui.Text($"Estimated EV/sample: {ev:N0} gil");
        if (stats.FirstDesynth != default)
            ImGui.TextDisabled($"First: {stats.FirstDesynth:g}   Last: {stats.LastDesynth:g}");

        ImGui.Separator();
        DrawDesynthStatsTable(cfg);
        ImGui.Separator();
        DrawDesynthConfidence(stats.TotalKingcakesDesynthed);
        ImGui.Separator();
        DrawDesynthStatsActions();
    }

    private void DrawDesynthStatsTable(Configuration cfg)
    {
        if (!ImGui.BeginTable("##desynthStats", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Count");
        ImGui.TableSetupColumn("Observed");
        ImGui.TableSetupColumn("Expected");
        ImGui.TableSetupColumn("Value");
        ImGui.TableHeadersRow();

        foreach (var drop in KingcakeDesynth.Drops)
        {
            DesynthTracker.Stats.ItemStats.TryGetValue(drop.ItemId, out var stat);
            var observed = DesynthTracker.ObservedRate(drop.ItemId);
            var variance = observed - drop.DropRate;
            var price = ResolveDesynthPrice(cfg, drop, out _);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(drop.Name);
            ImGui.TableNextColumn();
            ImGui.Text($"{stat?.TimesObtained ?? 0:N0}");
            ImGui.TableNextColumn();
            ImGui.TextColored(VarianceColor(variance), $"{observed:P2}");
            ImGui.TableNextColumn();
            ImGui.Text($"{drop.DropRate:P2}");
            ImGui.TableNextColumn();
            ImGui.Text($"{(stat?.TimesObtained ?? 0) * price:N0}");
        }

        ImGui.EndTable();
    }

    private static void DrawDesynthConfidence(int sampleSize)
    {
        var label = sampleSize switch
        {
            >= 500 => "High confidence",
            >= 100 => "Medium confidence",
            >= 30 => "Low confidence",
            _ => "Very low confidence",
        };
        var color = sampleSize >= 100 ? ColGreen : sampleSize >= 30 ? ColYellow : ColGray;
        ImGui.TextColored(color, $"{label} ({sampleSize:N0} sample size)");
    }

    private void DrawDesynthStatsActions()
    {
        if (ImGui.Button("Export to CSV"))
        {
            _desynthExportStatus = DesynthTracker.ExportCsv()
                ? $"Exported to {DesynthTracker.ExportPath}"
                : "CSV export failed; check plugin log.";
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset Statistics"))
            _confirmResetDesynthStats = true;

        if (_confirmResetDesynthStats)
        {
            ImGui.TextColored(ColYellow, "Confirm reset? This clears local desynth stats.");
            if (ImGui.Button("Confirm reset"))
            {
                DesynthTracker.Reset();
                _confirmResetDesynthStats = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel reset"))
                _confirmResetDesynthStats = false;
        }

        if (!string.IsNullOrWhiteSpace(_desynthExportStatus))
            ImGui.TextWrapped(_desynthExportStatus);
    }

    private int ResolveDesynthPrice(Configuration cfg, DesynthDrop drop, out string source)
    {
        if (cfg.DesynthPriceOverrideEnabled.GetValueOrDefault(drop.ItemId)
            && cfg.DesynthPriceOverrides.TryGetValue(drop.ItemId, out var overridePrice)
            && overridePrice > 0)
        {
            source = "Manual";
            return overridePrice;
        }

        if (_desynthPrices != null
            && _desynthPrices.TryGetValue(drop.ItemId, out var market)
            && market.LowestPrice > 0)
        {
            source = "Live";
            return market.LowestPrice;
        }

        source = "Fallback";
        return drop.FallbackPrice;
    }

    private double CalculateKingcakeEv(Configuration cfg)
    {
        var total = 0d;
        foreach (var drop in KingcakeDesynth.Drops)
            total += ResolveDesynthPrice(cfg, drop, out _) * drop.DropRate;
        return total;
    }

    private double CalculateObservedDesynthValue(Configuration cfg)
    {
        var total = 0d;
        foreach (var drop in KingcakeDesynth.Drops)
        {
            DesynthTracker.Stats.ItemStats.TryGetValue(drop.ItemId, out var stat);
            total += (stat?.TimesObtained ?? 0) * ResolveDesynthPrice(cfg, drop, out _);
        }
        return total;
    }

    private static Vector4 VarianceColor(float variance)
    {
        if (Math.Abs(variance) < 0.02f)
            return ColGray;
        return variance > 0 ? ColGreen : ColYellow;
    }

    private static void DrawConfigTab(Configuration cfg)
    {
        UiTheme.SectionTitle("General configuration");

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

        var showWindowBanner = cfg.ShowWindowBanner;
        if (ImGui.Checkbox("Show window banner", ref showWindowBanner))
        {
            cfg.ShowWindowBanner = showWindowBanner;
            cfg.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Disable this if the banner image or custom title font causes display/rendering issues.");

        if (cfg.DutyRunner == 0 && ImGui.CollapsingHeader("AutoDuty Dungeon", ImGuiTreeNodeFlags.DefaultOpen))
            DrawAutoDutyDutySection(cfg);

        if (cfg.DutyRunner == 1 && ImGui.CollapsingHeader("ADS Duty Support", ImGuiTreeNodeFlags.DefaultOpen))
            DrawAdsDutySupportSection(cfg);

        if (ImGui.CollapsingHeader("Repair", ImGuiTreeNodeFlags.DefaultOpen))
            DrawRepairConfigSection(cfg);

        DrawGrandCompanyOverrideSection(cfg);

        var autoDismiss = cfg.AutoDismissGcOfficerMenu;
        if (ImGui.Checkbox("Auto-close GC officer menus", ref autoDismiss))
        { cfg.AutoDismissGcOfficerMenu = autoDismiss; cfg.Save(); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When enabled, SealBreaker closes the personnel officer prompt after delivery/shop automation.\nDisable to talk to the officer manually while the plugin is loaded.");

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Materia extraction"))
            DrawMateriaExtractionSection(cfg);
    }

    private static void DrawGrandCompanyOverrideSection(Configuration cfg)
    {
        if (GrandCompanyState.TryGetDetected(out var detectedGcIdx, out var detectedRank, out var detectedSealCap))
        {
            ImGui.TextDisabled($"Detected GC: {GrandCompanyState.GrandCompanyName(detectedGcIdx)}");
            ImGui.TextDisabled($"Detected rank: {GrandCompanyState.RankName(detectedRank)} — seal cap {detectedSealCap:N0}");
        }
        else
        {
            ImGui.TextColored(ColYellow, "Could not detect current Grand Company/rank; using saved values.");
        }

        var open = ImGui.CollapsingHeader("Manual GC/seal override");
        ImGui.SameLine();
        DrawInfoMarker("Use at your own risk or for debugging only. When override is off, SealBreaker automatically uses your current Grand Company and rank-based seal cap.");
        if (!open)
            return;

        ImGui.Indent();
        var useOverride = cfg.UseGrandCompanyOverride;
        if (ImGui.Checkbox("Use manual override", ref useOverride))
        {
            cfg.UseGrandCompanyOverride = useOverride;
            if (!useOverride)
                cfg.ApplyAutomaticGrandCompanySettings();
            cfg.Save();
        }

        if (!cfg.UseGrandCompanyOverride)
            ImGui.BeginDisabled();

        var gcItems = new[] { "Maelstrom (Limsa)", "Order of the Twin Adder (Gridania)", "Immortal Flames (Ul'dah)" };
        var gcIdx = cfg.GrandCompanyIndex;
        if (ImGui.Combo("Grand Company", ref gcIdx, gcItems, gcItems.Length))
        {
            cfg.GrandCompanyIndex = gcIdx;
            cfg.Save();
        }

        var sealCap = cfg.SealCap;
        if (ImGui.SliderInt("Seal cap", ref sealCap, 10000, 90000))
        {
            cfg.SealCap = sealCap;
            cfg.Save();
        }

        var sealRes = cfg.SealReserve;
        if (ImGui.SliderInt("Seal reserve", ref sealRes, 0, 10000))
        {
            cfg.SealReserve = sealRes;
            cfg.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Stop buying from the buy list when seals drop to this amount.");

        if (!cfg.UseGrandCompanyOverride)
            ImGui.EndDisabled();

        ImGui.Unindent();
    }

    private static void DrawInfoMarker(string tooltip)
    {
        var size = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##infoMarker", new Vector2(size, size));

        var drawList = ImGui.GetWindowDrawList();
        var center = new Vector2(pos.X + size * 0.5f, pos.Y + size * 0.5f);
        drawList.AddCircleFilled(
            center,
            size * 0.42f,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.45f, 0.45f, 0.45f, 1f)));

        var text = "i";
        var textSize = ImGui.CalcTextSize(text);
        drawList.AddText(
            new Vector2(center.X - textSize.X * 0.5f, center.Y - textSize.Y * 0.5f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.95f, 0.95f, 1f)),
            text);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    private static void DrawRepairConfigSection(Configuration cfg)
    {
        var repairEnabled = cfg.RepairEnabled;
        if (ImGui.Checkbox("Repair between runs", ref repairEnabled))
        {
            cfg.RepairEnabled = repairEnabled;
            cfg.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Checks gear before run 1 of each duty cycle. Mid-cycle runs do not repair.");

        if (!cfg.RepairEnabled)
            ImGui.BeginDisabled();

        var repairPct = cfg.RepairThresholdPercent;
        if (ImGui.SliderInt("Repair below %", ref repairPct, 10, 90))
        {
            cfg.RepairThresholdPercent = repairPct;
            cfg.Save();
        }

        var repairProvider = Math.Clamp(cfg.RepairProvider, 0, RepairProviderItems.Length - 1);
        if (ImGui.Combo("Repair option", ref repairProvider, RepairProviderItems, RepairProviderItems.Length))
        {
            cfg.RepairProvider = repairProvider;
            cfg.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Seal Breaker uses the configured GC mender route. ADS sends an /ads repair command.");

        if (cfg.RepairProvider == Configuration.RepairProviderAds)
        {
            var adsMode = Math.Clamp(cfg.AdsRepairMode, 0, AdsRepairModeItems.Length - 1);
            if (ImGui.Combo("ADS repair mode", ref adsMode, AdsRepairModeItems, AdsRepairModeItems.Length))
            {
                cfg.AdsRepairMode = adsMode;
                cfg.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("/ads repair self|npc|npc-no-inn|npc-no-teleport-no-inn");

            if (!IpcManager.AdsPluginLoaded)
                ImGui.TextColored(ColYellow, "ADS is not loaded; repair command will be skipped.");
        }
        else
        {
            ImGui.TextDisabled($"Configure mender and repair route on the GC Towns tab ({GcTownTabNames[cfg.GrandCompanyIndex]}).");
        }

        if (!cfg.RepairEnabled)
            ImGui.EndDisabled();
    }

    private static readonly string[] AutoDutyModeItems =
        ["Use AutoDuty's setting", "Duty Support", "Trust", "Regular (party finder queue)", "Squadron"];

    private static uint _autoDutyPathCheckTerritory;
    private static bool _autoDutyPathCheckResult = true;
    private static DateTime _autoDutyPathCheckAt = DateTime.MinValue;

    private static void DrawAutoDutyDutySection(Configuration cfg)
    {
        AutoDutyCatalog.EnsureInitialized();
        var duties = AutoDutyCatalog.Duties.ToList();
        if (duties.Count == 0)
        {
            ImGui.TextColored(ColYellow, "No dungeons found in game data.");
            return;
        }

        var selectedIndex = AutoDutyCatalog.IndexOfSelected(cfg);
        var labels = duties.Select(AutoDutyCatalog.FormatLabel).ToArray();
        ImGui.SetNextItemWidth(320);
        if (ImGui.Combo("Dungeon", ref selectedIndex, labels, labels.Length))
            AutoDutyCatalog.ApplySelection(cfg, duties[Math.Clamp(selectedIndex, 0, duties.Count - 1)]);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("AutoDuty runs this dungeon each cycle. AutoDuty must have a path for it.");

        var mode = Math.Clamp(cfg.AutoDutyDutyMode, 0, AutoDutyModeItems.Length - 1);
        ImGui.SetNextItemWidth(320);
        if (ImGui.Combo("Duty mode", ref mode, AutoDutyModeItems, AutoDutyModeItems.Length))
        {
            cfg.AutoDutyDutyMode = mode;
            cfg.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Sets AutoDuty's duty mode before each run.\n"
                + "Duty Support / Trust run with NPCs — no other players needed, works on free trial (FTP) accounts.\n"
                + "'Use AutoDuty's setting' leaves whatever mode is configured in AutoDuty itself.");

        var selected = AutoDutyCatalog.SelectedOrDefault(cfg);
        ImGui.TextDisabled($"Selected: {selected.Name} (territory {selected.TerritoryType})");

        if (cfg.AutoDutyDutyMode is Configuration.AutoDutyModeSupport or Configuration.AutoDutyModeTrust
            && !selected.HasDutySupport)
        {
            ImGui.TextColored(ColYellow, "This dungeon may not offer Duty Support/Trust — AutoDuty could fail to queue.");
        }

        if (IpcManager.AutoDutyAvailable)
        {
            var now = DateTime.UtcNow;
            if (_autoDutyPathCheckTerritory != selected.TerritoryType || now - _autoDutyPathCheckAt > TimeSpan.FromSeconds(5))
            {
                _autoDutyPathCheckTerritory = selected.TerritoryType;
                _autoDutyPathCheckResult = IpcManager.AutoDutyContentHasPath(selected.TerritoryType);
                _autoDutyPathCheckAt = now;
            }

            if (!_autoDutyPathCheckResult)
                ImGui.TextColored(ColYellow, "AutoDuty has no path for this dungeon — it cannot run it.");
        }
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
        using (UiTheme.Card())
        {
            UiTheme.SectionTitle("Step 1 — Required plugins");
            DrawPluginStatus("vnavmesh", IpcManager.VnavAvailable);
            DrawPluginStatus("Lifestream", IpcManager.LifestreamAvailable);
            DrawPluginStatus(cfg.DutyRunner == 0 ? "AutoDuty" : "ADS", cfg.DutyRunner == 0 ? IpcManager.AutoDutyAvailable : IpcManager.AdsAvailable);
        }

        using (UiTheme.Card())
        {
            UiTheme.SectionTitle("Step 2 — Game settings");
            DrawGuideWarning("Disable auto-equip new gear: Character Config → Item Settings → Equip Retrieved Gear → OFF — gear must stay in your bags for Expert Delivery to see it");
            DrawGuideWarning("Ensure your Grand Company rank is high enough for Expert Delivery (Second Lieutenant or above)");
            DrawGuideWarning("Make sure your inventory has free space before starting — a full inventory will cause drops to be lost");
        }

        using (UiTheme.Card())
        {
            UiTheme.SectionTitle("Step 3 — Grand Company setup");
            DrawGuideItem("Your Grand Company and seal cap are detected automatically (override on the Config tab)");
            DrawGuideItem($"Current seals: {FarmController.GetCurrentSeals():N0} / {cfg.SealCap:N0}");
            DrawGuideItem("Add the items to buy with seals on the Buy List tab — the Duck Bones preset is the default gil loop");
        }

        using (UiTheme.Card())
        {
            UiTheme.SectionTitle("Step 4 — First run checklist");
            DrawGuideCheck("Set Runs Per Cycle to 1 for your first test run");
            DrawGuideCheck("Set List Mode to Off so all gear is turned in automatically");
            DrawGuideCheck("Click Start and watch the Farm tab log output");
            DrawGuideCheck("Verify: zones into dungeon → completes run → teleports to GC → delivers gear → buys items → loops");
            DrawGuideCheck("Once confirmed working, increase Runs Per Cycle and configure your item filter");
        }

        using (UiTheme.Card())
        {
            UiTheme.SectionTitle("Step 5 — Item filter setup");
            DrawGuideItem("Run one cycle with List Mode = Off");
            DrawGuideItem("Check plugin log for 'DROP: ItemID XXXXX' lines to find IDs of dropped gear");
            DrawGuideItem("Add any item IDs you want to KEEP to the Protected Item IDs list in Configuration");
            DrawGuideItem("Switch List Mode to Blacklist — everything except your protected IDs will be turned in");
            DrawGuideItem("Use Whitelist mode instead if you only want to deliver specific items and keep everything else");
        }
    }

    private static void DrawGuideWarning(string text)
    {
        UiTheme.Icon(FontAwesomeIcon.ExclamationTriangle, UiTheme.Yellow);
        ImGui.SameLine(0, 6);
        ImGui.TextWrapped(text);
    }

    private static void DrawGuideItem(string text)
    {
        UiTheme.Icon(FontAwesomeIcon.AngleRight, UiTheme.Gray);
        ImGui.SameLine(0, 6);
        ImGui.TextWrapped(text);
    }

    private static void DrawGuideCheck(string text)
    {
        UiTheme.Icon(FontAwesomeIcon.Check, UiTheme.Green);
        ImGui.SameLine(0, 6);
        ImGui.TextWrapped(text);
    }

    private static void DrawPluginStatus(string pluginName, bool available)
    {
        UiTheme.Icon(available ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.TimesCircle, available ? UiTheme.Green : UiTheme.Red);
        ImGui.SameLine(0, 6);
        ImGui.TextColored(
            available ? UiTheme.Green : UiTheme.Red,
            $"{pluginName}: {(available ? "Installed" : "NOT DETECTED — install from your plugin list")}");
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
        var town = cfg.TownNav(gcIdx);

        ImGui.TextColored(ColGray, GcTownFullNames[gcIdx]);
        if (gcIdx == cfg.GrandCompanyIndex)
            ImGui.TextColored(ColGreen, "  ← active Grand Company");
        else
            ImGui.TextColored(ColGray, "  (configure now, switch GC on Config tab when ready)");

        if (ImGui.CollapsingHeader("Seal Breaker Repair", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawGcTownRepairSection(cfg, town, gcIdx);
            ImGui.Separator();
            DrawGcRouteSection(cfg, town, gcIdx, GcRouteKind.Repair);
            if (GcNavRoutes.BakedRepairReturnCount(gcIdx) > 0 || town.UseCustomRepairReturnNavWaypoints)
            {
                ImGui.Separator();
                DrawGcRouteSection(cfg, town, gcIdx, GcRouteKind.RepairReturn);
            }
        }

        if (ImGui.CollapsingHeader("GC navigation route", ImGuiTreeNodeFlags.DefaultOpen))
            DrawGcRouteSection(cfg, town, gcIdx, GcRouteKind.Approach);

        if (ImGui.CollapsingHeader("GC corridor route"))
            DrawGcRouteSection(cfg, town, gcIdx, GcRouteKind.Corridor);
    }

    private enum GcRouteKind { Approach, Corridor, Repair, RepairReturn }

    private static void DrawGcTownRepairSection(Configuration cfg, GcTownNavSettings town, int gcIdx)
    {
        ImGui.TextColored(ColYellow, "Mender NPC");
        ImGui.TextDisabled("Used only when Config > Repair option is Seal Breaker.");

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
        var (useCustom, bakedCount, label, tooltip, idPrefix, waypoints) = kind switch
        {
            GcRouteKind.Approach => (
                town.UseCustomGcNavWaypoints,
                GcNavRoutes.BakedGcApproachCount(gcIdx),
                gcIdx == 0 ? "Approach waypoints" : "Entry waypoints",
                gcIdx == 0
                    ? "Y=40 supply deck walk (after port-in). Skipped on main deck."
                    : "Walk from city arrival to GC staging area.",
                $"gcnav{gcIdx}",
                town.GcNavWaypoints),
            GcRouteKind.Corridor => (
                town.UseCustomGcCorridorWaypoints,
                GcNavRoutes.BakedGcCorridorCount(gcIdx),
                "Corridor waypoints",
                gcIdx == 0
                    ? "Main deck Aftcastle → GC command corridor."
                    : "Optional second segment (stairs/hallway to officer area).",
                $"gccor{gcIdx}",
                town.GcCorridorWaypoints),
            GcRouteKind.Repair => (
                town.UseCustomRepairNavWaypoints,
                GcNavRoutes.BakedRepairCount(gcIdx),
                "Repair waypoints",
                "GC area → mender forward.",
                $"repair{gcIdx}",
                town.RepairNavWaypoints),
            GcRouteKind.RepairReturn => (
                town.UseCustomRepairReturnNavWaypoints,
                GcNavRoutes.BakedRepairReturnCount(gcIdx),
                "Repair return waypoints",
                "Mender area → GC interact point after repair.",
                $"repairret{gcIdx}",
                town.RepairReturnNavWaypoints),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        ImGui.TextColored(ColYellow, label);
        var useCustomLocal = useCustom;
        if (ImGui.Checkbox($"Use custom route (override baked-in)##{idPrefix}", ref useCustomLocal))
        {
            if (useCustomLocal && !useCustom && waypoints.Count == 0)
                waypoints.AddRange(GetRouteEditorDefaults(cfg, gcIdx, kind).Select(NavWaypoint.From));

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
                case GcRouteKind.RepairReturn:
                    town.UseCustomRepairReturnNavWaypoints = useCustomLocal;
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

    private static Vector3[] GetRouteEditorDefaults(Configuration cfg, int gcIdx, GcRouteKind kind) => kind switch
    {
        GcRouteKind.Approach => GcNavRoutes.GetGcApproachPath(cfg, gcIdx),
        GcRouteKind.Corridor => GcNavRoutes.GetGcCorridorPath(cfg, gcIdx),
        GcRouteKind.Repair => GcNavRoutes.GetRepairPath(cfg, gcIdx),
        GcRouteKind.RepairReturn => GcNavRoutes.GetRepairReturnPath(cfg, gcIdx),
        _ => [],
    };

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
        var cfg = Plugin.Config;

        using (UiTheme.Card())
        {
            var (label, col) = ctrl.State switch
            {
                FarmController.FarmState.Idle  => ("Idle",    UiTheme.Gray),
                FarmController.FarmState.Error => ("Error",   UiTheme.Red),
                _                              => ("Running", UiTheme.Green),
            };

            UiTheme.StatusDot(col);
            ImGui.SameLine(0, 5);
            ImGui.TextColored(col, label);

            ImGui.SameLine(0, 8);
            var dutyName = cfg.DutyRunner == 0
                ? AutoDutyCatalog.SelectedOrDefault(cfg).Name
                : DutySupportCatalog.SelectedOrDefault(cfg).Name;
            var runnerLabel = cfg.DutyRunner == 0
                ? cfg.AutoDutyModeConfigValue() is { } mode ? $"AutoDuty · {mode}" : "AutoDuty"
                : "ADS · Duty Support";
            ImGui.TextColored(UiTheme.TextBright, dutyName);
            ImGui.SameLine(0, 8);
            ImGui.TextColored(UiTheme.Gray, runnerLabel);

            if (ctrl.IsRunning)
            {
                var elapsed = DateTime.Now - ctrl.StartTime;
                ImGui.SameLine();
                UiTheme.RightAlignedText($"{elapsed:hh\\:mm\\:ss}", UiTheme.Gray);
            }

            if (ctrl.IsRunning && !ctrl.IsAnyTestMode && cfg.RunsPerCycle > 0)
            {
                var done = Math.Clamp(ctrl.RunsThisCycle, 0, cfg.RunsPerCycle);
                var current = Math.Min(done + 1, cfg.RunsPerCycle);
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, UiTheme.GreenDark);
                ImGui.ProgressBar(done / (float)cfg.RunsPerCycle, new Vector2(-1, 16), $"Run {current} / {cfg.RunsPerCycle}");
                ImGui.PopStyleColor();
            }

            ImGui.TextColored(UiTheme.Gray, ctrl.StatusMessage);
        }

        if (ctrl.LastError != null)
        {
            using (UiTheme.Card())
            {
                UiTheme.Icon(FontAwesomeIcon.ExclamationTriangle, UiTheme.Red);
                ImGui.SameLine(0, 6);
                ImGui.TextColored(UiTheme.Red, "Last error");
                ImGui.TextWrapped(ctrl.LastError);
            }
        }
    }

    private static void DrawStatsPanel(FarmController ctrl)
    {
        var elapsed = ctrl.IsRunning
            ? DateTime.Now - ctrl.StartTime
            : TimeSpan.Zero;
        var duckBonesTotal = FarmController.GetDuckBoneInventoryCount();
        var duckBonesValue = duckBonesTotal * 360;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(9, 6));
        if (ImGui.BeginTable("##farmMetrics", 3, ImGuiTableFlags.SizingStretchSame))
        {
            var cellBg = ImGui.ColorConvertFloat4ToU32(UiTheme.CardBg);
            void Cell(string label, string value, Vector4? color = null)
            {
                ImGui.TableNextColumn();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, cellBg);
                UiTheme.MetricCell(label, value, color);
            }

            Cell("Seals earned", $"{ctrl.TotalSeals:N0}", UiTheme.Accent);
            Cell("Runs", $"{ctrl.TotalRuns}");
            Cell("Cycles", $"{ctrl.TotalCycles}");
            Cell("Duck bones", $"{duckBonesTotal:N0}");
            Cell("Bought", $"{ctrl.TotalDuckbones:N0}");
            Cell("Est. value", $"{duckBonesValue:N0}g", UiTheme.Green);
            Cell("Runtime", $"{elapsed:hh\\:mm\\:ss}");
            if (ctrl.TotalRunsTracked > 0)
            {
                Cell("Avg clear", $"{ctrl.AverageClearTime:mm\\:ss}");
                Cell("Best / worst", $"{ctrl.FastestClearTime:mm\\:ss} / {ctrl.SlowestClearTime:mm\\:ss}");
            }
            else
            {
                Cell("Avg clear", "—");
                Cell("Best / worst", "—");
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        ImGui.Spacing();
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
            return;
        }

        var removeIdx = -1;
        var moveFrom = -1;
        var moveTo = -1;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 3));

        static float IconButtonWidth(FontAwesomeIcon icon)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            var glyphWidth = ImGui.CalcTextSize(icon.ToIconString()).X;
            ImGui.PopFont();
            return glyphWidth + ImGui.GetStyle().FramePadding.X * 2;
        }

        var actionsWidth = IconButtonWidth(FontAwesomeIcon.ArrowUp)
                           + IconButtonWidth(FontAwesomeIcon.ArrowDown)
                           + IconButtonWidth(FontAwesomeIcon.Edit)
                           + IconButtonWidth(FontAwesomeIcon.Trash)
                           + 3 * 2 + 6;

        if (ImGui.BeginTable("##buyListTable", 6,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX))
        {
            ImGui.TableSetupColumn("##on", ImGuiTableColumnFlags.WidthFixed, 26);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Keep", ImGuiTableColumnFlags.WidthFixed, 52);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 48);
            ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 52);
            ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, actionsWidth);
            ImGui.TableHeadersRow();

            for (var i = 0; i < buyList.Count; i++)
            {
                var entry = buyList[i];
                ImGui.PushID(i);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                var enabled = entry.Enabled;
                if (ImGui.Checkbox("##enabled", ref enabled))
                { entry.Enabled = enabled; cfg.Save(); }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Enable/disable this entry.");

                ImGui.TableNextColumn();
                var label = string.IsNullOrWhiteSpace(entry.ItemName) ? "(unnamed)" : entry.ItemName;
                ImGui.TextColored(entry.Enabled ? UiTheme.Accent : UiTheme.Gray, label);
                ImGui.SameLine(0, 6);
                ImGui.TextColored(UiTheme.Gray, $"{entry.SealCost:N0}s");

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var keep = entry.KeepAmount;
                if (ImGui.InputInt("##keep", ref keep, 0, 0))
                { entry.KeepAmount = Math.Max(0, keep); cfg.Save(); }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Keep amount — stop buying at this inventory count. 0 = buy until seal reserve.");

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var qty = entry.BuyQtyPerPurchase;
                if (ImGui.InputInt("##qty", ref qty, 0, 0))
                { entry.BuyQtyPerPurchase = Math.Clamp(qty, 0, 99); cfg.Save(); }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Buy quantity per purchase. 0 = max affordable (up to 99).");

                ImGui.TableNextColumn();
                ImGui.Text($"{FarmController.GetInventoryItemCountPublic(entry):N0}");

                ImGui.TableNextColumn();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowUp) && i > 0)
                { moveFrom = i; moveTo = i - 1; }
                ImGui.SameLine(0, 2);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowDown) && i < buyList.Count - 1)
                { moveFrom = i; moveTo = i + 1; }
                ImGui.SameLine(0, 2);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Edit))
                {
                    var wasEditing = _buyEditGc == gcIdx && _buyEditIdx == i;
                    _buyEditGc = wasEditing ? -1 : gcIdx;
                    _buyEditIdx = wasEditing ? -1 : i;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Edit name, item ID, and shop tuning.");
                ImGui.SameLine(0, 2);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
                    removeIdx = i;

                ImGui.PopID();
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(2);

        if (moveFrom >= 0 && moveTo >= 0)
        {
            (buyList[moveFrom], buyList[moveTo]) = (buyList[moveTo], buyList[moveFrom]);
            if (_buyEditGc == gcIdx && _buyEditIdx == moveFrom)
                _buyEditIdx = moveTo;
            else if (_buyEditGc == gcIdx && _buyEditIdx == moveTo)
                _buyEditIdx = moveFrom;
            cfg.Save();
        }

        if (removeIdx >= 0)
        {
            buyList.RemoveAt(removeIdx);
            if (_buyEditGc == gcIdx && _buyEditIdx == removeIdx)
            { _buyEditGc = -1; _buyEditIdx = -1; }
            else if (_buyEditGc == gcIdx && _buyEditIdx > removeIdx)
                _buyEditIdx--;
            cfg.Save();
        }

        if (_buyEditGc == gcIdx && _buyEditIdx >= 0 && _buyEditIdx < buyList.Count)
            DrawBuyEntryEditor(cfg, gcIdx, buyList[_buyEditIdx]);
    }

    private static int _buyEditGc = -1;
    private static int _buyEditIdx = -1;

    private static void DrawBuyEntryEditor(Configuration cfg, int gcIdx, GcShopBuyEntry entry)
    {
        ImGui.Spacing();
        using (UiTheme.Card())
        {
            ImGui.TextColored(UiTheme.Accent, $"Edit entry #{_buyEditIdx + 1}");
            ImGui.SameLine();
            var closeWidth = ImGui.CalcTextSize("Close").X + 14;
            var avail = ImGui.GetContentRegionAvail().X;
            if (avail > closeWidth)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - closeWidth);
            if (ImGui.SmallButton("Close"))
            { _buyEditGc = -1; _buyEditIdx = -1; return; }

            DrawEntryCatalogPicker(cfg, entry, gcIdx);

            var name = entry.ItemName;
            if (ImGui.InputText("Item name", ref name, 128))
            { entry.ItemName = name; cfg.Save(); }

            var itemId = (int)entry.ItemId;
            if (ImGui.InputInt("Item ID (0=lookup)", ref itemId))
            { entry.ItemId = (uint)Math.Max(0, itemId); cfg.Save(); }

            ImGui.Spacing();
            ImGui.TextColored(UiTheme.Gray, "Shop tuning (manual override)");
            ImGui.TextDisabled("Seal cost, tabs, and list row are auto-resolved from game data when possible.");

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

        if (ImGui.Button("Delivery"))
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

        if (ImGui.Button("Shop"))
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

    private static void DrawKingcakeTestButton(FarmController ctrl)
    {
        var canRun = IpcManager.VnavAvailable
                     && IpcManager.LifestreamAvailable
                     && !ctrl.IsRunning;

        if (!canRun)
            ImGui.BeginDisabled();

        if (ImGui.Button("Kingcake"))
            ctrl.StartKingcakeBuyTest();

        if (!canRun)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (!IpcManager.VnavAvailable || !IpcManager.LifestreamAvailable)
                ImGui.SetTooltip("Requires vnavmesh and Lifestream.");
            else if (ctrl.IsRunning)
                ImGui.SetTooltip("Stop the farm before running the one-shot Kingcake buy test.");
            else
                ImGui.SetTooltip("Navigate to the quartermaster and fire one attempt to buy 1 Kingcake.");
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

        if (ImGui.Button("Repair"))
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

        if (ImGui.Button("Extract"))
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

        DrawPrereqChips(cfg);
        DrawGrandCompanyLine(cfg);
        ImGui.Spacing();

        var dutyReady = cfg.DutyRunner == 0
            ? IpcManager.AutoDutyAvailable
            : IpcManager.AdsAvailable;

        var allReady = dutyReady
                    && IpcManager.VnavAvailable
                    && IpcManager.LifestreamAvailable;

        var buttonSize = new Vector2(ImGui.GetContentRegionAvail().X, 34);
        if (ctrl.IsRunning)
        {
            if (UiTheme.StopButton("Stop farm##farm", buttonSize)) ctrl.Stop();
        }
        else
        {
            if (!allReady) ImGui.BeginDisabled();
            if (UiTheme.StartButton("Start farm##farm", buttonSize)) ctrl.Start();
            if (!allReady) ImGui.EndDisabled();
            if (!allReady && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Install/enable the plugins marked red above.");
        }

        ImGui.Spacing();
        ImGui.TextColored(UiTheme.Gray, "Tests");
        ImGui.SameLine();
        DrawExpertDeliveryTestButton(ctrl);
        ImGui.SameLine();
        DrawShopTestButton(ctrl);
        ImGui.SameLine();
        DrawRepairTestButton(ctrl);
        ImGui.SameLine();
        DrawExtractTestButton(ctrl);
        ImGui.SameLine();
        DrawKingcakeTestButton(ctrl);

        if (ctrl.State == FarmController.FarmState.Error)
        {
            ImGui.Spacing();
            if (UiTheme.SolidButton("Clear error", UiTheme.RedDark, new Vector2(120, 26))) ctrl.Stop();
        }
    }

    private static void DrawGrandCompanyLine(Configuration cfg)
    {
        var detected = GrandCompanyState.TryGetDetected(out var gcIdx, out var rank, out var sealCap);
        if (!detected)
            gcIdx = cfg.GrandCompanyIndex;

        UiTheme.Icon(FontAwesomeIcon.Flag, detected ? UiTheme.Accent : UiTheme.Gray);
        ImGui.SameLine(0, 6);
        ImGui.TextColored(UiTheme.TextBright, GrandCompanyState.GrandCompanyName(gcIdx));
        ImGui.SameLine(0, 8);
        ImGui.TextColored(
            UiTheme.Gray,
            detected
                ? $"{GrandCompanyState.RankName(rank)} · {GcTownTabNames[gcIdx]}"
                : $"{GcTownTabNames[gcIdx]} (rank not detected — using saved GC)");

        if (detected)
        {
            ImGui.SameLine();
            UiTheme.RightAlignedText($"{FarmController.GetCurrentSeals():N0} / {sealCap:N0} seals", UiTheme.Gray);
        }
    }

    private static void DrawPrereqChips(Configuration cfg)
    {
        if (cfg.DutyRunner == 0)
        {
            if (IpcManager.AutoDutyAvailable)
                UiTheme.Chip(FontAwesomeIcon.Check, "AutoDuty", UiTheme.Green);
            else if (IpcManager.AutoDutyPluginLoaded)
                UiTheme.Chip(FontAwesomeIcon.ExclamationTriangle, "AutoDuty IPC not ready", UiTheme.Yellow);
            else
                UiTheme.Chip(FontAwesomeIcon.Times, "AutoDuty", UiTheme.Red);

            if (IpcManager.AutoDutyPluginLoaded && !IpcManager.AutoDutyAvailable
                && ImGui.IsItemHovered())
                ImGui.SetTooltip("AutoDuty is loaded but the Run IPC is not ready — restart AutoDuty if this persists.");
        }
        else
        {
            if (IpcManager.AdsAvailable)
                UiTheme.Chip(FontAwesomeIcon.Check, "ADS", UiTheme.Green);
            else if (IpcManager.AdsPluginLoaded)
                UiTheme.Chip(FontAwesomeIcon.ExclamationTriangle, "ADS IPC not ready", UiTheme.Yellow);
            else
                UiTheme.Chip(FontAwesomeIcon.Times, "ADS", UiTheme.Red);
        }

        ImGui.SameLine(0, 14);
        UiTheme.Chip(
            IpcManager.VnavAvailable ? FontAwesomeIcon.Check : FontAwesomeIcon.Times,
            "vnavmesh",
            IpcManager.VnavAvailable ? UiTheme.Green : UiTheme.Red);

        ImGui.SameLine(0, 14);
        if (!IpcManager.LifestreamAvailable)
            UiTheme.Chip(FontAwesomeIcon.Times, "Lifestream", UiTheme.Red);
        else if (!IpcManager.LifestreamMoveAvailable)
        {
            UiTheme.Chip(FontAwesomeIcon.ExclamationTriangle, "Lifestream (vnav fallback)", UiTheme.Yellow);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Lifestream.Move IPC unavailable — walking routes fall back to vnavmesh.");
        }
        else
            UiTheme.Chip(FontAwesomeIcon.Check, "Lifestream", UiTheme.Green);
    }
}
