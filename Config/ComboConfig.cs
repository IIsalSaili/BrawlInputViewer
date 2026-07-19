using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BrawlhallaOverlay;

/// <summary>
/// Persistence for user-created combos (combos.json next to the exe), same
/// pattern as KeyBindConfig. No built-in defaults on purpose: the app starts
/// with zero combos and the user creates their own (recorded or hand-edited).
/// </summary>
public static class ComboConfig
{
    private const string FileName = "combos.json";

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static List<Combo> LoadOrEmpty()
    {
        if (!File.Exists(ConfigPath)) return new List<Combo>();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<List<Combo>>(json) ?? new List<Combo>();
        }
        catch
        {
            // Fichier corrompu ou mal formé : on retombe sur une liste vide plutôt
            // que de planter l'appli (l'utilisateur pourra recréer ses combos).
            return new List<Combo>();
        }
    }

    public static void Save(List<Combo> combos)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(combos, options));
    }
}
