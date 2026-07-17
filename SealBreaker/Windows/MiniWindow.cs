using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using SealBreaker.Services;
using System;
using System.Numerics;

namespace SealBreaker.Windows;

/// <summary>Minimal always-small status widget — state, run progress, Start/Stop, expand.</summary>
public sealed class MiniWindow : Window
{
    private const float MinContentWidth = 280f;
    private const int StatusMaxChars = 48;

    public MiniWindow() : base(
        "SealBreaker Mini###SealBreakerMini",
        ImGuiWindowFlags.NoTitleBar
        | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse
        | ImGuiWindowFlags.NoCollapse)
    {
    }

    public override void Draw()
    {
        var ctrl = Plugin.Controller;
        var cfg = Plugin.Config;
        using var theme = UiTheme.Begin();

        ImGui.Dummy(new Vector2(MinContentWidth, 0));

        var (stateLabel, stateColor) = ctrl.State switch
        {
            FarmController.FarmState.Idle  => ("Idle",    UiTheme.Gray),
            FarmController.FarmState.Error => ("Error",   UiTheme.Red),
            _                              => ("Running", UiTheme.Green),
        };

        UiTheme.StatusDot(stateColor);
        ImGui.SameLine(0, 5);
        ImGui.TextColored(stateColor, stateLabel);
        ImGui.SameLine(0, 8);
        var dutyName = cfg.DutyRunner == 0
            ? AutoDutyCatalog.SelectedOrDefault(cfg).Name
            : DutySupportCatalog.SelectedOrDefault(cfg).Name;
        ImGui.TextColored(UiTheme.TextBright, dutyName);

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
            ImGui.ProgressBar(done / (float)cfg.RunsPerCycle, new Vector2(-1, 14), $"Run {current} / {cfg.RunsPerCycle}");
            ImGui.PopStyleColor();
        }

        var line = ctrl.LastError ?? ctrl.StatusMessage;
        var lineColor = ctrl.LastError != null ? UiTheme.Red : UiTheme.Gray;
        var shown = line.Length > StatusMaxChars ? line[..StatusMaxChars] + "..." : line;
        ImGui.TextColored(lineColor, shown);
        if (line.Length > StatusMaxChars && ImGui.IsItemHovered())
            ImGui.SetTooltip(line);

        ImGui.Spacing();

        var dutyReady = cfg.DutyRunner == 0
            ? IpcManager.AutoDutyAvailable
            : IpcManager.AdsAvailable;
        var allReady = dutyReady && IpcManager.VnavAvailable && IpcManager.LifestreamAvailable;

        var buttonSize = new Vector2(70, 23);
        if (ctrl.IsRunning)
        {
            if (UiTheme.StopButton("Stop##mini", buttonSize)) ctrl.Stop();

            if (!ctrl.IsAnyTestMode)
            {
                ImGui.SameLine(0, 4);
                var armed = ctrl.StopAfterRunRequested;
                if (armed)
                    ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.YellowDark);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.FlagCheckered))
                    ctrl.ToggleStopAfterRun();
                if (armed)
                    ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(armed
                        ? "Stopping after the current run — click to cancel"
                        : "Stop after this run (lets the dungeon finish cleanly)");
            }
        }
        else
        {
            if (!allReady) ImGui.BeginDisabled();
            if (UiTheme.StartButton("Start##mini", buttonSize)) ctrl.Start();
            if (!allReady) ImGui.EndDisabled();
            if (!allReady && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("A required plugin is missing — expand for details.");
        }

        ImGui.SameLine(0, 10);
        ImGui.TextColored(UiTheme.Gray, $"{FarmController.GetCurrentSeals():N0} / {cfg.SealCap:N0}");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Current seals / cap");

        ImGui.SameLine();
        var expandWidth = ImGui.GetFrameHeight() + 6;
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > expandWidth)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - expandWidth);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Expand))
            Plugin.SwitchToFullWindow();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Expand to the full window");
    }
}
