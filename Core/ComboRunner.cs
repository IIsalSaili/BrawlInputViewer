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

/// <summary>Why a step failed. WrongInput = mauvaise touche (Feed). Timeout = plus de
/// CheckMoveTimeout.MaxDelay écoulé depuis la dernière étape réussie (CheckMoveTimeout).
/// L'UI rend réellement les deux différemment depuis l'audit 2026-08-07 (§M10) : rouge pour
/// une mauvaise touche, bleu "trop lent" pour un timeout — voir MainWindow.OnComboStepFailed.</summary>
public enum ComboFailReason
{
    WrongInput,
    Timeout,
}

/// <summary>
/// Pure validation logic for a Combo: fed one "coup joué" (the set of KeyBind
/// actions *currently held*, recomputed by the caller on every key change) at
/// a time, tracks progress through the combo's steps, and resets on a wrong
/// input.
///
/// - Timing PAR ÉTAPE (MinDelayMs/MaxDelayMs, Combo.DefaultToleranceMs) n'est
///   jamais une condition d'échec — le vrai jeu n'exige aucun rythme précis
///   d'une étape à l'autre pour qu'un combo touche. Ces champs ne servent qu'à
///   la barre de tolérance visuelle.
/// - Délai maximum GLOBAL entre deux coups (CheckMoveTimeout, voir plus bas) :
///   contrairement au timing par étape ci-dessus, demande explicite de
///   l'utilisateur — au-delà de 2.5s sans qu'une étape n'avance, la tentative
///   en cours est invalidée (StepFailed avec ComboFailReason.Timeout), même si
///   le joueur arrive ensuite à reprendre le combo depuis le début. Ce n'est
///   pas une histoire de rythme précis entre étapes voisines (ça, ça reste
///   toléré), juste un plafond large pour couper une tentative clairement
///   morte plutôt que de la laisser traîner indéfiniment.
/// - "Les déplacements sont libres, une mauvaise attaque invalide" (règle
///   formulée explicitement par l'utilisateur, corrigée le 2026-08-08). Deux
///   moitiés à ne pas confondre :
///   * ENTRE les coups, tout est libre : se déplacer, sauter, esquiver/dasher,
///     même non demandé, ne casse jamais une tentative. Ça remplace l'ancienne
///     distinction Combo.MatchMode (Strict/IgnoreExtraneous) et
///     ComboStep.FreeMovement : les deux champs restent dans le modèle pour ne
///     pas casser le schéma de combos.json existant, mais ne sont plus lus.
///   * AU MOMENT de frapper, la direction fait partie du coup : nLight, sLight
///     et dLight sont le MÊME bouton, seule la direction tenue change le move
///     qui sort. Une étape qui frappe exige donc la direction EXACTE (SetEquals),
///     pas "au moins ça" (IsSubsetOf). Sans ça, une étape nLight était validée
///     par un dLight (le "Bas" en trop passant pour du mouvement libre), et
///     réciproquement faire nLight quand l'étape demande dLight ne cassait rien
///     — la différence ensembliste des boutons étant vide, l'entrée passait pour
///     "étape en cours de construction" et était ignorée.
///   Les étapes qui ne frappent pas (Saut, Esquive/Dash, direction seule) gardent
///   la tolérance : une direction en plus n'y change réellement rien (sauter en
///   bougeant est normal en jeu).
/// - Un tick contenant une attaque ne signifie pas qu'un coup vient de sortir :
///   il faut que ce soit l'attaque elle-même qui ait déclenché le tick (voir le
///   paramètre triggeredBy de Feed). Ajouter une direction en gardant le bouton
///   d'attaque enfoncé — typiquement la transition dLight → sLight, où on presse
///   Droite avant d'avoir relâché Light — ne fait sortir aucun nouveau coup et
///   reste donc transparent. C'est ce test qui a remplacé l'ancienne "tolérance
///   de mash" (ignorer tout re-appui du bouton précédent), laquelle avalait
///   justement le cas signalé ci-dessus.
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
    /// dans MainWindow.UpdateHudSensitivityForCurrentStep. Sert à deux choses : (1)
    /// déterminer quelles étapes attendent un hit HUD confirmé (RequireHitConfirmation),
    /// (2) déterminer, dans Feed, quels boutons pressés en trop peuvent casser une
    /// tentative en cours — tout le reste (mouvement, Saut, Esquive/Dash) est toujours
    /// toléré, voir le bullet dédié dans la docstring de classe.</summary>
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

    /// <summary>Hits HUD encore attendus pour la tentative EN COURS, un par étape d'attaque
    /// réussie (FIFO) — voir Feed (enqueue), ConfirmHit (dequeue sur confirmation),
    /// CheckHitConfirmationTimeout (échec si le plus ancien expire sans être confirmé).
    /// Vidée dès que la tentative en cours meurt (mauvaise touche, timeout, reset).</summary>
    private readonly Queue<DateTime> _pendingHitDeadlines = new();

    /// <summary>Hits encore attendus par une tentative DÉJÀ TERMINÉE (toutes les bonnes touches
    /// jouées) dont la finalisation est différée. Séparée de _pendingHitDeadlines depuis l'audit
    /// 2026-08-07 (§E2) : les deux files étaient confondues, donc démarrer la répétition suivante
    /// avant que le dernier hit soit confirmé faisait créditer la tentative n°1 par un hit de la
    /// n°2 — et surtout, un échec de la n°2 purgeait la file commune, ce qui faisait perdre
    /// définitivement le ComboCompleted (et le Streak++) d'une tentative pourtant réussie.
    /// Une tentative en cours ne peut plus toucher à cette file-ci.</summary>
    private readonly Queue<DateTime> _finalizingHitDeadlines = new();

    /// <summary>Vrai si la dernière étape du combo a réussi mais attend encore que
    /// _finalizingHitDeadlines se vide avant d'annoncer ComboCompleted/Streak++.</summary>
    private bool _awaitingFinalConfirmation;

    // Historique de ce nombre (même jour) : 900ms (trop lent, vérifié seulement en fin de
    // combo) → 400ms → 250ms (demandé explicitement par l'utilisateur, "encore trop lent") →
    // **remonté à 600ms** après test réel : à 250ms, de vrais coups qui touchaient bel et
    // bien étaient invalidés avant même que le HUD ait eu le temps de refléter le hit (temps
    // de trajet de l'attaque + latence de detection), cassant la tentative en cours en plein
    // milieu. Ne pas redescendre sous ce seuil sans un vrai test en jeu qui le justifie.
    // Voir aussi HudDamageSource.DebounceMs : avec les files FIFO ci-dessus, un HitDetected en
    // trop (rien à confirmer) est silencieusement ignoré, donc le debounce court n'a pas besoin
    // de remonter avec ceci.
    //
    // Réglable depuis settings.json (OverlaySettings.HitConfirmationWindowMs) depuis l'audit
    // 2026-08-07 (§F9) : c'est le paramètre du chemin critique qui a demandé le plus de
    // réajustements en test réel, et il dépend du setup (latence d'affichage, type de coup) —
    // le figer en constante privée obligeait à recompiler pour l'ajuster.
    public TimeSpan HitConfirmationWindow { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>Number of consecutive full-combo completions since the last failure.</summary>
    public int Streak { get; private set; }

    public event Action<int>? StepSucceeded;
    public event Action<int, ComboFailReason>? StepFailed;
    public event Action? ComboCompleted;
    public event Action? ComboReset;

    /// <summary>Levé quand les bonnes touches ont toutes été jouées mais qu'il manque encore
    /// des hits HUD confirmés pour finaliser (voir RequireHitConfirmation) — permet à l'UI de
    /// distinguer "en attente" d'un vrai ComboCompleted silencieux.</summary>
    public event Action? ComboAwaitingHitConfirmation;

    /// <summary>Levé quand la fenêtre d'attente (HitConfirmationWindow) expire sans assez de
    /// hits confirmés : la tentative, pourtant jouée avec les bonnes touches, est invalidée
    /// (Streak remis à zéro sauf KeepStreakOnFail).</summary>
    public event Action? HitNotConfirmed;

    /// <summary>Dernier instant où une étape a réussi (avancé CurrentStepIndex) — sert
    /// uniquement à CheckMoveTimeout (voir plus bas), volontairement pas "dernier input
    /// quel qu'il soit" : du bruit qui n'avance rien (mouvement, Saut...) ne doit pas
    /// repousser ce délai.</summary>
    private DateTime _lastStepSuccessUtc = DateTime.MinValue;

    // _lastConsumedActionKeys / _lastConsumedMovement (mémorisation du dernier coup validé, qui
    // servait à tolérer un re-appui du même bouton) ont été supprimés le 2026-08-08 : la
    // tolérance qu'ils alimentaient avalait le cas "nLight alors que l'étape demande dLight",
    // signalé par l'utilisateur. Elle est remplacée par le paramètre triggeredBy de Feed, bien
    // plus précis — voir la branche d'échec.

    /// <summary>If true, a failed combo keeps its Streak instead of resetting to 0.</summary>
    public bool KeepStreakOnFail { get; set; }

    public ComboRunner(Combo combo, bool keepStreakOnFail = false)
    {
        Combo = combo;
        KeepStreakOnFail = keepStreakOnFail;
    }

    /// <param name="triggeredBy">La touche/bouton NOUVELLEMENT pressé qui provoque ce tick
    /// (l'auto-répétition OS est déjà filtrée par l'appelant), ou null si l'appelant ne le sait
    /// pas. C'est le discriminant qui permet de distinguer "un nouveau coup vient de sortir en
    /// jeu" de "l'ensemble tenu a changé sans qu'aucune attaque ne parte" — voir la branche
    /// d'échec.</param>
    public void Feed(List<KeyBind> pressedBindsThisTick, DateTime timestamp, KeyBind? triggeredBy = null)
    {
        if (Combo.Steps.Count == 0) return;

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

        // Une étape qui FRAPPE est jugée sur la direction EXACTE, pas sur un simple "au moins
        // ça" — parce qu'en Brawlhalla la direction fait partie du coup : nLight, sLight et
        // dLight sont le MÊME bouton, seule la direction tenue change le move qui sort. Tolérer
        // une direction en trop revenait donc à confondre trois attaques différentes :
        //   - une étape nLight (aucune direction requise) était validée par un dLight, puisque
        //     le "Bas" en trop passait pour du mouvement libre ;
        //   - et réciproquement, faire nLight quand l'étape demande dLight ne cassait rien (voir
        //     la branche d'échec plus bas).
        // "Les déplacements sont libres" veut dire qu'on peut se déplacer ENTRE les coups, pas
        // qu'on peut tenir une direction au moment de frapper sans changer d'attaque. Les étapes
        // qui ne frappent pas (Saut, Esquive/Dash, direction seule) gardent la tolérance : là,
        // une direction en plus ne change réellement rien.
        // Signalé par l'utilisateur le 2026-08-08 après test en jeu.
        var stepHasAttack = requiredAction.Overlaps(DamagingActions);
        bool movementOk = stepHasAttack
            ? requiredMovementNames.SetEquals(pressedMovement)
            : requiredMovementNames.IsSubsetOf(pressedMovement);

        if (movementOk && pressedAction.SetEquals(requiredAction))
        {
            _lastStepSuccessUtc = timestamp;
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
                    // peut-être pas eu lieu. Les échéances sont TRANSFÉRÉES dans la file de
                    // finalisation (voir _finalizingHitDeadlines) : la tentative suivante,
                    // qui peut démarrer dès maintenant, ne doit plus pouvoir ni les consommer
                    // ni les purger.
                    while (_pendingHitDeadlines.Count > 0) _finalizingHitDeadlines.Enqueue(_pendingHitDeadlines.Dequeue());
                    _awaitingFinalConfirmation = true;
                    ComboAwaitingHitConfirmation?.Invoke();
                }
            }

            return;
        }

        // Attaques réellement pressées ce tick (Att. légère/Att. forte/Lancer).
        //
        // On regarde ce qui est PRESSÉ, plus la différence avec ce qui est requis : c'était le
        // second volet du même bug. Faire nLight quand l'étape demande dLight presse bien
        // "Att. légère", qui EST l'action requise — la différence ensemblistE était donc vide et
        // l'entrée passait pour "on est encore en train de construire l'étape", silencieusement
        // ignorée. Or en jeu un nLight est bel et bien sorti, et il n'a pas touché : le combo est
        // mort. Ce qui compte n'est pas "a-t-il appuyé sur un bouton interdit", c'est "le coup
        // qui vient de sortir est-il celui qu'on attendait" — et si l'étape n'a pas été validée
        // juste au-dessus, c'est non.
        var pressedAttacks = new HashSet<string>(pressedAction);
        pressedAttacks.IntersectWith(DamagingActions);

        // Aucune attaque ce tick : mouvement, Saut, Esquive/Dash en trop, ou étape en cours de
        // construction (la direction tenue avant d'appuyer sur l'attaque). Jamais fautif — c'est
        // la vraie "liberté de déplacement", et RequireHitConfirmation vérifie de toute façon si
        // le combo touche vraiment.
        if (pressedAttacks.Count == 0)
        {
            return;
        }

        // Un bouton d'attaque figure dans l'ensemble tenu, mais est-ce qu'un NOUVEAU coup vient
        // réellement de sortir en jeu ? Seulement si c'est bien lui qui a déclenché ce tick.
        //
        // Sans cette distinction, ajouter une direction en gardant le bouton d'attaque enfoncé
        // (typiquement en transition dLight → sLight : on presse Droite avant d'avoir relâché
        // Light) produisait un tick contenant une attaque, jugé comme un coup raté — alors
        // qu'aucun nouveau coup n'est parti, la touche était déjà tenue.
        //
        // C'est ce discriminant qui remplace l'ancienne "tolérance de mash" : celle-ci laissait
        // passer tout re-tap du bouton précédent, ce qui avalait justement le cas signalé par
        // l'utilisateur (refaire nLight quand l'étape demande dLight ne cassait rien). Un vrai
        // second appui sur l'attaque fait maintenant sortir un vrai second coup, donc casse la
        // tentative si ce n'est pas celui attendu — pendant qu'un simple changement de direction
        // touche tenue reste transparent.
        var newAttackPressed = triggeredBy is null
            ? true // appelant qui ne fournit pas l'info : on reste strict (comportement d'avant)
            : DamagingActions.Contains(triggeredBy.Action);

        if (!newAttackPressed)
        {
            return;
        }

        // L'ancienne "tolérance de mash" (ignorer tout re-appui du bouton qui venait de valider
        // l'étape précédente) et le garde-fou firstStepInputRecurring qui l'encadrait ont tous
        // deux été supprimés ici, remplacés par le test newAttackPressed ci-dessus :
        //   - la tolérance avalait exactement le cas signalé par l'utilisateur (refaire nLight
        //     quand l'étape demande dLight ne cassait rien), puisqu'elle ne regardait que le
        //     bouton et pas la direction ;
        //   - le garde-fou anti-mash devient inutile maintenant que la direction fait partie du
        //     coup : traverser un combo par hasard exigerait de reproduire la bonne direction ET
        //     le bon bouton à chaque étape, c'est-à-dire de jouer le combo.
        // Le seul cas légitime que la tolérance protégeait (chevauchement de touches, quand on
        // ajoute une direction sans avoir relâché l'attaque) est couvert plus précisément par
        // newAttackPressed, qui distingue un vrai second appui d'un simple changement de
        // l'ensemble tenu.

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
        ClearAllHitConfirmation();
    }

    /// <summary>Appelé périodiquement (polling, pas à chaque input) pour invalider une
    /// tentative en cours si plus de <paramref name="maxDelay"/> s'est écoulé depuis la
    /// dernière étape réussie : demande explicite de l'utilisateur — au-delà de ce délai
    /// entre deux coups, c'est forcément raté (l'ouverture réelle en jeu ne dure pas aussi
    /// longtemps), même si le joueur arrive ensuite à reprendre le combo depuis le début.
    /// Contrairement au reste du moteur (voir docstring de classe : ni MinDelayMs/MaxDelayMs
    /// par étape, ni le mouvement/Saut/Esquive en trop ne sont des échecs), c'est une vraie
    /// faute — mêmes conséquences qu'une mauvaise touche (StepFailed avec
    /// ComboFailReason.Timeout, flash rouge côté UI, Streak remis à zéro sauf
    /// KeepStreakOnFail) plutôt qu'un simple relâchement silencieux. Sans effet tant
    /// qu'aucune étape n'a encore été validée (rien à invalider).</summary>
    public void CheckMoveTimeout(DateTime now, TimeSpan maxDelay)
    {
        if (CurrentStepIndex == 0) return;
        if (now - _lastStepSuccessUtc < maxDelay) return;

        var failedIndex = CurrentStepIndex;
        StepFailed?.Invoke(failedIndex, ComboFailReason.Timeout);
        State = ComboRunState.Failed;
        if (!KeepStreakOnFail) Streak = 0;
        CurrentStepIndex = 0;
        State = ComboRunState.Waiting;
        ClearPendingHitConfirmation();
        ComboReset?.Invoke();
    }

    /// <summary>Purge les hits attendus par la tentative EN COURS — appelé partout où celle-ci
    /// est abandonnée (mauvaise touche, timeout de coup, reset manuel) : ces hits n'ont plus de
    /// sens à confirmer. Ne touche volontairement PAS à _finalizingHitDeadlines (§E2) : une
    /// tentative déjà entièrement jouée garde sa chance d'être confirmée même si la répétition
    /// suivante, démarrée entre-temps, échoue.</summary>
    private void ClearPendingHitConfirmation()
    {
        _pendingHitDeadlines.Clear();
    }

    /// <summary>Purge tout, tentative en cours ET finalisation différée — uniquement pour un
    /// vrai reset externe (changement de combo actif), où plus rien du passé n'a de sens.</summary>
    private void ClearAllHitConfirmation()
    {
        _pendingHitDeadlines.Clear();
        _finalizingHitDeadlines.Clear();
        _awaitingFinalConfirmation = false;
    }

    /// <summary>Appelé quand HudDamageSource détecte un hit (voir MainWindow.OnHudHitDetected) :
    /// confirme le plus ancien hit encore attendu. Une tentative déjà terminée en attente de
    /// finalisation (_finalizingHitDeadlines) est servie EN PREMIER — ses hits sont forcément
    /// plus anciens que ceux d'une tentative démarrée après elle, donc c'est bien l'ordre FIFO
    /// global. Un hit sans rien à confirmer (les deux files vides) est silencieusement ignoré :
    /// le signal HUD ne sait pas distinguer la source des dégâts, un hit "en trop" (debounce
    /// court, plusieurs frames d'une même animation) n'est pas une erreur.</summary>
    public void ConfirmHit(DateTime now)
    {
        if (_finalizingHitDeadlines.Count > 0)
        {
            _finalizingHitDeadlines.Dequeue();
            if (_awaitingFinalConfirmation && _finalizingHitDeadlines.Count == 0)
            {
                _awaitingFinalConfirmation = false;
                Streak++;
                ComboCompleted?.Invoke();
            }
            return;
        }

        if (_pendingHitDeadlines.Count == 0) return;
        _pendingHitDeadlines.Dequeue();
    }

    /// <summary>Polling fréquent (voir MainWindow, timer dédié) : si le plus ancien hit encore
    /// attendu dépasse sa fenêtre (HitConfirmationWindow) sans être confirmé, la tentative
    /// concernée est invalidée tout de suite — y compris si le joueur est déjà allé plus loin
    /// dans le combo (un coup qui a raté au milieu casse la tentative, continuer à taper les
    /// étapes suivantes ne peut plus la sauver). Traite les deux files séparément (§E2) : une
    /// finalisation qui expire invalide la tentative terminée, sans toucher à celle en cours,
    /// et réciproquement.</summary>
    public void CheckHitConfirmationTimeout(DateTime now)
    {
        var failed = false;

        if (_finalizingHitDeadlines.Count > 0 && now > _finalizingHitDeadlines.Peek())
        {
            _finalizingHitDeadlines.Clear();
            _awaitingFinalConfirmation = false;
            failed = true;
        }

        if (_pendingHitDeadlines.Count > 0 && now > _pendingHitDeadlines.Peek())
        {
            _pendingHitDeadlines.Clear();
            if (CurrentStepIndex > 0)
            {
                CurrentStepIndex = 0;
                State = ComboRunState.Waiting;
            }
            failed = true;
        }

        if (!failed) return;

        if (!KeepStreakOnFail) Streak = 0;
        HitNotConfirmed?.Invoke();
    }
}
