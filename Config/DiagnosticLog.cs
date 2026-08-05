using System;
using System.IO;

namespace BrawlhallaOverlay;

/// <summary>
/// Journal de diagnostic minimal (docs/amelioration.md piste #9) : ajoute une ligne horodatée à
/// diagnostic.log à côté de l'exe à chaque exception non gérée. Rien d'automatique au-delà de ça
/// (pas de log de chaque action) — l'app n'avait jusque-là aucune trace persistante d'un crash une
/// fois la MessageBox fermée, ce qui rend un bug intermittent non reproduit sous les yeux de
/// l'utilisateur impossible à diagnostiquer après coup (voir mémoire
/// feedback_diagnose_intermittent_issues). Écriture "best effort" : une erreur d'écriture du log
/// lui-même ne doit jamais faire planter l'app par-dessus l'erreur qu'on essayait de journaliser.
/// </summary>
public static class DiagnosticLog
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "diagnostic.log");

    public static void LogException(string context, Exception ex)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Best effort — ne jamais faire planter l'app à cause du logging lui-même.
        }
    }
}
