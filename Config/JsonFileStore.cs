using System;
using System.IO;
using System.Text.Json;

namespace BrawlhallaOverlay;

/// <summary>
/// Shared load/save skeleton for the small JSON config files persisted next to
/// the exe (combos.json, stats.json, settings.json, keybinds*.json) : lit le
/// fichier s'il existe, retombe sur <paramref name="fallback"/> si absent ou
/// corrompu, sauvegarde en JSON indenté.
///
/// Écriture ATOMIQUE et corruption NON silencieuse depuis l'audit 2026-08-07 (§E1). Avant :
/// <c>File.WriteAllText</c> direct + un <c>catch</c> muet au chargement. Une coupure pendant une
/// écriture laissait un JSON tronqué, le chargement suivant retombait silencieusement sur une
/// liste vide, et le constructeur statique d'AppState réécrivait aussitôt le fichier par-dessus —
/// les combos personnels étaient irrécupérables sans le moindre message. La fenêtre d'exposition
/// était large : combos.json (~100 entrées) était réécrit à chaque tentative de combo.
/// </summary>
public static class JsonFileStore
{
    /// <summary>Vrai si le dernier <see cref="Load{T}"/> a rencontré un fichier illisible et l'a
    /// mis de côté en <c>.corrupt</c>. Consulté par l'UI pour prévenir l'utilisateur plutôt que
    /// de le laisser découvrir la perte tout seul.</summary>
    public static string? LastCorruptBackupPath { get; private set; }

    public static T Load<T>(string path, T fallback)
    {
        if (!File.Exists(path)) return fallback;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json) ?? fallback;
        }
        catch (Exception ex)
        {
            // Fichier corrompu ou mal formé : on retombe sur la valeur par défaut plutôt que de
            // planter l'appli — mais on met d'abord l'original de côté, pour qu'il reste
            // récupérable au lieu d'être écrasé par la première sauvegarde qui suit.
            PreserveCorruptFile(path, ex);
            return fallback;
        }
    }

    private static void PreserveCorruptFile(string path, Exception ex)
    {
        try
        {
            var backup = $"{path}.corrupt";
            // Un seul niveau de sauvegarde suffit, mais on n'écrase jamais une sauvegarde
            // antérieure : c'est peut-être la seule copie encore valide.
            if (File.Exists(backup)) backup = $"{path}.{DateTime.Now:yyyyMMdd_HHmmss}.corrupt";
            File.Copy(path, backup, overwrite: false);
            LastCorruptBackupPath = backup;
            DiagnosticLog.LogException($"Fichier de configuration illisible, copié en {backup}", ex);
        }
        catch
        {
            // Best effort — ne jamais empêcher le démarrage à cause de la sauvegarde elle-même.
        }
    }

    public static void Save<T>(string path, T value)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(value, options);

        // Écrit d'abord entièrement à côté, puis remplace : à aucun instant le fichier final
        // n'est dans un état partiellement écrit. Si quoi que ce soit échoue avant le Replace,
        // l'ancien fichier (valide) est toujours en place.
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(path))
            {
                // File.Replace est atomique côté NTFS et conserve les métadonnées du fichier
                // d'origine. Le fichier .bak intermédiaire est supprimé juste après.
                var backupPath = path + ".bak";
                File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
                try { File.Delete(backupPath); } catch { /* sans conséquence */ }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.LogException($"Échec de sauvegarde de {Path.GetFileName(path)}", ex);
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* sans conséquence */ }
            throw;
        }
    }
}
