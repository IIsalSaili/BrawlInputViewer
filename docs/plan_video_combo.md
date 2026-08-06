# Plan — Visionner une vidéo qui montre le combo

Rédigé le 2026-08-05, révisé le 2026-08-05 suite à un retour de l'utilisateur
(voir §0.1). Objectif : pouvoir *voir* à quoi ressemble un combo en jeu, pas
seulement lire sa suite de pastilles d'inputs.

Ce document tranche 4 questions dans l'ordre, parce qu'elles se contraignent
les unes les autres :

1. **D'où vient le contenu, et qui le fournit ?**
2. **Quel format / quelle techno ?** → tranché : **GIF**, précisé au §1.
3. **Où ça s'affiche ?** (contrainte technique dure sur l'overlay, mais elle
   joue maintenant *en faveur* du GIF plutôt que contre)
4. **Quand / déclenché comment ?** (bouton, survol, auto)

---

## 0. Sourcing — qui fournit le contenu

### 0.1 Correction actée par l'utilisateur

La V1 de ce plan posait une limite ("l'app ne livre aucun lien/média
préchargé") calquée sur le problème des combos hallucinés (CLAUDE.md,
"Version 9"/"Correctif Faux") — mais cette limite ne s'applique pas ici :
dans ce cas précédent, *moi* j'inventais/paraphrasais une source sans la
vérifier. Ici, c'est l'**utilisateur lui-même** qui trouve les vidéos, les
regarde, et les convertit — il n'y a aucune donnée factuelle non vérifiée
puisque la vérification, c'est lui qui la fait en les regardant. Le risque de
sourcing ne s'applique donc pas, et je n'aurais pas dû l'invoquer par défaut.

**Décision actée : les combos préréglés de l'app (`WeaponComboPresets.cs`,
~90 combos) sont livrés avec un GIF préchargé**, comme les portraits de
légend (`Assets/Legends/*.png`) ou les icônes d'arme (`Assets/Weapons/*.png`)
le sont déjà — même mécanique, nouveau dossier `Assets/Combos/`. C'est
l'utilisateur qui fournira ces GIF (trouvés + convertis lui-même, voir §5
pour l'outil de conversion), pas moi qui les génère ou les source.

### 0.2 Ce que ça change concrètement

- Le §0/§4.4 de la V1 ("l'app n'affiche aucun lien en dur", "bouton chercher
  sur YouTube") disparaît comme *règle* — mais reste utile comme *raccourci*
  pratique tant que tous les combos n'ont pas encore leur GIF (voir §4.3).
- Les combos créés à la main par l'utilisateur (`ComboEditorWindow`) restent
  logiquement couverts par le même mécanisme d'attache que prévu : un GIF
  choisi au moment de la création/édition, copié à côté de l'exe (ceux-là ne
  peuvent pas être des ressources embarquées à la compilation puisqu'ils
  n'existent pas encore quand l'app est buildée).
- Deux catégories de stockage bien distinctes désormais (voir §2) :
  préchargé/embarqué (presets) vs déposé par l'utilisateur (combos perso).

---

## 1. Format — GIF, tranché

### 1.1 Pourquoi le GIF est maintenant le format principal (et plus un pis-aller)

Contrainte technique inchangée : `Windows/MainWindow.xaml` déclare
`AllowsTransparency="True"`. En WPF, une fenêtre en couches force le rendu
logiciel — `MediaElement`/WebView2/tout contenu D3D n'y apparaissent pas
(airspace WPF). Seul un `ImageSource` (donc un GIF décodé en frames WPF)
peut s'afficher **directement dans l'overlay, par-dessus le jeu**.

Avec la décision du §0.1, ce n'est plus une limite qu'on subit : c'est
maintenant le format qu'on veut de toute façon, puisqu'il est le seul à
pouvoir apparaître **là où ça sert le plus** — à côté des pastilles, en jeu,
sans sortir de Brawlhalla ni ouvrir de fenêtre séparée.

### 1.2 Décodage — éviter le piège de corruption

WPF n'anime pas les GIF nativement. Un décodeur maison
(`GifBitmapDecoder.Frames[i]` réaffiché tel quel) **corrompt l'affichage sur
la moitié des GIF réels** : les GIF optimisés stockent des frames
*partielles* avec des méthodes de *disposal* (garder le fond précédent,
l'effacer, etc.), qu'un décodeur naïf ignore.

Deux façons de s'en prémunir, complémentaires plutôt qu'exclusives :

- **Encoder proprement en amont** (côté outil de conversion, §5) : `ffmpeg`
  avec un flux de palette à deux passes (`palettegen`/`paletteuse`, détail
  §5.2) produit des GIF où le problème de disposal ne se pose quasiment plus
  en pratique pour ce type de contenu court/simple.
- **Décoder avec une lib éprouvée côté app** plutôt qu'un décodeur maison :
  `XamlAnimatedGif` (NuGet, `<Image gif:AnimationBehavior.SourceUri="..."/>`
  en XAML, ou pilotable en code derrière) gère le disposal correctement et
  fournit play/pause/vitesse sans qu'on réécrive un moteur de rendu GIF.
  Seule dépendance ajoutée par ce chantier — légère (~100 Ko), gérable en
  single-file publish.

### 1.3 Ce qui reste vrai de la comparaison précédente

Pour mémoire, les formats écartés comme *format principal* et pourquoi :

- **MP4/`MediaElement`** : ne s'affiche pas dans l'overlay transparent (même
  contrainte qu'avant). Reste une option pour un *futur* lot "clip personnel
  en pleine qualité dans une fenêtre séparée", mais hors scope de cette V1
  puisque la demande explicite est "on part sur des GIF".
- **YouTube embarqué (WebView2)** : même limite d'airspace + dépendance
  runtime + casse "un seul exe à copier" — non retenu.
- **Lien externe / navigateur** : reste utile comme filet pour les combos
  qui n'ont pas encore de GIF (§4.3), pas comme format principal.

---

## 2. Modèle de données et stockage

Deux catégories bien séparées, avec des mécaniques différentes :

### 2.1 Combos préréglés (`WeaponComboPresets`) — GIF embarqué à la compilation

- Fichier `Assets/Combos/<comboId>.gif`, un par combo preset
  (`preset-<arme>-<n>.gif`, même schéma d'Id que `combos.json`).
- Déclaré `<Resource Include="Assets\Combos\*.gif" />` dans le `.csproj`,
  chargé par pack URI — **exactement** le pattern déjà en place pour
  `Assets/Legends/*.png` (voir `MainWindow.LegendPortraitFileName`) et
  `Assets/Weapons/*.png` (`WeaponIconAssets.cs`). Rien de nouveau à inventer
  côté chargement, juste un troisième dossier du même genre.
- **Aucun champ JSON requis pour cette catégorie** : comme pour les
  portraits, la présence du fichier suffit (`ComboGifFileName(comboId)` →
  cherche le fichier → l'affiche s'il existe, se masque sinon). Ça règle
  d'un coup le risque relevé dans la V1 de ce plan (upsert de
  `ImportWeaponPresets` qui écraserait un champ JSON attaché à `Combo`) :
  un fichier de ressource statique n'est jamais touché par l'upsert
  runtime, il n'y a donc plus rien à protéger.

### 2.2 Combos créés par l'utilisateur — GIF déposé à côté de l'exe

- Un GIF choisi via file picker au moment de la création/édition
  (`ComboEditorWindow`), copié dans `media/<comboId>.gif` (copier, pas
  garder le chemin d'origine — un fichier déplacé/renommé ailleurs ne doit
  pas casser le lien).
- Référence stockée dans `combo_media.json`
  (`Dictionary<comboId, string fileName>`, via `Config/ComboMediaConfig.cs`,
  même pattern minimal que `StatsConfig`) — nécessaire ici puisque ces
  fichiers ne peuvent pas être des ressources compilées (ils n'existent pas
  au moment du build).

### 2.3 API `AppState` (unifiée pour les deux catégories)

```csharp
// Résout : Assets/Combos/<id>.gif embarqué si présent, sinon media/<id>.gif
// si combo_media.json le référence, sinon null. Le point d'entrée unique
// utilisé par tout affichage (Dashboard, overlay, fenêtre dédiée) — la
// distinction §2.1 vs §2.2 reste un détail interne à cette méthode.
public static Uri? ComboGifSource(string comboId);

public static void AttachComboGif(string comboId, string sourceFilePath); // copie + persiste + event
public static void RemoveComboGif(string comboId);                        // combos perso uniquement
public static event Action? ComboMediaChanged;
```

Même contrat que le reste de `AppState` : toute mutation persiste et lève un
event, pour que Dashboard/overlay/panneau restent synchro sans redémarrage.

---

## 3. Où ça s'affiche

### 3.1 Dans l'overlay, à côté des pastilles — l'emplacement qui justifie le GIF

Contrairement à la V1 (qui devait sortir de l'overlay faute de mieux), le
GIF peut maintenant vivre **directement dans `BuildMode3Panel`**
(`Windows/MainWindow.xaml.cs`), à côté du portrait de légend déjà affiché en
haut à gauche du panneau (`_legendPortraitImage`, ligne ~417) :

- Un second petit visuel, ex. sous le nom du combo ou à droite des
  pastilles — format compact (le panneau Tutoriel est volontairement discret
  par-dessus le jeu, pas question d'un gros pavé qui masque l'action).
- **Replié par défaut, dépliable** (un petit bouton ⏵/🎬 sur le panneau, ou
  via `OverlayControlBarWindow` — voir §4.1) : même en boucle, un GIF qui
  tourne en permanence dans le champ de vision pendant qu'on joue est une
  distraction, pas une aide. On le déplie pour se rafraîchir la mémoire entre
  deux tentatives, on le replie pour jouer.
- Nécessite `XamlAnimatedGif` (ou équivalent) pour la lecture — voir §1.2.

### 3.2 `DashboardWindow` — aperçu avant de lancer

Sous la liste de combos (`RefreshCombosList`, ligne ~715), le GIF du combo
sélectionné s'affiche directement dans la bande de prévisualisation (pas
besoin de fenêtre séparée ni de clic pour *voir* — contrairement au MP4 de la
V1, un GIF est léger et ne clignote pas au changement de sélection) :

```
┌──────────────────────────────────────────────────┐
│  [ GIF animé du combo sélectionné, en boucle ]   │
│                                    [Changer…] [✕] │  ← si présent
│  — ou —                                           │
│  "Pas encore de démo pour ce combo"   [Ajouter…] │  ← si absent
└──────────────────────────────────────────────────┘
```

Léger throttle recommandé quand même : ne (re)décoder le GIF que ~150 ms
après le dernier changement de sélection (évite de décoder pour rien en
survolant la liste au clavier/molette).

### 3.3 `ComboEditorWindow` — attacher/remplacer

Une ligne « Démo (GIF, optionnel) » sous le champ Description existant
(ligne ~82) : miniature + bouton « Choisir un GIF… » (file picker,
`.gif`) + « Retirer ». Copie vers `media/<comboId>.gif` à la sauvegarde
(§2.2). Cohérent avec le champ « Arme (optionnel) » déjà ajouté au même
endroit pour la même raison (rendre visible/éditable un attribut optionnel
du combo).

### 3.4 `ControlPanelWindow`, onglet Combos

Un bouton « Démo… » dans la rangée d'actions existante
(modifier/dupliquer/supprimer) qui ouvre le même mini-éditeur que §3.3, sans
dupliquer la logique.

---

## 4. Quand / déclenché comment

### 4.1 En jeu : replié par défaut, un geste explicite pour déplier

Cohérent avec la doctrine déjà en place pour `OverlayControlBarWindow`
("rien d'obligatoire ne passe uniquement par un raccourci", mais aussi
"masqué par défaut, un survol/clic pour révéler") : le GIF ne se déplie
jamais tout seul. Bouton dédié sur le panneau du mode Tutoriel (à côté du
nom du combo) + éventuellement un raccourci `Ctrl+Alt+`… si l'usage montre
que retourner à la souris casse le rythme (à évaluer après un premier test
en jeu, pas à décider a priori).

### 4.2 Dashboard : affichage direct, pas de clic nécessaire pour voir

Différence assumée avec le comportement en jeu (§4.1) : ici on est en phase
de préparation, pas en train de jouer, donc l'aperçu automatique ne distrait
personne — au contraire, c'est le moment où « à quoi ça ressemble » est la
question posée.

### 4.3 Filet pour les combos sans GIF pas encore fait

Tant que les ~90 combos preset n'ont pas tous leur GIF (l'utilisateur les
fournit au fil de l'eau, §0.1), l'état "pas encore de démo" de la Dashboard
(§3.2) garde un bouton **« 🔍 Chercher ce combo sur YouTube »** (requête
pré-remplie à partir de `Combo.Weapon`/`Legend`/`Name`, `Process.Start` du
lien de recherche) : utile en attendant, pas une doctrine permanente comme
dans la V1 de ce plan.

### 4.4 Marqueur dans la liste

🎬 dans le libellé de `RefreshCombosList` (même mécanique que le ✓ de
`Mastered`, ligne ~732) quand `AppState.ComboGifSource(comboId)` renvoie
quelque chose — pour repérer d'un coup d'œil quels combos ont déjà leur
démo.

---

## 5. L'outil de conversion — à concevoir ensemble (pas figé ici)

Demande explicite mais volontairement ouverte ("faudra juste me créer un
logiciel qui convertit proprement mais ça aussi on peut y réfléchir") — ce
qui suit est une proposition de départ, pas une spec verrouillée.

### 5.1 Le vrai problème à résoudre

Convertir un clip vidéo en GIF *correctement* n'est pas trivial : un export
naïf donne un fichier lourd (plusieurs dizaines de Mo pour quelques
secondes) avec une palette baveuse (256 couleurs globales mal choisies →
bandes de compression visibles, surtout sur les couleurs vives de
Brawlhalla). Vu que ~90 GIF vont être produits à la main par l'utilisateur,
la qualité et la taille de chacun comptent (poids cumulé du repo/exe,
lisibilité réelle du geste).

### 5.2 Approche technique recommandée : `ffmpeg`, palette à deux passes

`ffmpeg` n'est **pas présent sur le PATH de cette machine** (vérifié) — il
faudrait soit le bundler avec l'outil (~80 Mo, lourd pour un outil annexe),
soit documenter une install une fois (`winget install Gyan.FFmpeg`), soit
utiliser un binding managé (`Xabe.FFmpeg`, télécharge le binaire au premier
lancement). À trancher ensemble selon si cet outil doit lui aussi être
"un seul exe" ou si un prérequis d'install ponctuel est acceptable puisqu'il
ne tourne que sur la machine de l'utilisateur, pas distribué à un tiers.

Pipeline recommandé (deux passes, évite la palette baveuse d'un export
GIF naïf) :

```
ffmpeg -i clip.mp4 -vf "fps=15,scale=360:-1:flags=lanczos,palettegen" palette.png
ffmpeg -i clip.mp4 -i palette.png -filter_complex \
  "fps=15,scale=360:-1:flags=lanczos[x];[x][1:v]paletteuse" combo.gif
```

Paramètres à ajuster ensemble par essai (pas décidés à l'aveugle ici) :
`fps` (12-15 suffit pour lire un enchaînement de coups, au-delà ça alourdit
sans gain de lisibilité), largeur de sortie (le GIF s'affiche petit — en
jeu à côté des pastilles §3.1, en aperçu compact §3.2 — donc 320-400 px de
large est probablement largement suffisant, pas besoin de 1080p).

### 5.3 Forme de l'outil — options à trancher ensemble

- **Option A — mini-utilitaire séparé** (script/petit exe dédié, hors de
  `BrawlhallaOverlay.exe`) : ouvre un fichier vidéo, affiche un scrubber
  pour poser un point d'entrée/sortie (trim), un bouton "Convertir" qui
  shelle vers `ffmpeg`, résultat déposé directement dans
  `Assets/Combos/<comboId>.gif` du repo (ou `media/` pour un combo perso).
  Le plus simple à faire, complètement découplé de l'app principale.
- **Option B — intégré à `ComboEditorWindow`** : le bouton « Choisir un
  GIF… » du §3.3 devient « Importer une vidéo… » avec trim intégré,
  conversion à la volée. Plus confortable (un seul endroit), mais alourdit
  l'app principale d'une dépendance `ffmpeg` que la majorité des sessions
  (jouer avec l'overlay) n'utilise jamais.

Recommandation : **Option A**, un outil à part — cohérent avec le fait que
la conversion est un travail ponctuel de préparation de contenu (fait une
fois par combo, par l'utilisateur, hors session de jeu), pas une fonction du
runtime de l'overlay. Garde aussi `BrawlhallaOverlay.exe` libre de toute
dépendance `ffmpeg` pour les utilisateurs qui n'en ont pas besoin.

**Non tranché ici, à discuter à l'ouverture de ce lot** : nom/emplacement de
l'outil, install de `ffmpeg` requise vs bundlée, UI du trim (scrubber simple
vs aperçu image par image), traitement par lot (convertir plusieurs clips
d'affilée) si le volume de ~90 combos rend ça nécessaire.

---

## 6. Découpage en lots

### Lot 1 — plomberie + affichage, sans encore les GIF réels
- `Config/ComboMediaConfig.cs` (`combo_media.json`, combos perso), dossier
  `Assets/Combos/` + entrée `<Resource>` dans le `.csproj` (combos preset).
- `AppState.ComboGifSource/AttachComboGif/RemoveComboGif/ComboMediaChanged`.
- NuGet `XamlAnimatedGif` (ou équivalent retenu après un essai rapide).
- `DashboardWindow` : bande de prévisualisation (§3.2) + bouton "chercher
  sur YouTube" en filet (§4.3) + marqueur 🎬 dans la liste.
- `ComboEditorWindow` : champ "Démo (GIF)" (§3.3).
- Testable dès ce lot avec 1-2 GIF de test déposés à la main (n'importe quel
  GIF, pas besoin d'attendre l'outil de conversion pour valider la
  plomberie).

### Lot 2 — affichage en jeu (overlay)
- Bloc GIF repliable dans `BuildMode3Panel` (§3.1).
- Bouton/déclencheur pour déplier (`OverlayControlBarWindow` ou bouton sur
  le panneau lui-même — à choisir après un premier test visuel des deux).
- Vérifier concrètement (capture d'écran en jeu) que l'`Image`
  `XamlAnimatedGif` s'affiche bien dans la fenêtre `AllowsTransparency=True`
  — c'est l'hypothèse centrale de tout ce plan, à confirmer avant d'aller
  plus loin plutôt qu'après.

### Lot 3 — `ControlPanelWindow`
- Bouton "Démo…" dans l'onglet Combos, réutilise le mini-éditeur du Lot 1.

### Lot 4 — l'outil de conversion (§5)
- À planifier séparément une fois l'Option A/B et la question `ffmpeg`
  tranchées ensemble (§5.3) — c'est le plus gros morceau restant et il
  mérite sa propre session de conception plutôt qu'être improvisé à la
  suite des Lots 1-3.

### Contenu (les ~90 GIF eux-mêmes)
- Hors scope "code" : c'est un travail de production de contenu par
  l'utilisateur, au fil de l'eau, avec l'outil du Lot 4. Rien à bloquer
  côté app pour commencer — Lot 1 fonctionne avec zéro, un, ou 90 GIF
  indifféremment (état "pas encore de démo" géré proprement, §3.2).

---

## 7. Risques et points encore ouverts

| # | Question | Statut |
|---|---|---|
| 1 | `XamlAnimatedGif` s'affiche-t-il vraiment dans une fenêtre `AllowsTransparency=True` ? | **À vérifier en premier** (Lot 2), c'est l'hypothèse porteuse de tout le plan |
| 2 | `ffmpeg` bundlé, requis en install à part, ou binding managé ? | Ouvert, §5.2/5.3 |
| 3 | Outil de conversion : séparé (A) ou intégré (B) ? | Recommandation A, à confirmer |
| 4 | Taille/fps cible des GIF | À caler par essai réel, pas à l'aveugle |
| 5 | Poids cumulé de `Assets/Combos/` (~90 fichiers) sur la taille de l'exe publié | À surveiller une fois quelques GIF réels en main — actuellement `Assets/Legends`+`Assets/Weapons` sont déjà ~100 PNG, donc le mécanisme encaisse déjà ce volume ; le GIF est juste plus lourd à l'unité qu'un PNG |

**Non-objectifs** (inchangés) : pas de téléchargement/rehébergement de vidéo
YouTube sans conversion faite par l'utilisateur lui-même (question de CGU
sur le contenu de tiers, distincte du sourcing — à garder à l'esprit si les
clips source viennent de vidéos YouTube d'autrui plutôt que de ses propres
clips), pas d'analyse automatique d'une vidéo pour en déduire les inputs.

---

## 8. Vérification

Le projet n'a pas de tests automatisés (choix documenté). Comme pour les
chantiers précédents :

- `dotnet build -c Release` à 0 avertissement/erreur après chaque lot.
- Lot 1 : déposer un GIF de test dans `media/`, vérifier qu'il apparaît dans
  la Dashboard, redémarrer l'app, vérifier qu'il est toujours là.
- Lot 2, **test critique** : lancer l'overlay en jeu (ou par-dessus une
  fenêtre quelconque, borderless), déplier le bloc GIF, confirmer par
  capture d'écran qu'il s'anime réellement par-dessus le contenu en dessous
  (et pas juste dans un `Snapshot` qui masquerait un souci d'airspace).
- Test de non-régression : un combo preset avec GIF, réimporté via "Importer
  les 5 combos de cette arme" → le GIF doit rester affiché (comme prévu par
  construction au §2.1, mais à vérifier réellement plutôt qu'à supposer).
- Rappel de la règle de test du projet : ne jamais simuler de frappes
  clavier si le jeu est au premier plan, et demander avant.
