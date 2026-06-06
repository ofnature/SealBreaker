using System.Text;
using System.Text.Json;

namespace SealBreaker.Services;

public class DesynthStats
{
    public int TotalKingcakesDesynthed { get; set; }
    public Dictionary<uint, DesynthItemStat> ItemStats { get; set; } = new();
    public DateTime FirstDesynth { get; set; }
    public DateTime LastDesynth { get; set; }
}

public class DesynthItemStat
{
    public string ItemName { get; set; } = string.Empty;
    public uint ItemId { get; set; }
    public int TimesObtained { get; set; }
}

internal static class DesynthTracker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static DesynthStats Stats { get; private set; } = new();

    private static string StatsPath =>
        Path.Combine(Service.PluginInterface.ConfigDirectory.FullName, "DesynthStats.json");

    public static string ExportPath =>
        Path.Combine(Service.PluginInterface.ConfigDirectory.FullName, "DesynthStats_export.csv");

    public static void Load()
    {
        EnsureDropStats();
        try
        {
            if (!File.Exists(StatsPath))
                return;

            var json = File.ReadAllText(StatsPath);
            Stats = JsonSerializer.Deserialize<DesynthStats>(json) ?? new DesynthStats();
            EnsureDropStats();
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Failed to load desynth stats");
            Stats = new DesynthStats();
            EnsureDropStats();
        }
    }

    public static void RecordKingcakeDesynth(IEnumerable<uint> obtainedItemIds)
    {
        var now = DateTime.Now;
        if (Stats.TotalKingcakesDesynthed == 0)
            Stats.FirstDesynth = now;

        Stats.TotalKingcakesDesynthed++;
        Stats.LastDesynth = now;
        EnsureDropStats();

        foreach (var itemId in obtainedItemIds)
        {
            if (Stats.ItemStats.TryGetValue(itemId, out var stat))
                stat.TimesObtained++;
        }

        Save();
    }

    public static float ObservedRate(uint itemId)
    {
        if (Stats.TotalKingcakesDesynthed <= 0)
            return 0f;

        return Stats.ItemStats.TryGetValue(itemId, out var stat)
            ? stat.TimesObtained / (float)Stats.TotalKingcakesDesynthed
            : 0f;
    }

    public static void Reset()
    {
        Stats = new DesynthStats();
        EnsureDropStats();
        Save();
    }

    public static bool ExportCsv()
    {
        try
        {
            Directory.CreateDirectory(Service.PluginInterface.ConfigDirectory.FullName);
            var sb = new StringBuilder();
            sb.AppendLine("Item ID,Item Name,Times Obtained,Observed Rate,Published Rate,Variance");
            foreach (var drop in KingcakeDesynth.Drops)
            {
                Stats.ItemStats.TryGetValue(drop.ItemId, out var stat);
                var observed = ObservedRate(drop.ItemId);
                sb.AppendLine($"{drop.ItemId},\"{drop.Name}\",{stat?.TimesObtained ?? 0},{observed:P2},{drop.DropRate:P2},{observed - drop.DropRate:P2}");
            }

            File.WriteAllText(ExportPath, sb.ToString());
            return true;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Failed to export desynth stats CSV");
            return false;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Service.PluginInterface.ConfigDirectory.FullName);
            File.WriteAllText(StatsPath, JsonSerializer.Serialize(Stats, JsonOptions));
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Failed to save desynth stats");
        }
    }

    private static void EnsureDropStats()
    {
        Stats.ItemStats ??= new Dictionary<uint, DesynthItemStat>();
        var currentDropIds = KingcakeDesynth.Drops.Select(d => d.ItemId).ToHashSet();
        foreach (var key in Stats.ItemStats.Keys.Where(k => !currentDropIds.Contains(k)).ToList())
            Stats.ItemStats.Remove(key);

        foreach (var drop in KingcakeDesynth.Drops)
        {
            if (!Stats.ItemStats.TryGetValue(drop.ItemId, out var stat))
            {
                Stats.ItemStats[drop.ItemId] = new DesynthItemStat
                {
                    ItemId = drop.ItemId,
                    ItemName = drop.Name,
                };
                continue;
            }

            stat.ItemId = drop.ItemId;
            stat.ItemName = drop.Name;
        }
    }
}
