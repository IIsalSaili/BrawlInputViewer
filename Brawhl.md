# Plan de refonte visuelle — DA "Brawlhalla-like" pour l'overlay

> Document de design uniquement. **Aucun code n'a été modifié.** Objectif :
> définir une direction artistique cohérente avec l'identité visuelle de
> Brawlhalla, sans copier ses assets, sans perdre la lisibilité "outil
> d'entraînement" qui fait l'intérêt de l'app.

## 1. Ce qu'on sait de la DA Brawlhalla (recherche, sources en bas)

- **Pas de palette unique figée.** Le jeu tourne sur un système de
  "Color Scheme Palettes" (118 thèmes : Art Deco, Synthwave, Kira-kira,
  Starlight, Darkheart, Armageddon, Frozen Forest, variantes Esports/Team
  Red-Blue-Yellow...). La marque n'est pas "violet/or" comme un Diablo, elle
  est **modulaire et colorée** — le joueur personnalise ses propres couleurs.
  → Le point commun n'est pas *une* teinte, c'est le **principe** : accents
  vifs et saturés sur fond neutre sombre, avec de la couleur d'identité par
  élément (arme, personnage, équipe).
- **Rebrand officiel (agence Biborg pour Ubisoft)** : identité décrite comme
  *"colorful, dynamic, hand-drawn"*, avec un travail chromatique qui infuse
  logo, DA et UI. Style *"western cartoon sensibilities + dynamic fighting
  game aesthetics"* (vitrine officielle "Community Art Showcase"). Donc :
  **cartoon/BD énergique**, pas cyberpunk/néon, pas skeuomorphe réaliste.
- **UI Themes en jeu** = 3 éléments récurrents : *Nameplate* (écusson/crest
  au-dessus du cadre de chargement), *Killplate* (emblème autour de l'icône
  dans le killfeed), *Scoreplate* (crest en fin de partie). → motif visuel
  répété : **écusson/blason ("crest")**, pas hexagone ni bord biseauté
  futuriste. Formes et textures "distinctes" mentionnées par Biborg, cohérent
  avec un style dessiné à la main plutôt que métallique/high-tech.
- **HUD de dégâts en combat** : pas de barre de vie — l'icône du joueur
  **change de couleur** (blanc → rouge → noir) selon les dégâts encaissés.
  Retours communautaires : HUD jugé parfois **trop petit / trop regroupé
  dans un coin**, au point que des joueurs font leurs propres overlays tiers
  pour afficher les dégâts plus lisiblement (cf. mod GitHub
  "Brawlhalla-health-hud-Enhanced") — un signal direct que la lisibilité
  prime sur la fidélité esthétique, y compris pour les joueurs eux-mêmes.
- **Typographie** : indices communautaires (non officiels) pointent vers
  **Eras Bold Italic** pour les menus/UI et **Grind Zero Bold Italic** pour
  le logo — une police **ronde, pleine, en gras italique**, pas une
  condensée agressive type "esport tryhard". À vérifier visuellement avant
  de s'en servir de référence ferme.
- **Icônes d'armes/perso** : aucune source fiable trouvée sur le rendu exact
  (contours/ombres/saturation) — juste confirmation qu'il existe des skins
  d'armes personnalisables. Pas de fait exploitable ici, à laisser de côté.

## 2. Ce que l'app a déjà (état actuel, factuel)

- Aucun thème centralisé : chaque couleur est un littéral hardcodé dans
  `MainWindow.xaml.cs` / `ControlPanelWindow.xaml.cs` / `ComboEditorWindow.xaml.cs`.
  Pas de `ResourceDictionary` dans `App.xaml`.
- Overlay (in-game) : fonds translucides noir/blanc à faible opacité
  (`#66000000`, `#33FFFFFF`...), couleurs d'accent par action déjà **vives et
  saturées** (bleu `#5DADE2`, vert `#58D68D`, jaune `#F4D03F`, orange
  `#E67E22`, rouge `#E74C3C`, violet `#9B59B6`) — c'est déjà proche de
  l'esprit "coloré et lisible" de Brawlhalla, presque par accident.
  `CornerRadius` variés (4/6/10/14), pas de dégradés, pas de DropShadow nulle
  part dans le code.
- Panneau de contrôle / éditeur de combo : thème sombre neutre `#1E1E1E`/
  `#2A2A2A`, très "outil dev", aucun accent de marque — c'est la partie la
  plus éloignée visuellement de Brawlhalla.
- Tray icon déjà "brandée" à la main : cercle sombre + anneau/lettre "B" en
  or `#E8C44A` — seul endroit avec un accent chaud doré, isolé du reste.
- Police système Segoe UI partout (Consolas seulement pour l'affichage des
  touches) — aucune tentative de personnalisation typographique.

## 3. Principe directeur

**Ne pas repeindre l'overlay en template "jeu de combat" générique.**
L'app doit rester lisible en une fraction de seconde par-dessus le jeu
(click-through, faible opacité, contraste net) — c'est sa fonction. La DA
Brawlhalla doit être **empruntée par petites touches cohérentes** (palette,
forme d'écusson, ton "cartoon énergique"), jamais au prix de la lisibilité.
Le panneau de contrôle et l'éditeur de combo, eux, peuvent porter la marque
plus franchement puisqu'ils ne sont jamais vus par-dessus le jeu en combat.

Garde-fous à ne pas franchir :
- Pas de dégradés/glow/blur ajoutés juste pour faire "gaming" — chaque effet
  visuel doit avoir une fonction (feedback d'état), sinon il distrait pendant
  un combo rapide.
- Pas de police décorative illisible à petite taille dans l'historique de
  coups (12-14px) — la police de marque, si adoptée, reste réservée aux
  titres/labels, jamais au texte fin qu'on doit lire vite.
- Ne jamais dépendre d'assets Brawlhalla réels (extraits du jeu) : tout doit
  être recréé/inspiré, pas copié (droits, + les icônes `Assets/Icons/` déjà
  fournies par l'utilisateur restent la référence graphique existante à
  respecter, voir section "Historique des décisions" du CLAUDE.md).

## 4. Palette proposée

Garder l'esprit actuel (couleurs vives et saturées par action) mais
l'ancrer dans un **fond de marque cohérent** au lieu du noir/gris neutre
actuel, et introduire **un accent doré unique** comme signature — en écho
direct à la tray icon déjà validée par l'utilisateur.

| Rôle | Valeur actuelle | Proposition | Notes |
|---|---|---|---|
| Fond overlay (mode 1/3) | `#66000000` / `#77000000` | `#7A1B1B24` (bleu-nuit très sombre, légère teinte violette) au lieu de noir pur | Reste translucide, mais évite le "gris technique" pour un ton plus "arène" |
| Fond panneau de contrôle | `#1E1E1E` | `#181722` à `#221F2E` (même famille bleu-nuit/violet sombre) | Un seul fond de marque partagé overlay ↔ panneau, au lieu de deux thèmes disjoints |
| Cartes / sections (Control Panel) | `#2A2A2A` | `#2A2735` avec liseré `#3A3548` | Garde le contraste, ajoute une teinte de marque |
| Accent signature (bordures actives, titres, sélection) | — (inexistant) | Or `#E8C44A` (repris tel quel de la tray icon) | Un seul accent doré cohérent tray ↔ UI, pas une nouvelle couleur inventée |
| Accents par action (attaque, esquive, saut...) | Déjà bons (`#58D68D`, `#F4D03F`, `#E74C3C`, `#9B59B6`, `#E67E22`, bleus `#5DADE2`/`#2E86C1`) | **Conserver tels quels** | Ils remplissent déjà le rôle "palette vive et modulaire" du vrai jeu — ne pas les uniformiser vers une seule teinte de marque |
| État succès (mode Tutoriel) | Vert générique | Garder vert, mais aligner sur un vert "Frozen Forest"-like `#4CAF7D` si retouché | Optionnel, faible priorité |
| État échec | Rouge générique | Garder tel quel | Le rouge d'alerte ne doit pas être touché par la marque, c'est un signal fonctionnel |

## 5. Typographie

- Ne pas migrer tout le texte vers une police custom (risque de lisibilité +
  dépendance à une police tierce à embarquer). Recommandation : **une seule
  police d'accent**, réservée aux titres/labels courts (nom de mode, nom de
  combo, titres d'onglet, badge tray), dans un style rond/gras proche de la
  piste "Eras Bold" identifiée en recherche — par exemple une police système
  déjà bold/ronde comme **Segoe UI Black** ou **Segoe UI Semibold** en
  majuscules, sans dépendance externe à installer.
- Le corps de texte (historique de coups, labels de touches, listes) reste
  en Segoe UI standard : c'est déjà lisible, ne pas y toucher.
- Effet "italique dynamique" (cf. logo Brawlhalla) réservable uniquement au
  badge de mode ("Mode : Tutoriel") ou au nom de combo affiché en mode
  Tutoriel — jamais sur du texte qu'on doit lire vite pendant un combo.

## 6. Formes et motifs

- **Motif "écusson"** (nameplate/killplate/scoreplate du vrai jeu) à
  réutiliser pour : le badge de mode temporaire, l'indicateur de série
  ("streak") en mode Tutoriel, et la bordure du panneau principal en mode 1 —
  un cadre légèrement en écusson (coins supérieurs arrondis plus prononcés
  que les coins inférieurs, ou un petit chanfrein en haut) plutôt que le
  rectangle à coins uniformément arrondis actuel.
- Garder les pastilles d'action du mode Tutoriel **telles quelles**
  (icônes carrées sans fond, coins légèrement arrondis) — c'est un choix
  déjà itéré 3 fois avec retours utilisateur explicites (voir CLAUDE.md,
  section "logo/"), ne pas revenir dessus.
- Introduire une **bordure dorée fine (1-2px, `#E8C44A` à faible opacité
  ~40-50%)** sur l'élément actif/sélectionné (combo active dans la liste,
  mode actuellement affiché dans le cycle) — signature de marque discrète,
  cohérente avec la tray icon, sans dégradé ni glow.
- Ne pas introduire de texture/pattern de fond (grain, motif répété) : le
  jeu original en a peu dans son UI de HUD, et ça nuirait à la
  transparence/lisibilité par-dessus le jeu.

## 7. Application par fenêtre

- **Overlay mode 1 (Historique)** : fond bleu-nuit au lieu de noir neutre,
  accent doré sur le séparateur vertical entre cluster de touches et
  historique (actuellement `#33FFFFFF` uni). Couleurs d'action inchangées.
- **Overlay mode 2 (Grandes flèches)** : même traitement de fond ; le badge
  rond bleu/rouge (`#3498DB`/`#E74C3C`) reste tel quel, c'est un signal
  d'état fonctionnel (repos/pressé), pas un élément de marque.
- **Overlay mode 3 (Tutoriel)** : bannière du haut avec liseré doré fin en
  bordure basse (comme un "nameplate"), reste du contenu inchangé (pastilles
  déjà validées, barre de tolérance jaune `#F4D03F` conservée).
- **ControlPanelWindow** : fond bleu-nuit/violet sombre au lieu de gris
  neutre, titres d'onglet en police d'accent + soulignement doré sur
  l'onglet actif, cartes avec liseré `#3A3548`. C'est la fenêtre où la
  marque peut s'exprimer le plus, car jamais vue en jeu.
- **ComboEditorWindow** : même fond que Control Panel (cohérence), chips
  d'action avec liseré doré léger au survol pour signaler l'interactivité,
  reste identique sinon (le système de puces "Écouter" a déjà été validé
  après plusieurs itérations, ne pas y toucher).
- **Tray icon** : déjà alignée, à garder comme référence de la nouvelle
  identité (c'est elle qui fixe le doré `#E8C44A`, pas l'inverse).

## 8. Ce qu'on ne touche pas

- Le système de couleurs par action (`KeyBindConfig.cs` defaults) — déjà
  dans l'esprit "coloré et lisible" du jeu, changer risquerait de casser des
  associations couleur↔action que l'utilisateur reconnaît déjà à l'œil.
- Les icônes d'action du mode Tutoriel (`Assets/Icons/`) — validées après
  plusieurs allers-retours explicites, aucune retouche de forme.
- Le comportement (click-through, hooks, ComboRunner...) — ce plan est
  strictement visuel (couleurs, forme, typo), zéro changement de logique.

## 9. Prochaines étapes suggérées (à valider avant tout code)

1. Valider la palette bleu-nuit/violet + doré proposée (section 4) — ou
   ajuster les teintes exactes sur maquette rapide avant implémentation.
2. Décider si la police d'accent (section 5) vaut le coup ou si Segoe UI
   Semibold partout suffit — impact visuel modeste vs effort d'intégration
   d'une police custom si on veut aller plus loin que les polices système.
3. Si validé : centraliser les couleurs dans un `ResourceDictionary`
   (`App.xaml`) au lieu des littéraux hardcodés actuels, pour que ce
   changement (et les suivants) ne demande plus de toucher 3 fichiers
   `.xaml.cs` à chaque fois — travail de refactor pur, pas de nouveau
   comportement.
4. Prototyper d'abord sur `ControlPanelWindow` (zéro risque de gêner le jeu)
   avant de toucher à l'overlay lui-même.

## Sources (recherche DA Brawlhalla)

- brawlhalla.fandom.com/wiki/UI_Themes
- brawlhalla.wiki.gg/wiki/Category:Color_Scheme_Palettes
- steamcommunity.com/app/291550/discussions/0/613956964592284270 (retours HUD)
- github.com/Perkelo/Brawlhalla-health-hud-Enhanced
- brawlhalla.fandom.com/wiki/Settings
- dafont.com/forum/read/425234 (identification police, non officiel)
- biborg.com/work/ubisoft-brawlhalla-brand-identity (rebrand officiel)
- colorswall.com/palette/97401
- brawlhalla.wiki.gg/wiki/Weapon_Skins
- tumblr.com/theartofbrawlhalla
