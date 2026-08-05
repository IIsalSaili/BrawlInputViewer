using System.Collections.Generic;

namespace BrawlhallaOverlay;

/// <summary>
/// Mapping nom d'arme (Combo.Weapon, voir WeaponComboPresets.WeaponNames) -> nom de fichier
/// dans Assets/Weapons/. Icônes officielles récupérées sur brawlhalla.com/legends/ (page qui liste
/// aussi les portraits de personnage, cms.brawlhalla.com — même source que Assets/Legends), une par
/// arme réelle du jeu (mêmes 15 noms que WeaponComboPresets.WeaponNames). Fichier séparé plutôt
/// qu'un dictionnaire en dur dans MainWindow, sur le même principe que LegendPortraitFileName.
/// </summary>
public static class WeaponIconAssets
{
    public static readonly Dictionary<string, string> FileNameByWeapon = new()
    {
        ["Épée"] = "Epee.png",
        ["Lance"] = "Lance.jpg",
        ["Marteau"] = "Marteau.png",
        ["Blasters"] = "Blasters.jpg",
        ["Katars"] = "Katars.png",
        ["Hache"] = "Hache.jpg",
        ["Arc"] = "Arc.png",
        ["Faux"] = "Faux.png",
        ["Épée à deux mains"] = "EpeeDeuxMains.jpg",
        ["Gantelets"] = "Gantelets.png",
        ["Canon"] = "Canon.jpg",
        ["Orbe"] = "Orbe.jpg",
        ["Lance-fusée"] = "LanceFusee.png",
        ["Bottes de combat"] = "BottesDeCombat.png",
        ["Chakram"] = "Chakram.png",
    };
}
