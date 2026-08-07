namespace BrawlhallaOverlay;

/// <summary>
/// Persisted, user-editable overlay behavior (Apparence/Général tabs of the
/// control panel). Everything here is meant to change live via AppState
/// without restarting the app.
/// </summary>
public sealed class OverlaySettings
{
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;

    public bool LaunchAtStartup { get; set; }

    /// <summary>Null = pas encore vu l'écran de fourche "Tu es plutôt…" (voir DashboardWindow).
    /// Distinct de OverlaySettingsConfig.WasFirstRun : une installation qui existait déjà avant
    /// l'ajout de cet écran (settings.json présent mais champ absent du JSON, donc null après
    /// désérialisation) ne doit pas se le voir imposer rétroactivement — seul un vrai tout premier
    /// lancement (WasFirstRun) déclenche la fourche, voir OverlaySettingsConfig.LoadOrCreateDefault.</summary>
    public bool? OnboardingCompleted { get; set; }

    /// <summary>Nom du profil de touches actif (voir KeyBindConfig.ListProfiles/LoadProfile).</summary>
    public string ActiveProfile { get; set; } = KeyBindConfig.DefaultProfileName;

    /// <summary>Si vrai, un combo raté ne remet pas la série de réussites à zéro.</summary>
    public bool KeepStreakOnFail { get; set; }

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

    /// <summary>Code de touche virtuelle (VK) pour "combo suivante"/"combo précédente",
    /// utilisés en plus de Ctrl+Alt (le préfixe Ctrl+Alt reste fixe comme tous les autres
    /// raccourcis — seule la touche finale est reconfigurable, via l'onglet Général du panneau
    /// de contrôle). Défauts : K (suivante, historique — anciennement le seul raccourci
    /// existant) et J (précédente, nouveau — jusque-là accessible uniquement à la manette via
    /// Start+LB). Stockés en VK plutôt qu'en nom de touche .NET pour réutiliser directement
    /// AppState.Hook.KeyDown (qui fournit des vkCode) sans conversion aller-retour.</summary>
    public int ComboNextVk { get; set; } = 0x4B; // K
    public int ComboPrevVk { get; set; } = 0x4A; // J

    /// <summary>Codes de touche virtuelle (VK) des autres raccourcis globaux Ctrl+Alt+*, tous
    /// reconfigurables individuellement (onglet Général, réglages avancés) pour éviter une
    /// collision irréparable avec un autre logiciel (OBS, Discord, un launcher) sans devoir
    /// recompiler — auparavant des `const int` figés dans MainWindow.xaml.cs. Le préfixe
    /// Ctrl+Alt reste fixe comme pour ComboNextVk/ComboPrevVk ; seule la touche finale change.
    /// Défauts identiques aux anciennes constantes : O = verrouiller, R = enregistrer un combo,
    /// U = accueil, I = révéler, H = suspendre la capture, M = masquer l'overlay.</summary>
    public int LockVk { get; set; } = 0x4F; // O
    public int RecordVk { get; set; } = 0x52; // R
    public int DashboardVk { get; set; } = 0x55; // U
    public int RevealVk { get; set; } = 0x49; // I
    public int SuspendVk { get; set; } = 0x48; // H
    public int HideVk { get; set; } = 0x4D; // M

    /// <summary>Estompe le panneau du mode Tutoriel (opacité réduite, pas masqué comme
    /// OverlayHidden) après AutoHideIdleSeconds sans input, pour ne pas polluer l'écran pendant
    /// les phases sans combat — réapparaît en opacité pleine dès le prochain appui. Désactivé par
    /// défaut (comportement historique inchangé si l'utilisateur n'active rien).</summary>
    public bool AutoHideEnabled { get; set; }
    public int AutoHideIdleSeconds { get; set; } = 6;

    /// <summary>Détection de hit par lecture d'écran (phase 1a de docs/plan_improve_combo.md) :
    /// capture périodique d'une petite zone du HUD (dégâts adverses) et détection d'un
    /// changement de pixels, sans OCR — juste "un hit a probablement eu lieu", jamais un montant.
    /// Purement additif à ComboRunner (voir Core/Vision/HudDamageSource.cs) : ne fait jamais
    /// échouer ni valider une étape, affiche seulement un badge informatif. Désactivé par défaut
    /// et nécessite un calibrage explicite (HudRoiCalibrated) avant de pouvoir s'activer.</summary>
    public bool HudDetectionEnabled { get; set; }

    /// <summary>Vrai une fois que l'utilisateur a dessiné la zone à surveiller (voir
    /// Windows/HudCalibrationWindow). Tant que c'est faux, HudDetectionEnabled ne doit
    /// avoir aucun effet — pas de zone valide à capturer.</summary>
    public bool HudRoiCalibrated { get; set; }

    /// <summary>Rectangle de capture en pixels physiques d'écran (coordonnées de
    /// System.Windows.Forms.Screen, PAS en unités WPF) — la capture GDI
    /// (System.Drawing.Graphics.CopyFromScreen) travaille nativement dans cet espace, donc
    /// aucune conversion DPI n'est nécessaire côté Core/Vision. Défini par
    /// HudCalibrationWindow.</summary>
    public int HudRoiX { get; set; }
    public int HudRoiY { get; set; }
    public int HudRoiWidth { get; set; }
    public int HudRoiHeight { get; set; }

    /// <summary>Palier de dégâts par couleur (phase 1b, docs/plan_improve_combo.md §3.1.1) :
    /// contrairement à HudRoiX/Y/Width/Height (diff de pixels sur le nombre de dégâts, nécessite
    /// l'option "Nombre de dégâts" activée en jeu), celle-ci lit la couleur de la barre sous
    /// l'icône adverse (toujours visible, aucun réglage de jeu requis) et la classe en
    /// Blanc/Jaune/Orange/Rouge/Noir — les 5 paliers officiels du jeu (0/50/100/150/200%).
    /// Bien plus simple qu'un OCR de chiffres : une couleur moyenne + une classification par
    /// teinte, pas de gabarit à fabriquer.</summary>
    public bool HudTierDetectionEnabled { get; set; }
    public bool HudTierRoiCalibrated { get; set; }
    public int HudTierRoiX { get; set; }
    public int HudTierRoiY { get; set; }
    public int HudTierRoiWidth { get; set; }
    public int HudTierRoiHeight { get; set; }
}
