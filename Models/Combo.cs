using System;
using System.Collections.Generic;

namespace BrawlhallaOverlay;

/// <summary>
/// How ComboRunner treats an input that doesn't match the current step.
/// Only Strict is implemented for the MVP; IgnoreExtraneous is reserved for
/// a later "tolerant" mode that would ignore pure-movement noise while
/// waiting for an attack step.
/// </summary>
public enum MatchMode
{
    Strict,
    IgnoreExtraneous,
}

public sealed class Combo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Nom de l'arme ciblée par cette combo (voir WeaponComboPresets.Weapons),
    /// vide pour une combo perso non liée à une arme précise.</summary>
    public string Weapon { get; set; } = "";

    public List<ComboStep> Steps { get; set; } = new();
    public int DefaultToleranceMs { get; set; } = 400;
    public MatchMode MatchMode { get; set; } = MatchMode.Strict;

    // Performance cumulée, persistée dans combos.json (historique de session
    // guidée : combien de fois cette combo a été tentée/réussie, sa meilleure
    // série jamais atteinte, et si elle a déjà atteint le seuil de "maîtrise"
    // configuré dans les réglages au moins une fois).
    public int BestStreak { get; set; }
    public int TotalCompletions { get; set; }
    public int TotalAttempts { get; set; }
    public bool Mastered { get; set; }
}
