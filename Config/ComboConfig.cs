using System;
using System.Collections.Generic;
using System.IO;

namespace BrawlhallaOverlay;

/// <summary>
/// Persistence for user-created combos (combos.json next to the exe), via
/// JsonFileStore. No built-in defaults on purpose: the app starts with zero
/// combos and the user creates their own (recorded or hand-edited).
/// </summary>
public static class ComboConfig
{
    private const string FileName = "combos.json";

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static List<Combo> LoadOrEmpty() => JsonFileStore.Load(ConfigPath, new List<Combo>());

    public static void Save(List<Combo> combos) => JsonFileStore.Save(ConfigPath, combos);
}
