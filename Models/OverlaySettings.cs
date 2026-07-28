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

    /// <summary>Nom du profil de touches actif (voir KeyBindConfig.ListProfiles/LoadProfile).</summary>
    public string ActiveProfile { get; set; } = KeyBindConfig.DefaultProfileName;

    /// <summary>Mode affiché au démarrage : 0 = Historique, 1 = Grandes flèches, 2 = Tutoriel.</summary>
    public int DefaultMode { get; set; }

    /// <summary>Modes inclus dans le cycle rapide Ctrl+Alt+P.</summary>
    public List<int> FavoriteModes { get; set; } = new() { 0, 1, 2 };

    /// <summary>Si vrai, une combo ratée ne remet pas la série de réussites à zéro.</summary>
    public bool KeepStreakOnFail { get; set; }

    /// <summary>Nombre de lignes conservées dans l'historique de coups.</summary>
    public int MaxHistoryEntries { get; set; } = 12;

    /// <summary>Bips de succès/échec en mode Tutoriel.</summary>
    public bool SoundEnabled { get; set; }

    /// <summary>Après une combo réussie, passe automatiquement à la combo suivante de la liste.</summary>
    public bool ChainCombos { get; set; }

    /// <summary>Nombre de réussites consécutives (série) requis avant de passer à la combo
    /// suivante en mode enchaînement — permet une "session guidée" où chaque combo doit être
    /// maîtrisée (répétée N fois d'affilée) avant de progresser, pas juste réussie une fois.</summary>
    public int ChainStreakThreshold { get; set; } = 1;

    /// <summary>Mode révision : masque les étapes pas encore jouées de la combo active (Ctrl+Alt+I pour révéler temporairement).</summary>
    public bool QuizMode { get; set; }

    /// <summary>Arme sur laquelle s'entraîner (voir WeaponComboPresets.Weapons) : filtre la liste
    /// des combos affichées/cyclées (Ctrl+Alt+K) au panneau de contrôle et en mode Tutoriel.
    /// Vide = toutes les combos, sans distinction d'arme.</summary>
    public string TrainingWeaponFilter { get; set; } = "";

    /// <summary>Index dans System.Windows.Forms.Screen.AllScreens de l'écran sur lequel afficher
    /// l'overlay. -1 = écran principal (Screen.PrimaryScreen), comportement historique. Utile sur
    /// un setup multi-écran où le jeu tourne sur un moniteur secondaire : sans ce réglage l'overlay
    /// restait toujours calé sur SystemParameters.WorkArea (toujours l'écran principal Windows),
    /// voir docs/audit_features.md §1.4.</summary>
    public int MonitorIndex { get; set; } = -1;
}
