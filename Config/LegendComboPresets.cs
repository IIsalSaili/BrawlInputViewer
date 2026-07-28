using System.Collections.Generic;
using System.Linq;

namespace BrawlhallaOverlay;

/// <summary>
/// Bibliothèque de true combos propres à un légend précis (utilisant une
/// attaque Signature exclusive à ce légend sur une arme donnée), fournie par
/// l'utilisateur via un post Reddit collé directement dans la conversation —
/// pas de web fetch à faire/refaire, contrairement à WeaponComboPresets.cs.
///
/// Une combo de légende dépend à la fois d'un légend ET d'une arme (ex. "Ada
/// Blasters" ≠ "Ada" tout court) : chaque <see cref="Combo"/> généré porte
/// donc à la fois <see cref="Combo.Legend"/> et <see cref="Combo.Weapon"/>.
///
/// Traduction : une attaque Signature (nSig/sSig/dSig) est le même bouton que
/// l'attaque forte générique (Att. forte) — voir WeaponComboPresets.cs pour
/// le détail des conventions de traduction (nAir/sAir/dAir, GC, Recovery,
/// XPivot, etc.), réutilisées ici à l'identique.
///
/// Chargées à la demande via AppState.ImportLegendPresets(legend) et
/// fusionnées dans combos.json (Id stable "preset-legend-&lt;légend&gt;-&lt;n&gt;").
/// </summary>
public static class LegendComboPresets
{
    public static readonly List<string> Legends = new()
    {
        "Ada", "Asuri", "Azoth", "Barraza", "Bodvar", "Cassidy", "Cross", "Diana", "Ember",
        "Isaiah", "Jhala", "Jiro", "Koji", "Kor", "Lin Fei", "Mirage", "Mordex", "Queen Nai",
        "Nix", "Ragnir", "Scarlet", "Sentinel", "Sidra", "Teros", "Thatch", "Val", "Vraxx",
        "Xull", "Yumiko", "Zariel",
    };

    private static ComboStep S(params string[] actions) => new() { RequiredActions = new List<string>(actions) };

    private sealed record LegendComboDef(string Name, string Description, string Weapon, ComboStep[] Steps);

    private const string Src = "source : true combos Reddit (testés à 0% de dégâts), fournis par l'utilisateur.";

    private static readonly Dictionary<string, LegendComboDef[]> Table = new()
    {
        ["Ada"] = new[]
        {
            new LegendComboDef("DLight vers SSig", $"dLight > sSig — doit toucher les frames actives tardives du dernier tir de dLight ({Src})", "Blasters",
                new[] { S("Bas", "Att. légère"), S("Droite", "Att. forte") }),
            new LegendComboDef("DLight vers DSig", $"dLight > dSig ({Src})", "Blasters",
                new[] { S("Bas", "Att. légère"), S("Bas", "Att. forte") }),
        },
        ["Asuri"] = new[]
        {
            new LegendComboDef("NSig vers DLight", $"nSig > dLight ({Src})", "Katars",
                new[] { S("Att. forte"), S("Bas", "Att. légère") }),
        },
        ["Azoth"] = new[]
        {
            new LegendComboDef("SLight vers NSig", $"sLight > nSig — doit toucher les frames actives tardives de sLight ({Src})", "Hache",
                new[] { S("Droite", "Att. légère"), S("Att. forte") }),
        },
        ["Barraza"] = new[]
        {
            new LegendComboDef("SLight vers NSig", $"sLight > nSig — doit toucher les frames actives tardives de sLight ({Src})", "Hache",
                new[] { S("Droite", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("DLight vers NSig", $"dLight > nSig ({Src})", "Blasters",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
        },
        ["Bodvar"] = new[]
        {
            new LegendComboDef("DLight vers SSig", $"dLight > sSig — doit toucher les frames actives tardives de dLight ({Src})", "Marteau",
                new[] { S("Bas", "Att. légère"), S("Droite", "Att. forte") }),
        },
        ["Cassidy"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig ({Src})", "Blasters",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("DLight vers SSig", $"dLight > sSig ({Src})", "Blasters",
                new[] { S("Bas", "Att. légère"), S("Droite", "Att. forte") }),
            new LegendComboDef("NAir vers NSig", $"nAir > nSig — doit toucher le 2e tir de nAir ({Src})", "Blasters",
                new[] { S("Saut"), S("Att. légère"), S("Att. forte") }),
            new LegendComboDef("DLight vers NSig (Marteau)", $"dLight > nSig ({Src})", "Marteau",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("DLight, GC, SSig", $"dLight > GC > sSig — doit toucher les frames actives tardives de dLight ({Src}) GC = Esquive.", "Marteau",
                new[] { S("Bas", "Att. légère"), S("Esquive"), S("Droite", "Att. forte") }),
            new LegendComboDef("NLight vers NSig", $"nLight > nSig ({Src})", "Marteau",
                new[] { S("Att. légère"), S("Att. forte") }),
        },
        ["Cross"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig — doit toucher les frames actives tardives du dernier tir de dLight ({Src})", "Blasters",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("NAir vers NSig", $"nAir > nSig — doit toucher le 2e tir de nAir ({Src})", "Blasters",
                new[] { S("Saut"), S("Att. légère"), S("Att. forte") }),
        },
        ["Diana"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig — doit toucher les frames actives tardives de dLight ({Src})", "Arc",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("DAir vers DSig", $"dAir > dSig — doit toucher la dernière frame active du dAir au sol ({Src})", "Arc",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Bas", "Att. forte") }),
        },
        ["Ember"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig — doit toucher les frames actives tardives de dLight ({Src})", "Arc",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("DAir vers NSig", $"dAir > nSig — doit toucher les frames actives tardives de dAir ({Src})", "Arc",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Att. forte") }),
        },
        ["Isaiah"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig ({Src})", "Blasters",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("DSig vers XPivot NAir", $"dSig > XPivot nAir ({Src}) XPivot = pivot par élan horizontal, à exécuter au bon timing en jeu.", "Blasters",
                new[] { S("Bas", "Att. forte"), S("Droite"), S("Saut"), S("Att. légère") }),
            new LegendComboDef("DLight vers NSig (Canon)", $"dLight > nSig — doit toucher les frames actives tardives de dLight ({Src})", "Canon",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
        },
        ["Jhala"] = new[]
        {
            new LegendComboDef("SLight vers NSig", $"sLight > nSig — doit toucher les frames actives tardives de sLight ({Src})", "Hache",
                new[] { S("Droite", "Att. légère"), S("Att. forte") }),
        },
        ["Jiro"] = new[]
        {
            new LegendComboDef("DSig vers NLight", $"dSig > nLight — anecdotique (en réalité 1 frame de dodge, mentionné à titre indicatif) ({Src})", "Épée",
                new[] { S("Bas", "Att. forte"), S("Att. légère") }),
        },
        ["Koji"] = new[]
        {
            new LegendComboDef("DLight, GC, NSig", $"dLight > GC > nSig ({Src}) GC = Esquive.", "Épée",
                new[] { S("Bas", "Att. légère"), S("Esquive"), S("Att. forte") }),
            new LegendComboDef("DLight vers NSig", $"dLight > nSig ({Src})", "Arc",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("DAir vers DSig", $"dAir > dSig — doit toucher les frames actives tardives de dAir ({Src})", "Arc",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Bas", "Att. forte") }),
        },
        ["Kor"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig ({Src})", "Marteau",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
        },
        ["Lin Fei"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig ({Src})", "Canon",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
        },
        ["Mirage"] = new[]
        {
            new LegendComboDef("SSig vers NLight", $"sSig > nLight ({Src})", "Lance",
                new[] { S("Droite", "Att. forte"), S("Att. légère") }),
        },
        ["Mordex"] = new[]
        {
            new LegendComboDef("DSig vers NLight", $"dSig > nLight ({Src})", "Gantelets",
                new[] { S("Bas", "Att. forte"), S("Att. légère") }),
        },
        ["Queen Nai"] = new[]
        {
            new LegendComboDef("DLight, GC, NSig", $"dLight > GC > nSig ({Src}) GC = Esquive.", "Lance",
                new[] { S("Bas", "Att. légère"), S("Esquive"), S("Att. forte") }),
        },
        ["Nix"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig — doit toucher les dernières frames actives de dLight ({Src})", "Blasters",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("NAir vers NSig", $"nAir > nSig — doit toucher les frames actives tardives de nAir ({Src})", "Blasters",
                new[] { S("Saut"), S("Att. légère"), S("Att. forte") }),
        },
        ["Ragnir"] = new[]
        {
            new LegendComboDef("SLight vers NSig", $"sLight > nSig ({Src})", "Hache",
                new[] { S("Droite", "Att. légère"), S("Att. forte") }),
        },
        ["Scarlet"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig — doit toucher les frames actives tardives de dLight ({Src})", "Marteau",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
        },
        ["Sentinel"] = new[]
        {
            new LegendComboDef("NSig vers Récupération", $"nSig > Rec ({Src})", "Marteau",
                new[] { S("Att. forte"), S("Haut", "Att. forte") }),
            new LegendComboDef("DSig vers DLight", $"dSig > dLight ({Src})", "Katars",
                new[] { S("Bas", "Att. forte"), S("Bas", "Att. légère") }),
        },
        ["Sidra"] = new[]
        {
            new LegendComboDef("NSig vers SLight", $"nSig > sLight ({Src})", "Canon",
                new[] { S("Att. forte"), S("Droite", "Att. légère") }),
            new LegendComboDef("NSig vers SSig", $"nSig > sSig ({Src})", "Canon",
                new[] { S("Att. forte"), S("Droite", "Att. forte") }),
            new LegendComboDef("NSig vers NLight", $"nSig > nLight ({Src})", "Épée",
                new[] { S("Att. forte"), S("Att. légère") }),
        },
        ["Teros"] = new[]
        {
            new LegendComboDef("NSig vers DLight", $"nSig > dLight ({Src})", "Hache",
                new[] { S("Att. forte"), S("Bas", "Att. légère") }),
            new LegendComboDef("SLight vers NSig", $"sLight > nSig — doit toucher les frames actives tardives de sLight ({Src})", "Hache",
                new[] { S("Droite", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("DLight vers NSig", $"dLight > nSig ({Src})", "Marteau",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("DLight vers DSig", $"dLight > dSig — doit toucher les frames actives tardives de dLight ({Src})", "Marteau",
                new[] { S("Bas", "Att. légère"), S("Bas", "Att. forte") }),
        },
        ["Thatch"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig ({Src})", "Blasters",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
            new LegendComboDef("DSig vers DLight", $"dSig > dLight ({Src})", "Blasters",
                new[] { S("Bas", "Att. forte"), S("Bas", "Att. légère") }),
            new LegendComboDef("DSig vers NLight", $"dSig > nLight ({Src})", "Blasters",
                new[] { S("Bas", "Att. forte"), S("Att. légère") }),
        },
        ["Val"] = new[]
        {
            new LegendComboDef("DSig vers Récupération", $"dSig > Rec ({Src})", "Gantelets",
                new[] { S("Bas", "Att. forte"), S("Haut", "Att. forte") }),
        },
        ["Vraxx"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig ({Src})", "Blasters",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
        },
        ["Xull"] = new[]
        {
            new LegendComboDef("DLight vers NSig", $"dLight > nSig — doit toucher les frames actives tardives de dLight ({Src})", "Canon",
                new[] { S("Bas", "Att. légère"), S("Att. forte") }),
        },
        ["Yumiko"] = new[]
        {
            new LegendComboDef("DSig vers NLight", $"dSig > nLight ({Src})", "Arc",
                new[] { S("Bas", "Att. forte"), S("Att. légère") }),
            new LegendComboDef("DSig vers Récupération", $"dSig > Rec ({Src})", "Arc",
                new[] { S("Bas", "Att. forte"), S("Haut", "Att. forte") }),
            new LegendComboDef("DSig vers SAir", $"dSig > sAir ({Src})", "Arc",
                new[] { S("Bas", "Att. forte"), S("Saut"), S("Droite", "Att. légère") }),
            new LegendComboDef("DSig vers Récupération (Marteau)", $"dSig > Rec ({Src})", "Marteau",
                new[] { S("Bas", "Att. forte"), S("Haut", "Att. forte") }),
            new LegendComboDef("DSig vers SAir (Marteau)", $"dSig > sAir ({Src})", "Marteau",
                new[] { S("Bas", "Att. forte"), S("Saut"), S("Droite", "Att. légère") }),
            new LegendComboDef("NSig vers XPivot DAir", $"nSig > XPivot dAir ({Src}) XPivot = pivot par élan horizontal.", "Marteau",
                new[] { S("Att. forte"), S("Droite"), S("Saut"), S("Bas", "Att. légère") }),
            new LegendComboDef("DSig vers NAir", $"dSig > nAir ({Src})", "Marteau",
                new[] { S("Bas", "Att. forte"), S("Saut"), S("Att. légère") }),
            new LegendComboDef("DSig vers NSig", $"dSig > nSig ({Src})", "Marteau",
                new[] { S("Bas", "Att. forte"), S("Att. forte") }),
            new LegendComboDef("NSig vers SAir", $"nSig > sAir ({Src})", "Marteau",
                new[] { S("Att. forte"), S("Saut"), S("Droite", "Att. légère") }),
        },
        ["Zariel"] = new[]
        {
            new LegendComboDef("NSig, GC, NLight", $"nSig > GC > nLight ({Src}) GC = Esquive.", "Gantelets",
                new[] { S("Att. forte"), S("Esquive"), S("Att. légère") }),
        },
    };

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
        .Replace(" ", "-").ToLowerInvariant();

    /// <summary>Armes distinctes pour lesquelles ce légend a au moins une combo répertoriée dans
    /// Table (donnée dérivée du contenu déjà vérifié, pas une nouvelle table à sourcer séparément).
    /// Sert à restreindre le sous-filtre d'arme de ControlPanelWindow aux seules armes pertinentes
    /// une fois un personnage choisi, au lieu de proposer les 15 armes du jeu sans distinction.</summary>
    public static List<string> WeaponsFor(string legend) =>
        Table.TryGetValue(legend, out var defs)
            ? defs.Select(d => d.Weapon).Distinct().ToList()
            : new List<string>();
}
