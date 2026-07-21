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

    /// <summary>Note optionnelle indiquant si cette combo est un "true combo" seulement
    /// à faible pourcentage de dégâts de l'adversaire (mécanique Brawlhalla : le
    /// knockback augmente avec les dégâts déjà subis, donc un enchaînement qui connecte
    /// à 0% peut laisser l'adversaire s'échapper/DI à haut %). Vide = non renseigné,
    /// ne pas déduire "fonctionne à tout %" de l'absence de note. Ne jamais remplir ce
    /// champ pour les combos de WeaponComboPresets sans avoir vérifié une source réelle
    /// qui le dit explicitement (voir historique des combos hallucinées dans CLAUDE.md) —
    /// à défaut, laisser vide plutôt qu'inventer une plage de %.</summary>
    public string DamageNote { get; set; } = "";

    // Performance cumulée, persistée dans combos.json (historique de session
    // guidée : combien de fois cette combo a été tentée/réussie, sa meilleure
    // série jamais atteinte, et si elle a déjà atteint le seuil de "maîtrise"
    // configuré dans les réglages au moins une fois).
    public int BestStreak { get; set; }
    public int TotalCompletions { get; set; }
    public int TotalAttempts { get; set; }
    public bool Mastered { get; set; }
}
