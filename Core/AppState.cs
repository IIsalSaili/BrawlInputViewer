using System;
using System.Collections.Generic;
using System.Linq;

namespace BrawlhallaOverlay;

/// <summary>
/// Single source of truth shared between the overlay window, the control
/// panel window and the tray icon. Holds the loaded KeyBind/Combo lists and
/// overlay settings in memory, persists on every mutation, and raises events
/// so all UI surfaces stay in sync without a restart.
/// </summary>
public static class AppState
{
    // Hook installé une seule fois ; l'overlay et le panneau de contrôle
    // (pour la réassignation de touches) s'abonnent tous deux dessus.
    public static KeyboardHook Hook { get; } = new();

    // Polling manette (XInput), même statut de sécurité que le hook clavier :
    // lecture passive de l'état des boutons, aucune injection ni automatisation.
    // Démarré inconditionnellement — sans manette branchée, le polling échoue
    // silencieusement (voir GamepadHook.Poll) sans coût notable.
    public static GamepadHook Gamepad { get; } = new();

    // Sources de vision (docs/plan_improve_combo.md, phase 1) : partagées comme Hook/Gamepad
    // pour que MainWindow (qui les démarre/arrête selon Settings) ET ControlPanelWindow (qui a
    // besoin de s'abonner à leur événement Sampled pour un aperçu en direct pendant le
    // calibrage) accèdent à la même instance, sans passer par MainWindow.
    public static HudDamageSource HudDamageSource { get; } = new();
    public static HudDamageTierSource HudTierSource { get; } = new();

    // Chargé avant les touches : le profil actif détermine quel fichier
    // keybinds*.json charger (voir KeyBindConfig.ListProfiles/LoadProfile).
    public static OverlaySettings Settings { get; private set; } = OverlaySettingsConfig.LoadOrCreateDefault();

    private static List<KeyBind> _binds = KeyBindConfig.LoadProfile(Settings.ActiveProfile);
    public static List<KeyBind> Binds => _binds;

    private static List<Combo> _combos = ComboConfig.LoadOrEmpty();
    public static List<Combo> Combos => _combos;

    // Tous les combos d'arme préréglés (les seuls vérifiés actuellement, voir
    // LegendComboPresets.Table qui est vide) sont importés d'office au démarrage plutôt que
    // d'attendre que l'utilisateur sélectionne chaque personnage/arme un par un — demande
    // explicite de l'utilisateur ("toutes les légendes devraient avoir leur combo importé de
    // base"). Upsert par Id stable (ImportWeaponPresets) donc sans effet sur les combos perso
    // de l'utilisateur, et sans risque de dupliquer à chaque lancement.
    static AppState()
    {
        foreach (var weapon in WeaponComboPresets.Weapons) ImportWeaponPresets(weapon);
    }

    public static bool Locked { get; private set; } = true;
    public static int ActiveComboIndex { get; private set; } = _combos.Count > 0 ? 0 : -1;
    public static bool Recording { get; private set; }

    // Le hook clavier/manette est global par nature (il doit capter les touches
    // même quand Brawlhalla a le focus) : hors du jeu, ça veut dire que
    // l'historique et le suivi de combo réagissent à n'importe quelle frappe
    // faite dans d'autres fenêtres (navigateur, éditeur de code...), ce qui
    // n'est pas évident pour un utilisateur qui laisse juste l'app tourner en
    // faisant autre chose. Cette bascule (tray + panneau + Ctrl+Alt+H) coupe le
    // suivi sans fermer l'app ni perdre la config.
    public static bool CaptureSuspended { get; private set; }

    // Masque complètement l'overlay (fenêtre principale + barre de contrôle overlay) sans
    // fermer l'app ni désinstaller les hooks — contrairement au verrouillage (AppState.Locked),
    // qui laisse l'overlay affiché mais non interactif, ceci le rend invisible à l'écran tout en
    // gardant l'icône de tray et le panneau de contrôle utilisables pour le réafficher. Demande
    // explicite de l'utilisateur ("pouvoir totalement masquer l'appli sans la fermer").
    public static bool OverlayHidden { get; private set; }

    // Stats de précision par action (mode Tutoriel), persistées entre sessions.
    private static readonly Dictionary<string, ActionStat> _stats = BuildStatsIndex(StatsConfig.LoadOrEmpty());
    public static IReadOnlyCollection<ActionStat> Stats => _stats.Values;

    // Journal de la session en cours (tous les coups joués, indépendamment du
    // mode ou de l'enregistrement de combo) pour l'export CSV des analytics.
    public static readonly DateTime SessionStart = DateTime.UtcNow;
    public static readonly List<(DateTime Time, string Actions)> SessionLog = new();

    public static event Action? BindsChanged;
    public static event Action? CombosChanged;
    public static event Action? SettingsChanged;
    public static event Action<bool>? LockChanged;
    public static event Action<int>? ActiveComboChanged;
    public static event Action<bool>? RecordingChanged;
    public static event Action<bool>? CaptureSuspendedChanged;
    public static event Action<bool>? OverlayHiddenChanged;

    public static void SetCaptureSuspended(bool suspended)
    {
        if (CaptureSuspended == suspended) return;
        CaptureSuspended = suspended;
        CaptureSuspendedChanged?.Invoke(suspended);
    }

    public static void SetOverlayHidden(bool hidden)
    {
        if (OverlayHidden == hidden) return;
        OverlayHidden = hidden;
        OverlayHiddenChanged?.Invoke(hidden);
    }

    public static void ToggleOverlayHidden() => SetOverlayHidden(!OverlayHidden);

    public static void ToggleCaptureSuspended() => SetCaptureSuspended(!CaptureSuspended);

    /// <summary>Demande à l'overlay de révéler temporairement le combo en mode révision
    /// (déclenché depuis le panneau de contrôle, en plus du raccourci Ctrl+Alt+I).</summary>
    public static event Action? QuizRevealRequested;
    public static void RequestQuizReveal() => QuizRevealRequested?.Invoke();

    /// <summary>Persiste combos.json sans lever CombosChanged : utilisé pour les compteurs de
    /// performance (BestStreak/TotalAttempts...) mutés en place à chaque tentative, pour ne
    /// pas reconstruire le ComboRunner actif (et perdre sa série en cours) à chaque coup joué.</summary>
    public static void SaveCombosQuiet() => ComboConfig.Save(_combos);

    private static Dictionary<string, ActionStat> BuildStatsIndex(List<ActionStat> stats)
    {
        var dict = new Dictionary<string, ActionStat>();
        foreach (var stat in stats) dict[stat.Action] = stat;
        return dict;
    }

    /// <summary>Appelé par ComboRunner (via l'overlay) à chaque étape de combo réussie/ratée.</summary>
    public static void RecordStepResult(IEnumerable<string> actions, bool success)
    {
        foreach (var action in actions)
        {
            if (!_stats.TryGetValue(action, out var stat))
            {
                stat = new ActionStat { Action = action };
                _stats[action] = stat;
            }
            if (success) stat.Successes++; else stat.Failures++;
        }
        StatsConfig.Save(new List<ActionStat>(_stats.Values));
    }

    public static void LogSessionMove(string actionsText) => SessionLog.Add((DateTime.UtcNow, actionsText));

    public static void ReplaceBinds(List<KeyBind> binds)
    {
        _binds = binds;
        NotifyBindsMutated();
    }

    /// <summary>Call after mutating Binds/KeyBind items in place (color, symbol, keys...).</summary>
    public static void NotifyBindsMutated()
    {
        KeyBindConfig.RecomputeVirtualKeyCodes(_binds);
        KeyBindConfig.SaveProfile(Settings.ActiveProfile, _binds);
        BindsChanged?.Invoke();
    }

    /// <summary>Bascule vers un autre profil de touches (le crée s'il n'existe pas encore,
    /// avec les valeurs par défaut). Le profil actif est mémorisé dans settings.json.</summary>
    public static void SwitchProfile(string profileName)
    {
        if (Settings.ActiveProfile == profileName) return;
        Settings.ActiveProfile = profileName;
        SaveSettings();
        _binds = KeyBindConfig.LoadProfile(profileName);
        BindsChanged?.Invoke();
    }

    /// <summary>Duplique le profil actif sous un nouveau nom et bascule dessus.</summary>
    public static void DuplicateProfileAs(string newName)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<List<KeyBind>>(System.Text.Json.JsonSerializer.Serialize(_binds))!;
        KeyBindConfig.SaveProfile(newName, copy);
        SwitchProfile(newName);
    }

    /// <summary>Supprime un profil (jamais le profil "Défaut") et rebascule sur "Défaut" si
    /// c'était le profil actif.</summary>
    public static void DeleteProfile(string profileName)
    {
        if (profileName == KeyBindConfig.DefaultProfileName) return;
        KeyBindConfig.DeleteProfile(profileName);
        if (Settings.ActiveProfile == profileName) SwitchProfile(KeyBindConfig.DefaultProfileName);
    }

    public static void ReplaceCombos(List<Combo> combos)
    {
        _combos = combos;
        NotifyCombosMutated();
    }

    /// <summary>Call after mutating Combos/Combo items in place (add/edit/delete/reorder).</summary>
    public static void NotifyCombosMutated()
    {
        ComboConfig.Save(_combos);
        CombosChanged?.Invoke();
        if (ActiveComboIndex < 0 || ActiveComboIndex >= _combos.Count) SetActiveCombo(FirstFilteredIndex());
    }

    /// <summary>Importe les 5 combos préréglés de l'arme donnée (voir WeaponComboPresets) dans
    /// la liste. Idempotent par Id stable, mais en "upsert" plutôt qu'en skip : si le contenu
    /// d'un combo préréglé a changé depuis un import précédent (ex. correction d'un combo
    /// infaisable), la réimporter met à jour son contenu (stats de performance remises à 0,
    /// l'ancien combo étant différent) au lieu de laisser l'ancien contenu figé.</summary>
    public static void ImportWeaponPresets(string weapon)
    {
        var indexById = new Dictionary<string, int>();
        for (var i = 0; i < _combos.Count; i++) indexById[_combos[i].Id] = i;

        var changed = false;
        foreach (var preset in WeaponComboPresets.BuildPresetCombos().Where(c => c.Weapon == weapon))
        {
            if (indexById.TryGetValue(preset.Id, out var idx)) _combos[idx] = preset;
            else _combos.Add(preset);
            changed = true;
        }
        if (changed) NotifyCombosMutated();
    }

    /// <summary>Importe les combos préréglées d'un légend (voir LegendComboPresets), même logique
    /// d'upsert par Id stable que ImportWeaponPresets.</summary>
    public static void ImportLegendPresets(string legend)
    {
        var indexById = new Dictionary<string, int>();
        for (var i = 0; i < _combos.Count; i++) indexById[_combos[i].Id] = i;

        var changed = false;
        foreach (var preset in LegendComboPresets.BuildPresetCombos().Where(c => c.Legend == legend))
        {
            if (indexById.TryGetValue(preset.Id, out var idx)) _combos[idx] = preset;
            else _combos.Add(preset);
            changed = true;
        }
        if (changed) NotifyCombosMutated();
    }

    /// <summary>Importe, pour un personnage, à la fois ses combos de légend (ImportLegendPresets)
    /// et les combos génériques de chacune des armes qu'il utilise réellement
    /// (LegendComboPresets.WeaponsFor + ImportWeaponPresets par arme) — un seul clic pour couvrir
    /// tout ce qui est jouable sur ce personnage, plutôt que de devoir cliquer séparément sur le
    /// bouton légend puis sur le bouton arme pour chacune des deux armes.</summary>
    public static void ImportCharacterPresets(string legend)
    {
        ImportLegendPresets(legend);
        foreach (var weapon in LegendComboPresets.WeaponsFor(legend)) ImportWeaponPresets(weapon);
    }

    /// <summary>Index absolus (dans Combos) des combos qui correspondent au personnage/arme actifs
    /// (Settings.TrainingLegendFilter / TrainingWeaponFilter), ou tous les index si aucun filtre.
    /// Un personnage n'est pas un simple ET avec l'arme : un personnage regroupe ses combos de
    /// légend (Combo.Legend == personnage) ET les combos génériques des armes qu'il utilise
    /// réellement (Combo.Weapon dans LegendComboPresets.WeaponsFor(personnage), Combo.Legend vide)
    /// — sinon "Ada" tout seul ne montrerait que ses 2 combos Signature et cacherait les combos
    /// génériques Blasters qu'elle peut pourtant jouer. TrainingWeaponFilter, quand un personnage
    /// est actif, ne sert plus qu'à sous-filtrer sur une seule de ses armes ; sans personnage actif,
    /// il redevient un filtre d'arme pur comme avant (parcourir une arme sans se soucier du perso).</summary>
    public static List<int> FilteredComboIndices()
    {
        var weaponFilter = Settings.TrainingWeaponFilter;
        var legendFilter = Settings.TrainingLegendFilter;
        var result = new List<int>();
        var legendWeapons = string.IsNullOrEmpty(legendFilter) ? null : LegendComboPresets.WeaponsFor(legendFilter);
        var maxDex = string.IsNullOrEmpty(legendFilter) ? -1 : LegendStats.MaxReachableDex(legendFilter);
        for (var i = 0; i < _combos.Count; i++)
        {
            var combo = _combos[i];
            if (legendWeapons is not null)
            {
                var matchesLegend = combo.Legend == legendFilter;
                var matchesCharacterWeapon = string.IsNullOrEmpty(combo.Legend) && legendWeapons.Contains(combo.Weapon);
                if (!matchesLegend && !matchesCharacterWeapon) continue;
            }
            if (!string.IsNullOrEmpty(weaponFilter) && combo.Weapon != weaponFilter) continue;

            // Un combo qui demande plus de Dex que ce que le personnage choisi peut atteindre même
            // avec une stance (base + 1, voir LegendStats) est physiquement injouable sur lui — ne
            // devrait pas apparaître comme une option pour ce personnage (demande explicite de
            // l'utilisateur, confirmée par un cas réel : Teros, Dex 3, avec d'anciens combos
            // demandant "7+"/"9" Dex). maxDex == -1 (pas de personnage choisi, ou stats inconnues)
            // laisse tout passer, par prudence plutôt que de masquer sans certitude.
            if (maxDex >= 0 && combo.MinDex is int minDex && minDex > maxDex) continue;

            result.Add(i);
        }
        return result;
    }

    private static int FirstFilteredIndex()
    {
        var filtered = FilteredComboIndices();
        return filtered.Count > 0 ? filtered[0] : -1;
    }

    /// <summary>Position (0-based) et nombre du combo actif parmi les combos filtrés par
    /// arme, pour l'affichage "X/Y" du mode Tutoriel.</summary>
    public static (int Position, int Count) ActiveComboFilteredPosition()
    {
        var filtered = FilteredComboIndices();
        return (filtered.IndexOf(ActiveComboIndex), filtered.Count);
    }

    /// <summary>Change l'arme sur laquelle s'entraîner : filtre la liste de combos cyclée/affichée.
    /// Sans personnage actif, filtre pur sur l'arme. Avec un personnage actif, sous-filtre sur une
    /// seule de ses armes (voir FilteredComboIndices). Si le combo actif ne correspond plus au
    /// nouveau filtre, bascule sur le premier combo filtré (ou -1 s'il n'y en a aucun).</summary>
    public static void SetTrainingWeaponFilter(string weapon)
    {
        if (Settings.TrainingWeaponFilter == weapon) return;
        Settings.TrainingWeaponFilter = weapon;
        SaveSettings();
        if (!FilteredComboIndices().Contains(ActiveComboIndex)) SetActiveCombo(FirstFilteredIndex());
    }

    /// <summary>Change le personnage sur lequel s'entraîner : ses combos de légend + les combos
    /// génériques des armes qu'il utilise réellement (voir FilteredComboIndices). Même logique de
    /// repli que SetTrainingWeaponFilter.</summary>
    public static void SetTrainingLegendFilter(string legend)
    {
        if (Settings.TrainingLegendFilter == legend) return;
        Settings.TrainingLegendFilter = legend;
        SaveSettings();
        if (!FilteredComboIndices().Contains(ActiveComboIndex)) SetActiveCombo(FirstFilteredIndex());
    }

    public static void SaveSettings()
    {
        OverlaySettingsConfig.Save(Settings);
        SettingsChanged?.Invoke();
    }

    public static void SetLocked(bool locked)
    {
        if (Locked == locked) return;
        Locked = locked;
        LockChanged?.Invoke(locked);
    }

    public static void ToggleLock() => SetLocked(!Locked);

    public static void SetActiveCombo(int index)
    {
        if (ActiveComboIndex == index) return;
        ActiveComboIndex = index;
        ActiveComboChanged?.Invoke(index);
    }

    public static void CycleCombo()
    {
        var filtered = FilteredComboIndices();
        if (filtered.Count == 0) return;
        var pos = filtered.IndexOf(ActiveComboIndex);
        SetActiveCombo(filtered[(pos + 1) % filtered.Count]);
    }

    /// <summary>Symétrique de CycleCombo en sens inverse — nécessaire pour la barre de contrôle
    /// overlay et le chord manette Start+LB (§5.3 du plan UX), qui offrent tous deux un aller ET
    /// un retour dans la liste plutôt que de ne pouvoir que tourner en avant.</summary>
    public static void CyclePreviousCombo()
    {
        var filtered = FilteredComboIndices();
        if (filtered.Count == 0) return;
        var pos = filtered.IndexOf(ActiveComboIndex);
        var prevPos = pos <= 0 ? filtered.Count - 1 : pos - 1;
        SetActiveCombo(filtered[prevPos]);
    }

    public static void SetRecording(bool recording)
    {
        if (Recording == recording) return;
        Recording = recording;
        RecordingChanged?.Invoke(recording);
    }

    // --- Parcours (docs/plan_ux_onboarding.md §4) : progression persistée entre sessions,
    // séparée de combos.json/stats.json (contenu différent, pas de raison de les coupler). ---
    private static readonly ParcoursProgress _parcoursProgress = ParcoursProgressConfig.LoadOrEmpty();
    private static readonly HashSet<string> _completedLessonIds = new(_parcoursProgress.CompletedLessonIds);

    public static IReadOnlyCollection<string> CompletedLessonIds => _completedLessonIds;
    public static string ParcoursCurrentLessonId => _parcoursProgress.CurrentLessonId;

    public static bool IsLessonCompleted(string lessonId) => _completedLessonIds.Contains(lessonId);

    public static void MarkLessonCompleted(string lessonId)
    {
        if (!_completedLessonIds.Add(lessonId)) return;
        _parcoursProgress.CompletedLessonIds = new List<string>(_completedLessonIds);
        ParcoursProgressConfig.Save(_parcoursProgress);
    }

    public static void SetParcoursCurrentLesson(string lessonId)
    {
        if (_parcoursProgress.CurrentLessonId == lessonId) return;
        _parcoursProgress.CurrentLessonId = lessonId;
        ParcoursProgressConfig.Save(_parcoursProgress);
    }
}
