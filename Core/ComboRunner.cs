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
///   pour toucher la hitbox, sans que ce décalage fasse partie du combo).
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
/// - Priorité verticale : en jeu, tenir Haut ou Bas en même temps qu'une
///   direction horizontale écrase cette dernière — tenir Gauche+Bas revient
///   exactement à tenir Bas. Les directions horizontales sont donc retirées
///   des deux côtés de la comparaison dès qu'une verticale est présente (voir
///   NormalizeMovement) : une horizontale tenue en plus d'un Bas/Haut requis
///   n'est plus un excédent fautif en Strict, puisqu'elle ne sort pas en jeu.
/// - Gauche/Droite d'un combo sont totalement interchangeables, à chaque
///   étape indépendamment (retour utilisateur explicite : "gauche et droite
///   ça revient au même", pas de notion de "sens du combo" à respecter). Une
///   étape qui exige Gauche accepte aussi bien Gauche que Droite, et
///   inversement — CanonicalizeHorizontal fusionne les deux en un seul jeton
///   avant toute comparaison, des deux côtés (requis et pressé). Pas de
///   mémoire d'une étape à l'autre ni sur la tentative (contrairement à
///   l'ancien mécanisme de verrouillage par tentative, abandonné : il
///   pouvait laisser l'affichage désynchronisé de ce que le joueur venait
///   de jouer). Haut/Bas ne sont jamais concernés par cette fusion.
/// - Hit confirmé (RequireHitConfirmation, voir docstring dédié plus bas) :
///   couche orthogonale à tout ce qui précède, greffée sur chaque étape
///   d'attaque — n'affecte jamais l'avancement étape par étape ci-dessus
///   (les pastilles restent instantanées), mais peut invalider toute la
///   tentative en cours dès qu'un hit attendu n'arrive pas à temps.
///
/// No UI, no keyboard hook — testable independently.
/// </summary>
public sealed class ComboRunner
{
    /// <summary>Actions qui infligent des dégâts à l'adversaire — mêmes noms déjà en dur
    /// dans MainWindow.UpdateHudSensitivityForCurrentStep. Détermine quelles étapes
    /// attendent un hit HUD confirmé, voir RequireHitConfirmation.</summary>
    private static readonly HashSet<string> DamagingActions = new() { "Att. légère", "Att. forte", "Lancer" };

    public Combo Combo { get; }
    public int CurrentStepIndex { get; private set; }
    public ComboRunState State { get; private set; } = ComboRunState.Waiting;

    /// <summary>Si vrai, chaque étape d'attaque réussie (bonnes touches) attend un hit HUD
    /// confirmé (voir ConfirmHit) dans une fenêtre courte (HitConfirmationWindow) — piloté
    /// par MainWindow depuis AppState.Settings.HudDetectionEnabled/HudRoiCalibrated. Par
    /// défaut faux : aucun changement de comportement tant que la détection de hit n'est
    /// pas calibrée. Vérifie dès la 1ère étape d'attaque, pas seulement à la fin du combo :
    /// un premier coup joué dans le vide invalide la tentative tout de suite, plutôt que de
    /// laisser le joueur continuer un combo déjà mort jusqu'à la dernière étape.</summary>
    public bool RequireHitConfirmation { get; set; }

    /// <summary>Hits HUD encore attendus pour la tentative en cours, un par étape d'attaque
    /// réussie (FIFO) — voir Feed (enqueue), ConfirmHit (dequeue sur confirmation),
    /// CheckHitConfirmationTimeout (échec si le plus ancien expire sans être confirmé).</summary>
    private readonly Queue<DateTime> _pendingHitDeadlines = new();

    /// <summary>Vrai si la dernière étape du combo a réussi mais attend encore que
    /// _pendingHitDeadlines se vide avant d'annoncer ComboCompleted/Streak++.</summary>
    private bool _awaitingFinalConfirmation;

    // Historique de ce nombre (même jour) : 900ms (trop lent, vérifié seulement en fin de
    // combo) → 400ms → 250ms (demandé explicitement par l'utilisateur, "encore trop lent") →
    // **remonté à 600ms** après test réel : à 250ms, de vrais coups qui touchaient bel et
    // bien étaient invalidés avant même que le HUD ait eu le temps de refléter le hit (temps
    // de trajet de l'attaque + latence de detection), cassant la tentative en cours en plein
    // milieu — et au passage, ce reset prématuré remettait aussi l'orientation miroir à zéro
    // (ResetMirrorState), ce qui donnait l'impression que "la flèche changeait de sens" toute
    // seule alors que le joueur continuait de jouer correctement. Ne pas redescendre sous ce
    // seuil sans un vrai test en jeu qui le justifie. Voir aussi HudDamageSource.DebounceMs :
    // avec la file FIFO ci-dessus, un HitDetected en trop (rien à confirmer) est
    // silencieusement ignoré, donc le debounce court n'a pas besoin de remonter avec ceci.
    private static readonly TimeSpan HitConfirmationWindow = TimeSpan.FromMilliseconds(600);

    /// <summary>Number of consecutive full-combo completions since the last failure.</summary>
    public int Streak { get; private set; }

    public event Action<int>? StepSucceeded;
    public event Action<int, ComboFailReason>? StepFailed;
    public event Action? ComboCompleted;
    public event Action? ComboReset;
    public event Action? ComboAbandoned;

    /// <summary>Levé quand les bonnes touches ont toutes été jouées mais qu'il manque encore
    /// des hits HUD confirmés pour finaliser (voir RequireHitConfirmation) — permet à l'UI de
    /// distinguer "en attente" d'un vrai ComboCompleted silencieux.</summary>
    public event Action? ComboAwaitingHitConfirmation;

    /// <summary>Levé quand la fenêtre d'attente (HitConfirmationWindow) expire sans assez de
    /// hits confirmés : la tentative, pourtant jouée avec les bonnes touches, est invalidée
    /// (Streak remis à zéro sauf KeepStreakOnFail).</summary>
    public event Action? HitNotConfirmed;

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

        _lastFeedUtc = timestamp;

        var step = Combo.Steps[CurrentStepIndex];
        var required = new HashSet<string>(step.RequiredActions);

        // On ne juge une étape que sur ses boutons d'action (Group == "Action") :
        // les directions tenues en plus (Group == "Movement") ne comptent jamais
        // contre le joueur, tenir une direction en enchaînant est normal.
        var pressedMovement = NormalizeMovement(new HashSet<string>(pressedBindsThisTick
            .Where(b => b.Group == "Movement").Select(b => b.Action)));
        var pressedAction = new HashSet<string>(pressedBindsThisTick
            .Where(b => b.Group != "Movement").Select(b => b.Action));

        var (requiredMovementNames, requiredAction) = SplitByMovement(required, pressedBindsThisTick);
        requiredMovementNames = CanonicalizeHorizontal(NormalizeMovement(requiredMovementNames));
        pressedMovement = CanonicalizeHorizontal(pressedMovement);

        var effectiveRequiredMovement = requiredMovementNames;

        bool movementOk = effectiveRequiredMovement.IsSubsetOf(pressedMovement);

        // En mode Strict, une direction tenue en plus de ce qui est demandé compte
        // aussi contre le joueur (pas seulement les boutons d'action) — c'est ce qui
        // distingue réellement Strict de IgnoreExtraneous, qui lui ignore toujours le
        // mouvement pur hors combo (comportement précédemment appliqué aux deux modes
        // sans distinction, un bug signalé dans docs/audit_features.md §1.1).
        // ComboStep.FreeMovement permet de tolérer ce même excédent sur une étape
        // précise même en Strict (ex. un coup qui demande de se décaler pour toucher
        // la hitbox, sans que ce décalage fasse partie du combo lui-même).
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
            var stepWasDamaging = RequireHitConfirmation && requiredAction.Any(DamagingActions.Contains);
            CurrentStepIndex++;
            StepSucceeded?.Invoke(succeededIndex);

            var comboFinished = CurrentStepIndex >= Combo.Steps.Count;
            if (comboFinished)
            {
                State = ComboRunState.Success;
                CurrentStepIndex = 0;
                State = ComboRunState.Waiting;
            }

            // Chaque étape d'attaque réussie attend son propre hit HUD (voir docstring de
            // RequireHitConfirmation) — vérifié dès la 1ère étape, pas seulement à la fin.
            if (stepWasDamaging) _pendingHitDeadlines.Enqueue(timestamp + HitConfirmationWindow);

            if (comboFinished)
            {
                if (_pendingHitDeadlines.Count == 0)
                {
                    Streak++;
                    ComboCompleted?.Invoke();
                }
                else
                {
                    // Le dernier coup du combo (ou un coup précédent) n'a pas encore été
                    // confirmé — on attend que la file se vide (ConfirmHit) ou expire
                    // (CheckHitConfirmationTimeout) avant d'annoncer une réussite qui n'a
                    // peut-être pas eu lieu.
                    _awaitingFinalConfirmation = true;
                    ComboAwaitingHitConfirmation?.Invoke();
                }
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
            ClearPendingHitConfirmation();
            ComboReset?.Invoke();
            return;
        }

        // Le bouton de la toute première étape qui revient pendant une tentative en cours
        // doit TOUJOURS faire échouer le combo, même s'il correspond à la tolérance de
        // mash ci-dessous : sans ça, marteler l'ensemble de ses touches en boucle finit
        // par "valider" un combo par hasard (chaque bonne touche apparaît tôt ou tard
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
        ClearPendingHitConfirmation();
        ComboReset?.Invoke();
    }

    /// <summary>
    /// Applique la priorité verticale du jeu à un ensemble de directions : tenir Haut ou
    /// Bas en même temps qu'une direction horizontale écrase cette dernière en jeu (tenir
    /// Gauche+Bas revient exactement à tenir Bas ; idem Gauche+Haut ≡ Haut). Les
    /// horizontales sont donc retirées dès qu'une verticale est présente — elles ne
    /// sortent pas en jeu, donc ni les exiger ni les compter comme un excédent fautif
    /// n'aurait de sens.
    ///
    /// Appliqué des DEUX côtés de la comparaison (ce qui est requis par l'étape ET ce qui
    /// est réellement tenu) : sans normaliser aussi le requis, une étape enregistrée avec
    /// Gauche+Bas tenus ensemble deviendrait impossible à satisfaire, puisque le côté
    /// pressé, lui, aurait perdu son "Gauche".
    ///
    /// Haut+Bas tenus ensemble : les deux sont conservés, aucune priorité entre verticales
    /// n'est supposée ici (comportement de jeu non vérifié — ne rien inventer).
    /// </summary>
    private static HashSet<string> NormalizeMovement(HashSet<string> movement)
    {
        if (!movement.Contains("Haut") && !movement.Contains("Bas")) return movement;

        var result = new HashSet<string>(movement);
        result.Remove("Gauche");
        result.Remove("Droite");
        return result;
    }

    /// <summary>Fusionne Gauche et Droite en un seul jeton ("Gauche", choisi arbitrairement
    /// comme canonique) avant toute comparaison de mouvement — Gauche/Droite sont
    /// interchangeables partout dans l'app (voir docstring de classe), donc plus besoin de
    /// savoir laquelle des deux est réellement requise/pressée, seulement qu'une direction
    /// horizontale l'est des deux côtés. Appliqué aux deux côtés de la comparaison (requis et
    /// pressé), après NormalizeMovement (la priorité verticale doit avoir déjà retiré les
    /// horizontales superflues avant cette fusion).</summary>
    private static HashSet<string> CanonicalizeHorizontal(HashSet<string> movement)
    {
        if (!movement.Contains("Droite")) return movement;

        var result = new HashSet<string>(movement);
        result.Remove("Droite");
        result.Add("Gauche");
        return result;
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
        ClearPendingHitConfirmation();
    }

    /// <summary>Appelé périodiquement (polling, pas à chaque input) pour abandonner
    /// une tentative en cours si le joueur n'a rien pressé depuis <paramref name="timeout"/> :
    /// pas une faute de timing sur une étape (voir docstring de classe, le timing n'est
    /// jamais un échec), juste un "il a arrêté, on relâche l'attente" pour ne pas rester
    /// bloqué indéfiniment au milieu d'un combo. Sans effet tant qu'aucune étape n'a
    /// encore été validée (rien à abandonner).</summary>
    public void CheckAbandon(DateTime now, TimeSpan timeout)
    {
        if (CurrentStepIndex == 0) return;
        if (now - _lastFeedUtc < timeout) return;

        if (!KeepStreakOnFail) Streak = 0;
        CurrentStepIndex = 0;
        State = ComboRunState.Waiting;
        _lastConsumedActionKeys = new HashSet<string>();
        ClearPendingHitConfirmation();
        ComboAbandoned?.Invoke();
    }

    /// <summary>Purge la file de hits attendus et l'attente de finalisation — appelé partout
    /// où la tentative en cours est abandonnée (échec, abandon, reset manuel) : les hits
    /// encore attendus pour cette tentative morte n'ont plus de sens à confirmer.</summary>
    private void ClearPendingHitConfirmation()
    {
        _pendingHitDeadlines.Clear();
        _awaitingFinalConfirmation = false;
    }

    /// <summary>Appelé quand HudDamageSource détecte un hit (voir MainWindow.OnHudHitDetected) :
    /// confirme le plus ancien hit encore attendu (FIFO), qu'il s'agisse d'une étape déjà
    /// dépassée mid-combo ou de la finalisation du combo. Un hit sans rien à confirmer (file
    /// vide) est silencieusement ignoré — le signal HUD ne sait pas distinguer la source des
    /// dégâts, un hit "en trop" (ex. debounce court, plusieurs frames d'une même animation)
    /// n'est pas une erreur.</summary>
    public void ConfirmHit(DateTime now)
    {
        if (_pendingHitDeadlines.Count == 0) return;

        _pendingHitDeadlines.Dequeue();

        if (_awaitingFinalConfirmation && _pendingHitDeadlines.Count == 0)
        {
            _awaitingFinalConfirmation = false;
            Streak++;
            ComboCompleted?.Invoke();
        }
    }

    /// <summary>Polling fréquent (voir MainWindow, timer dédié) : si le plus ancien hit encore
    /// attendu dépasse sa fenêtre (HitConfirmationWindow) sans être confirmé, toute la
    /// tentative en cours est invalidée tout de suite — y compris si le joueur est déjà allé
    /// plus loin dans le combo (un coup qui a raté au milieu casse la tentative, continuer à
    /// taper les étapes suivantes ne peut plus la sauver). Voir docstring de
    /// RequireHitConfirmation : vérifié dès la 1ère étape d'attaque, pas seulement en fin de
    /// combo.</summary>
    public void CheckHitConfirmationTimeout(DateTime now)
    {
        if (_pendingHitDeadlines.Count == 0) return;
        if (now <= _pendingHitDeadlines.Peek()) return;

        ClearPendingHitConfirmation();
        if (!KeepStreakOnFail) Streak = 0;

        if (CurrentStepIndex > 0)
        {
            CurrentStepIndex = 0;
            State = ComboRunState.Waiting;
            _lastConsumedActionKeys = new HashSet<string>();
        }

        HitNotConfirmed?.Invoke();
    }
}
