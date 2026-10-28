using Dalamud.Game.Command;
using Dalamud.Interface.Textures;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using SealBreaker.Windows;
using SealBreaker.Windows.Dock;
using SealBreaker.Services;
using System;
using System.IO;

namespace SealBreaker;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/sealbreaker";
    private const string CommandAlias = "/seal";

    public static Configuration  Config     { get; private set; } = null!;
    public static FarmController Controller { get; private set; } = null!;
    public static ISharedImmediateTexture? PluginIcon { get; private set; }
    public static ISharedImmediateTexture? PluginBanner { get; private set; }

    private readonly WindowSystem _windowSystem = new("SealBreaker");
    private readonly MainWindow   _mainWindow;
    private readonly MiniWindow   _miniWindow;
    private readonly EdgeDockWindow _edgeDock;
    private readonly DockLayoutShare _dockLayout;
    private readonly IDalamudPluginInterface _pi;
    private static Plugin _instance = null!;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        _pi = pluginInterface;
        _pi.Create<Service>();

        Config = _pi.GetPluginConfig() as Configuration ?? new Configuration();
        Config.EnsureGcTownNav();
        Config.ApplyAutomaticGrandCompanySettings();
        Controller = new FarmController();
        SealBreakerIpc.Register(_pi); // [STOP-ALERT] [FC-RANK-STOP] FarmStopped, FcRankMilestoneReached, GetFcRank

        var dir = Service.PluginInterface.AssemblyLocation.DirectoryName!;
        PluginIcon = LoadTexture(Path.Combine(dir, "icon.png"));
        PluginBanner = LoadTexture(Path.Combine(dir, "banner.png"));

        GcShopCatalog.EnsureInitialized();
        DesynthTracker.Load();

        _instance = this;
        _mainWindow = new MainWindow();
        _miniWindow = new MiniWindow();
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_miniWindow);

        // The edge dock: the minimized form when Config.EdgeDockEnabled — a tab on the screen edge whose
        // pop-out is the mini widget's own body. The layout is shared with Daedalus's and Charon's tabs so
        // none cover each other (order 10 and 20; ours sorts after them on a shared spot).
        _dockLayout = new DockLayoutShare(_pi, "SealBreaker", order: 30);
        _edgeDock = new EdgeDockWindow(
            [new EdgeDockWindow.Tab("main", "Seal Breaker", () => true, () => _miniWindow.Draw())],
            _ => (Config.EdgeDockEdge, Config.EdgeDockAlong),
            (_, edge, along) =>
            {
                Config.EdgeDockEdge = edge;
                Config.EdgeDockAlong = along;
                Config.Save();
            },
            _dockLayout);
        _windowSystem.AddWindow(_edgeDock);

        Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Seal Breaker window — add 'mini' for the compact widget"
        });
        Service.CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Seal Breaker window (short alias) — /seal mini for the compact widget"
        });

        _pi.UiBuilder.Draw         += DrawUI;
        _pi.UiBuilder.OpenConfigUi += DrawConfigUI;
        _pi.UiBuilder.OpenMainUi   += DrawMainUI;

        Service.PluginLog.Information("SealBreaker loaded.");
    }

    public void Dispose()
    {
        Controller.Dispose();
        SealBreakerIpc.Unregister(); // [STOP-ALERT] [FC-RANK-STOP]
        _windowSystem.RemoveAllWindows();
        _dockLayout.Dispose();
        _mainWindow.Dispose();
        Service.CommandManager.RemoveHandler(CommandName);
        Service.CommandManager.RemoveHandler(CommandAlias);
        _pi.UiBuilder.Draw         -= DrawUI;
        _pi.UiBuilder.OpenConfigUi -= DrawConfigUI;
        _pi.UiBuilder.OpenMainUi   -= DrawMainUI;
        PluginIcon = null;
        PluginBanner = null;
    }

    private static ISharedImmediateTexture? LoadTexture(string path) =>
        File.Exists(path) ? Service.TextureProvider.GetFromFile(path) : null;

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("mini", StringComparison.OrdinalIgnoreCase))
        {
            if (Config.EdgeDockEnabled)
            {
                // The edge tab is the minimized form: /seal mini goes to it, or back if already there.
                if (DockedAway) SwitchToFullWindow();
                else SwitchToMiniWindow();
                return;
            }

            _miniWindow.Toggle();
            if (_miniWindow.IsOpen)
                _mainWindow.IsOpen = false;
            RememberWindowMode();
            return;
        }

        if (DockedAway)
            SwitchToFullWindow();
        else if (Config.MiniModeActive && !Config.EdgeDockEnabled)
            _miniWindow.Toggle();
        else
            _mainWindow.Toggle();
    }

    /// <summary>Minimized to the edge tab: the dock is on and neither window is open.</summary>
    private bool DockedAway => Config.EdgeDockEnabled && Config.MiniModeActive && !_mainWindow.IsOpen;

    /// <summary>Minimize (main window title-bar button): to the edge tab, or the floating compact widget.</summary>
    public static void SwitchToMiniWindow()
    {
        _instance._mainWindow.IsOpen = false;
        _instance._miniWindow.IsOpen = !Config.EdgeDockEnabled;
        _instance.SetWindowMode(mini: true);
    }

    /// <summary>Swap back to the full window (mini widget expand button).</summary>
    public static void SwitchToFullWindow()
    {
        _instance._miniWindow.IsOpen = false;
        _instance._mainWindow.IsOpen = true;
        _instance.SetWindowMode(mini: false);
    }

    private void SetWindowMode(bool mini)
    {
        if (Config.MiniModeActive == mini)
            return;

        Config.MiniModeActive = mini;
        Config.Save();
    }

    private void RememberWindowMode()
    {
        var mini = _miniWindow.IsOpen || (!_mainWindow.IsOpen && Config.MiniModeActive);
        if (Config.MiniModeActive == mini)
            return;

        Config.MiniModeActive = mini;
        Config.Save();
    }

    private void DrawUI()
    {
        _edgeDock.IsOpen = DockedAway && Service.ClientState.IsLoggedIn && _edgeDock.AnyTab();
        _windowSystem.Draw();
    }

    private void DrawConfigUI() { _miniWindow.IsOpen = false; _mainWindow.Toggle(); }
    private void DrawMainUI()   => OnCommand(CommandName, string.Empty);
}
