using System.IO;
using System.Text.Json;

namespace BrawlhallaOverlay;

/// <summary>
/// Shared load/save skeleton for the small JSON config files persisted next to
/// the exe (combos.json, stats.json, settings.json, keybinds*.json) : lit le
/// fichier s'il existe, retombe sur <paramref name="fallback"/> si absent ou
/// corrompu, sauvegarde en JSON indenté.
/// </summary>
public static class JsonFileStore
{
    public static T Load<T>(string path, T fallback)
    {
        if (!File.Exists(path)) return fallback;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json) ?? fallback;
        }
        catch
        {
            // Fichier corrompu ou mal formé : on retombe sur la valeur par défaut
            // plutôt que de planter l'appli.
            return fallback;
        }
    }

    public static void Save<T>(string path, T value)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(value, options));
    }
}
