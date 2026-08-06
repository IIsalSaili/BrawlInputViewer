# Plan — détecter ce qui se passe **à l'écran / en jeu**, pas seulement les inputs

> Étude de faisabilité, **aucun code écrit**. Rédigée le 2026-08-06 à la demande
> de l'utilisateur : « comment détecter à l'écran les moves qui sont effectués
> plutôt que se baser uniquement sur les inputs clavier, potentiellement via un
> modèle de classification ».
>
> Ce document tranche dans cet ordre : (1) quel est le vrai manque, (2) quelles
> routes techniques existent pour le combler, (3) laquelle mérite d'être
> construite en premier, (4) ce que coûterait vraiment le modèle de vision
> demandé. Tout ce qui est affirmé ici sur le format des fichiers du jeu a été
> **vérifié en exécutant du code sur tes propres fichiers**, pas repris d'un
> README (voir §3.2) — vu l'historique du projet sur les données non sourcées
> (CLAUDE.md, « Version 9 » / « Correctif Faux »).

---

## 0. Verdict en une page

**La demande est légitime : l'app ne sait aujourd'hui que ce que tu as *appuyé*,
jamais ce qui est *sorti*.** C'est une vraie limite, pas un détail : elle
plafonne la valeur du mode Tutoriel (`ComboRunner` valide un combo qui n'a
peut-être jamais touché) et empêche toute la partie « true combo vs string » du
plan `plan_split_combo.md`.

**Mais le modèle de classification d'animation n'est pas la première brique à
construire.** Classé par (valeur × probabilité de succès) / coût :

| # | Route | Ce que ça donne | Coût | Risque | Verdict |
|---|---|---|---|---|---|
| **R1** | OCR du HUD (dégâts, stocks) | « le coup a touché / pour combien » en temps réel | **faible** (2-4 j) | très faible | **à faire en premier** |
| **R2** | Parsing des `.replay` | vérité terrain complète post-match (inputs frame par frame, KO, perso, arme) | moyen (RE partielle nécessaire, §3.2) | nul (fichier local) | **à faire en second** |
| **R3** | `-writestats` (option officielle du jeu) | stats de fin de match écrites par le jeu lui-même | faible **si** ça marche hors spectateur | nul | **à tester avant R2** (30 min de test) |
| **R4** | Classifieur d'animation (la demande) | « quel move est sorti » image par image | **très élevé** (voir §4) | élevé (perf, dataset, dérive à chaque patch) | phase 3, **seulement si R1+R2 ne suffisent pas** |
| **R5** | Lecture mémoire du jeu | tout, parfaitement | moyen | **inacceptable** (anti-ban) | **exclu définitivement** |

Raison courte du classement : **R1 et R2 répondent à 80 % de la question réelle
(« est-ce que mon combo a marché ? ») pour 10 % du coût de R4**, et R2 fournit en
prime le jeu de données étiqueté sans lequel R4 est de toute façon impossible à
entraîner sérieusement.

---

## 1. Le problème réel : `input ≠ move`

Aujourd'hui, la seule source de vérité de l'app est `Core/KeyboardHook.cs` /
`Core/GamepadHook.cs` → `MainWindow` → `ComboRunner.Feed(...)`. Le moteur
raisonne donc sur *des intentions*, jamais sur *des faits de jeu*. Les cas où
les deux divergent, tous réels en jeu :

1. **Le coup ne sort pas** — le personnage est encore en récupération, en
   hitstun, en esquive : l'input part dans le vide (ou dans le buffer), l'app
   valide quand même l'étape.
2. **Ce n'est pas le move attendu** — `Att. légère` au sol ≠ en l'air (nLight vs
   nAir). L'app ne sait pas si tu es au sol. C'est déjà admis explicitement
   dans le Parcours (`FullyValidatedByApp = false`, chapitres 1 et 3).
3. **Ce n'est pas la bonne arme** — les combos préréglés sont *par arme*
   (`Combo.Weapon`), mais un personnage a 2 armes + le désarmé. Le même bouton
   produit trois moves différents. L'app ne sait pas laquelle tu tiens.
4. **Le coup sort mais ne touche pas** — whiff complet, adversaire qui esquive.
   L'app affiche un ✓ et incrémente la série. C'est le cas le plus trompeur
   pédagogiquement : on répète et on « maîtrise » un combo qui ne connecte
   jamais.
5. **Le coup touche mais l'adversaire pouvait s'échapper** — c'est exactement la
   distinction true combo / string de `plan_split_combo.md`, aujourd'hui non
   modélisée et non mesurable.
6. **L'orientation** — déjà contournée par heuristique (`_mirroredDirections`
   dans `ComboRunner`), justement parce que l'app ne voit pas dans quel sens le
   perso regarde.
7. **La faisabilité liée au %** — `Combo.DamageNote` / `MinDex` décrivent des
   conditions (« marche jusqu'à ~40 % ») que l'app ne peut ni vérifier ni
   rappeler au bon moment, faute de connaître le % adverse.

**Conclusion de cadrage : la question la plus utile n'est pas « quel move est
sorti ? » mais « est-ce que ça a touché, pour combien, et dans quel état
j'étais ».** Le move exact est un moyen, pas la fin. Ça change complètement le
classement des solutions (§3).

---

## 2. Taxonomie des signaux, par valeur décroissante

| Signal | Valeur pour l'app | Où le lire |
|---|---|---|
| **% de dégâts adverse (et sa variation)** | Détecte le hit, mesure le combo réel, valide/invalide un preset, alimente true-combo vs string | **HUD**, coin haut-droit — chiffres officiels opt-in (§3.1) |
| **Nombre de stocks** | Fin de séquence, KO confirmé | HUD (icônes) / replay |
| **Légende adverse** | Contextualise la mesure (poids/gravité), info que l'app n'a par aucun autre moyen | **HUD**, vignette de personnage — corrélation avec les 69 portraits déjà embarqués (§3.1.1b) |
| **Arme tenue** | Lève l'ambiguïté n°3 | Sprite du perso (dur) — ou choix déclaré par l'utilisateur (gratuit) |
| **Au sol / en l'air** | Lève l'ambiguïté n°2 | Position verticale du sprite vs plateformes (dur) |
| **Move exact joué** | Confort, diagnostic fin | Classifieur d'animation (§4) |
| **Frame data exacte (startup/recovery)** | Analyse pro | Hors de portée de la vision ; existe côté wiki/fichiers du jeu |

Le tableau se lit de haut en bas : **les deux premières lignes sont dans un HUD
à position fixe, en gros caractères, sur fond stable. Les trois suivantes sont
dans un sprite de 40-80 px qui bouge, tourne, clignote et est masqué par des
effets.** Tout l'écart de coût entre R1 et R4 est là.

---

## 3. Les routes, en détail

### 3.1 R1 — Lire le HUD (recommandé en premier)

**Le point qui rend ça viable** : Brawlhalla a ajouté (patch de sept. 2024) une
option **« Damage Numbers »** — un affichage opt-in, dans le menu joueur en haut
à droite, qui montre **le chiffre exact de dégâts** à côté de la barre de vie de
chaque joueur. Ce n'est donc pas de l'estimation de teinte (blanc → jaune →
orange → rouge → noir), c'est un **nombre lisible**, à une position fixe, dans
une police fixe, sur un fond de HUD opaque.

Conséquences techniques :

- **Pas de modèle ML nécessaire.** 10 gabarits de chiffres (un par glyphe),
  corrélation normalisée sur une ROI de quelques centaines de pixels. Ça tourne
  en < 1 ms/frame en CPU pur, sans ONNX, sans GPU, sans dataset.
- **Robustesse** : le seul aléa est l'échelle (résolution de l'écran) et le
  thème de couleur du joueur. Les deux se règlent avec un **calibrage en 3
  clics** (« encadre la zone du HUD une fois »), qu'on peut mémoriser dans
  `settings.json` comme `MonitorIndex` l'est déjà.
- **Ce que ça débloque immédiatement** :
  - `ComboRunner` peut passer de « bonnes touches » à « bonnes touches **et**
    dégâts encaissés dans la fenêtre » → un vrai signal de réussite ;
  - mesure du **dégât total réel** d'un combo → validation empirique des ~90
    combos préréglés (dont on sait qu'ils ne sont pas 100 % de confiance,
    CLAUDE.md Version 17), sans dépendre d'un post Reddit ;
  - base concrète pour le champ `IsTrueCombo` de `plan_split_combo.md` : un
    combo qui a touché en entier N fois sans interruption, à partir de tel %.
- **Limite honnête** : ça détecte le *résultat*, pas la *cause*. Un dégât dans
  la fenêtre pourrait venir d'autre chose (2v2, pièges de map). À restreindre
  au 1v1/training pour la mesure, comme l'app le fait déjà pour d'autres
  hypothèses.

**Prérequis produit** : demander à l'utilisateur d'activer « Damage Numbers » en
jeu (une case). Sans ça, il reste la lecture de la **couleur** de la barre
(blanc/jaune/orange/rouge/noir), qui donne une résolution grossière (~5 paliers)
— suffisant pour « ça a touché », pas pour « combien ».

#### 3.1.1 Précisions actées après relecture (2026-08-06)

Trois points confirmés/ajoutés par l'utilisateur, qui simplifient encore la
phase 1 :

**(a) « Ça a touché » ne demande même pas d'OCR.** Détecter qu'un nombre a
*changé* est strictement plus simple que de le lire : une différence de pixels
sur la ROI du HUD adverse suffit. Ça découpe la phase 1 en deux livrables
indépendants, dont le premier est quasi gratuit :

- **1a — détection de hit** : delta de pixels sur la ROI → événement
  `DamageDealt(confidence)` sans montant. Une poignée d'heures de travail, aucun
  gabarit, aucune police à reconnaître. **C'est déjà 80 % de la valeur** (l'app
  cesse de valider des combos qui n'ont jamais touché).
- **1b — montant exact** : OCR par gabarits par-dessus, seulement pour chiffrer
  les dégâts et alimenter la mesure de faisabilité par %.

Découpage important parce qu'il rend la phase 1 **non bloquante** : même si
l'OCR se révèle capricieux (échelles, thèmes de couleur), 1a fonctionne seul.

**(b) L'icône de personnage du HUD identifie la légende — et l'app a déjà les
69 images.** `Assets/Legends/*.png` contient les 69 portraits, tous du même
gabarit et de la même source depuis la Version 20 (CLAUDE.md). Une corrélation
sur la vignette du HUD donne donc la légende **de l'adversaire** — information
que l'app n'a aujourd'hui par aucun moyen (elle ne connaît que le personnage
*que tu as déclaré* dans la Dashboard). Ça permet, à terme, de contextualiser
la mesure (un combo qui passe sur un poids plume ne passe pas forcément sur un
poids lourd).

Réserve à garder en tête : le portrait affiché peut être teinté par le
**costume/skin** choisi, ce qui dégrade la corrélation. **Convention assumée
plutôt que problème technique** : en contexte d'entraînement, on demande le skin
par défaut (comme on demande déjà d'activer « Damage Numbers » et de jouer en
fenêtré). Si la corrélation est en dessous d'un seuil, on n'affiche rien —
règle générale du §6 : *un signal incertain se tait*.

**(c) Le % permet de mesurer « jusqu'où ça connecte », pas « c'est un true
combo ».** Nuance à ne pas perdre, parce qu'elle décide de ce qu'on a le droit
d'écrire à l'écran. Le knockback grandit avec le %, donc une suite qui enchaîne
à 0 % décroche à partir d'un certain seuil : **ça, c'est directement mesurable**
(« ton combo a cessé de connecter à partir de ~45 % ») et c'est exactement ce
que `Combo.DamageNote` décrit aujourd'hui en texte libre non vérifié.

En revanche, « l'adversaire ne pouvait pas s'échapper » n'est **pas** mesurable
contre une cible qui ne cherche pas à s'échapper : un mannequin d'entraînement
immobile ne fait ni DI ni esquive. Contre lui, on prouve que **la suite
connecte**, pas qu'elle est **inesquivable** — soit précisément la frontière
true combo / string de `plan_split_combo.md`. Formulation correcte côté UI :
« connecte jusqu'à ~45 % (mesuré sur cible passive) », jamais « true combo
confirmé ». Pour trancher l'inesquivabilité il faudrait un adversaire qui essaie
réellement — donc de la mesure en match réel, sur plusieurs tentatives.

### 3.2 R2 — Parser les `.replay` (vérité terrain, post-match)

**Ce que j'ai vérifié moi-même sur ta machine**, pas lu dans un README :

- Tu as **549 fichiers `.replay`** dans `C:\Users\ilias\BrawlhallaReplays`,
  version de jeu 10.08 / 10.09.
- Le format est bien : **zlib** (en-tête `78 DA`) → **XOR** avec une clé de 64
  octets → flux **de bits** MSB-first. J'ai décodé un de tes fichiers réels
  (`[10.09] SmallWorld'sEnd (7).replay`, 29 197 octets décompressés) : la clé
  XOR publique de 2022 **fonctionne toujours** en 10.09.
- Ce qui a changé depuis les parsers publics (qui ciblent la 9.08) : le flux
  commence par un **`u32` de version** (valeur **268** sur ton fichier, contre
  253 en 9.08) et les tags d'état font **4 bits** (et non 3). Avec cette
  correction, j'ai extrait proprement `playlist=26688
  "PlaylistType_2v2Unranked_DisplayName"` et `level=233`.
- **Ce qui ne marche pas encore** : le bloc `PlayerData`/`GameSettings` a dérivé
  (un ou plusieurs champs ajoutés depuis la 9.08). J'ai testé
  automatiquement 270 hypothèses simples (nombre de champs de `GameSettings`,
  champs ajoutés autour de `connection_time` / du bloc héros, `avatar_id` en 16
  ou 32 bits) : **aucune ne recolle**. Il y a donc une vraie petite passe de
  reverse-engineering à faire (estimation : une demi-journée à une journée, avec
  un excellent oracle — on connaît les noms de joueurs, les légendes jouées et
  les maps de nos propres matchs).

**Ce que le format contient une fois parsé** (structure confirmée par les
parsers publics et cohérente avec ce que j'ai décodé) :

- `inputs[entityId] = [{ timestamp, inputState }]` — **l'état des boutons
  horodaté, sous forme de bitmap 14 bits**, pour *tous* les joueurs (donc aussi
  l'adversaire) ;
- `deaths[]` (entityId + timestamp), `results` (score final), `length` ;
- les entités : nom, équipe, **`heroId` (la légende), `costumeId`, `stance`**,
  skins d'armes ; plus `levelId`, réglages de partie.

**Ce que ça vaut pour l'app** :

- **Une vérité terrain gratuite et exacte** pour valider tout le reste : on peut
  rejouer nos propres logs clavier contre le replay du même match et mesurer
  l'écart. C'est aussi **la seule façon honnête de calibrer un futur modèle de
  vision** (§4.4).
- Un **débrief post-match** : combos réellement tentés, KO, tendances de
  l'adversaire. Un outil tiers fait déjà exactement ça (BRAT, §10) — preuve que
  c'est faisable, pas une spéculation.
- **La sémantique du bitmap 14 bits n'est documentée nulle part** — mais on n'a
  pas besoin de la deviner : l'app **connaît déjà nos propres appuis horodatés**
  (`AppState.SessionLog`). Il suffit de corréler notre log avec les inputs du
  replay du même match pour **déduire empiriquement quel bit = quel bouton**.
  C'est le genre de sourcing qui ne peut pas être halluciné, contrairement aux
  combos.

**Limites** : post-match uniquement (le fichier est écrit à la fin), donc aucun
feedback en direct ; et le format casse potentiellement à **chaque patch** du
jeu (c'est exactement ce qui vient d'arriver aux parsers publics). Un parser
maison doit donc être **tolérant à l'échec** : si la version est inconnue, on
n'affiche rien, on ne plante pas.

### 3.3 R3 — `-writestats` : laisser le jeu écrire lui-même les stats

Brawlhalla a une option de lancement officielle **`-writestats`** qui écrit des
statistiques de match en JSON dans `BrawlhallaStatDumps` et `BrawlhallaStatsLive`
(le nom du second dossier suggère une écriture **pendant** le match). Des outils
de streaming l'exploitent déjà pour afficher des stats de fin de match
(dégâts infligés/subis, etc.).

**Pourquoi c'est en tête de liste malgré une doc pauvre** : si ça fonctionne, on
obtient des données **produites par le jeu lui-même**, sans OCR, sans RE, sans
ML, et sans la moindre zone grise vis-à-vis de l'anti-cheat — le jeu écrit un
fichier, on le lit.

**Le doute à lever, et il est sérieux** : la doc communautaire dit que ça
enregistre les matchs **que l'utilisateur spectate**. Si c'est limité au mode
spectateur, ça ne sert pas pour un entraînement solo. **C'est un test de 30
minutes** (ajouter l'option de lancement, jouer un match d'entraînement, aller
voir si un fichier apparaît et ce qu'il contient) et il doit être fait **avant**
d'investir dans R1 ou R2, parce qu'un résultat positif change le plan.

### 3.4 R4 — Le classifieur d'animation (la demande d'origine)

Traité en détail au §4 : faisable sur le papier, très coûteux en pratique,
et — point important — **il ne répond pas directement à la question qui compte**
(« est-ce que ça a touché »), il ne fait que nommer le move.

### 3.5 R5 — Lecture mémoire / injection : exclu

Lire la mémoire du processus Brawlhalla donnerait tout (état d'animation, %,
positions, hitboxes) de façon parfaite. **C'est exclu définitivement** : ça
tombe exactement dans ce qu'Easy Anti-Cheat surveille (inspection de processus,
lecture de mémoire de jeu), et ça contredit la doctrine du projet depuis le
départ (CLAUDE.md : « aucune injection dans le processus du jeu »). Même chose
pour le déchiffrement des fichiers `.swz` du jeu (techniquement documenté
publiquement, mais c'est de la manipulation d'assets du jeu — hors doctrine).
**Ne pas rouvrir cette porte, même « juste en lecture ».**

---

## 4. Faisabilité détaillée du modèle de classification

### 4.1 La capture d'écran elle-même : résolu, avec des contraintes connues

- **API** : `Windows.Graphics.Capture` (WGC) est la bonne option — elle partage
  des textures GPU via DWM, sans copie CPU, conçue pour de la capture continue
  faible latence. Alternative : Desktop Duplication (DXGI). Coût
  d'intégration en WPF/.NET 8 non négligeable : il faut cibler
  `net8.0-windows10.0.x`, faire l'interop COM (`IGraphicsCaptureItemInterop`),
  un device D3D11 et un `DispatcherQueueController` de longue durée.
- **Deux contraintes héritées, déjà connues du projet** :
  - le jeu en **plein écran exclusif** n'est pas capturable proprement — mais
    c'est *déjà* la limite de l'overlay actuel (documentée dans CLAUDE.md),
    donc l'utilisateur est forcément en fenêtré/borderless ;
  - WGC affiche historiquement une **bordure jaune** de capture (désactivable
    sur les builds récents de Windows) — à vérifier, c'est un irritant visuel
    en jeu, pas un blocage.
- **Coût** : capturer une **ROI** (le HUD, ou une boîte autour du perso) à
  15-30 Hz est marginal. Capturer le plein écran à 60 Hz **pendant que le jeu
  tourne** prend du GPU au jeu — inacceptable pour un outil dont la promesse est
  de ne pas gêner. Toute la conception doit être « petite ROI, basse fréquence ».

### 4.2 Le vrai mur : la combinatoire visuelle de Brawlhalla

Ce n'est pas un problème de « faire un CNN », c'est un problème de **couverture
de l'espace des apparences**. Pour un move donné, l'image varie selon :

- **69 légendes** × plusieurs **costumes/skins** (dont des skins qui changent
  totalement la silhouette) ;
- **15 armes** × **skins d'arme** (l'arme est souvent l'élément le plus
  discriminant… et le plus repeint) ;
- **~30 moves par arme** (3 lights, 3 aériens, 3 signatures, récupération,
  ground pound, throw…) — et c'est justement l'axe qu'on veut classifier ;
- le **zoom dynamique de la caméra** (Brawlhalla dézoome quand les joueurs
  s'écartent : le perso peut faire 120 px de haut ou 30) ;
- les **VFX** (traînées, impacts, particules) qui masquent le sprite au moment
  précis où l'info est la plus utile ;
- les **fonds de map** très variés, souvent contrastés ;
- **2 à 4 personnages** à l'écran, qu'il faut distinguer (lequel est moi ?).

Ajouté à ça : le jeu tourne à **60 fps** et une attaque légère dure quelques
frames de startup + actifs. Pour distinguer « nLight » de « sLight », il faut
attraper les **bonnes 3-6 frames**, donc capturer à 30-60 Hz, donc classifier à
30-60 Hz — ce qui multiplie le coût de perf du §4.1.

**Comparaison honnête avec l'état de l'art** : les travaux publics les plus
proches portent sur **Super Smash Bros. Melee**, un jeu avec **26 personnages
fixes, sans skins arbitraires, sans zoom aussi agressif, et avec un corpus vidéo
massif déjà annoté**. Résultats publiés : un détecteur de personnages maison
(« SmashNet ») à **68 % de précision** sur 4 personnages ; les modèles à
80-90 % de réussite ne travaillent **pas sur les pixels** mais sur des
**replays Slippi déjà décodés** (données d'inputs, exactement l'équivalent de
notre R2). **Le signal est clair : même dans un jeu plus simple, la vision brute
plafonne bas, et ce qui marche, c'est la donnée structurée.**

### 4.3 Le problème du dataset — et l'astuce (avec son piège)

Il n'existe **aucun jeu de données étiqueté de moves Brawlhalla**. Il faudrait
le créer. Étiqueter à la main quelques milliers de frames par move est hors de
proportion avec ce projet.

**L'astuce qui rend la chose envisageable** : l'app **connaît déjà ce que tu
appuies**. On peut donc capturer, en salle d'entraînement, des séquences où
**l'étiquette vient gratuitement du hook clavier** (auto-labellisation faible) :
tu joues 20 minutes avec une légende / une arme, l'app enregistre
`(frames, input, contexte déclaré)`.

**Le piège logique, qu'il faut énoncer clairement** : ces étiquettes sont
exactes *seulement dans les cas où input = move* — c'est-à-dire précisément les
cas où on n'a **pas besoin** du modèle. Les cas intéressants (le coup ne sort
pas, mauvaise arme, aérien vs sol) sont **mal étiquetés par construction**. Un
modèle entraîné là-dessus apprend à prédire ton clavier, pas le jeu.

Ça ne condamne pas l'approche, mais ça impose un protocole plus lourd :
capture en conditions **contrôlées** (salle d'entraînement, une arme, sol
uniquement, sans adversaire), puis **revue manuelle des désaccords** entre le
modèle et le clavier (apprentissage actif) — c'est-à-dire exactement le travail
d'annotation qu'on espérait éviter, en plus petit volume.

### 4.4 Budget performance

À titre de repère public : un petit CNN type SqueezeNet en ONNX Runtime tourne
autour de **7 ms** par image en CPU sur un i9, et des modèles « moyens »
descendent à ~12-18 ms après optimisation (formes fixes, fusion, réglage des
threads). C'est **compatible avec 30 Hz**, à condition :

- de travailler sur une **petite ROI** (128-224 px), pas sur l'écran entier ;
- d'exécuter sur **CPU** (ou DirectML) en acceptant de ne pas voler du GPU au
  jeu ;
- de ne classifier que **quand c'est utile** (une fenêtre de quelques centaines
  de ms après un input détecté), pas en continu. C'est le même principe que les
  polls existants du projet (`CheckAbandon` à 300 ms, `_autoHideCheckTimer` à
  500 ms).

Ce budget est tenable. **Ce n'est pas la perf qui bloque, c'est le dataset et la
maintenance.**

### 4.5 La maintenance, coût caché

Brawlhalla est patché en continu (nouvelles légendes, skins, retouches
d'animation, changements de HUD). Un modèle de vision est **couplé à
l'apparence du jeu** : chaque patch peut dégrader silencieusement la précision
— sans erreur, sans log, juste des résultats un peu faux. Pour un projet à un
seul mainteneur, c'est le risque le plus lourd, et il est permanent (contrairement
au coût d'entraînement, qui est ponctuel).

Un OCR de HUD (R1) subit le même risque mais **en version détectable** : si les
gabarits ne matchent plus, la confiance tombe à zéro et l'app peut le dire
(« zone HUD non reconnue, recalibrer »). C'est un argument de conception
important : **préférer les signaux dont on sait détecter la panne.**

### 4.6 Verdict sur R4

Faisable techniquement, mais :
- effort réaliste : **plusieurs semaines** (capture + dataset + entraînement +
  intégration + calibrage), contre 2-4 jours pour R1 ;
- précision attendue : **modeste** au vu de l'état de l'art sur un jeu plus
  simple ;
- valeur marginale **après** R1+R2 : faible, car l'essentiel de ce qu'on voulait
  savoir (ça a touché ? pour combien ? quelle légende/arme ?) est déjà couvert.

→ **À garder comme phase 3, conditionnée à un besoin qui survivrait à R1 et R2.**

---

## 5. Risque anti-ban et EULA

Cadre de référence du projet (CLAUDE.md) : lecture passive, aucune injection,
aucune automatisation. Situation de chaque route :

- **R1 (capture d'écran)** : même catégorie que OBS et les overlays de stream.
  EAC cible l'injection dans le jeu, la modification de fichiers et
  l'automatisation d'inputs — pas la lecture de l'image affichée. OBS est
  explicitement considéré comme compatible. **Risque réaliste : nul**, à
  condition de rester sur de la capture d'écran/fenêtre standard et de **ne
  jamais hooker le processus du jeu** (c'est précisément le mode « game capture »
  d'OBS qui pose des soucis de compatibilité avec EAC — l'app ne doit pas faire
  ça).
- **R2 (replays)** : lecture d'un fichier écrit par le jeu dans le dossier
  utilisateur. **Risque nul.**
- **R3 (`-writestats`)** : option de lancement officielle du jeu. **Risque nul.**
- **R4** : c'est R1 + du calcul local. Même statut que R1.
- **R5** : inacceptable, voir §3.5.

**Règle absolue à conserver, quelle que soit la route** : rien de tout ça ne doit
jamais **agir** sur le jeu. Détecter, afficher, mesurer — jamais envoyer un
input. Le jour où on afficherait « appuie maintenant » avec un timing calculé,
on reste du bon côté ; le jour où on l'appuie à la place du joueur, c'est un
cheat. La frontière est là et elle est nette.

---

## 6. Intégration dans l'app existante (esquisse)

Le principe directeur : **ne pas toucher `ComboRunner`.** Le moteur actuel est
stable, testé, et corrigé plusieurs fois sur des cas de jeu réels (miroir,
priorité verticale, tolérance saut). On ajoute une **source de signaux
parallèle**, pas une réécriture.

```
Core/Vision/                  (nouveau)
  IGameSignalSource.cs        interface : événements de jeu observés
  HudDamageSource.cs          R1 — OCR du HUD (ROI + gabarits)
  ScreenCapture.cs            WGC/DXGI, ROI, cadence réglable
Core/Replays/                 (nouveau)
  ReplayParser.cs             R2 — zlib + XOR + bitstream (voir §3.2)
  InputBitmapDecoder.cs       corrélation avec AppState.SessionLog
```

Contrat proposé, volontairement minimal :

```csharp
public enum GameSignalKind { DamageDealt, DamageTaken, StockLost, HudLost }

public readonly record struct GameSignal(
    GameSignalKind Kind, int Value, DateTime Timestamp, double Confidence);

public interface IGameSignalSource : IDisposable
{
    event Action<GameSignal>? Signal;
    bool IsCalibrated { get; }
    void Start();  void Stop();
}
```

Branchement côté `MainWindow` (là où `ComboRunner.Feed` est déjà appelé) :

- une étape validée par les inputs reste validée **exactement comme
  aujourd'hui** (aucune régression possible si la vision se trompe) ;
- si un `DamageDealt` arrive dans une fenêtre courte après l'étape, la pastille
  est **enrichie** (badge « ✔ touché · 17 dmg ») ;
- si aucun dégât n'arrive sur **tout** le combo, on affiche une note *non
  bloquante* en fin de tentative (« exécuté, mais rien n'a touché ») — jamais un
  échec : la vision n'a pas le droit de casser une série. C'est la même
  philosophie que le retrait du timing comme condition d'échec (CLAUDE.md), et
  que les leçons `FullyValidatedByApp = false` du Parcours.
- `Confidence` faible ou `HudLost` → on n'affiche rien du tout. **Le silence est
  le comportement par défaut d'un signal incertain.**

Réglages (onglet Général, section avancée) : activer/désactiver la lecture
d'écran, calibrer la zone HUD, choisir le slot joueur (P1..P4), cadence de
capture. Désactivé par défaut, comme `AutoHideEnabled`.

---

## 7. Roadmap proposée, avec critères d'abandon

**Phase 0 — 30 minutes.** Tester `-writestats` (R3) : lancer Brawlhalla avec
l'option, jouer un match d'entraînement solo, inspecter `BrawlhallaStatDumps` /
`BrawlhallaStatsLive`. → *Si le jeu écrit des stats exploitables hors mode
spectateur, tout ce qui suit se simplifie énormément.*

**Phase 1 — HUD (R1), découpée en deux (voir §3.1.1a).**

- **1a — ~1 jour.** Capture WGC d'une ROI + calibrage manuel + **détection de
  changement** de la zone de dégâts adverse → `DamageDealt` sans montant.
  Affichage purement additif dans le panneau Tutoriel (badge « ✔ touché »).
  *Critère d'abandon* : si le delta de pixels produit des faux positifs en
  situation réelle (animations du HUD, clignotements) qu'un simple seuil ne
  filtre pas, on arrête là — inutile d'aller vers l'OCR sur une base instable.
- **1b — +1 à 3 jours.** Gabarits de chiffres (montant exact) et corrélation de
  la vignette de légende adverse contre `Assets/Legends/*.png`.
  *Critère d'abandon* : si la lecture des chiffres n'atteint pas ~99 % de
  fiabilité sur un enregistrement de 10 minutes, on garde 1a seul (« a touché »
  sans montant) plutôt que d'afficher des dégâts faux.

**Phase 2 — 1 à 2 jours + RE. Replays (R2).** Finir le parsing 10.09 (§3.2),
décoder le bitmap d'inputs par corrélation avec `SessionLog`, écran de débrief
post-match. Bonus immédiat : validation empirique des ~90 combos préréglés.
*Critère d'abandon* : si le format se révèle plus retors que prévu (> 2 jours
de RE), on garde uniquement l'en-tête (légende, map, durée, KO), déjà lisible.

**Phase 3 — conditionnelle. Vision fine (R4).** Uniquement si, après les
phases 1 et 2, il reste un besoin **formulé** que ni le HUD ni les replays ne
couvrent. Commencer par le sous-problème **le plus rentable et le plus simple** :
**au sol / en l'air**, un classifieur binaire sur une petite ROI autour du
personnage — pas les 30 moves d'un coup.
*Critère d'abandon* : si le binaire sol/air ne dépasse pas ~90 % en conditions
réelles (zoom variable, VFX), la classification de move complète est hors de
portée et on arrête là.

---

## 8. Ce que ça débloque côté produit

- **`plan_split_combo.md` devient mesurable** : true combo vs string cesse
  d'être une étiquette déclarative pour devenir une observation (« sur tes 12
  tentatives, l'adversaire s'est échappé 5 fois entre l'étape 2 et 3 »).
- **Les presets deviennent vérifiables** — c'est le point le plus important au
  vu de l'historique du projet : au lieu de faire confiance à un post Reddit,
  l'app peut mesurer elle-même qu'un combo inflige bien X dégâts. C'est le seul
  chemin qui sort définitivement le projet du problème de sourcing.
- **Les stats par action** (`Models/ActionStat.cs`) passent de « j'ai appuyé au
  bon moment » à « ça a effectivement touché ».
- **Le Parcours** peut retirer certains `FullyValidatedByApp = false` (au moins
  ceux qui dépendent d'un dégât constaté).

---

## 9. Questions ouvertes (à trancher avant tout code)

1. ~~**Es-tu prêt à activer « Damage Numbers » en jeu** ?~~ → **Tranché**
   (2026-08-06) : oui, avec la même logique pour le **skin par défaut** en
   entraînement (§3.1.1b). Ces deux prérequis sont assumés comme des conditions
   d'usage de la feature, pas comme des contraintes à contourner. Note : la
   phase 1a ne dépend même pas de « Damage Numbers » (elle détecte un
   changement, pas un chiffre) — l'option ne devient nécessaire qu'en 1b.
2. **Sur quel mode s'entraîne-t-on** — salle d'entraînement solo, 1v1 contre
   bot, en ligne ? Ça détermine si les signaux HUD sont interprétables sans
   ambiguïté (le 2v2 rend la mesure de dégât beaucoup moins fiable).
3. **Le débrief post-match (R2) t'intéresse-t-il en soi**, ou seulement comme
   moyen de valider le reste ? La réponse change la priorité de la phase 2.
4. **Acceptes-tu un calibrage manuel** (encadrer une zone du HUD une fois par
   résolution) ? L'alternative — détection automatique de la zone — est un
   petit problème de vision en soi, pour un gain de confort limité.

---

## 10. Sources

Techniques / état de l'art :
- [SmashScan — réseaux de neurones sur Super Smash Bros. Melee](https://medium.com/@seft/smashscan-using-neural-networks-to-analyze-super-smash-bros-melee-a7d0ab5c0755)
- [Slippify: Parsing Super Smash Bros. Melee Frames (CS231n, Stanford)](https://cs231n.stanford.edu/2025/papers/text_file_840589945-Parsing_Super_Smash_Bros__Melee_Frames.pdf)
- [Object tracking in games using CNN (thèse, Cal Poly — « SmashNet », 68 %)](https://digitalcommons.calpoly.edu/cgi/viewcontent.cgi?article=3192&context=theses)
- [ssbmachine-learning (80-90 %, sur replays Slippi, pas sur pixels)](https://github.com/ZackMagnotti/ssbmachine-learning)
- [New Ways to do Screen Capture — Windows Developer Blog (WGC)](https://blogs.windows.com/windowsdeveloper/2019/09/16/new-ways-to-do-screen-capture/)
- [dotnet-window-capture (WGC en WPF/.NET)](https://github.com/mika-f/dotnet-window-capture)
- [ONNX Runtime C++ Inference — repères de latence CPU](https://leimao.github.io/blog/ONNX-Runtime-CPP-Inference/)

Brawlhalla — formats et outils (tous vérifiés ou testés, voir §3.2) :
- [brawlhalla-replay-reader (itselectroz) — clé XOR, bitstream, structure](https://github.com/itselectroz/brawlhalla-replay-reader)
- [brawlhalla-replay-parser (mrtz6, Rust) — version en tête, états sur 4 bits](https://github.com/mrtz6/brawlhalla-replay-parser)
- [BRAT — Brawlhalla Replay Analyzer & Tracker (true combos, punish rate, à partir des replays)](https://github.com/Anton1P/Workspace-BRAT)
- [post-game-stats-writer — exploitation de `-writestats`](https://github.com/BuildBot42/post-game-stats-writer/blob/main/README.md)
- [Launch options — Brawlhalla Wiki officiel (`-writestats`, 60 fps par défaut)](https://brawlhalla.wiki.gg/wiki/Launch_options)
- [Template:Frames — Brawlhalla Wiki officiel (frame data)](https://brawlhalla.wiki.gg/wiki/Template:Frames)
- [Annonce officielle des « Damage Numbers » (compte Brawlhalla)](https://x.com/Brawlhalla/status/1833568159602741477)

Anti-cheat :
- [Easy Anti-Cheat (Wikipédia) — périmètre de surveillance](https://en.wikipedia.org/wiki/Easy_Anti-Cheat)
- [OBS Capture Hook & EAC — pourquoi le « game capture » pose problème, pas la capture d'écran](https://wiki.brianturchyn.net/technology/obs-capture-hook-eac/)
