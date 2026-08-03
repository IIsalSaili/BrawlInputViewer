using System;
using System.Collections.Generic;
using System.Linq;

namespace BrawlhallaOverlay;

public enum ComboRunState
{
    Waiting,
    InProgress,
    Success,
    Failed,
}

/// <summary>Why a step failed. Timeout is kept for backward-compat with saved combos/UI
/// color-mapping but is never raised anymore — see the timing note on Feed below.</summary>
public enum ComboFailReason
{
    WrongInput,
    Timeout,
}

/// <summary>
/// Pure validation logic for a Combo: fed one "coup joué" (the set of KeyBind
/// actions *currently held*, recomputed by the caller on every key change) at
/// a time, tracks progress through the combo's steps, and resets on a wrong
/// input. Aucune notion de timing nulle part dans ce moteur :
///
/// - Timing (MinDelayMs/MaxDelayMs, Combo.DefaultToleranceMs) n'est jamais une
///   condition d'échec — le vrai jeu n'exige aucun rythme précis pour qu'une
///   combo touche. Ces champs ne servent qu'à la barre de tolérance visuelle.
/// - Les touches de direction (Group == "Movement") tenues en plus de ce qui
///   est demandé ne cassent une étape que si Combo.MatchMode == Strict — en
///   IgnoreExtraneous (et c'était, par bug, aussi vrai en Strict avant ce
///   correctif — voir docs/audit_features.md §1.1), un joueur réel garde
///   souvent une direction enfoncée en enchaînant (ex. tenir Droite en
///   sautant), ce n'est pas une faute. Les touches d'action (Group ==
///   "Action") sont, elles, toujours jugées à l'identique quel que soit le
///   MatchMode. ComboStep.FreeMovement tolère cet excédent de direction sur
///   une étape précise même en Strict (ex. un coup qui demande de se décaler
///   pour toucher la hitbox, sans que ce décalage fasse partie de la combo).
/// - Un mash/double-clic/chevauchement du bouton qui vient tout juste de
///   valider l'étape précédente (ex. cliquer Saut 3 fois pour caler son
///   timing, ou retaper la touche suivante avant d'avoir complètement
///   relâché la précédente — le move est déjà lancé en jeu, retaper dessus
///   ne fait rien) est ignoré au lieu de casser l'étape suivante : on ne
///   fail que si le résidu inattendu contient autre chose que ce dernier
///   bouton validé (IsSubsetOf, pas une égalité stricte).
/// - Une étape qui demande "Saut" tolère toujours une direction tenue en plus
///   (sauter en bougeant est normal en jeu), même en MatchMode.Strict et sans
///   que ComboStep.FreeMovement soit coché — pas besoin de le configurer à la
///   main pour chaque étape de saut, voir requiresJump ci-dessous.
/// - Gauche/Droite d'une combo sont symétriques : la 1ère fois qu'une étape
///   exige une direction gauche/droite alors que le joueur presse l'opposée
///   (et que le reste de l'étape correspond), toute la tentative en cours
///   bascule en "miroir" (_mirroredDirections) — attendre l'opposé de ce que
///   Combo.Steps décrit pour Gauche/Droite jusqu'à la fin de la tentative. Une
///   combo pensée "vers la droite" doit donc marcher identiquement jouée
///   "vers la gauche" sans avoir à la dupliquer. Haut/Bas ne sont jamais
///   inversés (l'orientation gauche/droite du joueur ne les affecte pas).
///   Verrouillé une seule fois par tentative (voir UpdateMirrorLock), remis à
///   zéro à chaque retour à l'étape 0 (échec, abandon, ou succès complet).
///
/// No UI, no keyboard hook — testable independently.
/// </summary>
public sealed class ComboRunner
{
    public Combo Combo { get; }
    public int CurrentStepIndex { get; private set; }
    public ComboRunState State { get; private set; } = ComboRunState.Waiting;

    /// <summary>Number of consecutive full-combo completions since the last failure.</summary>
    public int Streak { get; private set; }

    /// <summary>True si cette tentative a été détectée comme jouée en miroir
    /// (Gauche/Droite inversés par rapport à Combo.Steps) — voir docstring de classe.</summary>
    public bool IsMirrored => _mirroredDirections == true;

    public event Action<int>? StepSucceeded;
    public event Action<int, ComboFailReason>? StepFailed;
    public event Action? ComboCompleted;
    public event Action? ComboReset;
    public event Action? ComboAbandoned;

    /// <summary>Levé chaque fois que IsMirrored est (re)déterminé : passage à true dès
    /// qu'une direction opposée est détectée, et repassage à false à chaque retour à
    /// l'étape 0 (échec/abandon/succès) pour que l'UI désinverse ses flèches avant la
    /// tentative suivante. Sert à l'UI pour inverser l'affichage des flèches de direction.</summary>
    public event Action<bool>? MirrorChanged;

    /// <summary>Dernier instant où Feed a été appelé — sert uniquement à
    /// CheckAbandon (voir plus bas), pas à juger une étape.</summary>
    private DateTime _lastFeedUtc = DateTime.MinValue;

    /// <summary>Ensemble d'actions (hors mouvement) validé par la dernière étape réussie.
    /// Sert uniquement à tolérer un mash/répétition du même bouton juste après (voir
    /// docstring de classe) — pas une histoire de délai, juste un état mémorisé.</summary>
    private HashSet<string> _lastConsumedActionKeys = new();

    /// <summary>Null tant que l'orientation n'a pas encore été déterminée pour la
    /// tentative en cours ; true/false une fois verrouillée (voir docstring de classe).</summary>
    private bool? _mirroredDirections;

    /// <summary>If true, a failed combo keeps its Streak instead of resetting to 0.</summary>
    public bool KeepStreakOnFail { get; set; }

    public ComboRunner(Combo combo, bool keepStreakOnFail = false)
    {
        Combo = combo;
        KeepStreakOnFail = keepStreakOnFail;
    }

    public void Feed(List<KeyBind> pressedBindsThisTick, DateTime timestamp)
    {
        if (Combo.Steps.Count == 0) return;

        _lastFeedUtc = timestamp;

        var step = Combo.Steps[CurrentStepIndex];
        var required = new HashSet<string>(step.RequiredActions);

        // On ne juge une étape que sur ses boutons d'action (Group == "Action") :
        // les directions tenues en plus (Group == "Movement") ne comptent jamais
        // contre le joueur, tenir une direction en enchaînant est normal.
        var pressedMovement = new HashSet<string>(pressedBindsThisTick
            .Where(b => b.Group == "Movement").Select(b => b.Action));
        var pressedAction = new HashSet<string>(pressedBindsThisTick
            .Where(b => b.Group != "Movement").Select(b => b.Action));

        var (requiredMovementNames, requiredAction) = SplitByMovement(required, pressedBindsThisTick);

        // Verrouille (une seule fois par tentative) si cette étape se joue en miroir —
        // voir docstring de classe. Doit tourner avant toute comparaison de mouvement
        // ci-dessous pour que l'étape courante valide déjà avec l'orientation détectée.
        UpdateMirrorLock(requiredMovementNames, pressedMovement);
        var effectiveRequiredMovement = _mirroredDirections == true
            ? MirrorMovementNames(requiredMovementNames)
            : requiredMovementNames;

        bool movementOk = effectiveRequiredMovement.IsSubsetOf(pressedMovement);

        // En mode Strict, une direction tenue en plus de ce qui est demandé compte
        // aussi contre le joueur (pas seulement les boutons d'action) — c'est ce qui
        // distingue réellement Strict de IgnoreExtraneous, qui lui ignore toujours le
        // mouvement pur hors combo (comportement précédemment appliqué aux deux modes
        // sans distinction, un bug signalé dans docs/audit_features.md §1.1).
        // ComboStep.FreeMovement permet de tolérer ce même excédent sur une étape
        // précise même en Strict (ex. un coup qui demande de se décaler pour toucher
        // la hitbox, sans que ce décalage fasse partie de la combo elle-même).
        var extraMovement = new HashSet<string>(pressedMovement);
        extraMovement.ExceptWith(effectiveRequiredMovement);

        // Sauter tient presque toujours une direction en même temps en jeu (bouger en
        // l'air) — jamais une faute, quel que soit MatchMode, sans avoir à cocher
        // FreeMovement à la main sur chaque étape de saut.
        bool requiresJump = requiredAction.Contains("Saut");

        bool strictMovementViolation = Combo.MatchMode == MatchMode.Strict
            && !step.FreeMovement
            && !requiresJump
            && extraMovement.Count > 0;

        if (movementOk && !strictMovementViolation && pressedAction.SetEquals(requiredAction))
        {
            _lastConsumedActionKeys = requiredAction;
            State = ComboRunState.InProgress;

            // CurrentStepIndex doit avancer AVANT de lever StepSucceeded : les handlers UI
            // (UpdateComboStepVisuals, ApplyQuizMask) comparent l'index reçu à CurrentStepIndex
            // pour décider si une pastille est "déjà réussie" (vert) ou "courante" (noir/masquée).
            // Avec l'ancien ordre (event levé avant l'incrément), le handler voyait encore l'étape
            // qui vient de réussir comme "courante" et la repassait en noir — elle n'apparaissait
            // vraiment vert qu'au Feed suivant (décalage d'un input, signalé par l'utilisateur).
            var succeededIndex = CurrentStepIndex;
            CurrentStepIndex++;
            StepSucceeded?.Invoke(succeededIndex);

            if (CurrentStepIndex >= Combo.Steps.Count)
            {
                State = ComboRunState.Success;
                Streak++;
                ComboCompleted?.Invoke();
                CurrentStepIndex = 0;
                State = ComboRunState.Waiting;
                ResetMirrorState();
            }

            return;
        }

        // Boutons d'action pressés qui ne font pas partie de ce qui est attendu ici.
        var wrongActions = new HashSet<string>(pressedAction);
        wrongActions.ExceptWith(requiredAction);

        // Rien d'inattendu : soit on est encore en train de construire l'étape (une
        // partie seulement des boutons requis est enfoncée, ou la direction requise
        // manque encore), soit c'est du mouvement pur toléré (IgnoreExtraneous, ou
        // Strict sans extra de mouvement) — jamais un échec dans ce cas.
        if (wrongActions.Count == 0 && !strictMovementViolation)
        {
            return;
        }

        // Un extra de mouvement en Strict est une faute à part entière, jamais
        // toléré comme du mash (contrairement aux boutons d'action, tenir une
        // direction en trop n'est pas un "réflexe de martelage").
        if (strictMovementViolation)
        {
            if (CurrentStepIndex == 0) return;

            StepFailed?.Invoke(CurrentStepIndex, ComboFailReason.WrongInput);
            State = ComboRunState.Failed;
            if (!KeepStreakOnFail) Streak = 0;
            CurrentStepIndex = 0;
            State = ComboRunState.Waiting;
            _lastConsumedActionKeys = new HashSet<string>();
            ResetMirrorState();
            ComboReset?.Invoke();
            return;
        }

        // Le bouton de la toute première étape qui revient pendant une tentative en cours
        // doit TOUJOURS faire échouer la combo, même s'il correspond à la tolérance de
        // mash ci-dessous : sans ça, marteler l'ensemble de ses touches en boucle finit
        // par "valider" une combo par hasard (chaque bonne touche apparaît tôt ou tard
        // dans la boucle, et le retour périodique de la 1ère touche était toléré comme du
        // mash au lieu de reset). Ne s'applique qu'à partir de la 2ème étape : à l'étape 0,
        // c'est justement l'input attendu.
        var firstRequired = new HashSet<string>(Combo.Steps[0].RequiredActions);
        var (_, firstRequiredAction) = SplitByMovement(firstRequired, pressedBindsThisTick);
        bool firstStepInputRecurring = CurrentStepIndex != 0 && wrongActions.SetEquals(firstRequiredAction);

        // Mash/répétition/chevauchement du bouton qui vient de valider l'étape précédente
        // (ex. cliquer Saut 3 fois pour caler son timing, ou retaper la touche suivante
        // avant d'avoir complètement relâché la précédente — le move est déjà lancé en
        // jeu, donc retaper dessus ne fait rien) : on l'ignore au lieu de casser la suite.
        // IsSubsetOf (pas SetEquals) tolère aussi le cas où SEULE une partie de l'excédent
        // est ce résidu — le reste (une vraie touche inattendue) fait toujours échouer,
        // seul le résidu du bouton précédent est transparent.
        if (!firstStepInputRecurring && wrongActions.IsSubsetOf(_lastConsumedActionKeys))
        {
            return;
        }

        // Tant qu'aucune étape n'a encore été validée (CurrentStepIndex == 0), il
        // n'y a aucune progression à perdre : un input qui ne correspond pas au
        // premier pas (ex. se replacer avant de lancer le combo) ne doit pas être
        // traité comme un échec (pas de flash rouge, pas de reset de série).
        if (CurrentStepIndex == 0) return;

        StepFailed?.Invoke(CurrentStepIndex, ComboFailReason.WrongInput);
        State = ComboRunState.Failed;
        if (!KeepStreakOnFail) Streak = 0;
        CurrentStepIndex = 0;
        State = ComboRunState.Waiting;
        _lastConsumedActionKeys = new HashSet<string>();
        ResetMirrorState();
        ComboReset?.Invoke();
    }

    /// <summary>Actions de direction qui ont un opposé gauche/droite (Haut/Bas ne sont
    /// jamais inversés — l'orientation gauche/droite du joueur ne les affecte pas).</summary>
    private static readonly Dictionary<string, string> DirectionOpposite = new()
    {
        ["Gauche"] = "Droite",
        ["Droite"] = "Gauche",
    };

    private static HashSet<string> MirrorMovementNames(HashSet<string> names)
    {
        var result = new HashSet<string>();
        foreach (var n in names)
        {
            result.Add(DirectionOpposite.TryGetValue(n, out var opposite) ? opposite : n);
        }
        return result;
    }

    /// <summary>Verrouille _mirroredDirections dès que le joueur presse l'opposé exact
    /// d'une direction gauche/droite requise (et rien qu'elle) — une seule fois par
    /// tentative, voir docstring de classe. Ne fait rien tant que l'étape en cours ne
    /// requiert aucune direction gauche/droite (Haut/Bas seuls ne peuvent pas déterminer
    /// l'orientation) ou si l'orientation est déjà verrouillée.</summary>
    private void UpdateMirrorLock(HashSet<string> requiredMovementNames, HashSet<string> pressedMovement)
    {
        if (_mirroredDirections.HasValue) return;
        if (!requiredMovementNames.Any(n => DirectionOpposite.ContainsKey(n))) return;

        var mirrored = MirrorMovementNames(requiredMovementNames);
        bool matchesNormal = requiredMovementNames.IsSubsetOf(pressedMovement);
        bool matchesMirrored = mirrored.IsSubsetOf(pressedMovement);

        if (matchesMirrored && !matchesNormal)
        {
            _mirroredDirections = true;
            MirrorChanged?.Invoke(true);
        }
        else if (matchesNormal)
        {
            _mirroredDirections = false;
        }
        // Sinon : encore en train de construire l'étape (aucune des deux orientations
        // n'est encore confirmée), on retente au prochain Feed.
    }

    /// <summary>Remet l'orientation à "non déterminée" pour la prochaine tentative, et
    /// notifie l'UI si elle affichait des flèches inversées, pour qu'elle redevienne
    /// normale avant que la tentative suivante ne (re)détecte une éventuelle inversion.</summary>
    private void ResetMirrorState()
    {
        var wasMirrored = _mirroredDirections == true;
        _mirroredDirections = null;
        if (wasMirrored) MirrorChanged?.Invoke(false);
    }

    /// <summary>Sépare un ensemble d'actions requises en (directions, boutons d'action),
    /// en se basant sur le Group du bind pressé ce tick, ou sur IsKnownMovement en
    /// secours si la direction requise n'est plus tenue à cet instant.</summary>
    private static (HashSet<string> movement, HashSet<string> action) SplitByMovement(
        HashSet<string> required, List<KeyBind> pressedBindsThisTick)
    {
        var movement = new HashSet<string>(required.Where(a =>
            pressedBindsThisTick.Any(b => b.Action == a && b.Group == "Movement") || IsKnownMovement(a)));
        var action = new HashSet<string>(required.Except(movement));
        return (movement, action);
    }

    /// <summary>Combos enregistrées ou importées peuvent contenir un nom d'action de
    /// direction sans qu'un bind "Movement" ne soit dans le tick courant pour le confirmer
    /// (ex. étape suivante où la direction n'est déjà plus tenue) — on retombe sur les noms
    /// de direction standards de l'app pour ne pas mal classer ces actions comme "Action".</summary>
    private static bool IsKnownMovement(string action) =>
        action is "Gauche" or "Droite" or "Haut" or "Bas";

    /// <summary>Manual reset (e.g. switching the active combo), no events fired.</summary>
    public void Reset()
    {
        CurrentStepIndex = 0;
        State = ComboRunState.Waiting;
        _lastConsumedActionKeys = new HashSet<string>();
        ResetMirrorState();
    }

    /// <summary>Appelé périodiquement (polling, pas à chaque input) pour abandonner
    /// une tentative en cours si le joueur n'a rien pressé depuis <paramref name="timeout"/> :
    /// pas une faute de timing sur une étape (voir docstring de classe, le timing n'est
    /// jamais un échec), juste un "il a arrêté, on relâche l'attente" pour ne pas rester
    /// bloqué indéfiniment au milieu d'une combo. Sans effet tant qu'aucune étape n'a
    /// encore été validée (rien à abandonner).</summary>
    public void CheckAbandon(DateTime now, TimeSpan timeout)
    {
        if (CurrentStepIndex == 0) return;
        if (now - _lastFeedUtc < timeout) return;

        if (!KeepStreakOnFail) Streak = 0;
        CurrentStepIndex = 0;
        State = ComboRunState.Waiting;
        _lastConsumedActionKeys = new HashSet<string>();
        ResetMirrorState();
        ComboAbandoned?.Invoke();
    }
}
