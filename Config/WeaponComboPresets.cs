using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BrawlhallaOverlay;

/// <summary>
/// Bibliothèque de combos d'arme (true combos, non-esquivables), remplacée en
/// entier suite à un signalement de l'utilisateur : la version précédente de
/// ce fichier citait des sources (bluestacks.com/gamespecifications.com/etc.)
/// pour des séquences qui, une fois vraiment récupérées, ne correspondaient à
/// rien de réel — deuxième occurrence du même problème après un premier
/// correctif sur la Faux (voir CLAUDE.md, "Correctif Faux"). Cette fois, la
/// source n'est pas un guide web mais un post Reddit fourni directement par
/// l'utilisateur (liste de true combos avec seuils de Dex, testés à 0% de
/// dégâts), collé tel quel dans la conversation — pas de web fetch à refaire,
/// pas de paraphrase de source à vérifier a posteriori.
///
/// Seuls les combos d'arme génériques (non liés à un légend précis) sont ici.
/// Les combos de légende (utilisant une attaque Signature propre au légend)
/// sont dans LegendComboPresets.cs — voir ce fichier pour le détail.
///
/// Le post ne couvre que 11 armes sur les 15 du jeu (aucune entrée pour
/// Épée à deux mains/Orbe/Lance-fusée/Bottes de combat/Chakram) : ces armes
/// n'ont donc volontairement AUCUN combo ici plutôt que de garder l'ancien
/// contenu non vérifié à côté des combos vérifiés — mieux vaut une liste vide
/// (visible comme telle dans l'onglet Combos) qu'un mélange qui laisserait
/// croire que tout le fichier est au même niveau de confiance.
///
/// Le post liste séparément une section "Spear Combos" et une section "Lance
/// Combos" avec des entrées différentes. Le jeu actuel n'a pas d'arme "Lance"
/// distincte de "Spear" (voir liquipedia.net/brawlhalla/Weapons) — les deux
/// sections sont donc fusionnées ici sous "Lance", le nom français de Spear
/// dans ce projet (voir WeaponComboPresets.Weapons). Un doublon exact entre
/// les deux sections (sLight > nLight, 2+ Dex) a été dédupliqué.
///
/// Traduction vers le vocabulaire d'actions de l'app (mêmes conventions que
/// la version précédente, voir aussi git history) :
/// - nLight/sLight/dLight/nSig/sSig/dSig → direction (Gauche/Droite/Haut/Bas)
///   + Att. légère (Light) ou Att. forte (Sig — en Brawlhalla, l'attaque
///   Signature EST l'attaque forte : même bouton, la direction change juste
///   la variante nSig/sSig/dSig, comme pour Light), pressées ensemble.
/// - nAir/sAir/dAir → un "Saut" est inséré juste avant, même bouton
///   d'attaque ensuite (l'app ne suit pas l'état sol/air). Deux attaques
///   aériennes enchaînées dans un même combo ne répètent pas "Saut" entre
///   les deux (même saut/state aérien, convention déjà utilisée par
///   l'ancienne version de ce fichier pour les enchaînements de la Faux).
/// - GC (Gravity Cancel) → "Esquive" (confirmé en jeu par l'utilisateur : le
///   GC s'exécute en appuyant sur Esquive au bon moment en l'air, pas Saut).
/// - Rec/Recovery → Haut + Att. forte.
/// - GroundPound/GP → un double appui de direction seule (Bas, Bas).
/// - Dash → un appui de direction seule.
/// - XPivot, Reverse (ex. "Reverse Nair"), Ledge cancel, Wall cancel → ce
///   sont des techniques de mouvement/positionnement, pas des boutons
///   supplémentaires : traduites par la séquence de boutons la plus proche,
///   la technique exacte (à exécuter correctement en jeu, l'app ne peut pas
///   la valider) est précisée dans la Description.
/// - Le niveau de Dex minimum requis (mécanique Brawlhalla que l'app ne
///   modélise pas : le Dex du légend change les fenêtres de combo) est
///   indiqué dans la Description, à titre informatif seulement — l'app ne
///   bloque aucun combo selon un Dex.
/// - Testés par la source à 0% (blanc) de dégâts : peuvent ne plus connecter
///   à des % plus élevés (voir Combo.DamageNote, laissé vide ici faute de
///   détail par combo dans la source — le post précise juste "testé à 0%"
///   globalement, pas variante par variante).
///
/// Chargées à la demande via AppState.ImportWeaponPresets(weapon) et
/// fusionnées dans combos.json (Id stable "preset-&lt;arme&gt;-&lt;n&gt;" :
/// une réimportation met à jour le contenu existant au lieu de le dupliquer).
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

    private const string Src = "source : true combos Reddit (testés à 0% de dégâts), fournis par l'utilisateur.";

    private static readonly Dictionary<string, ComboDef[]> Table = new()
    {
        ["Épée"] = new[]
        {
            new ComboDef("DLight, GC, NLight", $"dLight > GC > nLight — 3+ Dex ({Src}) GC = Esquive, pas Saut.",
                new[] { S("Bas", "Att. légère"), S("Esquive"), S("Att. légère") }),
            new ComboDef("DLight vers Récupération", $"dLight > Rec — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("DLight, GC, SLight", $"dLight > GC > sLight — 3+ Dex ({Src}) GC = Esquive, pas Saut.",
                new[] { S("Bas", "Att. légère"), S("Esquive"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers SAir", $"dLight > sAir — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers Ground Pound", $"dLight > GroundPound — 3+ Dex ({Src}) GP = double Bas.",
                new[] { S("Bas", "Att. légère"), S("Bas"), S("Bas") }),
            new ComboDef("Ground Pound, GC, NLight", $"GroundPound > GC > nLight — 3+ Dex ({Src}) GP = double Bas, GC = Esquive.",
                new[] { S("Bas"), S("Bas"), S("Esquive"), S("Att. légère") }),
            new ComboDef("DLight vers NAir", $"dLight > nAir — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DLight vers DAir", $"dLight > dAir — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("DAir vers NLight", $"dAir > nLight — 4+ Dex ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Att. légère") }),
            new ComboDef("SAir vers NLight", $"sAir > nLight — 5+ Dex, doit toucher le 2e coup de sAir ({Src})",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("DAir vers Récupération", $"dAir > Rec — 5+ Dex, doit faire rebondir à des % de vie plus élevés ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("Ground Pound vers Récupération", $"GroundPound > Rec — 7+ Dex ({Src}) GP = double Bas.",
                new[] { S("Bas"), S("Bas"), S("Haut", "Att. forte") }),
            new ComboDef("SLight vers NLight", $"sLight > nLight — 9 Dex ({Src})",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("NAir vers Récupération", $"nAir > Rec — 9 Dex ({Src})",
                new[] { S("Saut"), S("Att. légère"), S("Haut", "Att. forte") }),
        },
        ["Marteau"] = new[]
        {
            new ComboDef("DLight, GC, NLight", $"dLight > GC > nLight — 2+ Dex ({Src}) GC = Esquive.",
                new[] { S("Bas", "Att. légère"), S("Esquive"), S("Att. légère") }),
            new ComboDef("DLight vers DAir", $"dLight > dAir — 2+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("DLight vers Récupération", $"dLight > Rec — 2+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("DLight vers SLight", $"dLight > sLight — 2+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers SAir", $"dLight > sAir — 2+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers NAir", $"dLight > nAir — 2+ Dex, très tardif en % de vie ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DAir vers Récupération", $"dAir > Rec — 2+ Dex ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("NAir vers Récupération", $"nAir > Rec — 2+ Dex, doit toucher les hitboxes tardives de nAir ({Src})",
                new[] { S("Saut"), S("Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("NLight vers SLight", $"nLight > sLight — 4+ Dex, seulement certains matchups Force/Défense ({Src})",
                new[] { S("Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("DAir vers NAir", $"dAir > nAir — 4+ Dex, seulement certains matchups Force/Défense ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Att. légère") }),
            new ComboDef("SAir vers DLight", $"sAir > dLight — 5+ Dex, doit toucher les hitboxes tardives de sAir ({Src})",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("Récupération vers DAir", $"Rec > dAir — 6+ Dex, doit faire rebondir à des % de vie plus élevés ({Src})",
                new[] { S("Haut", "Att. forte"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("SAir vers NLight", $"sAir > nLight — 6+ Dex ({Src})",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("NAir vers Récupération (variante)", $"nAir > Rec — 7+ Dex ({Src})",
                new[] { S("Saut"), S("Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("SLight vers NLight", $"sLight > nLight — 9 Dex ({Src})",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
        },
        ["Blasters"] = new[]
        {
            new ComboDef("DLight vers NLight", $"dLight > nLight — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Att. légère") }),
            new ComboDef("DLight vers XPivot NAir", $"dLight > XPivot nAir — 3+ Dex ({Src}) XPivot = utiliser l'élan horizontal pour pivoter l'attaque, à exécuter au bon timing en jeu.",
                new[] { S("Bas", "Att. légère"), S("Droite"), S("Saut"), S("Att. légère") }),
            new ComboDef("DLight vers Récupération", $"dLight > Rec — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("DLight vers SLight", $"dLight > sLight — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers SAir", $"dLight > sAir — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers DAir", $"dLight > dAir — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("NAir vers NAir", $"nAir > nAir — 3+ Dex, doit toucher les frames actives tardives ({Src})",
                new[] { S("Saut"), S("Att. légère"), S("Att. légère") }),
            new ComboDef("NAir vers Récupération", $"nAir > Rec — 3+ Dex, doit toucher les frames actives tardives ({Src})",
                new[] { S("Saut"), S("Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("SAir vers NLight", $"sAir > nLight — 3+ Dex ({Src})",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("SAir vers DLight", $"sAir > dLight — 3+ Dex ({Src})",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("SAir vers NAir", $"sAir > nAir — 3+ Dex ({Src})",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("SAir vers Récupération", $"sAir > Rec — 3+ Dex ({Src})",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("SLight vers XPivot NAir", $"sLight > XPivot nAir — 3+ Dex, doit toucher les frames actives tardives de sLight ({Src}) XPivot = pivot par élan horizontal.",
                new[] { S("Droite", "Att. légère"), S("Droite"), S("Saut"), S("Att. légère") }),
            new ComboDef("SLight vers SAir", $"sLight > sAir — 3+ Dex, doit toucher les frames actives tardives de sLight ({Src})",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("SAir vers SLight", $"sAir > sLight — 9 Dex seulement ({Src})",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Droite", "Att. légère") }),
        },
        ["Katars"] = new[]
        {
            new ComboDef("SLight vers DLight", $"sLight > dLight — 3+ Dex ({Src})",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("SAir vers DLight", $"sAir > dLight — 3+ Dex, doit toucher les hitboxes tardives de sAir ({Src})",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("Récupération, GC, DLight", $"Rec > GC > dLight — 4+ Dex ({Src}) GC = Esquive.",
                new[] { S("Haut", "Att. forte"), S("Esquive"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers NLight", $"sLight > nLight — 5+ Dex ({Src})",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("NAir vers DLight", $"nAir > dLight — 5+ Dex ({Src})",
                new[] { S("Saut"), S("Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("Ground Pound, GC, DLight", $"GroundPound > GC > dLight — 6+ Dex ({Src}) GP = double Bas, GC = Esquive.",
                new[] { S("Bas"), S("Bas"), S("Esquive"), S("Bas", "Att. légère") }),
            new ComboDef("SAir vers DAir", $"sAir > dAir — 8+ Dex, doit toucher la dernière frame active de sAir hors-scène ({Src})",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("NLight vers DLight", $"nLight > dLight — 8+ Dex, doit toucher nLight en pile ({Src})",
                new[] { S("Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("DAir vers DLight", $"dAir > dLight — 9 Dex ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Bas", "Att. légère") }),
        },
        ["Hache"] = new[]
        {
            new ComboDef("SLight vers DLight", $"sLight > dLight — 2+ Dex ({Src})",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers NAir", $"sLight > nAir — 2+ Dex ({Src})",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("SLight vers DAir", $"sLight > dAir — 2+ Dex ({Src})",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers SAir", $"sLight > sAir — 2+ Dex, doit toucher les hitboxes tardives de sLight ({Src})",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("SLight vers Ground Pound", $"sLight > GroundPound — 2+ Dex, doit toucher les hitboxes tardives de sLight ({Src}) GP = double Bas.",
                new[] { S("Droite", "Att. légère"), S("Bas"), S("Bas") }),
            new ComboDef("DAir vers DLight", $"dAir > dLight — 2+ Dex, doit toucher les dernières hitboxes de dAir ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("NAir, GC, DLight", $"nAir > GC > dLight — 6+ Dex ({Src}) GC = Esquive.",
                new[] { S("Saut"), S("Att. légère"), S("Esquive"), S("Bas", "Att. légère") }),
            new ComboDef("Récupération vers NLight", $"Rec > nLight — Dex non testé, non fiable (setup incohérent) ({Src})",
                new[] { S("Haut", "Att. forte"), S("Att. légère") }),
        },
        ["Arc"] = new[]
        {
            new ComboDef("DLight vers NLight", $"dLight > nLight — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Att. légère") }),
            new ComboDef("DLight vers NAir", $"dLight > nAir — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DLight vers Récupération", $"dLight > Rec — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("SLight vers NLight", $"sLight > nLight — 3+ Dex ({Src})",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("DAir vers NLight", $"dAir > nLight — 3+ Dex ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Att. légère") }),
            new ComboDef("NLight vers NAir", $"nLight > nAir — 3+ Dex ({Src})",
                new[] { S("Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DAir vers SLight", $"dAir > sLight — 3+ Dex, doit toucher les frames actives tardives du dAir au sol ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers DAir", $"dLight > dAir — 6+ Dex, doit toucher dAir sur scène ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("DLight vers SAir", $"dLight > sAir — 6+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
        },
        ["Faux"] = new[]
        {
            new ComboDef("Récupération vers NLight", $"Rec > nLight — 5+ Dex ({Src})",
                new[] { S("Haut", "Att. forte"), S("Att. légère") }),
            new ComboDef("Récupération vers SLight", $"Rec > sLight — 5+ Dex ({Src})",
                new[] { S("Haut", "Att. forte"), S("Droite", "Att. légère") }),
            new ComboDef("NAir vers SAir", $"nAir > sAir — 7+ Dex ({Src})",
                new[] { S("Saut"), S("Att. légère"), S("Droite", "Att. légère") }),
        },
        ["Canon"] = new[]
        {
            new ComboDef("DLight, GC, NLight", $"dLight > GC > nLight — 3+ Dex ({Src}) GC = Esquive.",
                new[] { S("Bas", "Att. légère"), S("Esquive"), S("Att. légère") }),
            new ComboDef("DLight vers SLight", $"dLight > sLight — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers NAir", $"dLight > nAir — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DLight vers SAir", $"dLight > sAir — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("DAir vers NLight", $"dAir > nLight — 3+ Dex, doit toucher les frames actives tardives de dAir ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Att. légère") }),
            new ComboDef("NLight vers SLight", $"nLight > sLight — 3+ Dex ({Src})",
                new[] { S("Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("DAir vers SLight", $"dAir > sLight — 3+ Dex, doit toucher les frames actives tardives de dAir ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("SAir vers NLight", $"sAir > nLight — 3+ Dex ({Src})",
                new[] { S("Saut"), S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("DLight vers DAir", $"dLight > dAir — 3+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers DLight", $"sLight > dLight — 3+ Dex ({Src})",
                new[] { S("Droite", "Att. légère"), S("Bas", "Att. légère") }),
        },
        ["Lance"] = new[]
        {
            // Le post distingue "Spear" et "Lance" comme deux armes séparées ;
            // le jeu actuel n'en a qu'une (Spear = "Lance" en français dans ce
            // projet). Les deux sections sont fusionnées ici, un doublon exact
            // (sLight > nLight, 2+ Dex) dédupliqué.
            new ComboDef("DLight vers SAir", $"dLight > sAir — 2+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers NAir", $"dLight > nAir — 2+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DLight vers Récupération", $"dLight > Rec — 2+ Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("SLight vers NLight", $"sLight > nLight — 2+ Dex, doit toucher le 2e coup de sLight ({Src})",
                new[] { S("Droite", "Att. légère"), S("Att. légère") }),
            new ComboDef("SLight vers DAir", $"sLight > dAir — 2+ Dex ({Src})",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Bas", "Att. légère") }),
            new ComboDef("SLight vers NAir", $"sLight > nAir — 2+ Dex ({Src})",
                new[] { S("Droite", "Att. légère"), S("Saut"), S("Att. légère") }),
            new ComboDef("DAir vers NLight", $"dAir > nLight — 2+ Dex, doit toucher les dernières frames actives du dAir au sol ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Att. légère") }),
            new ComboDef("DAir vers DLight", $"dAir > dLight — 2+ Dex, doit toucher les dernières frames actives du dAir au sol ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("DAir vers SLight", $"dAir > sLight — 2+ Dex, doit toucher les dernières frames actives du dAir au sol ({Src})",
                new[] { S("Saut"), S("Bas", "Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("Reverse NAir vers DAir", $"Reverse nAir > dAir — 2+ Dex ({Src}) « Reverse » = se retourner juste avant de frapper, technique à exécuter en jeu.",
                new[] { S("Saut"), S("Att. légère"), S("Bas", "Att. légère") }),
            new ComboDef("Reverse NAir vers SAir", $"Reverse nAir > sAir — 4+ Dex ({Src}) « Reverse » = se retourner juste avant de frapper.",
                new[] { S("Saut"), S("Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("Reverse NAir vers Récupération", $"Reverse nAir > Rec — 4+ Dex ({Src}) « Reverse » = se retourner juste avant de frapper.",
                new[] { S("Saut"), S("Att. légère"), S("Haut", "Att. forte") }),
            new ComboDef("NAir, GC, NLight", $"nAir > GC > nLight — 7+ Dex ({Src}) GC = Esquive.",
                new[] { S("Saut"), S("Att. légère"), S("Esquive"), S("Att. légère") }),
            new ComboDef("NAir, GC, DLight", $"nAir > GC > dLight — 7+ Dex ({Src}) GC = Esquive.",
                new[] { S("Saut"), S("Att. légère"), S("Esquive"), S("Bas", "Att. légère") }),
            new ComboDef("NAir vers SLight", $"nAir > sLight — 7+ Dex, doit ledge cancel (accrochage au bord) ({Src})",
                new[] { S("Saut"), S("Att. légère"), S("Droite", "Att. légère") }),
            new ComboDef("DLight vers NLight", $"dLight > nLight — 9 Dex ({Src})",
                new[] { S("Bas", "Att. légère"), S("Att. légère") }),
            new ComboDef("DLight vers SLight", $"dLight > sLight — 9 Dex, doit toucher les frames actives tardives de dLight ({Src})",
                new[] { S("Bas", "Att. légère"), S("Droite", "Att. légère") }),
        },
    };

    /// <summary>Construit la liste des combos préréglées par arme (nombre variable, voir Table),
    /// avec un Id stable ("preset-&lt;arme&gt;-&lt;n&gt;") pour permettre une réimportation idempotente.</summary>
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
                    MinDex = ParseMinDex(def.Description),
                });
            }
        }
        return result;
    }

    /// <summary>Extrait le seuil de Dex (ex. "3+ Dex", "9 Dex") déjà présent dans le texte de
    /// Description de chaque combo, plutôt que de dupliquer la valeur dans un second champ à
    /// resynchroniser à la main sur les ~90 combos existants. Null si absent (ex. "Dex non testé").</summary>
    private static readonly Regex DexPattern = new(@"(\d+)\+?\s*Dex", RegexOptions.Compiled);

    internal static int? ParseMinDex(string description)
    {
        var match = DexPattern.Match(description);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static string Slug(string weapon) => weapon
        .Replace("é", "e").Replace("è", "e").Replace("à", "a")
        .Replace(" ", "-").ToLowerInvariant();
}
