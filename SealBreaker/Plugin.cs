using Dalamud.Game.Command;
using Dalamud.Interface.Textures;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using SealBreaker.Windows;
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
        _windowSystem.RemoveAllWindows();
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
            _miniWindow.Toggle();
            if (_miniWindow.IsOpen)
                _mainWindow.IsOpen = false;
            RememberWindowMode();
            return;
        }

        if (Config.MiniModeActive)
            _miniWindow.Toggle();
        else
            _mainWindow.Toggle();
    }

    /// <summary>Swap to the compact widget (main window title-bar minimize button).</summary>
    public static void SwitchToMiniWindow()
    {
        _instance._mainWindow.IsOpen = false;
        _instance._miniWindow.IsOpen = true;
        _instance.RememberWindowMode();
    }

    /// <summary>Swap back to the full window (mini widget expand button).</summary>
    public static void SwitchToFullWindow()
    {
        _instance._miniWindow.IsOpen = false;
        _instance._mainWindow.IsOpen = true;
        _instance.RememberWindowMode();
    }

    private void RememberWindowMode()
    {
        var mini = _miniWindow.IsOpen || (!_mainWindow.IsOpen && Config.MiniModeActive);
        if (Config.MiniModeActive == mini)
            return;

        Config.MiniModeActive = mini;
        Config.Save();
    }

    private void DrawUI()       => _windowSystem.Draw();
    private void DrawConfigUI() { _miniWindow.IsOpen = false; _mainWindow.Toggle(); }
    private void DrawMainUI()   => OnCommand(CommandName, string.Empty);
}
