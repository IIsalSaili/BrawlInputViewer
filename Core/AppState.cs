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

    // Chargé avant les touches : le profil actif détermine quel fichier
    // keybinds*.json charger (voir KeyBindConfig.ListProfiles/LoadProfile).
    public static OverlaySettings Settings { get; private set; } = OverlaySettingsConfig.LoadOrCreateDefault();

    private static List<KeyBind> _binds = KeyBindConfig.LoadProfile(Settings.ActiveProfile);
    public static List<KeyBind> Binds => _binds;

    private static List<Combo> _combos = ComboConfig.LoadOrEmpty();
    public static List<Combo> Combos => _combos;

    public static int ActiveMode { get; private set; }
    public static bool Locked { get; private set; } = true;
    public static int ActiveComboIndex { get; private set; } = _combos.Count > 0 ? 0 : -1;
    public static bool Recording { get; private set; }

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
    public static event Action<int>? ModeChanged;
    public static event Action<bool>? LockChanged;
    public static event Action<int>? ActiveComboChanged;
    public static event Action<bool>? RecordingChanged;

    /// <summary>Demande à l'overlay de révéler temporairement la combo en mode révision
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
        KeyBindConfig.RecomputeVirtualKeyCodes(_binds);
        KeyBindConfig.SaveProfile(Settings.ActiveProfile, _binds);
        BindsChanged?.Invoke();
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
        ComboConfig.Save(_combos);
        CombosChanged?.Invoke();
        if (ActiveComboIndex < 0 || ActiveComboIndex >= _combos.Count) SetActiveCombo(FirstFilteredIndex());
    }

    /// <summary>Call after mutating Combos/Combo items in place (add/edit/delete/reorder).</summary>
    public static void NotifyCombosMutated()
    {
        ComboConfig.Save(_combos);
        CombosChanged?.Invoke();
        if (ActiveComboIndex < 0 || ActiveComboIndex >= _combos.Count) SetActiveCombo(FirstFilteredIndex());
    }

    /// <summary>Importe les 5 combos préréglées de l'arme donnée (voir WeaponComboPresets) dans
    /// la liste. Idempotent par Id stable, mais en "upsert" plutôt qu'en skip : si le contenu
    /// d'une combo préréglée a changé depuis un import précédent (ex. correction d'une combo
    /// infaisable), la réimporter met à jour son contenu (stats de performance remises à 0,
    /// l'ancienne combo étant différente) au lieu de laisser l'ancien contenu figé.</summary>
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

    /// <summary>Index absolus (dans Combos) des combos qui correspondent au filtre d'arme actif
    /// (Settings.TrainingWeaponFilter), ou tous les index si le filtre est vide.</summary>
    public static List<int> FilteredComboIndices()
    {
        var filter = Settings.TrainingWeaponFilter;
        var result = new List<int>();
        for (var i = 0; i < _combos.Count; i++)
            if (string.IsNullOrEmpty(filter) || _combos[i].Weapon == filter) result.Add(i);
        return result;
    }

    private static int FirstFilteredIndex()
    {
        var filtered = FilteredComboIndices();
        return filtered.Count > 0 ? filtered[0] : -1;
    }

    /// <summary>Position (0-based) et nombre de la combo active parmi les combos filtrées par
    /// arme, pour l'affichage "X/Y" du mode Tutoriel.</summary>
    public static (int Position, int Count) ActiveComboFilteredPosition()
    {
        var filtered = FilteredComboIndices();
        return (filtered.IndexOf(ActiveComboIndex), filtered.Count);
    }

    /// <summary>Change l'arme sur laquelle s'entraîner : filtre la liste de combos cyclée/affichée.
    /// Si la combo active ne correspond plus au nouveau filtre, bascule sur la première combo
    /// filtrée (ou -1 s'il n'y en a aucune pour cette arme).</summary>
    public static void SetTrainingWeaponFilter(string weapon)
    {
        if (Settings.TrainingWeaponFilter == weapon) return;
        Settings.TrainingWeaponFilter = weapon;
        SaveSettings();
        if (!FilteredComboIndices().Contains(ActiveComboIndex)) SetActiveCombo(FirstFilteredIndex());
    }

    public static void SaveSettings()
    {
        OverlaySettingsConfig.Save(Settings);
        SettingsChanged?.Invoke();
    }

    public static void SetMode(int mode)
    {
        if (mode is < 0 or > 2 || ActiveMode == mode) return;
        ActiveMode = mode;
        ModeChanged?.Invoke(mode);
    }

    public static void CycleMode()
    {
        var favorites = Settings.FavoriteModes.Count > 0 ? Settings.FavoriteModes : new List<int> { 0, 1, 2 };
        var idx = favorites.IndexOf(ActiveMode);
        var next = favorites[(idx + 1) % favorites.Count];
        SetMode(next);
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

    public static void SetRecording(bool recording)
    {
        if (Recording == recording) return;
        Recording = recording;
        RecordingChanged?.Invoke(recording);
    }
}
