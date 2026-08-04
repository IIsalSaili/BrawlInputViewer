using System.Collections.Generic;

namespace BrawlhallaOverlay;

/// <summary>Progression persistée dans le Parcours (parcours_progress.json).</summary>
public sealed class ParcoursProgress
{
    public List<string> CompletedLessonIds { get; set; } = new();

    /// <summary>Dernière leçon affichée — sert à "Reprendre" au prochain lancement de ParcoursWindow.</summary>
    public string CurrentLessonId { get; set; } = "";
}
