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
/// Pure validation logic for a Combo: fed one "coup joué" (a set of KeyBind
/// pressed together, already merged/deduplicated by the caller) at a time,
/// tracks progress through the combo's steps, and resets on a wrong input.
/// Timing (MinDelayMs/MaxDelayMs, Combo.DefaultToleranceMs) is intentionally
/// NOT enforced as a fail condition — the real game never demands frame-perfect
/// rhythm to land a combo, only the right buttons in the right order. Those
/// fields still drive the overlay's tolerance progress bar as a pacing guide,
/// they just never cause a reset on their own. No UI, no keyboard hook —
/// testable independently.
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

    private DateTime _lastAttackTime = DateTime.MinValue;

    /// <summary>Actions considérées comme des "attaques" pour la tolérance de récupération
    /// ci-dessous (pas Saut/Esquive/Lancer/Taunt : en jeu, seule une attaque bloque le
    /// personnage dans une animation qui empêche d'en relancer une autre au même moment).</summary>
    private static readonly HashSet<string> AttackActions = new() { "Att. légère", "Att. forte" };

    /// <summary>Fenêtre (ms) après une attaque pendant laquelle un bouton d'attaque différent de
    /// celui attendu est ignoré plutôt que de casser la combo : en jeu, le personnage est encore
    /// en animation de récupération et cet input est de toute façon avalé par le moteur, ce n'est
    /// donc pas une vraie faute de timing du joueur.</summary>
    private const int AttackRecoveryLockMs = 180;

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
        var pressedActions = new HashSet<string>(pressedBindsThisTick.Select(b => b.Action));
        var required = new HashSet<string>(step.RequiredActions);
        var pressedIsAttack = pressedActions.Overlaps(AttackActions);

        if (pressedActions.SetEquals(required))
        {
            if (pressedIsAttack) _lastAttackTime = timestamp;
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

        // Mode tolérant : un input hors combo composé uniquement de mouvement pur
        // (pas d'action) est ignoré plutôt que de casser la série en cours.
        if (Combo.MatchMode == MatchMode.IgnoreExtraneous &&
            pressedBindsThisTick.Count > 0 &&
            pressedBindsThisTick.All(b => b.Group == "Movement"))
        {
            return;
        }

        // Tolérance de récupération : un mauvais bouton d'attaque pressé juste après une
        // attaque (bonne ou mauvaise) est ignoré plutôt que de casser la combo — voir
        // AttackRecoveryLockMs ci-dessus. Ne s'applique pas au tout premier input d'une
        // tentative (rien à "récupérer" avant la toute première attaque).
        if (pressedIsAttack && _lastAttackTime != DateTime.MinValue &&
            (timestamp - _lastAttackTime).TotalMilliseconds < AttackRecoveryLockMs)
        {
            _lastAttackTime = timestamp;
            return;
        }

        if (pressedIsAttack) _lastAttackTime = timestamp;

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
        ComboReset?.Invoke();
    }

    /// <summary>Manual reset (e.g. switching the active combo), no events fired.</summary>
    public void Reset()
    {
        CurrentStepIndex = 0;
        State = ComboRunState.Waiting;
    }
}
