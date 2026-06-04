using Dalamud.Game.Addon.Lifecycle;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace SealBreaker.Services;

internal class Service
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager         CommandManager  { get; private set; } = null!;
    [PluginService] internal static IClientState            ClientState     { get; private set; } = null!;
    [PluginService] internal static ICondition              Condition       { get; private set; } = null!;
    [PluginService] internal static IDataManager            DataManager     { get; private set; } = null!;
    [PluginService] internal static IFramework              Framework       { get; private set; } = null!;
    [PluginService] internal static IGameGui                GameGui         { get; private set; } = null!;
    [PluginService] internal static IObjectTable            ObjectTable     { get; private set; } = null!;
    [PluginService] internal static IPluginLog              PluginLog       { get; private set; } = null!;
    [PluginService] internal static IChatGui                ChatGui         { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle         AddonLifecycle  { get; private set; } = null!;
    [PluginService] internal static ITargetManager          TargetManager   { get; private set; } = null!;
    [PluginService] internal static ITextureProvider        TextureProvider { get; private set; } = null!;
}

/// <summary>
/// Dalamud 15.0.2+ addon lifecycle: register once, unregister by delegate only.
/// </summary>
internal sealed class AddonLifecycleRegistration : IDisposable
{
    private readonly List<IAddonLifecycle.AddonEventDelegate> _handlers = [];
    private bool _disposed;

    public void Register(AddonEvent eventType, string addonName, IAddonLifecycle.AddonEventDelegate handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handlers.Contains(handler))
            return;

        Service.AddonLifecycle.RegisterListener(eventType, addonName, handler);
        _handlers.Add(handler);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var handler in _handlers)
            Service.AddonLifecycle.UnregisterListener(handler);

        _handlers.Clear();
        _disposed = true;
    }
}
