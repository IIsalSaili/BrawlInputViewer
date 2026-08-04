using System.Collections.Generic;

namespace BrawlhallaOverlay;

/// <summary>
/// Comment une leçon du "Parcours" (docs/plan_ux_onboarding.md §4) est validée. Reflète la
/// contrainte dure du §4.2 : l'app ne voit que les inputs, jamais l'état du jeu — donc seule une
/// *séquence d'appuis* est vérifiable, jamais un effet en jeu (survivre, toucher l'adversaire...).
/// </summary>
public enum LessonValidationKind
{
    /// <summary>Presser chaque action de RequiredActionsOnce au moins une fois, ordre libre.</summary>
    PressAllOnce,

    /// <summary>Suite ordonnée d'étapes (voir Sequence) — validée en interne via un ComboRunner
    /// éphémère, pas persisté dans combos.json : la mécanique de matching existe déjà et est déjà
    /// éprouvée, pas besoin d'un second moteur pour la même chose.</summary>
    Sequence,

    /// <summary>Ne PAS presser une des actions de AbsenceActions pendant AbsenceSeconds depuis le
    /// début de la leçon — seule forme de validation possible pour un conseil de type "retenue"
    /// (ex. ne pas paniquer au dodge), qui ne peut pas se vérifier par un effet en jeu.</summary>
    AbsenceTimer,

    /// <summary>Un event AppState précis (Lock/CaptureSuspended) se déclenche une fois — pour les
    /// leçons qui portent sur l'app elle-même (déverrouiller l'overlay, suspendre la capture)
    /// plutôt que sur une action de jeu mappée à une touche.</summary>
    ToggleOnce,
}

public sealed class LessonStep
{
    public List<string> RequiredActions { get; set; } = new();
}

public sealed class Lesson
{
    public string Id { get; set; } = "";
    public int Chapter { get; set; }
    public string ChapterTitle { get; set; } = "";
    public string Title { get; set; } = "";
    public string Objective { get; set; } = "";
    public string Explanation { get; set; } = "";

    public LessonValidationKind Kind { get; set; }

    /// <summary>PressAllOnce.</summary>
    public List<string> RequiredActionsOnce { get; set; } = new();

    /// <summary>Sequence.</summary>
    public List<LessonStep> Sequence { get; set; } = new();

    /// <summary>AbsenceTimer.</summary>
    public List<string> AbsenceActions { get; set; } = new();
    public int AbsenceSeconds { get; set; }

    /// <summary>ToggleOnce : "Lock" (AppState.LockChanged) ou "CaptureSuspended"
    /// (AppState.CaptureSuspendedChanged).</summary>
    public string ToggleEventName { get; set; } = "";

    /// <summary>Si faux, la leçon n'est validable qu'en partie ou pas du tout par l'app (badge
    /// différent affiché dans ParcoursWindow) — même quand un Kind existe pour le morceau
    /// vérifiable, certaines leçons restent honnêtement incomplètes sans lire l'état du jeu.</summary>
    public bool FullyValidatedByApp { get; set; } = true;

    /// <summary>Rappel affiché à l'écran pour la part non vérifiable par l'app (ex. "vérifie en
    /// Training Room que tu es bien remonté sur le stage").</summary>
    public string VerifyYourselfNote { get; set; } = "";

    /// <summary>Traçabilité de la source des faits de jeu cités (règle §3 du plan : aucune donnée
    /// factuelle sans source réelle et vérifiable) — affiché en petit dans ParcoursWindow.</summary>
    public string SourceNote { get; set; } = "";
}
