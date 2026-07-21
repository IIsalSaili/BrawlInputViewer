using System;
using System.Collections.Generic;
using System.IO;

namespace BrawlhallaOverlay;

/// <summary>
/// Persistence for per-action precision stats (stats.json next to the exe),
/// via JsonFileStore. No defaults: starts empty, filled as the user practices
/// combos in Tutorial mode.
/// </summary>
public static class StatsConfig
{
    private const string FileName = "stats.json";

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static List<ActionStat> LoadOrEmpty() => JsonFileStore.Load(ConfigPath, new List<ActionStat>());

    public static void Save(List<ActionStat> stats) => JsonFileStore.Save(ConfigPath, stats);
}
