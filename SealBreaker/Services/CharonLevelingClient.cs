using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SealBreaker.Services;

/// <summary>One combat exp track as Charon reports it. Blocker "" = workable right now;
/// blockerText is Charon's ready-made human line for the not-levelled summary.</summary>
public sealed record CharonJobTrack(
    uint Row, string Abbr, int Level, bool Unlocked, bool IsJob,
    string Parent, bool Capped, string Blocker, bool Hard, string BlockerText);

public sealed record CharonLevelingStatus(string Busy, bool FreeTrial, int MaxExpansion, int LevelCap);

/// <summary>
/// Parses the Charon.Leveling.* JSON payloads and holds the round-robin pick rule.
/// Payloads are extend-only, so parsing tolerates missing fields instead of throwing.
/// </summary>
internal static class CharonLevelingClient
{
    /// <summary>The plan's gate boundaries — level 30 (class quests) plus the expansion bridges.</summary>
    public static readonly int[] GateBoundaries = [30, 51, 61, 71, 81, 91];

    public static List<CharonJobTrack>? ParseTracks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var tracks = new List<CharonJobTrack>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                tracks.Add(new CharonJobTrack(
                    GetUInt(e, "row"),
                    GetString(e, "abbr"),
                    (int)GetLong(e, "level"),
                    GetBool(e, "unlocked"),
                    GetBool(e, "isJob"),
                    GetString(e, "parent"),
                    GetBool(e, "capped"),
                    GetString(e, "blocker"),
                    GetBool(e, "hard"),
                    GetString(e, "blockerText")));
            }

            return tracks;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static CharonLevelingStatus? ParseStatus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var e = doc.RootElement;
            return new CharonLevelingStatus(
                GetString(e, "busy"),
                GetBool(e, "freeTrial"),
                (int)GetLong(e, "maxExpansion"),
                (int)GetLong(e, "levelCap"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The gate target actually in effect: the configured boundary clamped to the
    /// account's live level cap (unknown cap → the configured value).</summary>
    public static int EffectiveGateTarget(int configuredTarget, int levelCap) =>
        levelCap > 0 ? Math.Min(configuredTarget, levelCap) : configuredTarget;

    /// <summary>The round-robin rule from the plan: the lowest-level unlocked track with an empty
    /// blocker that is still under the target. Null = nothing selectable, the run is done.</summary>
    public static CharonJobTrack? PickNextJob(
        IReadOnlyList<CharonJobTrack> tracks, int gateTarget, ISet<uint>? excluded = null)
    {
        return tracks
            .Where(t => t.Unlocked && t.Blocker.Length == 0 && t.Level < gateTarget)
            .Where(t => excluded == null || !excluded.Contains(t.Row))
            .OrderBy(t => t.Level)
            .ThenBy(t => t.Abbr, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>The overnight-run summary: what reached the target, and one Charon-worded line
    /// per job that could not be touched. A reminder list, not an error log.</summary>
    public static List<string> BuildSummary(IReadOnlyList<CharonJobTrack> tracks, int gateTarget)
    {
        var lines = new List<string>();

        var done = tracks
            .Where(t => t.Unlocked && t.Level >= gateTarget)
            .OrderBy(t => t.Abbr, StringComparer.OrdinalIgnoreCase)
            .Select(t => t.Abbr)
            .ToList();
        lines.Add(done.Count > 0
            ? $"Levelled to {gateTarget}: {string.Join(", ", done)}"
            : $"No job reached {gateTarget} this run.");

        var blocked = tracks
            .Where(t => t.Level < gateTarget && t.Blocker.Length > 0)
            .OrderBy(t => t.Abbr, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (blocked.Count > 0)
        {
            lines.Add("Not levelled:");
            foreach (var t in blocked)
                lines.Add($"  {t.Abbr} — {(t.BlockerText.Length > 0 ? t.BlockerText : t.Blocker)}");
        }

        return lines;
    }

    private static string GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True;

    private static long GetLong(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0;

    private static uint GetUInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetUInt32() : 0;
}
