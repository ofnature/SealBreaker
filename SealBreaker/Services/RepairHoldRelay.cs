using System;
using System.Text.Json;

namespace SealBreaker.Services;

/// <summary>
/// Codec for the "sealbreaker.repair" relay channel (Daedalus LAN bridge). A member farming in a
/// group broadcasts HOLD every couple of seconds while it repairs; the leader treats a hold as
/// active only while fresh, so a crashed client expires instead of wedging the queue (the same
/// self-expiring shape as Daedalus's rescue requests). CLEAR is a courtesy fast-path.
/// Payloads are extend-only JSON — unknown fields are ignored on parse.
/// </summary>
internal static class RepairHoldRelay
{
    public const string Channel = "sealbreaker.repair";
    public const string ActHold = "hold";
    public const string ActClear = "clear";

    /// <summary>A hold older than this is stale — the sender re-broadcasts well inside it.</summary>
    public static readonly TimeSpan HoldFreshness = TimeSpan.FromSeconds(8);

    /// <summary>How often an active hold is re-broadcast.</summary>
    public static readonly TimeSpan RebroadcastEvery = TimeSpan.FromSeconds(2);

    public sealed record Message(string Act, string Name, string Reason);

    public static string Encode(string act, string name, string reason)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("act", act);
            writer.WriteString("name", name);
            writer.WriteString("reason", reason);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static Message? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var act = GetString(doc.RootElement, "act");
            var name = GetString(doc.RootElement, "name");
            if (act.Length == 0 || name.Length == 0)
                return null;

            return new Message(act, name, GetString(doc.RootElement, "reason"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
}
