using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;

namespace SealBreaker.Services;

/// <summary>
/// IPC that SealBreaker itself provides, so Daedalus and Charon can react across a LAN fleet.
/// <list type="bullet">
/// <item><c>SealBreaker.FarmStopped</c> — [STOP-ALERT] message with the stop reason, sent on every stop of a running farm.</item>
/// <item><c>SealBreaker.FcRankMilestoneReached</c> — [FC-RANK-STOP] message with the FC rank, sent when a milestone is reached (before the farm reaches its safe stop).</item>
/// <item><c>SealBreaker.GetFcRank</c> — [FC-RANK-STOP] the current FC rank; 0 when not in an FC or not loaded.</item>
/// <item><c>SealBreaker.IsBusy</c> — Func&lt;bool&gt;: the farm or a Farm-tab test is running (in a duty, queued,
/// travelling, in the GC loop, waiting on its group, or finishing a run after a stop request). False once
/// stopped (Idle or Error) — nothing starts again without the user.</item>
/// <item><c>SealBreaker.RequestStopAfterRun</c> — Func&lt;bool&gt;: the UI's stop-after-run, armed (never toggled).
/// True when running and it will stop; false and nothing changed when idle. Call on the framework thread.</item>
/// </list>
/// </summary>
internal static class SealBreakerIpc
{
    public const string FarmStoppedName = "SealBreaker.FarmStopped";
    public const string FcRankMilestoneReachedName = "SealBreaker.FcRankMilestoneReached";
    public const string GetFcRankName = "SealBreaker.GetFcRank";
    public const string IsBusyName = "SealBreaker.IsBusy";
    public const string RequestStopAfterRunName = "SealBreaker.RequestStopAfterRun";

    private static ICallGateProvider<string, object>? _farmStopped;
    private static ICallGateProvider<int, object>? _fcRankMilestoneReached;
    private static ICallGateProvider<int>? _getFcRank;
    private static ICallGateProvider<bool>? _isBusy;
    private static ICallGateProvider<bool>? _requestStopAfterRun;

    public static void Register(IDalamudPluginInterface pluginInterface)
    {
        _farmStopped = pluginInterface.GetIpcProvider<string, object>(FarmStoppedName);
        _fcRankMilestoneReached = pluginInterface.GetIpcProvider<int, object>(FcRankMilestoneReachedName);
        _getFcRank = pluginInterface.GetIpcProvider<int>(GetFcRankName);
        _getFcRank.RegisterFunc(() => FcRankMonitor.TryGetFcRank(out var rank) ? rank : 0);
        _isBusy = pluginInterface.GetIpcProvider<bool>(IsBusyName);
        _isBusy.RegisterFunc(() => Plugin.Controller.IsRunning);
        _requestStopAfterRun = pluginInterface.GetIpcProvider<bool>(RequestStopAfterRunName);
        _requestStopAfterRun.RegisterFunc(() => Plugin.Controller.RequestStopAfterRun());
    }

    public static void Unregister()
    {
        _isBusy?.UnregisterFunc();
        _isBusy = null;
        _requestStopAfterRun?.UnregisterFunc();
        _requestStopAfterRun = null;
        _getFcRank?.UnregisterFunc();
        _getFcRank = null;
        _farmStopped = null;
        _fcRankMilestoneReached = null;
    }

    public static void NotifyFarmStopped(string reason) => Send(() => _farmStopped?.SendMessage(reason));

    public static void NotifyFcRankMilestoneReached(int rank) => Send(() => _fcRankMilestoneReached?.SendMessage(rank));

    // A subscriber throwing must never break the stop path.
    private static void Send(Action send)
    {
        try
        {
            send();
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning($"[SealBreaker] IPC notify failed: {ex.Message}");
        }
    }
}
