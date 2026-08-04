using System.Collections.Generic;

namespace BrawlhallaOverlay;

public enum OverlayPosition
{
    BottomLeft,
    BottomRight,
    TopLeft,
    TopRight,
    Free,
}

/// <summary>
/// Persisted, user-editable overlay behavior (Apparence/Général tabs of the
/// control panel). Everything here is meant to change live via AppState
/// without restarting the app.
/// </summary>
public sealed class OverlaySettings
{
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;
    public OverlayPosition Position { get; set; } = OverlayPosition.BottomLeft;

    // Position mémorisée quand Position == Free (dernier drag manuel).
    public double FreeLeft { get; set; } = 24;
    public double FreeTop { get; set; } = 24;

    public bool LaunchAtStartup { get; set; }

    /// <summary>Null = pas encore vu l'écran de fourche "Tu es plutôt…" (voir OnboardingWindow).
    /// Distinct de OverlaySettingsConfig.WasFirstRun : une installation qui existait déjà avant
    /// l'ajout de cet écran (settings.json présent mais champ absent du JSON, donc null après
    /// désérialisation) ne doit pas se le voir imposer rétroactivement — seul un vrai tout premier
    /// lancement (WasFirstRun) déclenche la fourche, voir OverlaySettingsConfig.LoadOrCreateDefault.</summary>
    public bool? OnboardingCompleted { get; set; }

    /// <summary>Profil choisi à la fourche du premier lancement ("Débutant"/"Connaisseur"/"Expert"),
    /// affiché en contexte sur l'écran de reprise. Purement informatif, ne pilote aucun comportement.</summary>
    public string OnboardingProfile { get; set; } = "";

    /// <summary>Nom du profil de touches actif (voir KeyBindConfig.ListProfiles/LoadProfile).</summary>
    public string ActiveProfile { get; set; } = KeyBindConfig.DefaultProfileName;

    /// <summary>Mode affiché au démarrage : 0 = Historique, 1 = Grandes flèches, 2 = Tutoriel.</summary>
    public int DefaultMode { get; set; }

    /// <summary>Modes inclus dans le cycle rapide Ctrl+Alt+P.</summary>
    public List<int> FavoriteModes { get; set; } = new() { 0, 1, 2 };

    /// <summary>Si vrai, un combo raté ne remet pas la série de réussites à zéro.</summary>
    public bool KeepStreakOnFail { get; set; }

    /// <summary>Nombre de lignes conservées dans l'historique de coups.</summary>
    public int MaxHistoryEntries { get; set; } = 12;

    /// <summary>Bips de succès/échec en mode Tutoriel.</summary>
    public bool SoundEnabled { get; set; }

    /// <summary>Après un combo réussi, passe automatiquement au combo suivant de la liste.</summary>
    public bool ChainCombos { get; set; }

    /// <summary>Nombre de réussites consécutives (série) requis avant de passer au combo
    /// suivant en mode enchaînement — permet une "session guidée" où chaque combo doit être
    /// maîtrisé (répété N fois d'affilée) avant de progresser, pas juste réussi une fois.</summary>
    public int ChainStreakThreshold { get; set; } = 1;

    /// <summary>Mode révision : masque les étapes pas encore jouées du combo actif (Ctrl+Alt+I pour révéler temporairement).</summary>
    public bool QuizMode { get; set; }

    /// <summary>Arme sur laquelle s'entraîner (voir WeaponComboPresets.Weapons) : filtre la liste
    /// des combos affichées/cyclées (Ctrl+Alt+K) au panneau de contrôle et en mode Tutoriel.
    /// Vide = toutes les combos, sans distinction d'arme.</summary>
    public string TrainingWeaponFilter { get; set; } = "";

    /// <summary>Légend sur lequel s'entraîner (voir LegendComboPresets.Legends) : filtre en plus
    /// du filtre d'arme la liste des combos affichées/cyclées. Vide = toutes les combos, sans
    /// distinction de légend (combos génériques d'arme incluses).</summary>
    public string TrainingLegendFilter { get; set; } = "";

    /// <summary>Index dans System.Windows.Forms.Screen.AllScreens de l'écran sur lequel afficher
    /// l'overlay. -1 = écran principal (Screen.PrimaryScreen), comportement historique. Utile sur
    /// un setup multi-écran où le jeu tourne sur un moniteur secondaire : sans ce réglage l'overlay
    /// restait toujours calé sur SystemParameters.WorkArea (toujours l'écran principal Windows),
    /// voir docs/audit_features.md §1.4.</summary>
    public int MonitorIndex { get; set; } = -1;
}
