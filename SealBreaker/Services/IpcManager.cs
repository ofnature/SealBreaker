using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;

namespace SealBreaker.Services;

/// <summary>
/// Wraps all third-party plugin IPC calls.
/// Each property lazily resolves the IPC gate so it won't throw if a
/// plugin isn't loaded — callers check IsAvailable before calling.
/// </summary>
internal static class IpcManager
{
    // ── AutoDuty ──────────────────────────────────────────────
    private const string AutoDutyPluginInternalName = "AutoDuty";

    // EzIPC registers void methods as actions — use HasAction, and object (not object?) as the trailing generic.
    private static ICallGateSubscriber<uint, int, bool, object>? _autoDutyRun;
    private static ICallGateSubscriber<uint, int, object>?         _autoDutyRunTwoArg;
    private static ICallGateSubscriber<object>?                  _autoDutyStop;
    private static ICallGateSubscriber<bool>?                     _autoDutyIsStopped;
    private static ICallGateSubscriber<string, object, object>?  _autoDutySetConfig;
    private static ICallGateSubscriber<string, string>?           _autoDutyGetConfig;
    private static ICallGateSubscriber<uint, bool>?               _autoDutyContentHasPath;

    public static bool AutoDutyPluginLoaded => IsPluginLoaded(AutoDutyPluginInternalName);

    public static bool AutoDutyAvailable
    {
        get
        {
            if (!AutoDutyPluginLoaded)
                return false;

            RefreshAutoDutySubscribers();
            return AutoDutyRunReady;
        }
    }

    private static bool AutoDutyRunReady =>
        (_autoDutyRun?.HasAction ?? false) || (_autoDutyRunTwoArg?.HasAction ?? false);

    private static void RefreshAutoDutySubscribers()
    {
        if (_autoDutyRun is { HasAction: false, HasFunction: false })
            _autoDutyRun = null;
        if (_autoDutyRunTwoArg is { HasAction: false, HasFunction: false })
            _autoDutyRunTwoArg = null;
        if (_autoDutyStop is { HasAction: false, HasFunction: false })
            _autoDutyStop = null;
        if (_autoDutyIsStopped is { HasFunction: false })
            _autoDutyIsStopped = null;
        if (_autoDutySetConfig is { HasAction: false, HasFunction: false })
            _autoDutySetConfig = null;
        if (_autoDutyGetConfig is { HasFunction: false })
            _autoDutyGetConfig = null;
        if (_autoDutyContentHasPath is { HasFunction: false })
            _autoDutyContentHasPath = null;

        EnsureAutoDutySubscribers();
    }

    private static void EnsureAutoDutySubscribers()
    {
        try
        {
            _autoDutyRun       ??= Service.PluginInterface.GetIpcSubscriber<uint, int, bool, object>("AutoDuty.Run");
            _autoDutyRunTwoArg ??= Service.PluginInterface.GetIpcSubscriber<uint, int, object>("AutoDuty.Run");
            _autoDutyStop      ??= Service.PluginInterface.GetIpcSubscriber<object>("AutoDuty.Stop");
            _autoDutyIsStopped ??= Service.PluginInterface.GetIpcSubscriber<bool>("AutoDuty.IsStopped");
            _autoDutySetConfig ??= Service.PluginInterface.GetIpcSubscriber<string, object, object>("AutoDuty.SetConfig");
            _autoDutyGetConfig ??= Service.PluginInterface.GetIpcSubscriber<string, string>("AutoDuty.GetConfig");
            _autoDutyContentHasPath ??= Service.PluginInterface.GetIpcSubscriber<uint, bool>("AutoDuty.ContentHasPath");
        }
        catch
        {
            // Availability is checked via HasAction/HasFunction per call.
        }
    }

    /// <summary>Start AutoDuty. dungeonId = territory content ID, runs = number of runs.</summary>
    public static bool AutoDutyRun(uint dungeonId, int runs)
    {
        RefreshAutoDutySubscribers();
        if (!AutoDutyRunReady)
            return false;

        try
        {
            if (!AutoDutyIsStopped())
            {
                Service.PluginLog.Information("[SealBreaker] AutoDuty.Run skipped because AutoDuty is not stopped");
                return false;
            }

            if (_autoDutyRun is { HasAction: true })
                _autoDutyRun.InvokeAction(dungeonId, runs, false);
            else if (_autoDutyRunTwoArg is { HasAction: true })
                _autoDutyRunTwoArg.InvokeAction(dungeonId, runs);
            else
                return false;

            return true;
        }
        catch (IpcNotReadyError)
        {
            _autoDutyRun = null;
            _autoDutyRunTwoArg = null;
            return false;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "AutoDuty.Run IPC failed");
            return false;
        }
    }

    public static void AutoDutyStop()
    {
        RefreshAutoDutySubscribers();
        if (_autoDutyStop is not { HasAction: true })
            return;

        try
        {
            if (AutoDutyIsStopped())
                return;

            _autoDutyStop.InvokeAction();
        }
        catch (IpcNotReadyError) { _autoDutyStop = null; }
        catch (Exception ex) { Service.PluginLog.Error(ex, "AutoDuty.Stop IPC failed"); }
    }

    /// <summary>Set an AutoDuty config value by name (case-insensitive), e.g. dutyModeEnum = Support.</summary>
    public static bool AutoDutySetConfig(string config, string value)
    {
        RefreshAutoDutySubscribers();
        if (_autoDutySetConfig is not { HasAction: true })
            return false;

        try
        {
            _autoDutySetConfig.InvokeAction(config, value);
            return true;
        }
        catch (IpcNotReadyError)
        {
            _autoDutySetConfig = null;
            return false;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "AutoDuty.SetConfig IPC failed");
            return false;
        }
    }

    /// <summary>Read an AutoDuty config value by name (case-insensitive). Null when the IPC is unavailable or the call fails.</summary>
    public static string? AutoDutyGetConfig(string config)
    {
        RefreshAutoDutySubscribers();
        if (_autoDutyGetConfig is not { HasFunction: true })
            return null;

        try { return _autoDutyGetConfig.InvokeFunc(config); }
        catch (IpcNotReadyError) { _autoDutyGetConfig = null; return null; }
        catch (Exception ex)
        {
            Service.PluginLog.Debug(ex, "AutoDuty.GetConfig IPC failed");
            return null;
        }
    }

    /// <summary>Whether AutoDuty's own Auto Repair is enabled. Null = could not determine.</summary>
    public static bool? AutoDutyAutoRepairEnabled()
    {
        var raw = AutoDutyGetConfig("AutoRepair");
        if (raw == null)
            return null;

        return bool.TryParse(raw.Trim(), out var enabled) ? enabled : null;
    }

    /// <summary>True when AutoDuty has a path file for the territory. Defaults to true when the IPC is unavailable.</summary>
    public static bool AutoDutyContentHasPath(uint territoryType)
    {
        RefreshAutoDutySubscribers();
        if (_autoDutyContentHasPath is not { HasFunction: true })
            return true;

        try { return _autoDutyContentHasPath.InvokeFunc(territoryType); }
        catch (IpcNotReadyError) { _autoDutyContentHasPath = null; return true; }
        catch { return true; }
    }

    public static bool AutoDutyIsStopped()
    {
        RefreshAutoDutySubscribers();
        if (_autoDutyIsStopped is not { HasFunction: true })
            return true;

        try { return _autoDutyIsStopped.InvokeFunc(); }
        catch (IpcNotReadyError) { _autoDutyIsStopped = null; return true; }
        catch { return true; }
    }

    // ── ADS (AI Duty Solver) ──────────────────────────────────
    // McVaxius/ADS registers these in AdsIpcService.cs — not ADS.SetDuty/Start/Stop.
    private const string AdsPluginInternalName = "ADS";

    private static ICallGateSubscriber<bool>? _adsStartDutyFromOutside;
    private static ICallGateSubscriber<bool>? _adsStartDutyFromInside;
    private static ICallGateSubscriber<bool>? _adsResumeDutyFromInside;
    private static ICallGateSubscriber<bool>? _adsLeaveDuty;
    private static ICallGateSubscriber<string>? _adsGetStatusJson;

    public static bool AdsPluginLoaded => IsPluginLoaded(AdsPluginInternalName);

    public static bool AdsAvailable
    {
        get
        {
            if (!AdsPluginLoaded)
                return false;

            RefreshAdsSubscribers();
            return (_adsStartDutyFromOutside?.HasFunction ?? false)
                   && (_adsGetStatusJson?.HasFunction ?? false);
        }
    }

    private static void RefreshAdsSubscribers()
    {
        if (_adsStartDutyFromOutside is { HasFunction: false })
            _adsStartDutyFromOutside = null;
        if (_adsStartDutyFromInside is { HasFunction: false })
            _adsStartDutyFromInside = null;
        if (_adsResumeDutyFromInside is { HasFunction: false })
            _adsResumeDutyFromInside = null;
        if (_adsLeaveDuty is { HasFunction: false })
            _adsLeaveDuty = null;
        if (_adsGetStatusJson is { HasFunction: false })
            _adsGetStatusJson = null;

        EnsureAdsSubscribers();
    }

    private static void EnsureAdsSubscribers()
    {
        try
        {
            _adsStartDutyFromOutside ??= Service.PluginInterface.GetIpcSubscriber<bool>("ADS.StartDutyFromOutside");
            _adsStartDutyFromInside  ??= Service.PluginInterface.GetIpcSubscriber<bool>("ADS.StartDutyFromInside");
            _adsResumeDutyFromInside ??= Service.PluginInterface.GetIpcSubscriber<bool>("ADS.ResumeDutyFromInside");
            _adsLeaveDuty            ??= Service.PluginInterface.GetIpcSubscriber<bool>("ADS.LeaveDuty");
            _adsGetStatusJson        ??= Service.PluginInterface.GetIpcSubscriber<string>("ADS.GetStatusJson");
        }
        catch
        {
            // Availability is checked via HasFunction per call.
        }
    }

    /// <summary>Queue outside ownership — ADS runs once instanced duty starts.</summary>
    public static bool AdsStartDutyFromOutside()
    {
        RefreshAdsSubscribers();
        if (_adsStartDutyFromOutside is not { HasFunction: true })
            return false;

        try { return _adsStartDutyFromOutside.InvokeFunc(); }
        catch (IpcNotReadyError) { _adsStartDutyFromOutside = null; return false; }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "ADS.StartDutyFromOutside IPC failed");
            return false;
        }
    }

    public static bool AdsStartDutyFromInside()
    {
        RefreshAdsSubscribers();
        if (_adsStartDutyFromInside is not { HasFunction: true })
            return false;

        try { return _adsStartDutyFromInside.InvokeFunc(); }
        catch (IpcNotReadyError) { _adsStartDutyFromInside = null; return false; }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "ADS.StartDutyFromInside IPC failed");
            return false;
        }
    }

    public static bool AdsResumeDutyFromInside()
    {
        RefreshAdsSubscribers();
        if (_adsResumeDutyFromInside is not { HasFunction: true })
            return false;

        try { return _adsResumeDutyFromInside.InvokeFunc(); }
        catch (IpcNotReadyError) { _adsResumeDutyFromInside = null; return false; }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "ADS.ResumeDutyFromInside IPC failed");
            return false;
        }
    }

    public static bool AdsStartOutsideCommand()
    {
        if (!AdsPluginLoaded)
            return false;

        return Service.Framework.RunOnFrameworkThread(() => SendAdsChatCommand("/ads outside")).GetAwaiter().GetResult();
    }

    public static bool AdsStartInsideCommand()
    {
        if (!AdsPluginLoaded)
            return false;

        return Service.Framework.RunOnFrameworkThread(() => SendAdsChatCommand("/ads inside")).GetAwaiter().GetResult();
    }

    public static bool AdsLeaveDuty()
    {
        RefreshAdsSubscribers();
        if (_adsLeaveDuty is { HasFunction: true })
        {
            try
            {
                if (_adsLeaveDuty.InvokeFunc())
                    return true;
            }
            catch (IpcNotReadyError)
            {
                _adsLeaveDuty = null;
            }
            catch (Exception ex)
            {
                Service.PluginLog.Warning(ex, "ADS.LeaveDuty IPC failed; falling back to /ads leave");
            }
        }

        return AdsPluginLoaded && Service.Framework.RunOnFrameworkThread(() => SendAdsChatCommand("/ads leave")).GetAwaiter().GetResult();
    }

    /// <summary>ADS has no Stop IPC — use the /ads stop chat command.</summary>
    public static void AdsStop() =>
        Service.Framework.RunOnFrameworkThread(() => SendAdsChatCommand("/ads stop")).GetAwaiter().GetResult();

    public static bool AdsStartRepair(string mode)
    {
        if (!AdsPluginLoaded)
            return false;

        mode = string.IsNullOrWhiteSpace(mode) ? "npc-no-teleport-no-inn" : mode.Trim();
        Service.Framework.RunOnFrameworkThread(() => SendAdsChatCommand($"/ads repair {mode}")).GetAwaiter().GetResult();
        return true;
    }

    public static void RefreshAdsCombatAutomation()
    {
        SendChatCommand("/rotation Settings KeyBoardNoise false");
        SendChatCommand("/rotation Settings BmrSafetyCheckAuto True");
        SendChatCommand("/rotation Settings BmrSafetyCheckIntercept True");
        SendChatCommand("/rotation Settings AutoOffBetweenArea False");
        SendChatCommand("/rotation Settings AutoOffCutScene False");
        SendChatCommand("/rotation Settings AutoOffSwitchClass False");
        SendChatCommand("/rotation Settings AutoOffWhenDead False");
        SendChatCommand("/rotation Settings AutoOffWhenDutyCompleted False");
        SendChatCommand("/rotation Settings AutoOffAfterCombatTime 6942069");
        SendChatCommand("/rotation Settings ToggleAuto False");
        SendChatCommand("/rotation Settings ToggleManual False");
        SendChatCommand("/rotation auto");
        SendChatCommand("/fr off");

        if (IsPluginLoaded("BossModReborn"))
            SendChatCommand("/bmrai on");
        else
            SendChatCommand("/vbmai on");
    }

    private static bool SendAdsChatCommand(string command) => SendChatCommand(command);

    private static unsafe bool SendChatCommand(string command)
    {
        try
        {
            Service.CommandManager.ProcessCommand(command);
            return true;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Debug(ex, $"CommandManager failed for {command}; falling back to chat box");
        }

        var uiModule = UIModule.Instance();
        if (uiModule == null)
        {
            Service.PluginLog.Error($"[SealBreaker] UIModule unavailable — cannot send {command}");
            return false;
        }

        var message = stackalloc Utf8String[1];
        message->SetString(command);
        uiModule->ProcessChatBoxEntry(message, nint.Zero, false);
        return true;
    }

    /// <summary>True when ADS is not actively owning a duty run.</summary>
    public static bool AdsIsStopped()
    {
        RefreshAdsSubscribers();
        if (_adsGetStatusJson is not { HasFunction: true })
            return true;

        try
        {
            var json = _adsGetStatusJson.InvokeFunc();
            return !AdsOwnershipActive(json);
        }
        catch (IpcNotReadyError)
        {
            _adsGetStatusJson = null;
            return true;
        }
        catch
        {
            return true;
        }
    }

    private static bool AdsOwnershipActive(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("ownershipMode", out var modeProp))
                return false;

            return modeProp.GetString() switch
            {
                "OwnedStartOutside" or "OwnedStartInside" or "OwnedResumeInside" or "Leaving" => true,
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }

    // ── vnavmesh ──────────────────────────────────────────────
    private static ICallGateSubscriber<Vector3, bool, bool>? _vnavPathfind;
    private static ICallGateSubscriber<Vector3, bool, float, bool>? _vnavPathfindClose;
    private static ICallGateSubscriber<bool>?                _vnavIsReady;
    private static ICallGateSubscriber<bool>?                _vnavPathfindInProgress;
    private static ICallGateSubscriber<bool>?                _vnavPathIsRunning;
    private static ICallGateSubscriber<object>?             _vnavStop;
    private static ICallGateSubscriber<float, object>?       _vnavSetTolerance;
    private static ICallGateSubscriber<int>?                _vnavNumWaypoints;

    public static bool VnavAvailable
    {
        get
        {
            RefreshVnavSubscribers();
            return (_vnavPathfind?.HasFunction ?? false)
                   && (_vnavIsReady?.HasFunction ?? false)
                   && (_vnavStop?.HasAction ?? false);
        }
    }

    private static void RefreshVnavSubscribers()
    {
        if (_vnavPathfind is { HasFunction: false })
            _vnavPathfind = null;
        if (_vnavPathfindClose is { HasFunction: false })
            _vnavPathfindClose = null;
        if (_vnavIsReady is { HasFunction: false })
            _vnavIsReady = null;
        if (_vnavPathfindInProgress is { HasFunction: false })
            _vnavPathfindInProgress = null;
        if (_vnavPathIsRunning is { HasFunction: false })
            _vnavPathIsRunning = null;
        if (_vnavStop is { HasAction: false, HasFunction: false })
            _vnavStop = null;
        if (_vnavSetTolerance is { HasAction: false, HasFunction: false })
            _vnavSetTolerance = null;
        if (_vnavNumWaypoints is { HasFunction: false })
            _vnavNumWaypoints = null;

        EnsureVnavSubscribers();
    }

    private static void EnsureVnavSubscribers()
    {
        try
        {
            _vnavPathfind ??= Service.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
            _vnavPathfindClose ??= Service.PluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
            _vnavIsReady  ??= Service.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
            _vnavPathfindInProgress ??= Service.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
            _vnavPathIsRunning ??= Service.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
            _vnavStop     ??= Service.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
            _vnavSetTolerance ??= Service.PluginInterface.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance");
            _vnavNumWaypoints ??= Service.PluginInterface.GetIpcSubscriber<int>("vnavmesh.Path.NumWaypoints");
        }
        catch
        {
            // Availability is checked via HasAction/HasFunction per call.
        }
    }

    /// <summary>AutoDuty-style prep: stop any path, wait idle, set tolerance before SimpleMove pathfind.</summary>
    public static async Task<bool> VnavPrepareForPathfindAsync(float tolerance = 0.25f)
    {
        RefreshVnavSubscribers();
        await RunVnavAsync(VnavStopCore);

        for (var i = 0; i < 40; i++)
        {
            if (!await RunVnavAsync(VnavPathActiveCore))
                break;
            await Task.Delay(50);
        }

        if (!await RunVnavAsync(VnavIsReadyCore))
            return false;

        await RunVnavAsync(() => VnavSetToleranceCore(tolerance));
        return true;
    }

    public static void VnavSetTolerance(float tolerance) =>
        RunVnav(() => VnavSetToleranceCore(tolerance));

    private static void VnavSetToleranceCore(float tolerance)
    {
        if (_vnavSetTolerance is not { HasAction: true })
            return;

        try { _vnavSetTolerance.InvokeAction(tolerance); }
        catch (Exception ex) { Service.PluginLog.Error(ex, "vnavmesh set tolerance IPC failed"); }
    }

    public static Task<bool> VnavMoveCloseToAsync(Vector3 dest, bool fly, float range) =>
        RunVnavAsync(() => VnavMoveCloseToCore(dest, fly, range));

    private static bool VnavMoveCloseToCore(Vector3 dest, bool fly, float range)
    {
        try
        {
            if (_vnavPathfindClose == null)
                return false;

            return _vnavPathfindClose.InvokeFunc(dest, fly, range);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "vnavmesh pathfind-close IPC failed");
            return false;
        }
    }

    public static async Task<bool> VnavMoveCloseToAndWaitAsync(
        Vector3 dest,
        float range,
        bool fly = false,
        int timeoutMs = 120_000,
        Func<Task<float?>>? getDistanceAsync = null,
        Func<Task<bool>>? isCancelledAsync = null)
    {
        if (!await VnavPrepareForPathfindAsync(range))
            return false;

        if (!await VnavMoveCloseToAsync(dest, fly, range))
            return false;

        return await VnavWaitUntilArrivedAsync(
            range,
            timeoutMs,
            getDistanceAsync,
            isCancelledAsync,
            async () =>
            {
                if (!await VnavPrepareForPathfindAsync(range))
                    return false;
                return await VnavMoveCloseToAsync(dest, fly, range);
            });
    }

    private static async Task<bool> VnavWaitUntilArrivedAsync(
        float arriveRange,
        int timeoutMs,
        Func<Task<float?>>? getDistanceAsync,
        Func<Task<bool>>? isCancelledAsync,
        Func<Task<bool>>? restartMoveAsync)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        var retries = 0;
        const int maxRetries = 10;

        while (DateTime.Now < deadline)
        {
            if (isCancelledAsync != null && await isCancelledAsync())
            {
                await RunVnavAsync(VnavStopCore);
                return false;
            }

            if (getDistanceAsync != null)
            {
                var dist = await getDistanceAsync();
                if (dist.HasValue && dist.Value <= arriveRange + 0.75f)
                {
                    await RunVnavAsync(VnavStopCore);
                    return true;
                }
            }

            if (!await RunVnavAsync(VnavPathActiveCore))
            {
                if (getDistanceAsync != null)
                {
                    var dist = await getDistanceAsync();
                    if (dist.HasValue && dist.Value <= arriveRange + 0.75f)
                    {
                        await RunVnavAsync(VnavStopCore);
                        return true;
                    }
                }

                if (restartMoveAsync == null || retries++ >= maxRetries)
                    return false;

                if (!await restartMoveAsync())
                    return false;

                await Task.Delay(400);
                continue;
            }

            await Task.Delay(100);
        }

        Service.PluginLog.Warning("[SealBreaker] vnavmesh move timed out");
        await RunVnavAsync(VnavStopCore);
        return false;
    }

    public static Task<bool> VnavMoveToAsync(Vector3 dest, bool fly = false) =>
        RunVnavAsync(() => VnavMoveToCore(dest, fly));

    private static bool VnavMoveToCore(Vector3 dest, bool fly)
    {
        try
        {
            if (_vnavPathfind == null)
                return false;

            return _vnavPathfind.InvokeFunc(dest, fly);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "vnavmesh pathfind IPC failed");
            return false;
        }
    }

    public static bool VnavPathActive() => RunVnav(VnavPathActiveCore);

    private static bool VnavPathActiveCore()
    {
        try
        {
            return (_vnavPathfindInProgress?.InvokeFunc() ?? false)
                || (_vnavPathIsRunning?.InvokeFunc() ?? false);
        }
        catch { return false; }
    }

    public static async Task<bool> VnavMoveToAndWaitAsync(
        Vector3 dest,
        bool fly = false,
        int timeoutMs = 120_000,
        Func<Task<bool>>? isCancelledAsync = null,
        Func<Task<float?>>? getDistanceAsync = null,
        float arriveRange = 2.5f)
    {
        if (!await VnavPrepareForPathfindAsync(arriveRange))
            return false;

        if (!await VnavMoveToAsync(dest, fly))
            return false;

        return await VnavWaitUntilArrivedAsync(
            arriveRange,
            timeoutMs,
            getDistanceAsync,
            isCancelledAsync,
            async () =>
            {
                if (!await VnavPrepareForPathfindAsync(arriveRange))
                    return false;
                return await VnavMoveToAsync(dest, fly);
            });
    }

    public static bool VnavIsReady() => RunVnav(VnavIsReadyCore);

    private static bool VnavIsReadyCore()
    {
        try { return _vnavIsReady?.InvokeFunc() ?? false; }
        catch { return false; }
    }

    public static void VnavStop() => RunVnav(VnavStopCore);

    private static void VnavStopCore()
    {
        if (_vnavStop is not { HasAction: true })
            return;

        try { _vnavStop.InvokeAction(); }
        catch (Exception ex) { Service.PluginLog.Error(ex, "vnavmesh stop IPC failed"); }
    }

    private static void RunVnav(Action action) =>
        Service.Framework.RunOnFrameworkThread(action).GetAwaiter().GetResult();

    private static T RunVnav<T>(Func<T> fn) =>
        Service.Framework.RunOnFrameworkThread(fn).GetAwaiter().GetResult();

    private static Task RunVnavAsync(Action action) =>
        Service.Framework.RunOnFrameworkThread(action);

    private static Task<T> RunVnavAsync<T>(Func<T> fn) =>
        Service.Framework.RunOnFrameworkThread(fn);

    public static Task<bool> VnavWaitForArrivalAsync(
        float arriveRange,
        int timeoutMs,
        Func<Task<float?>>? getDistanceAsync,
        Func<Task<bool>>? isCancelledAsync) =>
        VnavWaitUntilArrivedAsync(arriveRange, timeoutMs, getDistanceAsync, isCancelledAsync, null);

    // ── Lifestream ────────────────────────────────────────────
    private static ICallGateSubscriber<string, bool>? _lifestreamExecute;
    private static ICallGateSubscriber<string, bool>? _lifestreamAethernetTo;
    private static ICallGateSubscriber<string, bool>? _lifestreamAethernetTeleport;
    private static ICallGateSubscriber<uint, bool>? _lifestreamAethernetTeleportById;
    private static ICallGateSubscriber<uint, byte, bool>? _lifestreamTeleport;
    private static ICallGateSubscriber<bool>?          _lifestreamIsBusy;
    private static ICallGateSubscriber<List<Vector3>, bool>? _lifestreamMove;
    private static ICallGateSubscriber<object>?         _lifestreamAbort;
    private static bool? _lifestreamMoveReady;

    private static void EnsureLifestreamSubscribers()
    {
        try
        {
            _lifestreamExecute ??= Service.PluginInterface.GetIpcSubscriber<string, bool>("Lifestream.ExecuteCommand");
            _lifestreamAethernetTo ??= Service.PluginInterface.GetIpcSubscriber<string, bool>("Lifestream.AethernetTo");
            _lifestreamAethernetTeleport ??= Service.PluginInterface.GetIpcSubscriber<string, bool>("Lifestream.AethernetTeleport");
            _lifestreamAethernetTeleportById ??= Service.PluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportById");
            _lifestreamTeleport ??= Service.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
            _lifestreamIsBusy ??= Service.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        }
        catch
        {
            // Subscribers are resolved lazily; availability is checked via HasFunction per call.
        }
    }

    /// <summary>True when any core Lifestream teleport IPC is registered.</summary>
    public static bool LifestreamAvailable
    {
        get
        {
            EnsureLifestreamSubscribers();
            return (_lifestreamAethernetTo?.HasFunction ?? false)
                   || (_lifestreamAethernetTeleport?.HasFunction ?? false)
                   || (_lifestreamAethernetTeleportById?.HasFunction ?? false)
                   || (_lifestreamTeleport?.HasFunction ?? false)
                   || LifestreamExecuteAvailable;
        }
    }

    /// <summary>True when Lifestream.ExecuteCommand is registered (void or bool IPC).</summary>
    public static bool LifestreamExecuteAvailable
    {
        get
        {
            EnsureLifestreamSubscribers();
            return _lifestreamExecute?.HasFunction ?? false;
        }
    }

    /// <summary>True when Lifestream.Move IPC is registered (separate from teleport commands).</summary>
    public static bool LifestreamMoveAvailable
    {
        get
        {
            if (_lifestreamMoveReady == false)
                return false;

            try
            {
                _lifestreamMove ??= Service.PluginInterface.GetIpcSubscriber<List<Vector3>, bool>("Lifestream.Move");
                return true;
            }
            catch
            {
                _lifestreamMoveReady = false;
                return false;
            }
        }
    }

    /// <summary>Dedicated aethernet shard travel — does not use the full Lifestream navigation pipeline.</summary>
    public static bool LifestreamAethernetTo(string shardName)
    {
        EnsureLifestreamSubscribers();
        if (_lifestreamAethernetTo is not { HasFunction: true })
            return false;

        try { return _lifestreamAethernetTo.InvokeFunc(shardName); }
        catch (IpcNotReadyError) { _lifestreamAethernetTo = null; return false; }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "Lifestream.AethernetTo IPC failed");
            return false;
        }
    }

    public static bool LifestreamAethernetToAvailable
    {
        get
        {
            EnsureLifestreamSubscribers();
            return _lifestreamAethernetTo?.HasFunction ?? false;
        }
    }

    public static bool LifestreamAethernetTeleport(string destination)
    {
        EnsureLifestreamSubscribers();
        if (_lifestreamAethernetTeleport is not { HasFunction: true })
            return false;

        try { return _lifestreamAethernetTeleport.InvokeFunc(destination); }
        catch (IpcNotReadyError) { _lifestreamAethernetTeleport = null; return false; }
        catch (Exception ex) { Service.PluginLog.Warning(ex, "Lifestream.AethernetTeleport IPC failed"); return false; }
    }

    /// <summary>Questionable-style aethernet by Aetheryte sheet row id (e.g. 41 = The Aftcastle).</summary>
    public static bool LifestreamAethernetTeleportById(uint aetheryteSheetRowId)
    {
        EnsureLifestreamSubscribers();
        if (_lifestreamAethernetTeleportById is not { HasFunction: true })
            return false;

        try { return _lifestreamAethernetTeleportById.InvokeFunc(aetheryteSheetRowId); }
        catch (IpcNotReadyError) { _lifestreamAethernetTeleportById = null; return false; }
        catch (Exception ex) { Service.PluginLog.Warning(ex, "Lifestream.AethernetTeleportById IPC failed"); return false; }
    }

    public static bool LifestreamTeleport(uint aetheryteId, byte subIndex = 0)
    {
        EnsureLifestreamSubscribers();
        if (_lifestreamTeleport is not { HasFunction: true })
            return false;

        try { return _lifestreamTeleport.InvokeFunc(aetheryteId, subIndex); }
        catch (IpcNotReadyError) { _lifestreamTeleport = null; return false; }
        catch (Exception ex) { Service.PluginLog.Warning(ex, "Lifestream.Teleport IPC failed"); return false; }
    }

    /// <summary>Runs Lifestream.ProcessCommand via IPC when HasFunction is true. Zone teleports only.</summary>
    public static bool LifestreamExecute(string command)
    {
        EnsureLifestreamSubscribers();
        if (_lifestreamExecute is not { HasFunction: true })
            return false;

        try
        {
            _ = _lifestreamExecute.InvokeFunc(command);
            return true;
        }
        catch (IpcNotReadyError)
        {
            _lifestreamExecute = null;
            return false;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Lifestream.ExecuteCommand IPC failed");
            return false;
        }
    }

    public static bool LifestreamIsBusy()
    {
        EnsureLifestreamSubscribers();
        if (_lifestreamIsBusy is not { HasFunction: true })
            return false;

        try { return _lifestreamIsBusy.InvokeFunc(); }
        catch (IpcNotReadyError) { _lifestreamIsBusy = null; return false; }
        catch { return false; }
    }

    public static bool LifestreamMove(IReadOnlyList<Vector3> path)
    {
        if (!LifestreamMoveAvailable)
            return false;

        try
        {
            return _lifestreamMove!.InvokeFunc([.. path]);
        }
        catch (IpcNotReadyError)
        {
            _lifestreamMove = null;
            _lifestreamMoveReady = false;
            return false;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Lifestream.Move IPC failed");
            return false;
        }
    }

    public static void LifestreamAbort()
    {
        try
        {
            _lifestreamAbort ??= Service.PluginInterface.GetIpcSubscriber<object>("Lifestream.Abort");
            _lifestreamAbort.InvokeAction();
        }
        catch (IpcNotReadyError)
        {
            _lifestreamAbort = null;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Lifestream.Abort IPC failed");
        }
    }

    public static async Task<bool> LifestreamMoveAndWaitAsync(
        IReadOnlyList<Vector3> path,
        int timeoutMs,
        Func<Task<bool>>? isCancelledAsync = null)
    {
        if (!LifestreamMove(path))
            return false;

        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        await Task.Delay(100);

        while (DateTime.Now < deadline)
        {
            if (isCancelledAsync != null && await isCancelledAsync())
            {
                LifestreamAbort();
                return false;
            }

            if (!LifestreamIsBusy())
                return true;

            await Task.Delay(200);
        }

        LifestreamAbort();
        return false;
    }

    private static bool IsPluginLoaded(string internalName) =>
        Service.PluginInterface.InstalledPlugins.Any(
            p => string.Equals(p.InternalName, internalName, StringComparison.OrdinalIgnoreCase)
                 && p.IsLoaded
                 && !p.IsOutdated);

    /// <summary>Clears duty-runner IPC subscribers (call when switching AutoDuty/ADS in config).</summary>
    public static void ResetDutyRunners()
    {
        _autoDutyRun       = null;
        _autoDutyRunTwoArg = null;
        _autoDutyStop      = null;
        _autoDutyIsStopped = null;
        _autoDutySetConfig = null;
        _autoDutyGetConfig = null;
        _autoDutyContentHasPath = null;
        _adsStartDutyFromOutside = null;
        _adsStartDutyFromInside  = null;
        _adsResumeDutyFromInside = null;
        _adsLeaveDuty            = null;
        _adsGetStatusJson        = null;
    }

    /// <summary>Clears all cached IPC subscribers so they re-resolve on next use.</summary>
    public static void Reset()
    {
        ResetDutyRunners();
        _vnavPathfind      = null;
        _vnavPathfindClose = null;
        _vnavIsReady       = null;
        _vnavPathfindInProgress = null;
        _vnavPathIsRunning = null;
        _vnavStop          = null;
        _vnavSetTolerance  = null;
        _vnavNumWaypoints  = null;
        _lifestreamExecute = null;
        _lifestreamAethernetTo = null;
        _lifestreamAethernetTeleport = null;
        _lifestreamAethernetTeleportById = null;
        _lifestreamTeleport = null;
        _lifestreamIsBusy  = null;
        _lifestreamMove    = null;
        _lifestreamAbort   = null;
        _lifestreamMoveReady = null;
    }
}
