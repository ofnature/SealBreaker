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
    private readonly IDalamudPluginInterface _pi;

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

        _mainWindow = new MainWindow();
        _windowSystem.AddWindow(_mainWindow);

        Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Seal Breaker window"
        });
        Service.CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Seal Breaker window (short alias)"
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

    private void OnCommand(string command, string args) => _mainWindow.Toggle();
    private void DrawUI()       => _windowSystem.Draw();
    private void DrawConfigUI() => _mainWindow.Toggle();
    private void DrawMainUI()   => _mainWindow.Toggle();
}
