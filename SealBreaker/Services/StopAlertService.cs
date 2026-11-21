using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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
    // [STOP-ALERT] A .wav now plays to its end on a worker thread before repeating; a fixed spacing
    // restarted the file every 1.5 s and cut longer sounds off.
    // private static readonly TimeSpan WavSpacing = TimeSpan.FromMilliseconds(1500);

    // private const uint SndAsync = 0x0001;
    private const uint SndSync = 0x0000;
    private const uint SndNoDefault = 0x0002;
    private const uint SndFilename = 0x00020000;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    private static readonly object Gate = new();
    private static int _remaining;
    private static int _generation; // bumped by every Play(); a .wav loop from an earlier one stops
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
            _generation++;
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
        int plays;
        int generation;
        lock (Gate)
        {
            if (_remaining <= 0 || DateTime.UtcNow < _nextAtUtc)
                return;

            effect = _effect;
            wav = _wavPath;
            generation = _generation;
            if (wav != null)
            {
                // The worker thread plays every repeat; nothing is left to schedule here.
                plays = _remaining;
                _remaining = 0;
            }
            else
            {
                plays = 1;
                _remaining--;
                _nextAtUtc = DateTime.UtcNow + ChatSoundSpacing;
            }
        }

        try
        {
            if (wav != null)
            {
                // [STOP-ALERT]
                // if (File.Exists(wav) && PlaySound(wav, IntPtr.Zero, SndFilename | SndAsync | SndNoDefault))
                //     return;
                // Service.PluginLog.Warning($"[SealBreaker] Stop alert: could not play \"{wav}\" — using <se.{effect}> instead.");
                // lock (Gate)
                //     _wavPath = null;
                if (File.Exists(wav))
                    Task.Run(() => PlayWavRepeats(wav, plays, generation));
                else
                    FallBackToChatSound(wav, plays, generation);
                return;
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

    /// <summary>
    /// Worker thread: play the .wav <paramref name="plays"/> times, each to its end (a synchronous
    /// PlaySound returns when the sound finishes). A newer <see cref="Play"/> ends the loop — its own
    /// PlaySound call cuts the current sound short, and the generation check stops the repeats.
    /// </summary>
    private static void PlayWavRepeats(string wav, int plays, int generation)
    {
        for (var i = 0; i < plays; i++)
        {
            lock (Gate)
            {
                if (generation != _generation)
                    return;
            }

            if (!PlaySound(wav, IntPtr.Zero, SndFilename | SndSync | SndNoDefault))
            {
                FallBackToChatSound(wav, plays - i, generation);
                return;
            }
        }
    }

    /// <summary>The .wav could not be played: hand the repeats still owed to the chat sound, which
    /// <see cref="Tick"/> plays on the framework thread.</summary>
    private static void FallBackToChatSound(string wav, int plays, int generation)
    {
        Service.PluginLog.Warning($"[SealBreaker] Stop alert: could not play \"{wav}\" — using the chat sound instead.");
        lock (Gate)
        {
            if (generation != _generation)
                return;

            _wavPath = null;
            _remaining = plays;
            _nextAtUtc = DateTime.MinValue;
        }
    }
}
