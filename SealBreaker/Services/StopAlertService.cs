using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SealBreaker.Services;

/// <summary>
/// [STOP-ALERT] The sound played when the farm stops: one of the 16 chat sound effects
/// (&lt;se.1&gt;–&lt;se.16&gt;), or a .wav played through Windows so it is heard with the game muted.
/// <para>
/// Nothing plays from the caller's thread. <see cref="Play"/> only schedules; <see cref="Tick"/>, called
/// every frame from the farm's framework update, does the playing — the chat sound must be triggered on
/// the framework thread, and the UI's "Test sound" button draws on another one.
/// </para>
/// </summary>
internal static class StopAlertService
{
    public const int MinEffect = 1;
    public const int MaxEffect = 16;
    public const int MaxRepeat = 5;

    private static readonly TimeSpan ChatSoundSpacing = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan WavSpacing = TimeSpan.FromMilliseconds(1500);

    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndFilename = 0x00020000;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    private static readonly object Gate = new();
    private static int _remaining;
    private static DateTime _nextAtUtc;
    private static uint _effect = MinEffect;
    private static string? _wavPath;

    /// <summary>The farm stopped: play the alert if the settings ask for one.</summary>
    /// <param name="manual">The stop was the user's own (Stop button, stop-after-run, a test finishing).</param>
    public static void OnFarmStopped(bool manual, Configuration cfg)
    {
        if (!cfg.PlayStopSound)
            return;
        if (manual && !cfg.PlayStopSoundOnManualStop)
            return;

        Play(cfg);
    }

    /// <summary>Schedule the configured alert (also the "Test sound" button). Plays on the next frame.</summary>
    public static void Play(Configuration cfg)
    {
        var path = cfg.StopSoundCustomPath?.Trim();
        lock (Gate)
        {
            _effect = (uint)Math.Clamp(cfg.StopSoundEffect, MinEffect, MaxEffect);
            _wavPath = string.IsNullOrEmpty(path) ? null : path;
            _remaining = Math.Clamp(cfg.StopSoundRepeat, 1, MaxRepeat);
            _nextAtUtc = DateTime.MinValue;
        }
    }

    /// <summary>True when the custom path is set but does not point at an existing file.</summary>
    public static bool CustomPathMissing(Configuration cfg)
    {
        var path = cfg.StopSoundCustomPath?.Trim();
        return !string.IsNullOrEmpty(path) && !File.Exists(path);
    }

    /// <summary>Plays any scheduled repeat that is due. Framework thread only.</summary>
    public static void Tick()
    {
        uint effect;
        string? wav;
        lock (Gate)
        {
            if (_remaining <= 0 || DateTime.UtcNow < _nextAtUtc)
                return;

            _remaining--;
            effect = _effect;
            wav = _wavPath;
            _nextAtUtc = DateTime.UtcNow + (wav != null ? WavSpacing : ChatSoundSpacing);
        }

        try
        {
            if (wav != null)
            {
                if (File.Exists(wav) && PlaySound(wav, IntPtr.Zero, SndFilename | SndAsync | SndNoDefault))
                    return;

                Service.PluginLog.Warning($"[SealBreaker] Stop alert: could not play \"{wav}\" — using <se.{effect}> instead.");
                lock (Gate)
                    _wavPath = null;
            }

            UIGlobals.PlayChatSoundEffect(effect);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning($"[SealBreaker] Stop alert failed: {ex.Message}");
            lock (Gate)
                _remaining = 0;
        }
    }
}
