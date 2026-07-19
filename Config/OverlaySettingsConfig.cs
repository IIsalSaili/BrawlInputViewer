using System;
using System.IO;
using System.Text.Json;

namespace BrawlhallaOverlay;

public static class OverlaySettingsConfig
{
    private const string FileName = "settings.json";

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static OverlaySettings LoadOrCreateDefault()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<OverlaySettings>(json);
                if (loaded is not null) return loaded;
            }
            catch
            {
                // Fichier corrompu : retombe sur les valeurs par défaut.
            }
        }

        var settings = new OverlaySettings();
        Save(settings);
        return settings;
    }

    public static void Save(OverlaySettings settings)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, options));
    }
}
