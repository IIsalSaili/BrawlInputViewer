# TODO — pistes ouvertes, pas encore faites

> Remplace `Brawhl.md`, `amelioration.md`, `audit_features.md`,
> `combo_families_plan.md`, `plan.md` et `plan_ux_onboarding.md` (supprimés
> le 2026-08-05) : ces documents étaient soit entièrement implémentés (leur
> contenu fait foi dans `CLAUDE.md` désormais), soit contenaient un mélange de
> fait/pas fait devenu difficile à suivre. Ce fichier ne garde que ce qui
> reste **réellement à faire**, avec assez de contexte pour reprendre chaque
> piste sans redérouler l'historique complet. Pour le "pourquoi" détaillé
> d'une décision passée, voir `CLAUDE.md` (section "Historique des
> décisions").

---

## Refonte visuelle — DA "Brawlhalla-like"

Plan de design complet jamais implémenté (ex-`docs/Brawhl.md`), gardé ici
intégralement pour ne rien perdre. **Aucun code n'a été touché**, à valider
avant tout travail.

**Constat sur la DA du vrai jeu** : pas de palette unique figée (système de
"Color Scheme Palettes", 118 thèmes) — le point commun est le *principe*
(accents vifs/saturés sur fond neutre sombre), pas une teinte précise.
Rebrand officiel (agence Biborg) décrit un style "colorful, dynamic,
hand-drawn" façon cartoon énergique, pas cyberpunk/néon. Motif UI récurrent :
écusson/"crest" (nameplate/killplate/scoreplate), pas de forme hexagonale
futuriste. Police probable (non confirmée officiellement) : Eras Bold
Italic/Grind Zero Bold Italic — ronde, pleine, gras italique.

**État actuel de l'app** (déjà proche par accident) : couleurs d'accent par
action déjà vives et saturées (`#5DADE2`, `#58D68D`, `#F4D03F`, `#E67E22`,
`#E74C3C`, `#9B59B6`), mais aucun thème centralisé (littéraux hardcodés dans
`MainWindow.xaml.cs`/`ControlPanelWindow.xaml.cs`/`ComboEditorWindow.xaml.cs`,
pas de `ResourceDictionary` dans `App.xaml`). Panneau de contrôle/éditeur en
gris neutre `#1E1E1E`/`#2A2A2A`, "outil dev", aucun accent de marque — c'est
la partie la plus éloignée de l'identité Brawlhalla. Tray icon déjà brandée
(cercle sombre + "B" doré `#E8C44A`), seul accent chaud existant.

**Principe directeur** : ne pas repeindre l'overlay en template "jeu de
combat" générique — il doit rester lisible en une fraction de seconde
par-dessus le jeu (click-through, faible opacité, contraste net). La DA
Brawlhalla s'emprunte par petites touches cohérentes (palette, forme
d'écusson, ton cartoon), jamais au prix de la lisibilité. Le panneau de
contrôle et l'éditeur de combo peuvent porter la marque plus franchement
(jamais vus par-dessus le jeu en combat). Garde-fous : pas de
dégradé/glow/blur décoratif sans fonction de feedback d'état, pas de police
décorative dans l'historique de coups (12-14px), ne jamais dépendre d'assets
Brawlhalla réels (recréer/s'inspirer, pas copier).

**Palette proposée** :
| Rôle | Actuel | Proposition |
|---|---|---|
| Fond overlay | `#66000000`/`#77000000` | `#7A1B1B24` (bleu-nuit très sombre, légère teinte violette) |
| Fond panneau de contrôle | `#1E1E1E` | `#181722` à `#221F2E` (même famille bleu-nuit/violet) |
| Cartes/sections | `#2A2A2A` | `#2A2735` avec liseré `#3A3548` |
| Accent signature | — | Or `#E8C44A` (repris de la tray icon) |
| Accents par action | déjà bons | conserver tels quels, ne pas uniformiser |
| État succès | vert générique | garder, éventuellement `#4CAF7D` si retouché |
| État échec | rouge générique | ne pas toucher (signal fonctionnel) |

**Typo** : une seule police d'accent (titres/labels courts seulement — nom de
mode, nom de combo, titres d'onglet, badge tray), style rond/gras (Segoe UI
Black/Semibold en majuscules, pas de police tierce à embarquer). Corps de
texte (historique, labels, listes) reste Segoe UI standard.

**Formes** : motif "écusson" pour badge de mode/indicateur de série/bordure
du panneau principal (coins supérieurs plus prononcés qu'en bas, ou petit
chanfrein) plutôt que le rectangle à coins uniformément arrondis actuel.
Garder les pastilles d'action du mode Tutoriel telles quelles (déjà validées
3 fois, voir CLAUDE.md). Bordure dorée fine (1-2px, `#E8C44A` ~40-50%
opacité) sur l'élément actif/sélectionné. Pas de texture/pattern de fond.

**Ce qu'on ne touche pas** : couleurs par action de `KeyBindConfig.cs`
(associations déjà reconnues par l'utilisateur), icônes `Assets/Icons/`
(validées après plusieurs itérations), tout comportement (click-through,
hooks, ComboRunner) — strictement visuel.

**Étapes suggérées avant tout code** : (1) valider la palette bleu-nuit/violet
+ doré, (2) décider si la police d'accent vaut l'effort vs Segoe UI Semibold
partout, (3) centraliser les couleurs dans un `ResourceDictionary`
(`App.xaml`) au lieu des littéraux hardcodés (refactor pur), (4) prototyper
d'abord sur `ControlPanelWindow` (zéro risque en jeu) avant l'overlay.

**Effort** : élevé (touche 3 fenêtres + refactor de thème). Nécessite
validation utilisateur sur la palette/typo avant toute implémentation.

---

## Parcours — Chapitre 5 (avancé)

Chapitres 0 à 4 faits et testés (voir CLAUDE.md, Architecture
`ParcoursWindow`/`ParcoursCurriculum`). Le chapitre 5 (gravity cancel, chase
dodge, ledge/floor cancel, DI) reste à écrire — contenu explicitement
"avancé"/déblocable, jamais imposé, public plus restreint. Nécessite de
sourcer chaque affirmation de jeu avant écriture (interroger l'utilisateur en
premier, recouper avec une source écrite ensuite — méthode qui a fonctionné
pour les chapitres 1 à 4, voir CLAUDE.md Version 14-16).
**Effort** : élevé (nouveau contenu à sourcer, pas de réutilisation possible
comme pour le chapitre 4).

## Combos de légende (Signature) par personnage

`Config/LegendComboPresets.cs` a sa `Table` intentionnellement vide depuis la
Version 17 (perte de confiance dans la source Reddit d'origine — cas Teros,
Dex physiquement impossible). La structure (`Legends`/`WeaponsFor`/
`BuildPresetCombos`) et tout le filtrage par personnage existent déjà et
n'attendent qu'une source de confiance pour les vrais combos Signature des
69 légendes. **En pause, à ne reprendre que sur demande explicite avec une
source fournie par l'utilisateur** (deux erreurs de contenu déjà commises sur
ce fichier précis — voir "Correctif Faux"/combos hallucinées dans CLAUDE.md).
**Effort** : élevé si fait sérieusement (sourcing arme par arme/légende par
légende).

## Détecter ce qui se passe en jeu (et pas seulement les inputs)

Étude de faisabilité complète dans **`docs/plan_improve_combo.md`** (rédigée le
2026-08-06, aucun code écrit). Résumé : le modèle de vision qui classifie les
moves est faisable mais coûteux et peu rentable *en premier* ; trois routes plus
rentables passent avant (lecture du HUD « Damage Numbers » par OCR, parsing des
`.replay` locaux, option de lancement officielle `-writestats`). Vérifié
empiriquement sur les 549 replays réels de la machine : le format 10.09 se
décode (zlib + XOR), seuls quelques champs ont dérivé vs les parsers publics.
**Prochaine action concrète : le test `-writestats` de 30 minutes (§7, phase 0).**

## Détection de tech skip (buffer de saut, wavedash)

Mesure pure sur les inputs déjà captés (buffer de saut, wavedash), cohérente
avec la doctrine anti-ban (aucune lecture de l'état du jeu nécessaire).
Jamais commencée. **Effort** : moyen (nouvelle logique de détection à
concevoir).

## Comparaison de sessions dans l'app

`AppState.SessionLog` + export CSV existent déjà par session, mais rien ne
stocke ces exports pour comparer une session à l'autre *dans* l'app — il faut
aujourd'hui rouvrir les CSV dans un tableur externe. **Effort** : moyen
(stockage à ajouter + petit écran de comparaison).

## Mode "entraînement sans jeu ouvert" assumé

Beaucoup de drills du Parcours n'exigent pas Brawlhalla lancé. Assumer ce
mode explicitement changerait le cadrage de `AppState.CaptureSuspended`
(pensé aujourd'hui comme "coupure temporaire", pas comme un vrai mode de
fonctionnement). **C'est une question de positionnement produit à trancher
avec l'utilisateur avant tout code**, pas juste un chantier technique.

## Guide de rythme progressif (métronome)

La barre de tolérance visuelle du mode Tutoriel est déjà présente mais
purement indicative depuis le retrait du timing comme condition d'échec
(décision explicite de l'utilisateur). Idée ouverte : un guide de vitesse
croissante, mais **strictement indicatif** — ne doit jamais redevenir une
condition d'échec, c'est ce qui a été explicitement rejeté par le passé.
**Effort** : moyen. **Risque** : à traiter avec précaution.

## Vigilance passive : faux positifs de familles de combos

`Core/ComboFamilies.cs` regroupe deux combos qui partagent leurs premières
étapes comme une "famille" — un risque théorique jamais vérifié en pratique :
deux combos différentes de la même arme pourraient partager leurs 2
premières étapes par coïncidence et se retrouver groupées à tort. Aucune
action à mener maintenant, juste à garder en tête si un regroupement semble
illogique un jour (solution de repli : champ d'opt-out explicite plutôt que
complexifier la détection automatique).

---

## Ne pas reproposer (tranché, voir CLAUDE.md)

- Réactiver les modes Historique/Grand affichage — supprimés définitivement
  en Version 22, choix explicite de l'utilisateur.
- Réintroduire les portraits "splash art" — tranché en Version 20, une seule
  source de portraits (style "Roster Pose"/tête serrée) pour les 69 légendes.
- Intégration d'API externe Brawlhalla (stats de match) — écarté pour rester
  100% local, sauf demande explicite.
- Retour du timing comme condition d'échec de combo — retiré sur demande
  explicite, ne doit pas revenir même sous forme de métronome strict (voir
  "Guide de rythme progressif" ci-dessus).
