using System;
using System.IO;

namespace BrawlhallaOverlay;

public static class ParcoursProgressConfig
{
    private const string FileName = "parcours_progress.json";

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static ParcoursProgress LoadOrEmpty() => JsonFileStore.Load(ConfigPath, new ParcoursProgress());

    public static void Save(ParcoursProgress progress) => JsonFileStore.Save(ConfigPath, progress);
}
