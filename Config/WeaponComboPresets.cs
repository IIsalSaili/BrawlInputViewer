using System.Collections.Generic;

namespace BrawlhallaOverlay;

/// <summary>
/// Bibliothèque de combos prêts à l'emploi, 5 par arme, pour les 15 armes
/// actuelles de Brawlhalla (Sword, Spear, Hammer, Blasters, Katars, Axe, Bow,
/// Scythe, Greatsword, Gauntlets, Cannon, Orb, Rocket Lance, Battle Boots,
/// Chakram — liste vérifiée sur liquipedia.net/brawlhalla/Weapons ; ni "Fists"
/// ni "Battle Sammich" n'existent plus dans le jeu actuel, contrairement à une
/// version précédente de ce fichier qui les incluait par erreur).
///
/// Chaque combo ci-dessous vient de séquences réellement documentées par des
/// guides communautaires (gamespecifications.com "187 Brawlhalla Combos",
/// bluestacks.com "Combos Guide for All Weapons", dashfight.com pour Battle
/// Boots, mygamingtutorials.com pour Chakram), pas inventées. Seules les
/// combos non liées à un légend précis sont reprises (les guides marquent les
/// combos exclusives à un légend, ex. "(Yumiko) dSig; Rec" — exclues ici
/// puisque l'app n'est pas liée à un légend). Les combos nécessitant une
/// attaque chargée ("empowered"/"e") sont aussi exclues : l'app ne peut pas
/// valider une durée d'appui.
///
/// Traduction vers le vocabulaire d'actions de l'app (l'app ne distingue pas
/// sol/air ni les modes spéciaux, donc ces conventions sont volontairement
/// approximatives mais reflètent l'ordre et les boutons réels) :
/// - nLight/sLight/dLight/nSig/sSig/dSig → direction (Gauche/Droite/Haut/Bas)
///   + Att. légère ou Att. forte, pressées ensemble (une variante latérale
///   marquée "Droite" fonctionne identiquement avec "Gauche").
/// - nAir/sAir/dAir (attaque en l'air) → un "Saut" est inséré juste avant,
///   même bouton d'attaque ensuite (l'app ne suit pas l'état sol/air).
/// - GC (Gravity Cancel) → un "Saut" (confirmé par gamespecifications.com qui
///   note littéralement "Jump" aux mêmes endroits où bluestacks.com note "GC"
///   pour les combos Canon, ex. "dLight; GC; dAir" = "DL > Jump + DAir").
/// - Rec (Recovery) → Haut + Att. forte (convention déjà utilisée par les
///   combos Gantelets créées à la main par l'utilisateur avant ce fichier).
/// - GP (Ground Pound) → un double appui de direction seule (Bas, Bas),
///   distinct de dSig : en jeu c'est un double-tap Bas en l'air, pas un
///   bouton d'attaque.
/// - Dash → un appui de direction seule, pour la même raison.
///
/// Chargées à la demande via AppState.ImportWeaponPresets(weapon) et
/// fusionnées dans combos.json (Id stable "preset-&lt;arme&gt;-&lt;n&gt;" :
/// une réimportation met à jour le contenu existant au lieu de le dupliquer
/// ou de le laisser figé si le contenu a changé depuis).
/// </summary>
public static class WeaponComboPresets
{
    public static readonly List<string> Weapons = new()
    {
        "Épée", "Lance", "Marteau", "Blasters", "Katars", "Hache", "Arc", "Faux",
        "Épée à deux mains", "Gantelets", "Canon", "Orbe", "Lance-fusée", "Bottes de combat", "Chakram",
    };

    private static ComboStep S(params string[] actions) => new() { RequiredActions = new List<string>(actions) };

    private sealed record ComboDef(string Name, string Description, ComboStep[] Steps);

    private static readonly Dictionary<string, ComboDef[]> Table = new()
    {
        ["Épée"] = new[]
        {
            new ComboDef("DLight vers SAir", "dLight > sAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers NAir", "dLight > nAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DLight vers DAir", "dLight > dAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers NLight", "sLight > nLight (source : bluestacks.com).",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("SAir vers NLight", "sAir > nLight (source : bluestacks.com).",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Att. légère") }),
        },
        ["Lance"] = new[]
        {
            new ComboDef("DLight vers SAir", "dLight > sAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers NAir", "dLight > nAir (source : bluestacks.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("SLight vers NLight", "sLight > nLight (source : bluestacks.com).",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("SLight vers DLight", "sLight > dLight (source : gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("DLight vers Récupération", "dLight > Rec (source : bluestacks.com).",
                new[] { S("Bas", "Att. légère"), S("Haut", "Att. forte") }),
        },
        ["Marteau"] = new[]
        {
            new ComboDef("DLight vers SLight", "dLight > sLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers SAir", "dLight > sAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers DAir", "dLight > dAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("DLight vers Gravity Cancel puis NLight", "dLight > GC > nLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DLight vers Récupération", "dLight > Rec (source : bluestacks.com).",
                new[] { S("Bas", "Att. légère"), S("Haut", "Att. forte") }),
        },
        ["Blasters"] = new[]
        {
            new ComboDef("DLight vers NLight", "dLight > nLight (source : bluestacks.com).",
                new[] { S("Bas", "Att. légère"), S("Att. légère") }),
            new ComboDef("DLight vers SLight", "dLight > sLight (source : bluestacks.com / gamespecifications.com \"Guns\").",
                new[] { S("Bas", "Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers SAir", "dLight > sAir (source : bluestacks.com / gamespecifications.com \"Guns\").",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("SAir vers NLight", "sAir > nLight (source : bluestacks.com / gamespecifications.com \"Guns\").",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("DLight vers DAir", "dLight > dAir (source : bluestacks.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
        },
        ["Katars"] = new[]
        {
            new ComboDef("NLight vers DLight", "nLight > dLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers DLight", "sLight > dLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers NLight", "sLight > nLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("DAir vers DLight", "dAir > dLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("Ground Pound vers Gravity Cancel puis DLight", "GP > GC > dLight (source : bluestacks.com / gamespecifications.com) — GP = double appui Bas en l'air.",
                new[] { S("Bas"), S("Bas"), S("Saut"), S("Bas", "Att. légère") }),
        },
        ["Hache"] = new[]
        {
            new ComboDef("SLight vers DLight", "sLight > dLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers NAir", "sLight > nAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("SLight vers DAir", "sLight > dAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("NAir vers DLight", "nAir > dLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Saut"), S("Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers NLight", "sLight > nLight (source : gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
        },
        ["Arc"] = new[]
        {
            new ComboDef("SLight vers SAir", "sLight > sAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("NLight vers NAir", "nLight > nAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("SLight vers DLight", "sLight > dLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("DLight vers NLight", "dLight > nLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Att. légère") }),
            new ComboDef("DAir vers NLight", "dAir > nLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Att. légère") }),
        },
        ["Faux"] = new[]
        {
            // La version précédente de ce bloc citait bluestacks.com/gamespecifications.com
            // pour des combos qui n'y figurent pas réellement (vérifié en récupérant la page :
            // le vrai contenu bluestacks pour la Faux est "nAir>sAir", "sAir>sLight",
            // "Rec>nAir", "Rec>sLight" — aucun rapport avec ce qui était codé). Refait à partir
            // d'une page effectivement récupérée (theglobalgaming.com) + de la correction de
            // l'utilisateur pour le combo de base, qui prime sur toute source secondaire.
            new ComboDef("NLight vers Saut vers SAir vers SSig (combo de base)", "nLight > Jump > sAir > sSig — combo de base signalé par l'utilisateur comme confirmé en jeu (prime sur les sources écrites ci-dessous).",
                new[] { S("Att. légère"), S("Saut"), S("Droite", "Att. légère"), S("Droite", "Att. forte") }),
            new ComboDef("NLight vers NAir", "nLight > nAir (source : theglobalgaming.com, \"Scythe guide: combo strings\").",
                new[] { S("Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("SLight vers DLight", "sLight > dLight (source : theglobalgaming.com, \"Scythe guide: combo strings\").",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("NLight, NAir, SAir, Gravity Cancel, DLight", "nLight > nAir > sAir > GC > dLight (source : theglobalgaming.com, \"Scythe guide: combo strings\") — GC = un appui Saut supplémentaire pour se re-stabiliser avant le DLight au sol.",
                new[] { S("Att. légère"), S("Saut"), S("Att. légère"), S("Droite", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("DLight, SLight, NLight, Saut, NAir, Récupération", "dLight > sLight > nLight > Jump > nAir > Rec (source : theglobalgaming.com, \"Scythe guide: combo strings\").",
                new[] { S("Bas", "Att. légère"), S("Droite", "Att. légère"), S("Att. légère"), S("Saut"), S("Att. légère"), S("Haut", "Att. forte") }),
        },
        ["Épée à deux mains"] = new[]
        {
            new ComboDef("NLight vers SLight", "nLight > sLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("SLight vers Gravity Cancel puis SLight", "sLight > GC > sLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("SLight vers DLight", "sLight > dLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers NLight", "sLight > nLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("SLight, DLight, Dash, NLight", "sLight > dLight > Dash > nLight (source : bluestacks.com / gamespecifications.com) — Dash = tape la direction seule pour relancer.",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère"), S("Droite"), S("Att. légère") }),
        },
        ["Gantelets"] = new[]
        {
            new ComboDef("DLight vers NLight", "dLight > nLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Att. légère") }),
            new ComboDef("DLight vers SLight", "dLight > sLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers DAir", "dLight > dAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("DLight vers NAir", "dLight > nAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DLight vers Récupération", "dLight > Rec (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Haut", "Att. forte") }),
        },
        ["Canon"] = new[]
        {
            new ComboDef("NLight vers SAir", "nLight > sAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("NLight vers DAir", "nLight > dAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("DLight vers Gravity Cancel puis DAir", "dLight > GC > dAir (source : bluestacks.com \"GC\" = gamespecifications.com \"Jump\").",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("DLight vers Gravity Cancel puis NAir", "dLight > GC > nAir (source : bluestacks.com \"GC\" = gamespecifications.com \"Jump\").",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("SLight vers Gravity Cancel puis SAir", "sLight > GC > sAir (source : bluestacks.com \"GC\" = gamespecifications.com \"Jump\").",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
        },
        ["Orbe"] = new[]
        {
            new ComboDef("DLight vers NAir", "dLight > nAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DLight vers SAir", "dLight > sAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("SLight vers DLight", "sLight > dLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers SAir", "sLight > sAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("DAir vers SLight", "dAir > sLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Droite", "Att. légère") }),
        },
        ["Lance-fusée"] = new[]
        {
            new ComboDef("SLight vers NLight", "sLight > nLight (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("SLight vers NAir", "sLight > nAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("SLight vers DAir", "sLight > dAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers SAir", "sLight > sAir (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("SLight vers Récupération", "sLight > Rec (source : bluestacks.com / gamespecifications.com).",
                new[] { S("Droite", "Att. légère"), S("Haut", "Att. forte") }),
        },
        ["Bottes de combat"] = new[]
        {
            new ComboDef("DLight vers NAir", "Down Light > Neutral Air (source : dashfight.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DLight vers SAir", "Down Light > Side Air (source : dashfight.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers Récupération", "Down Light > Recovery (source : dashfight.com).",
                new[] { S("Bas", "Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("SLight vers NLight", "Side Light > Neutral Light (source : dashfight.com).",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("SLight vers SLight", "Side Light > Side Light (source : dashfight.com).",
                new[] { S("Droite", "Att. légère"), S("Droite", "Att. légère") }),
        },
        ["Chakram"] = new[]
        {
            new ComboDef("SLight vers NLight", "Side Light → Nlight, mode léger (source : mygamingtutorials.com).",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("SLight vers DLight", "Side Light → Dlight, mode léger (source : mygamingtutorials.com).",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers NAir", "Side Light → Nair, mode léger (source : mygamingtutorials.com).",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DLight vers NAir", "Down Light → Nair — DLight change aussi de mode (léger/lourd) sur le Chakram (source : mygamingtutorials.com).",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("NLight vers SAir", "Nlight → Sair, mode léger (source : mygamingtutorials.com).",
                new[] { S("Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
        },
    };

    /// <summary>Construit la liste des 75 combos préréglées (5 par arme), avec un Id stable
    /// ("preset-&lt;arme&gt;-&lt;n&gt;") pour permettre une réimportation idempotente.</summary>
    public static List<Combo> BuildPresetCombos()
    {
        var result = new List<Combo>();
        foreach (var weapon in Weapons)
        {
            if (!Table.TryGetValue(weapon, out var defs)) continue;
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                result.Add(new Combo
                {
                    Id = $"preset-{Slug(weapon)}-{i + 1}",
                    Name = def.Name,
                    Description = def.Description,
                    Weapon = weapon,
                    DefaultToleranceMs = 450,
                    Steps = new List<ComboStep>(def.Steps),
                });
            }
        }
        return result;
    }

    private static string Slug(string weapon) => weapon
        .Replace("é", "e").Replace("è", "e").Replace("à", "a")
        .Replace(" ", "-").ToLowerInvariant();
}
