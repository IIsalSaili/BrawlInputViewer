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
///   est demandé ne cassent jamais une étape : un joueur réel garde souvent
///   une direction enfoncée en enchaînant (ex. tenir Droite en sautant), ce
///   n'est pas une faute. Seules les touches d'action (Group == "Action")
///   comptent pour juger si une étape est correcte.
/// - Un mash/double-clic du bouton qui vient tout juste de valider l'étape
///   précédente (ex. cliquer Saut 3 fois pour caler son timing) est ignoré au
///   lieu de casser l'étape suivante — on ne compare plus "exactement", on ne
///   fail que sur un bouton d'action réellement différent de ce qui est
///   attendu et de ce qui vient d'être validé.
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

    public event Action<int>? StepSucceeded;
    public event Action<int, ComboFailReason>? StepFailed;
    public event Action? ComboCompleted;
    public event Action? ComboReset;
    public event Action? ComboAbandoned;

    /// <summary>Dernier instant où Feed a été appelé — sert uniquement à
    /// CheckAbandon (voir plus bas), pas à juger une étape.</summary>
    private DateTime _lastFeedUtc = DateTime.MinValue;

    /// <summary>Ensemble d'actions (hors mouvement) validé par la dernière étape réussie.
    /// Sert uniquement à tolérer un mash/répétition du même bouton juste après (voir
    /// docstring de classe) — pas une histoire de délai, juste un état mémorisé.</summary>
    private HashSet<string> _lastConsumedActionKeys = new();

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

        var step = Combo.Steps[CurrentStepIndex];
        var required = new HashSet<string>(step.RequiredActions);

        // On ne juge une étape que sur ses boutons d'action (Group == "Action") :
        // les directions tenues en plus (Group == "Movement") ne comptent jamais
        // contre le joueur, tenir une direction en enchaînant est normal.
        var pressedMovement = new HashSet<string>(pressedBindsThisTick
            .Where(b => b.Group == "Movement").Select(b => b.Action));
        var pressedAction = new HashSet<string>(pressedBindsThisTick
            .Where(b => b.Group != "Movement").Select(b => b.Action));
        var requiredMovementNames = new HashSet<string>(required.Where(a =>
            pressedBindsThisTick.Any(b => b.Action == a && b.Group == "Movement") || IsKnownMovement(a)));
        var requiredAction = new HashSet<string>(required.Except(requiredMovementNames));

        bool movementOk = requiredMovementNames.IsSubsetOf(pressedMovement);

        if (movementOk && pressedAction.SetEquals(requiredAction))
        {
            _lastConsumedActionKeys = requiredAction;
            State = ComboRunState.InProgress;
            StepSucceeded?.Invoke(CurrentStepIndex);
            CurrentStepIndex++;

            if (CurrentStepIndex >= Combo.Steps.Count)
            {
                State = ComboRunState.Success;
                Streak++;
                ComboCompleted?.Invoke();
                CurrentStepIndex = 0;
                State = ComboRunState.Waiting;
            }

            return;
        }

        // Boutons d'action pressés qui ne font pas partie de ce qui est attendu ici.
        var wrongActions = new HashSet<string>(pressedAction);
        wrongActions.ExceptWith(requiredAction);

        // Rien d'inattendu : soit on est encore en train de construire l'étape (une
        // partie seulement des boutons requis est enfoncée, ou la direction requise
        // manque encore), soit c'est du mouvement pur — jamais un échec.
        if (wrongActions.Count == 0)
        {
            return;
        }

        // Le bouton de la toute première étape qui revient pendant une tentative en cours
        // doit TOUJOURS faire échouer la combo, même s'il correspond à la tolérance de
        // mash ci-dessous : sans ça, marteler l'ensemble de ses touches en boucle finit
        // par "valider" une combo par hasard (chaque bonne touche apparaît tôt ou tard
        // dans la boucle, et le retour périodique de la 1ère touche était toléré comme du
        // mash au lieu de reset). Ne s'applique qu'à partir de la 2ème étape : à l'étape 0,
        // c'est justement l'input attendu.
        var firstStep = Combo.Steps[0];
        var firstRequired = new HashSet<string>(firstStep.RequiredActions);
        var firstRequiredMovementNames = new HashSet<string>(firstRequired.Where(a =>
            pressedBindsThisTick.Any(b => b.Action == a && b.Group == "Movement") || IsKnownMovement(a)));
        var firstRequiredAction = new HashSet<string>(firstRequired.Except(firstRequiredMovementNames));
        bool firstStepInputRecurring = CurrentStepIndex != 0 && wrongActions.SetEquals(firstRequiredAction);

        // Mash/répétition du bouton qui vient de valider l'étape précédente (ex. cliquer
        // Saut 3 fois pour caler son timing) : on l'ignore au lieu de casser la suite,
        // ce n'est pas un vrai mauvais input, juste un joueur qui n'est pas une machine.
        if (!firstStepInputRecurring && wrongActions.SetEquals(_lastConsumedActionKeys))
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
        ComboReset?.Invoke();
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
    }
}
