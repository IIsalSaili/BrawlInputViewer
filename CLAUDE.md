# Brawlhalla Input Overlay

Petite app WPF (.NET 8) qui affiche par-dessus Brawlhalla (ou n'importe quel jeu
en mode fenêtré/borderless) l'état des touches en temps réel + un historique
des coups joués dans l'ordre, en bas à gauche de l'écran.

## ⚠️ Piège de dossier à connaître

Il existe **deux dossiers presque identiques** sur le Bureau de l'utilisateur :

- `C:\Users\ilias\Desktop\BrawhallaButtonGui` (sans "h" après "Brawhalla")
  → **c'est ici que vit tout le code du projet** (ce fichier y compris).
- `C:\Users\ilias\Desktop\BrawhallahButtonGui` (avec un "h" en plus)
  → contient juste un `.git` vide. C'est le dossier que VS Code ouvrait par
  défaut au départ, ce qui a causé de la confusion ("pourquoi je vois aucun
  fichier à gauche ?").

Le repo Git (`.git`) est dans le dossier **avec le "h"**, mais le code est dans
celui **sans le "h"**. Si tu veux versionner ce projet avec git, il faudra soit
initialiser un nouveau `.git` ici, soit déplacer les fichiers dans l'autre
dossier. Ce n'est pas fait pour l'instant — vérifie avec l'utilisateur avant
de bouger quoi que ce soit.

## Build & run

```powershell
cd C:\Users\ilias\Desktop\BrawhallaButtonGui
dotnet build -c Release
.\bin\Release\net8.0-windows\BrawlhallaOverlay.exe
```

Le SDK .NET 8 a été installé via `winget install Microsoft.DotNet.SDK.8`.
Si une nouvelle session PowerShell ne trouve pas `dotnet`, rafraîchir le PATH :

```powershell
$env:PATH = [System.Environment]::GetEnvironmentVariable("PATH","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH","User")
```

## Structure des dossiers

Le code est rangé par rôle plutôt que d'avoir tous les fichiers en vrac à la
racine :

```
Models/    Combo.cs, ComboStep.cs, KeyBind.cs, OverlaySettings.cs, ActionStat.cs
           (POCOs sérialisés en JSON, pas de logique)
Config/    ComboConfig.cs, KeyBindConfig.cs, OverlaySettingsConfig.cs,
           StartupConfig.cs (chargement/écriture des .json à côté de l'exe,
           + StartupConfig pour la clé de registre "lancer au démarrage"),
           StatsConfig.cs (persistance des stats de précision par action,
           stats.json), WeaponComboPresets.cs (bibliothèque de 5 combos par
           arme sur les 15 armes du jeu, importables à la demande — voir
           Architecture)
Core/      AppState.cs (état partagé), KeyboardHook.cs (hook clavier bas niveau),
           GamepadHook.cs (polling manette XInput, même statut passif que le
           hook clavier), ComboRunner.cs (moteur de validation du mode Tutoriel)
Windows/   MainWindow.xaml(.cs), ControlPanelWindow.xaml(.cs),
           ComboEditorWindow.xaml(.cs) — les 3 fenêtres WPF
docs/      plan.md (doc de design du mode Tutoriel / refonte UI)
```

`App.xaml`, `App.xaml.cs`, `BrawlhallaOverlay.csproj`, `app.manifest`,
`CLAUDE.md` et `.gitignore` restent à la racine (fichiers d'entrée/projet).
Aucun namespace par dossier : tout reste dans `BrawlhallaOverlay` — WPF ne
lie pas `x:Class` à l'emplacement physique du fichier, donc c'est purement un
rangement, sans impact sur la compilation ni sur le code appelant.

## Architecture

- **Core/KeyboardHook.cs** — hook clavier global bas niveau (`WH_KEYBOARD_LL`
  via `SetWindowsHookEx`). Capture les touches même quand Brawlhalla a le
  focus. Expose deux events `KeyDown(int vkCode)` / `KeyUp(int vkCode)`.
- **Core/GamepadHook.cs** — polling passif de l'état des boutons manette via
  `XInputGetState` (même statut anti-ban que le hook clavier : lecture d'état
  en boucle, aucune injection, aucune automatisation). Boutons digitaux
  seulement (pas les sticks analogiques). Expose `ButtonDown(int)` /
  `ButtonUp(int)` avec des codes synthétiques décalés de `0x10000` au-dessus
  de la plage des vrais virtual-key codes (0-255), pour pouvoir les mélanger
  aux touches clavier dans `KeyBind.VirtualKeyCodes` sans collision — un même
  `KeyBind` peut avoir des `Keys` (clavier) et des `GamepadButtons` (manette),
  les deux déclenchent la même action. Sans manette branchée, le polling
  échoue silencieusement (aucun coût notable). `KeyBindConfig.RecomputeVirtualKeyCodes`
  résout les noms de boutons (`GamepadHook.Buttons`) en plus des noms de touches.
- **Models/KeyBind.cs** — modèle d'une action trackée : `Action` (nom affiché),
  `Keys` (liste de noms de touches .NET, une action peut avoir plusieurs
  touches, ex. esquive sur Shift *et* flèche bas), `Label` (texte sur le
  bouton/touche), `Symbol` (glyphe affiché dans l'historique), `Color`,
  `Group` (`"Movement"` ou `"Action"`), `Slot` (position dans le cluster
  directionnel : `Up`/`Left`/`Down`/`Right`, pour `Group="Movement"` seulement).
- **Config/KeyBindConfig.cs** — mapping par défaut (voir plus bas) +
  chargement/écriture de `keybinds.json` (généré à côté de l'exe au premier
  lancement, dans `bin\Release\net8.0-windows\`). Si tu changes le schéma du
  modèle `KeyBind`, **supprime le `keybinds.json` généré** pour qu'il se
  régénère avec les nouveaux champs (sinon `System.Text.Json` charge un objet
  incomplet).
- **Core/AppState.cs** — état applicatif partagé (classe statique) entre
  l'overlay, le panneau de contrôle et l'icône de tray : listes `Binds` /
  `Combos`, `Settings`, mode actif, verrouillage, combo active, enregistrement
  en cours. Toute mutation persiste immédiatement sur disque (via `Config/*`)
  et lève un event (`BindsChanged`, `CombosChanged`, `SettingsChanged`,
  `ModeChanged`, `LockChanged`, `ActiveComboChanged`, `RecordingChanged`) pour
  que les deux fenêtres restent synchronisées sans redémarrage. Au chargement,
  si `combos.json` contient déjà des combos, la première devient active
  automatiquement (sinon le mode Tutoriel affichait "Aucune combo" alors
  qu'il y en avait — bug corrigé, voir Historique des décisions).
- **Models/Combo.cs / ComboStep.cs**, **Core/ComboRunner.cs** — modèle et
  moteur du mode Tutoriel : une `Combo` est une séquence de `ComboStep`
  (ensemble d'`Action` à jouer ensemble + fenêtre de tolérance de timing
  `MinDelayMs`/`MaxDelayMs`, ignorés sur la 1ère étape puisqu'il n'y a pas
  d'étape précédente à chronométrer). `ComboRunner.Feed(...)` est appelé à
  chaque "coup joué" détecté par l'overlay (logique pure, sans UI) et lève
  `StepSucceeded` / `StepFailed(index, ComboFailReason)` / `ComboCompleted` /
  `ComboReset`. `ComboFailReason` distingue `WrongInput` (mauvaise touche) de
  `Timeout` (bonne touche mais hors délai) pour que l'UI affiche une couleur
  différente selon la cause (diagnostic immédiat, sans devoir recouper avec
  l'historique). `MatchMode.Strict` (défaut, exposé dans `ComboEditorWindow`) :
  tout input hors combo réinitialise direct. `MatchMode.IgnoreExtraneous` :
  ignore le mouvement pur hors combo au lieu de reset. Tolérance de
  récupération (`AttackRecoveryLockMs`, 180ms, indépendante du `MatchMode`) :
  un bouton d'attaque (`Att. légère`/`Att. forte`) différent de celui attendu,
  pressé dans les 180ms suivant une attaque précédente, est ignoré au lieu de
  casser la combo — en jeu le personnage est encore en animation de
  récupération et cet input est de toute façon avalé par le moteur, ce n'est
  donc pas une vraie faute de timing du joueur. Ne s'applique ni au tout
  premier input d'une tentative, ni aux actions hors attaque (`Saut`,
  `Esquive`, `Lancer`, `Taunt`, directions), qui gardent le comportement
  strict existant. **Le timing (`MinDelayMs`/`MaxDelayMs`/`DefaultToleranceMs`)
  n'est plus une condition d'échec** : `ComboRunner.Feed` valide une étape dès
  que les bonnes actions sont pressées, quel que soit le délai écoulé — le
  jeu réel n'exige jamais un rythme aussi précis pour qu'une combo touche.
  Ces champs pilotent seulement la barre de tolérance visuelle (indicatif de
  rythme dans l'overlay), plus aucun reset ni `ComboFailReason.Timeout` n'est
  déclenché pour une entrée juste mais tardive/trop rapide ; seule une
  mauvaise touche fait échouer la combo. `ComboFailReason.Timeout` reste dans
  l'enum pour compat (chargement de vieux combos.json / mapping couleur UI)
  mais n'est plus jamais levé.
- **Models/ActionStat.cs / Config/StatsConfig.cs** — compteurs cumulés
  (`Successes`/`Failures`) par action, alimentés à chaque étape de combo
  réussie/ratée (mode Tutoriel), persistés dans `stats.json`, consultables via
  le bouton "Voir les stats de précision par action" de l'onglet Combos —
  aide à cibler l'entraînement sur les actions qui font le plus échouer les
  combos.
- **Config/ComboConfig.cs** — persistance de `combos.json` (même pattern que
  `KeyBindConfig`), sans valeurs par défaut : l'app démarre à 0 combo, c'est
  à l'utilisateur d'en créer (enregistrement en direct ou éditeur manuel).
- **Config/WeaponComboPresets.cs / Combo.Weapon** — bibliothèque de 5 combos
  par arme, sur les 15 armes actuelles du jeu (Épée, Lance, Marteau, Blasters,
  Katars, Hache, Arc, Faux, Épée à deux mains, Gantelets, Canon, Orbe,
  Lance-fusée, Bottes de combat, Chakram — liste vérifiée sur
  liquipedia.net/brawlhalla/Weapons ; "Fists" et "Battle Sammich" n'existent
  plus dans le jeu actuel). Chaque combo vient de séquences réellement
  documentées par des guides communautaires (gamespecifications.com,
  bluestacks.com, dashfight.com pour Bottes de combat, mygamingtutorials.com
  pour Chakram), citées dans la `Description` de chaque combo — voir le
  correctif "Version 9" plus bas : une première version de ce fichier avait
  des combos **inventées** (mêmes 2-3 patterns génériques recopiés sur les 15
  armes), quasiment aucune ne fonctionnait en jeu. Ne jamais réintroduire de
  combo non sourcée dans ce fichier. Traduction vers le vocabulaire d'actions
  de l'app (voir le docstring en tête de fichier pour le détail) : une
  variante latérale/basse/haute d'une attaque = direction (`Gauche`/`Droite`/
  `Haut`/`Bas`) pressée avec `Att. légère`/`Att. forte` dans un même
  `ComboStep` ; un `Saut` explicite remplace un saut réel ou un Gravity
  Cancel (confirmé équivalent par les sources elles-mêmes pour le Canon) ;
  `Haut`+`Att. forte` représente la Récupération ; un double `Bas` seul
  représente un Ground Pound ; une direction seule représente un Dash. Chaque
  combo a un `Id` stable (`preset-<arme>-<n>`) et `AppState.ImportWeaponPresets`
  fait un **upsert** (pas juste un skip-si-présent) : si le contenu d'une
  combo préréglée change suite à une correction, la réimporter met à jour son
  contenu au lieu de laisser une ancienne version fausse bloquée dans
  `combos.json`. `Combo.Weapon` (vide pour les combos perso) sert de filtre :
  `AppState.Settings.TrainingWeaponFilter` + `AppState.FilteredComboIndices`/
  `CycleCombo`/`SetTrainingWeaponFilter` restreignent la liste cyclée
  (`Ctrl+Alt+K`) et affichée (mode Tutoriel, onglet Combos du panneau de
  contrôle) à l'arme choisie. Onglet Combos : sélecteur d'arme + bouton
  "Importer les 5 combos de cette arme" (`ControlPanelWindow.BuildCombosTab`),
  la liste elle-même est indexée sur le sous-ensemble filtré
  (`_visibleComboIndices` fait le lien avec les index absolus de
  `AppState.Combos` pour modifier/dupliquer/supprimer/réordonner sans se
  tromper de combo).
- **Models/OverlaySettings.cs / Config/OverlaySettingsConfig.cs** — réglages
  persistés dans `settings.json` : échelle, opacité, position de l'overlay
  (`BottomLeft`/`BottomRight`/`TopLeft`/`TopRight`/`Free`), mode par défaut au
  démarrage, modes favoris inclus dans le cycle `Ctrl+Alt+P`, option "garder
  la série de réussites même après une combo ratée".
- **Config/StartupConfig.cs** — active/désactive le lancement automatique au
  démarrage de Windows via la clé de registre utilisateur
  `HKCU\...\Run` (pas besoin de droits admin, pas de tâche planifiée).
- **Models/KeyBind.cs** — modèle d'une action trackée : `Action` (nom affiché),
  `Keys` (liste de noms de touches .NET, une action peut avoir plusieurs
  touches, ex. esquive sur Shift *et* flèche bas), `Label` (texte sur le
  bouton/touche), `Symbol` (glyphe affiché dans l'historique), `Color`,
  `Group` (`"Movement"` ou `"Action"`), `Slot` (position dans le cluster
  directionnel : `Up`/`Left`/`Down`/`Right`, pour `Group="Movement"` seulement).
- **Config/KeyBindConfig.cs** — mapping par défaut (voir plus bas) +
  chargement/écriture de `keybinds.json` (généré à côté de l'exe au premier
  lancement, dans `bin\Release\net8.0-windows\`). Si tu changes le schéma du
  modèle `KeyBind`, **supprime le `keybinds.json` généré** pour qu'il se
  régénère avec les nouveaux champs (sinon `System.Text.Json` charge un objet
  incomplet). Supporte aussi des **profils multiples** : le profil "Défaut"
  reste sur `keybinds.json` (compatibilité), les profils additionnels vivent
  dans `keybinds.<nom>.json`. `ListProfiles()` énumère les fichiers présents,
  `LoadProfile`/`SaveProfile`/`DeleteProfile` opèrent sur un profil nommé.
  `AppState.Settings.ActiveProfile` mémorise le profil actif entre sessions ;
  `AppState.SwitchProfile`/`DuplicateProfileAs`/`DeleteProfile` gèrent le
  changement à chaud (recharge `_binds`, lève `BindsChanged`). Géré depuis
  l'onglet Général du panneau de contrôle — utile pour un mapping
  AZERTY/QWERTY différent selon le PC, ou un jeu de touches distinct par
  contexte (solo/équipe). Les combos et l'apparence restent partagés entre
  profils (seules les touches changent).
- **Windows/MainWindow.xaml / .xaml.cs** — la fenêtre overlay (in-game
  uniquement, aucune UI de configuration dedans — ça, c'est le rôle de
  `ControlPanelWindow`) :
  - Transparente, `Topmost`, sans bordure, cachée du alt-tab/taskbar
    (`WS_EX_TOOLWINDOW`). Ne s'affiche pas par-dessus un jeu en plein écran
    *exclusif* (seulement fenêtré/borderless) — limitation Windows normale,
    pas un bug.
  - **Click-through par défaut** (`WS_EX_TRANSPARENT`) : les clics passent au
    jeu en dessous. `Ctrl+Alt+O` bascule en mode "déverrouillé" (déplaçable à
    la souris, cesse d'être click-through) pour repositionner l'overlay, puis
    reverrouille.
  - **Trois modes d'affichage**, basculés via `Ctrl+Alt+P` (cycle parmi les
    modes marqués favoris dans les réglages, 3 par défaut) ou explicitement
    depuis le panneau de contrôle :
    1. **Historique** (mode par défaut) : cluster directionnel ZQSD en
       triangle inversé (Haut en haut, Gauche/Bas/Droite en dessous) → gros
       boutons d'action (Saut, Att. légère, Att. forte, Esquive, Lancer,
       Taunt) → séparateur vertical → historique des coups joués.
    2. **Grandes flèches** : badges ronds géants collés aux 4 bords de
       l'écran (mouvement) + gros boutons d'attaque légère/forte au centre en
       haut + rangée d'icônes secondaires (Saut/Esquive/Lancer/Taunt) —
       pensé pour être lu d'un coup d'œil sans lire de texte.
    3. **Tutoriel** : bandeau centré en haut de l'écran (position fixe,
       ignore le réglage de position général) affichant la combo active sous
       forme de pastilles reliées par des flèches (étape à venir grisée,
       étape courante jaune, étape réussie verte, échec = flash rouge si
       mauvaise touche / orange si trop lent, puis reset), plus le compteur
       de série et l'historique de touches en dessous. Se branche sur
       `ComboRunner` (voir plus haut). Sous la pastille courante (à partir de
       la 2ème étape), une fine barre de progression se vide en temps réel
       jusqu'à la fenêtre de tolérance (`MaxDelayMs`), pour visualiser le
       temps restant avant un échec par timeout, avec un texte en secondes
       en dessous (`0.4s`…) lu depuis la valeur animée de la barre elle-même
       pour rester synchronisé avec ce que l'œil voit. Réglages associés
       (onglet Général) : `SoundEnabled` (bip de succès/échec/complétion via
       `System.Media.SystemSounds`), `QuizMode` (masque en `?` toutes les
       étapes pas encore jouées — dès le début si la combo n'a pas démarré,
       façon "mode quiz inversé" : mémoriser avant d'exécuter plutôt que lire
       puis jouer — `Ctrl+Alt+I` ou le bouton "Révéler" du panneau de
       contrôle révèlent 3s), `ChainCombos` + `ChainStreakThreshold` (session
       guidée façon playlist : passe à la combo suivante de la liste ~900ms
       après avoir atteint N réussites *consécutives* sur la combo active,
       pas juste après la 1ère — une combo qui atteint ce seuil est marquée
       `Mastered` de façon persistante, affichée avec un ✓ dans la liste de
       l'onglet Combos). Chaque combo persiste aussi ses stats de
       performance cumulées (`BestStreak`/`TotalCompletions`/`TotalAttempts`
       dans `combos.json`, mises à jour via `AppState.SaveCombosQuiet()` qui
       ne lève pas `CombosChanged` pour ne pas reconstruire le `ComboRunner`
       actif — donc perdre sa série en cours — à chaque coup joué).
    Un badge discret ("Mode : Tutoriel") s'affiche ~1.5s à chaque changement.
  - Repositionnée selon `Settings.Position` à chaque changement de taille
    (`SizeChanged`), ou suit le drag à la souris en mode déverrouillé (qui
    bascule automatiquement `Position` sur `Free`).
  - Chaque touche/bouton s'allume (opacity 0.25 → 1.0 sur sa `SolidColorBrush`,
    ou changement de couleur bleu→rouge pour les flèches du mode 2) tant
    qu'elle est physiquement enfoncée.
  - **Historique** : une ligne par appui, symbole + nom entre parenthèses
    (ex. `⚡ (Att. légère)`), les plus récentes en bas, limité à 12 lignes
    (`MaxHistoryEntries`), fondu d'entrée de 120ms. Panneau partagé entre le
    mode Historique et le mode Tutoriel (déplacé d'un conteneur à l'autre,
    pas dupliqué).
  - **Anti-spam / anti auto-répétition** :
    - L'auto-répétition Windows (rester appuyé → rafale de `WM_KEYDOWN`) est
      filtrée via un `HashSet<int> _pressedVks` : un seul événement d'historique
      par appui physique réel.
    - Le **mash/spam volontaire** (ex. marteler attaque légère pendant un combo)
      est fusionné : si la même action revient dans les 500ms
      (`MergeWindow`), on incrémente un compteur sur la dernière ligne
      (`Att. légère ×3`) au lieu d'empiler des lignes, avec un petit flash
      visuel (`Pulse`). Une action différente entre-temps casse la fusion.
  - **Icône de tray** (`System.Windows.Forms.NotifyIcon`) : clic gauche
    ouvre/donne le focus au panneau de contrôle, clic droit propose un menu
    (verrouiller, changer de mode, ouvrir le panneau, quitter).
- **Windows/ControlPanelWindow.xaml.cs** — fenêtre de réglages classique
  (bordée, dans la taskbar, séparée de l'overlay), ouverte via `Ctrl+Alt+U` ou
  le tray. Navigation latérale à 5 onglets : **Général** (verrouillage,
  reset position, lancement auto Windows, mode par défaut, modes favoris,
  option "garder la série", sons du mode Tutoriel, enchaînement de combos,
  mode révision, + section "Session" avec compteur d'actions/minute et export
  CSV du journal de coups de la session en cours — `AppState.SessionLog`),
  **Touches** (édition Label/Keys/Symbol/Color par action, bouton "Écouter"
  pour réassigner en appuyant sur une touche — refuse à la sauvegarde les
  touches invalides ou en double entre deux actions), **Combos** (liste,
  enregistrer/créer/dupliquer/modifier/supprimer/réordonner, exporter/importer
  une combo au format JSON pour la partager entre installations, bouton de
  consultation des stats de précision par action), **Apparence** (échelle,
  opacité, position, longueur de l'historique de coups), **À propos** (rappel
  des raccourcis). Tout passe par `AppState`, donc les changements sont
  immédiats sur l'overlay.
- **Windows/ComboEditorWindow.xaml.cs** — éditeur manuel d'une combo (fenêtre
  modale) : étapes en texte libre (actions séparées par `+`, délais min/max
  en ms). Vérifie à la sauvegarde que chaque action tapée correspond bien à
  une action existante dans `KeyBindConfig` (sinon message d'erreur listant
  les actions valides — une combo avec un nom d'action mal orthographié ne
  pourrait jamais être validée en jeu sans ce garde-fou).

## Mapping clavier actuel (défauts dans `KeyBindConfig.Defaults`)

| Action        | Touche(s)         | Groupe   | Slot  |
|---------------|--------------------|----------|-------|
| Gauche        | Q                  | Movement | Left  |
| Droite        | D                  | Movement | Right |
| Haut          | Z                  | Movement | Up    |
| Bas           | S                  | Movement | Down  |
| Saut          | Espace             | Action   | —     |
| Att. légère   | Flèche gauche      | Action   | —     |
| Att. forte    | Flèche droite      | Action   | —     |
| Esquive       | Shift gauche + Flèche bas | Action | —  |
| Lancer        | Flèche haut        | Action   | —     |
| Taunt         | G                  | Action   | —     |

⚠️ Important : Z/Q/S/D ne sont *que* des directions (le cluster "Haut/Gauche/
Bas/Droite"), **pas** le saut. Le saut est un bouton d'action séparé sur
Espace. Ça a fait l'objet d'une correction explicite de l'utilisateur — ne
pas réassigner "Saut" sur Z par erreur.

L'utilisateur peut éditer `keybinds.json` (à côté de l'exe) pour personnaliser
sans recompiler — noms de touches = valeurs de l'enum `System.Windows.Input.Key`
(`Left`, `Right`, `Up`, `Down`, `Space`, `LeftShift`, `Q`, `Z`, `S`, `D`, `G`, `L`…),
ou passer par l'onglet **Touches** du panneau de contrôle (avec validation).

## Raccourcis clavier globaux

Actifs partout (hook bas niveau), même jeu au premier plan. Tous préfixés
`Ctrl+Alt+` pour éviter toute collision avec les contrôles du jeu :

| Raccourci    | Action                                              |
|--------------|------------------------------------------------------|
| `Ctrl+Alt+O` | Verrouiller / déverrouiller l'overlay (déplaçable)    |
| `Ctrl+Alt+P` | Changer de mode (cycle parmi les modes favoris)       |
| `Ctrl+Alt+K` | Changer la combo active (mode Tutoriel)               |
| `Ctrl+Alt+R` | Démarrer / arrêter l'enregistrement d'une combo       |
| `Ctrl+Alt+U` | Ouvrir / donner le focus au panneau de contrôle       |
| `Ctrl+Alt+I` | Révéler temporairement (3s) la combo active en mode révision |

## Historique des décisions (contexte utilisateur)

- Demande initiale : un overlay façon "keyboard viewer" pour voir ses inputs
  par-dessus Brawlhalla en jeu. Confirmé par recherche web qu'aucun outil
  Brawlhalla-spécifique équivalent n'existait déjà.
- Question sur le risque de ban : conclusion que le hook clavier bas niveau
  (`WH_KEYBOARD_LL`, mode utilisateur, aucune injection dans le processus du
  jeu, aucune automatisation d'input) est dans la même catégorie que les
  overlays de streaming (OBS, Keypress Hero, etc.) et ne présente pas de
  risque réaliste avec Easy Anti-Cheat (EAC), qui cible l'injection dans le
  jeu / la modification de fichiers / l'automatisation, pas l'écoute passive
  d'un processus tiers.
- Version 1 : simple grille de touches statiques qui s'allument (arrows +
  JKL/Space/G, contrôles par défaut Brawlhalla).
- Version 2 : remplacement complet par un historique de coups empilé
  (l'utilisateur voulait comprendre l'enchaînement des coups, pas juste l'état
  des touches) + remapping ZQSD/flèches/Shift.
- Version 3 (retour en arrière partiel) : l'utilisateur voulait **garder**
  l'affichage à l'ancienne (touches qui s'allument) *en plus* de l'historique,
  pas le perdre. D'où le layout actuel : cluster ZQSD en triangle (comme un
  clavier) + gros boutons d'action à gauche, historique à droite, séparés par
  une ligne verticale.
- Version 4 : fusion anti-spam dans l'historique (voir plus haut) pour que le
  mash d'une touche pendant un combo ne pollue pas la lecture des inputs.
- Version 5 : remaps successifs demandés par l'utilisateur (Chute→Bas, Lancer
  sur flèche haut, Esquive aussi sur flèche bas, puis Saut→Espace et Z→Haut
  simple direction).
- Version 6 : ajout des symboles (`Symbol`) dans l'historique — flèches pour
  les directions, pictogrammes pour les actions — affichés en plus du nom
  entre parenthèses, pas à la place (l'utilisateur veut toujours pouvoir lire
  le texte).
- Version 7 (mode Tutoriel + refonte UI, voir `docs/plan.md` pour le design
  complet) : ajout d'un 3e mode d'affichage "Tutoriel" avec combos
  personnalisées créées par l'utilisateur (pas les combos officielles de
  Brawlhalla — exclu volontairement), validées en temps réel par
  `ComboRunner`. Séparation nette overlay (in-game, épuré) / panneau de
  contrôle (`ControlPanelWindow`, fenêtre classique ouverte via le tray ou
  `Ctrl+Alt+U`) pour héberger tous les réglages (touches, combos, apparence,
  lancement auto Windows) sans polluer l'overlay. Raccourci de verrouillage
  déplacé de `Ctrl+Alt+L` à `Ctrl+Alt+O` à cette occasion.
- Audit de bugs (voir aussi git log) : correction de plusieurs bugs trouvés
  lors d'un audit complet — la combo active ne se sélectionnait pas
  automatiquement au démarrage/à la création (mode Tutoriel affichait "Aucune
  combo" à tort), aucune validation des touches en double ou des noms
  d'action inconnus dans une combo manuelle (échec silencieux et
  indiagnosticable). Ces garde-fous sont documentés dans les sections
  `AppState`/`ControlPanelWindow`/`ComboEditorWindow` ci-dessus.
- Restructuration : tous les fichiers (auparavant en vrac à la racine) rangés
  en `Models/`/`Config/`/`Core/`/`Windows/`/`docs/` par rôle (voir "Structure
  des dossiers" en haut de ce fichier).
- Audit de features (voir `docs/audit_features.md`) suivi d'une implémentation
  large des pistes jugées les plus solides, en excluant systématiquement tout
  ce qui présenterait un risque anti-ban (voir doctrine section 0 du document
  d'audit) : `ComboFailReason` (timeout vs mauvaise touche, couleurs
  distinctes), barre de tolérance visuelle sur l'étape courante, correction
  du bug de liste d'extras figée en mode 2 (itère maintenant sur les binds du
  groupe "Action" au lieu d'une liste de noms en dur), feedback sonore
  optionnel, enchaînement automatique de combos façon playlist, mode révision
  (masquage + révélation temporaire), stats de précision persistées par
  action, import/export de combo en JSON, longueur d'historique configurable,
  analytics de session (actions/minute + export CSV).
- Passe complémentaire (suite à un retour jugeant la passe précédente
  incomplète) : support manette XInput (`GamepadHook.cs`, boutons digitaux
  uniquement, codes synthétiques mélangés aux touches clavier), profils
  multiples de touches (`KeyBindConfig` profils nommés +
  `AppState.SwitchProfile`/`DuplicateProfileAs`/`DeleteProfile`), session
  guidée avec seuil de réussites consécutives et marquage "maîtrisée"
  persistant par combo, historique de performance persisté par combo
  (`BestStreak`/`TotalCompletions`/`TotalAttempts`), countdown chiffré sous
  la barre de tolérance, bouton "Révéler" pour le mode révision en plus du
  raccourci clavier. Reste volontairement exclu (hors du principe "outil
  100% local/passif", à ne faire que sur demande explicite) : intégration
  d'API externe Brawlhalla — voir section 2.5 de `docs/audit_features.md`.
- Version 8 (bibliothèque de combos par arme + tolérance de récupération) :
  ajout de `Config/WeaponComboPresets.cs` (5 combos par arme) et d'un sélecteur
  d'arme dans l'onglet Combos du panneau de contrôle (`Combo.Weapon` +
  `AppState.Settings.TrainingWeaponFilter`), qui filtre à la fois la liste
  affichée et le cycle `Ctrl+Alt+K`/mode Tutoriel sur l'arme choisie. Ajout d'une
  tolérance de récupération dans `ComboRunner` (`AttackRecoveryLockMs`) :
  un mauvais bouton d'attaque pressé juste après une attaque précédente est
  ignoré plutôt que de casser la combo, pour refléter que le personnage est
  encore en animation de récupération à ce moment-là en jeu (donc cet input
  ne "compte" pas vraiment) — ne s'applique qu'aux deux boutons d'attaque,
  pas aux autres actions (mouvement, saut, esquive, lancer, taunt).
- Retour immédiat après test en jeu : même avec la tolérance de récupération,
  les combos (surtout enregistrées, dont le `MaxDelayMs` par étape vient du
  rythme de l'enregistrement × 1.6) échouaient encore dès que le joueur allait
  un peu moins vite que sa propre démo — perçu comme "un timer avant chaque
  input" alors que le vrai jeu n'exige aucune précision de rythme pour qu'une
  combo touche. Le timing a donc été retiré comme condition d'échec dans
  `ComboRunner` (voir section ComboRunner ci-dessus) : seule une mauvaise
  touche fait maintenant échouer une combo, le timing ne sert plus qu'à la
  barre de tolérance visuelle (indicatif, plus un couperet).
- Version 9 (correction : combos hallucinées) : l'utilisateur a signalé que
  quasiment aucune combo de `WeaponComboPresets.cs` n'était faisable en jeu.
  Diagnostic confirmé : la V8 avait généré 5 combos par arme en recopiant 2-3
  patterns génériques ("dLight > nLight", "sLight > dLight"...) quasiment
  identiques sur les 15 armes, sans vérifier qu'ils correspondaient à de
  vraies chaînes de la moveset de chaque arme — inventé, pas sourcé, malgré
  la mention "combos scrap internet" du prompt original. `WeaponComboPresets.cs`
  a été entièrement réécrit à partir de vrais guides communautaires
  (gamespecifications.com, bluestacks.com, dashfight.com, mygamingtutorials.com),
  chaque combo citant sa source dans sa `Description`. Au passage, la liste
  des 15 armes elle-même était fausse : elle incluait "Fists" et "Battle
  Sammich", qui n'existent plus dans le jeu actuel (vérifié sur
  liquipedia.net/brawlhalla/Weapons) — remplacés par Bottes de combat et
  Chakram, absents de la V8. `AppState.ImportWeaponPresets` a été changé de
  "skip si déjà importé" à "upsert" pour que ce genre de correction se
  propage automatiquement à une réimportation au lieu de rester bloquée sur
  l'ancien contenu faux (l'utilisateur avait déjà importé les 5 combos Faux
  buggées avant la correction ; son `combos.json` a été corrigé directement
  en plus du fix de la source). Leçon retenue : ne jamais générer de données
  factuelles (combos, stats, listes de jeu) sans les sourcer réellement
  (WebSearch/WebFetch), même sous forme "d'approximation raisonnable" —
  l'utilisateur ne peut pas distinguer une approximation documentée d'une
  pure invention tant que ce n'est pas marqué comme telle, et ici ce n'était
  ni l'un ni l'autre : c'était juste faux.

## Pistes évoquées mais pas demandées/faites

- Profils multiples de configuration de touches — fait, voir section KeyBindConfig ci-dessus.
- Intégration d'une API externe Brawlhalla (stats de match) — écarté pour
  rester un outil 100% local sans dépendance réseau/ToS tiers, à ne faire que
  sur demande explicite (voir section 2.5 de `docs/audit_features.md`).
- Pas de tests automatisés — vérifications faites manuellement via capture
  d'écran + simulation d'appuis clavier (`keybd_event` par P/Invoke depuis
  PowerShell) pendant le développement. Attention si le jeu est au premier
  plan et en train d'être joué en vrai : ne jamais simuler de touches dans ce
  cas (ça part sur le système entier, pas juste sur l'app) — vérifier plutôt
  via un changement temporaire de `DefaultMode` dans `settings.json`, ou
  demander confirmation avant de tester.
- ~~Lancement automatique au démarrage de Windows~~ — fait (`StartupConfig.cs`
  + case à cocher dans l'onglet Général).
- ~~Icône dans la barre système~~ — fait (tray icon, voir `MainWindow.xaml.cs`).
