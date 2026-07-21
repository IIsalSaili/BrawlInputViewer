using System;
using System.IO;

namespace BrawlhallaOverlay;

public static class OverlaySettingsConfig
{
    private const string FileName = "settings.json";

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Vrai si settings.json n'existait pas encore au moment du chargement,
    /// càd tout premier lancement de l'app sur cette machine — sert à afficher un
    /// message d'accueil une seule fois (voir MainWindow.ShowFirstRunHintIfNeeded).</summary>
    public static bool WasFirstRun { get; private set; }

    public static OverlaySettings LoadOrCreateDefault()
    {
        WasFirstRun = !File.Exists(ConfigPath);
        var settings = JsonFileStore.Load<OverlaySettings?>(ConfigPath, null) ?? new OverlaySettings();
        if (WasFirstRun) Save(settings);
        return settings;
    }

    public static void Save(OverlaySettings settings) => JsonFileStore.Save(ConfigPath, settings);
}
