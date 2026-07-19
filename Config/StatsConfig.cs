using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BrawlhallaOverlay;

/// <summary>
/// Persistence for per-action precision stats (stats.json next to the exe),
/// same pattern as ComboConfig. No defaults: starts empty, filled as the
/// user practices combos in Tutorial mode.
/// </summary>
public static class StatsConfig
{
    private const string FileName = "stats.json";

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static List<ActionStat> LoadOrEmpty()
    {
        if (!File.Exists(ConfigPath)) return new List<ActionStat>();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<List<ActionStat>>(json) ?? new List<ActionStat>();
        }
        catch
        {
            return new List<ActionStat>();
        }
    }

    public static void Save(List<ActionStat> stats)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(stats, options));
    }
}
