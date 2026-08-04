using System.Collections.Generic;
using System.Linq;

namespace BrawlhallaOverlay;

/// <summary>
/// Roster complet des 69 légendes + leurs 2 armes chacune, sourcé sur
/// https://www.brawlhalla.com/legends/ (liste) et les pages individuelles de chaque légende (armes),
/// recoupé avec https://brawlmance.com/legends, 2026-08-03. Remplace l'ancienne liste de 30 légendes
/// (couverture partielle, dérivée par accident des seuls combos disponibles à l'époque) — confirmé
/// par l'utilisateur qu'aucune des 39 légendes ajoutées n'est un skin/crossover, ce sont bien des
/// personnages de base.
///
/// <b>Les combos par légende (utilisant une Signature exclusive) ont été retirés</b> (Table vide
/// ci-dessous) : l'utilisateur ne les trouve pas fiables (source Reddit jamais re-vérifiée arme par
/// arme, et un cas prouvé impossible trouvé — Teros, Dex de base 3, avait des combos demandant
/// "7+"/"9" Dex alors que le max atteignable avec une stance est 4, voir LegendStats.cs). Faute
/// d'une source de combos de légende jugée fiable, les combos génériques d'arme
/// (WeaponComboPresets.cs) couvrent maintenant tout le monde via <see cref="WeaponsFor"/> : un
/// personnage sans combo dédié (donc tous, pour l'instant) affiche quand même les combos génériques
/// des 2 armes qu'il utilise réellement. La structure (Legends/WeaponsFor/BuildPresetCombos) reste
/// en place pour pouvoir réintroduire des combos de légende plus tard si une source fiable apparaît.
/// </summary>
public static class LegendComboPresets
{
    public static readonly List<string> Legends = new()
    {
        "Ada", "Arcadia", "Artemis", "Asuri", "Aurus", "Azoth", "Barraza", "Bodvar", "Brynn",
        "Caspian", "Cassidy", "Cross", "Diana", "Dusk", "Ember", "Ezio", "Fait", "Gnash", "Hattori",
        "Imugi", "Isaiah", "Jaeyun", "Jhala", "Jiro", "Kaya", "King Zuva", "Koji", "Kor", "Lady Vera",
        "Lin Fei", "Loki", "Lucien", "Magyar", "Mako", "Mirage", "Mordex", "Munin", "Nix", "Onyx",
        "Orion", "Petra", "Priya", "Queen Nai", "Ragnir", "Ransom", "Rayman", "Red Raptor", "Reno",
        "Rupture", "Scarlet", "Sentinel", "Seven", "Sidra", "Sir Roland", "Teros", "Tezca", "Thatch",
        "Thea", "Thor", "Ulgrim", "Val", "Vector", "Vivi", "Volkov", "Vraxx", "Wu Shang", "Xull",
        "Yumiko", "Zariel",
    };

    /// <summary>Les 2 armes de chaque légende, en français (voir WeaponComboPresets.Weapons pour les
    /// noms). Remplace l'ancienne dérivation via Table (qui ne couvrait que les armes citées dans les
    /// combos de légende, maintenant vides) — donnée indépendante, sourcée directement.</summary>
    private static readonly Dictionary<string, string[]> LegendWeapons = new()
    {
        ["Ada"] = new[] { "Blasters", "Lance" },
        ["Arcadia"] = new[] { "Lance", "Épée à deux mains" },
        ["Artemis"] = new[] { "Lance-fusée", "Faux" },
        ["Asuri"] = new[] { "Katars", "Épée" },
        ["Aurus"] = new[] { "Chakram", "Lance" },
        ["Azoth"] = new[] { "Arc", "Hache" },
        ["Barraza"] = new[] { "Hache", "Blasters" },
        ["Bodvar"] = new[] { "Marteau", "Épée" },
        ["Brynn"] = new[] { "Hache", "Lance" },
        ["Caspian"] = new[] { "Gantelets", "Katars" },
        ["Cassidy"] = new[] { "Blasters", "Marteau" },
        ["Cross"] = new[] { "Blasters", "Gantelets" },
        ["Diana"] = new[] { "Arc", "Blasters" },
        ["Dusk"] = new[] { "Lance", "Orbe" },
        ["Ember"] = new[] { "Arc", "Katars" },
        ["Ezio"] = new[] { "Épée", "Orbe" },
        ["Fait"] = new[] { "Faux", "Orbe" },
        ["Gnash"] = new[] { "Marteau", "Lance" },
        ["Hattori"] = new[] { "Épée", "Lance" },
        ["Imugi"] = new[] { "Hache", "Épée à deux mains" },
        ["Isaiah"] = new[] { "Canon", "Blasters" },
        ["Jaeyun"] = new[] { "Épée", "Épée à deux mains" },
        ["Jhala"] = new[] { "Hache", "Épée" },
        ["Jiro"] = new[] { "Épée", "Faux" },
        ["Kaya"] = new[] { "Lance", "Arc" },
        ["King Zuva"] = new[] { "Marteau", "Bottes de combat" },
        ["Koji"] = new[] { "Arc", "Épée" },
        ["Kor"] = new[] { "Gantelets", "Marteau" },
        ["Lady Vera"] = new[] { "Chakram", "Faux" },
        ["Lin Fei"] = new[] { "Katars", "Canon" },
        ["Loki"] = new[] { "Katars", "Faux" },
        ["Lucien"] = new[] { "Katars", "Blasters" },
        ["Magyar"] = new[] { "Marteau", "Épée à deux mains" },
        ["Mako"] = new[] { "Katars", "Épée à deux mains" },
        ["Mirage"] = new[] { "Faux", "Lance" },
        ["Mordex"] = new[] { "Faux", "Gantelets" },
        ["Munin"] = new[] { "Arc", "Faux" },
        ["Nix"] = new[] { "Faux", "Blasters" },
        ["Onyx"] = new[] { "Gantelets", "Canon" },
        ["Orion"] = new[] { "Lance-fusée", "Lance" },
        ["Petra"] = new[] { "Gantelets", "Orbe" },
        ["Priya"] = new[] { "Chakram", "Épée" },
        ["Queen Nai"] = new[] { "Lance", "Katars" },
        ["Ragnir"] = new[] { "Katars", "Hache" },
        ["Ransom"] = new[] { "Chakram", "Arc" },
        ["Rayman"] = new[] { "Gantelets", "Hache" },
        ["Red Raptor"] = new[] { "Bottes de combat", "Orbe" },
        ["Reno"] = new[] { "Blasters", "Orbe" },
        ["Rupture"] = new[] { "Katars", "Lance-fusée" },
        ["Scarlet"] = new[] { "Marteau", "Lance-fusée" },
        ["Sentinel"] = new[] { "Marteau", "Katars" },
        ["Seven"] = new[] { "Lance", "Canon" },
        ["Sidra"] = new[] { "Canon", "Épée" },
        ["Sir Roland"] = new[] { "Lance-fusée", "Épée" },
        ["Teros"] = new[] { "Hache", "Marteau" },
        ["Tezca"] = new[] { "Bottes de combat", "Gantelets" },
        ["Thatch"] = new[] { "Épée", "Blasters" },
        ["Thea"] = new[] { "Bottes de combat", "Lance-fusée" },
        ["Thor"] = new[] { "Marteau", "Orbe" },
        ["Ulgrim"] = new[] { "Hache", "Lance-fusée" },
        ["Val"] = new[] { "Gantelets", "Épée" },
        ["Vector"] = new[] { "Lance-fusée", "Arc" },
        ["Vivi"] = new[] { "Bottes de combat", "Blasters" },
        ["Volkov"] = new[] { "Hache", "Faux" },
        ["Vraxx"] = new[] { "Lance-fusée", "Blasters" },
        ["Wu Shang"] = new[] { "Gantelets", "Lance" },
        ["Xull"] = new[] { "Canon", "Hache" },
        ["Yumiko"] = new[] { "Arc", "Marteau" },
        ["Zariel"] = new[] { "Gantelets", "Arc" },
    };

    private static ComboStep S(params string[] actions) => new() { RequiredActions = new List<string>(actions) };

    private sealed record LegendComboDef(string Name, string Description, string Weapon, ComboStep[] Steps);

    /// <summary>Vide intentionnellement — voir docstring de classe. Gardée typée (au lieu de
    /// supprimer la déclaration) pour que BuildPresetCombos/ImportLegendPresets restent valides sans
    /// changement si des combos de légende reviennent plus tard avec une source fiable.</summary>
    private static readonly Dictionary<string, LegendComboDef[]> Table = new();

    /// <summary>Construit la liste des combos préréglées d'un légend (voir Table), avec un Id
    /// stable ("preset-legend-&lt;légend&gt;-&lt;n&gt;") pour permettre une réimportation idempotente.</summary>
    public static List<Combo> BuildPresetCombos()
    {
        var result = new List<Combo>();
        foreach (var legend in Legends)
        {
            if (!Table.TryGetValue(legend, out var defs)) continue;
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                result.Add(new Combo
                {
                    Id = $"preset-legend-{Slug(legend)}-{i + 1}",
                    Name = def.Name,
                    Description = def.Description,
                    Weapon = def.Weapon,
                    Legend = legend,
                    DefaultToleranceMs = 450,
                    Steps = new List<ComboStep>(def.Steps),
                });
            }
        }
        return result;
    }

    private static string Slug(string legend) => legend
        .Replace(" ", "").ToLowerInvariant();

    /// <summary>Les 2 armes réelles de cette légende (LegendWeapons), pas dérivées des combos —
    /// indépendant du contenu de Table, donc valable même vide. Sert à restreindre le sous-filtre
    /// d'arme de ControlPanelWindow, et surtout à choisir quels combos génériques d'arme afficher
    /// pour ce personnage (AppState.FilteredComboIndices).</summary>
    public static List<string> WeaponsFor(string legend) =>
        LegendWeapons.TryGetValue(legend, out var weapons) ? weapons.ToList() : new List<string>();
}
