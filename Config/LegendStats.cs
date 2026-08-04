using System.Collections.Generic;

namespace BrawlhallaOverlay;

/// <summary>
/// Stats de base (Force/Dex/Défense/Vitesse) des 69 légendes du jeu, sourcées sur
/// https://brawlmance.com/legends (site communautaire de stats Brawlhalla, pas officiel mais
/// largement utilisé par la scène compétitive), 2026-08-03. Mécanique de stance confirmée sur
/// https://brawlhalla.wiki.gg/wiki/Stats le même jour : une stance échange +1 sur un stat contre
/// -1 sur un autre (un seul swap, pas un gros boost) — donc le Dex max atteignable par une légende
/// est <c>Dex de base + 1</c>, jamais plus.
///
/// Sert à afficher/filtrer les combos par faisabilité réelle : un combo qui demande plus de Dex que
/// ce qu'une légende peut atteindre même avec une stance est physiquement injouable sur ce
/// personnage — confirmé en pratique par l'utilisateur sur Teros (Dex de base 3, combos
/// WeaponComboPresets historiques demandant "7+"/"9" Dex, donc impossibles même avec stance).
/// </summary>
public static class LegendStats
{
    public sealed record Stats(int Force, int Dex, int Defense, int Speed);

    public static readonly Dictionary<string, Stats> Table = new()
    {
        ["Ada"] = new(6, 7, 3, 6),
        ["Arcadia"] = new(7, 7, 4, 4),
        ["Artemis"] = new(5, 5, 4, 8),
        ["Asuri"] = new(4, 7, 5, 6),
        ["Aurus"] = new(6, 6, 6, 4),
        ["Azoth"] = new(7, 5, 6, 4),
        ["Barraza"] = new(6, 4, 8, 4),
        ["Bodvar"] = new(6, 6, 5, 5),
        ["Brynn"] = new(5, 5, 5, 7),
        ["Caspian"] = new(7, 5, 4, 6),
        ["Cassidy"] = new(6, 8, 4, 4),
        ["Cross"] = new(7, 4, 6, 5),
        ["Diana"] = new(5, 6, 5, 6),
        ["Dusk"] = new(6, 7, 4, 5),
        ["Ember"] = new(6, 6, 3, 7),
        ["Ezio"] = new(5, 7, 4, 6),
        ["Fait"] = new(7, 4, 4, 7),
        ["Gnash"] = new(7, 3, 5, 7),
        ["Hattori"] = new(4, 6, 4, 8),
        ["Imugi"] = new(8, 3, 8, 3),
        ["Isaiah"] = new(5, 6, 7, 4),
        ["Jaeyun"] = new(6, 5, 5, 6),
        ["Jhala"] = new(7, 7, 3, 5),
        ["Jiro"] = new(5, 7, 3, 7),
        ["Kaya"] = new(4, 4, 7, 7),
        ["King Zuva"] = new(8, 4, 6, 4),
        ["Koji"] = new(5, 8, 4, 5),
        ["Kor"] = new(6, 5, 7, 4),
        ["Lady Vera"] = new(3, 7, 8, 4),
        ["Lin Fei"] = new(3, 8, 4, 7),
        ["Loki"] = new(4, 8, 5, 5),
        ["Lucien"] = new(3, 5, 6, 8),
        ["Magyar"] = new(5, 4, 9, 4),
        ["Mako"] = new(6, 4, 4, 8),
        ["Mirage"] = new(7, 6, 4, 5),
        ["Mordex"] = new(6, 4, 5, 7),
        ["Munin"] = new(5, 6, 4, 7),
        ["Nix"] = new(4, 5, 7, 6),
        ["Onyx"] = new(5, 4, 8, 5),
        ["Orion"] = new(4, 6, 6, 6),
        ["Petra"] = new(8, 4, 4, 6),
        ["Priya"] = new(4, 6, 5, 7),
        ["Queen Nai"] = new(7, 4, 8, 3),
        ["Ragnir"] = new(5, 6, 6, 5),
        ["Ransom"] = new(7, 4, 3, 8),
        ["Rayman"] = new(5, 5, 6, 6),
        ["Red Raptor"] = new(6, 6, 4, 6),
        ["Reno"] = new(4, 7, 6, 5),
        ["Rupture"] = new(9, 3, 5, 5),
        ["Scarlet"] = new(8, 5, 5, 4),
        ["Sentinel"] = new(5, 4, 7, 6),
        ["Seven"] = new(7, 3, 8, 4),
        ["Sidra"] = new(6, 4, 6, 6),
        ["Sir Roland"] = new(5, 5, 8, 4),
        ["Teros"] = new(8, 3, 6, 5),
        ["Tezca"] = new(7, 5, 5, 5),
        ["Thatch"] = new(7, 5, 3, 7),
        ["Thea"] = new(4, 6, 3, 9),
        ["Thor"] = new(6, 4, 7, 5),
        ["Ulgrim"] = new(6, 3, 7, 6),
        ["Val"] = new(4, 5, 6, 7),
        ["Vector"] = new(5, 4, 6, 7),
        ["Vivi"] = new(6, 5, 4, 7),
        ["Volkov"] = new(4, 8, 6, 4),
        ["Vraxx"] = new(4, 8, 4, 6),
        ["Wu Shang"] = new(5, 7, 5, 5),
        ["Xull"] = new(9, 4, 5, 4),
        ["Yumiko"] = new(4, 7, 4, 7),
        ["Zariel"] = new(7, 4, 7, 4),
    };

    /// <summary>Dex maximum atteignable avec une stance (base + 1, jamais plus — voir docstring de
    /// classe). Retourne -1 si la légende est inconnue (pas de filtrage possible, laisser passer).</summary>
    public static int MaxReachableDex(string legend) =>
        Table.TryGetValue(legend, out var s) ? s.Dex + 1 : -1;
}
