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

## Distribution (exe autonome pour un tiers)

Le build normal ci-dessus (`dotnet build -c Release`) est *framework-dependent* :
`bin\Release\net8.0-windows\BrawlhallaOverlay.exe` a besoin du runtime .NET 8
Desktop installé sur la machine (present en dev, pas forcément ailleurs) et
s'accompagne de 3 fichiers annexes obligatoires (`.dll`/`.deps.json`/
`.runtimeconfig.json`) — pas un simple exe à copier seul.

Pour donner l'app à quelqu'un sans lui demander d'installer .NET, publier en
self-contained single-file :

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Produit un seul fichier `bin\Release\net8.0-windows\win-x64\publish\BrawlhallaOverlay.exe`
(~73 Mo, runtime .NET embarqué) — c'est le seul fichier à distribuer (le
`.pdb` à côté n'est que des symboles de debug, pas nécessaire). Aucune
dépendance sur le reste du repo (images embarquées dans l'assembly via les
`<Resource>` du `.csproj`) ni sur .NET installé côté destinataire. L'app
régénère quand même `keybinds.json`/`combos.json`/`settings.json`/`stats.json`
à côté de l'exe au premier lancement, comme d'habitude. Cette commande ne
modifie pas le `.csproj` (pas de `RuntimeIdentifier` en dur) pour ne pas
changer le chemin de sortie du build de dev habituel.

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
Windows/   StartupWindow.xaml(.cs) (écran d'accueil, point d'entrée réel de
           l'app), MainWindow.xaml(.cs), ControlPanelWindow.xaml(.cs),
           ComboEditorWindow.xaml(.cs) — les 4 fenêtres WPF
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
  `CaptureSuspended` (+ `CaptureSuspendedChanged`, bascule `Ctrl+Alt+H`) coupe
  le traitement des touches côté `MainWindow` (allumage, historique,
  `ComboRunner.Feed`) sans désinstaller le hook ni fermer l'app — utile car
  `KeyboardHook`/`GamepadHook` captent globalement même hors du jeu (voir
  Version 10 de l'historique).
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
  une direction tenue en plus de ce qui est demandé par l'étape (extra de
  mouvement) fait échouer la combo, comme n'importe quel mauvais bouton
  d'action. `MatchMode.IgnoreExtraneous` : le mouvement pur hors combo est
  ignoré au lieu de faire échouer — un joueur garde souvent une direction
  enfoncée en enchaînant (ex. tenir Droite en sautant), ce n'est pas une
  faute dans ce mode. **Correctif** : `ComboRunner.Feed` a longtemps ignoré
  `Combo.MatchMode` (le sélecteur de l'éditeur se sauvegardait mais n'avait
  aucun effet, les deux modes se comportaient comme `IgnoreExtraneous`) —
  voir `docs/audit_features.md` §1.1 pour le diagnostic, corrigé depuis (le
  choix fait dans l'éditeur a maintenant un effet réel). Les boutons
  d'action (`Group == "Action"`), eux, sont toujours jugés à l'identique
  quel que soit le `MatchMode` : un mash/répétition du bouton qui vient de
  valider l'étape précédente (`_lastConsumedActionKeys`, ex. cliquer Saut 3
  fois pour caler son timing) est ignoré au lieu de casser la suite — pas
  une histoire de délai (contrairement à une ancienne fenêtre de tolérance
  de récupération chronométrée à 180ms qui existait dans une version
  antérieure du moteur et a depuis été remplacée par ce mécanisme basé sur
  l'identité du dernier bouton validé, sans notion de temps). **Le timing
  (`MinDelayMs`/`MaxDelayMs`/`DefaultToleranceMs`)
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
- **Config/WeaponComboPresets.cs / Combo.Weapon** — bibliothèque de combos
  d'arme génériques (non liés à un légend), sur 11 des 15 armes du jeu
  (Épée, Lance, Marteau, Blasters, Katars, Hache, Arc, Faux, Gantelets, Canon —
  Épée à deux mains/Orbe/Lance-fusée/Bottes de combat/Chakram n'ont
  volontairement aucun combo, voir plus bas). Contenu **entièrement réécrit**
  suite à un nouveau signalement de combos fausses (2ème occurrence après le
  correctif "Version 9"/"Correctif Faux" plus bas, cette fois sur la quasi
  totalité du fichier) : les combos viennent maintenant d'un post Reddit de
  true combos (non-esquivables) collé directement par l'utilisateur dans la
  conversation, avec seuil de Dex par combo — pas d'un guide web à
  fetch/paraphraser, donc pas de risque de citation fabriquée comme les fois
  précédentes. Le post ne couvrait que 11 armes ; les 4 autres ont
  volontairement 0 combo plutôt que de garder l'ancien contenu non vérifié à
  côté du contenu vérifié. Ne jamais réintroduire de combo non sourcée dans ce
  fichier. Traduction vers le vocabulaire d'actions de l'app (voir le
  docstring en tête de fichier pour le détail) : une variante latérale/
  basse/haute d'une attaque (Light **ou** Signature — voir bullet
  LegendComboPresets ci-dessous) = direction (`Gauche`/`Droite`/`Haut`/`Bas`)
  pressée avec `Att. légère`/`Att. forte` dans un même `ComboStep` ; un `Saut`
  explicite remplace un saut réel avant une attaque aérienne (nAir/sAir/dAir) ;
  `Esquive` représente un Gravity Cancel ; `Haut`+`Att. forte` représente la
  Récupération ; un double `Bas` seul représente un Ground Pound ; une
  direction seule représente un Dash ; XPivot/Reverse/Ledge cancel/Wall
  cancel (techniques de mouvement, pas des boutons) sont traduits par la
  séquence la plus proche, la technique exacte étant précisée en
  `Description` (l'app ne peut pas la valider). Le niveau de Dex minimum
  (mécanique que l'app ne modélise pas) est indicatif, en `Description`
  uniquement — aucun combo n'est bloqué selon un Dex. Chaque combo a un `Id`
  stable (`preset-<arme>-<n>`) et `AppState.ImportWeaponPresets` fait un
  **upsert** (pas juste un skip-si-présent) : si le contenu d'une combo
  préréglée change suite à une correction, la réimporter met à jour son
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
- **Config/LegendComboPresets.cs / Combo.Legend** — bibliothèque séparée de
  true combos propres à un légend précis (utilisant une attaque Signature
  exclusive à ce légend sur une arme donnée, ex. "Ada Blasters" : dLight >
  sSig). Même source Reddit et mêmes conventions de traduction que
  `WeaponComboPresets.cs` — Signature (nSig/sSig/dSig) est **le même bouton**
  que l'attaque forte générique en Brawlhalla (direction + `Att. forte`), ce
  n'est pas une action séparée à ajouter au mapping clavier. Une combo de
  légende dépend à la fois d'un légend ET d'une arme (`Combo.Legend` +
  `Combo.Weapon` tous les deux renseignés), certains légends ayant des combos
  sur plusieurs armes (ex. Cassidy sur Blasters et Marteau). `Id` stable
  (`preset-legend-<légend>-<n>`), upsert via `AppState.ImportLegendPresets`.
  Filtre dédié en plus (pas à la place) du filtre d'arme :
  `AppState.Settings.TrainingLegendFilter` + `FilteredComboIndices` combinent
  les deux filtres en ET (ex. "Ada" + "Blasters" ne montre que les combos Ada
  sur Blasters). Onglet Combos : sélecteur de légend + bouton "Importer les
  combos de ce légend", sous le sélecteur d'arme existant. Tous les légends
  du jeu n'ont pas de combo listé dans la source. `ComboEditorWindow` a aussi
  un sélecteur "Légend (optionnel)" à côté du sélecteur d'arme, même raison
  que ce dernier (voir plus bas, correctif §1.3) : une combo créée à la main
  doit pouvoir être rattachée à un légend pour rester visible sous un filtre
  de légend actif.
- **Models/OverlaySettings.cs / Config/OverlaySettingsConfig.cs** — réglages
  persistés dans `settings.json` : échelle, opacité, position de l'overlay
  (`BottomLeft`/`BottomRight`/`TopLeft`/`TopRight`/`Free`), mode par défaut au
  démarrage, modes favoris inclus dans le cycle `Ctrl+Alt+P`, option "garder
  la série de réussites même après une combo ratée". `MonitorIndex` (-1 =
  écran principal, défaut) choisit l'écran cible sur un setup multi-moniteur
  (onglet Apparence, `ControlPanelWindow`) : `MainWindow.GetTargetWorkArea`
  résout l'écran voulu via `System.Windows.Forms.Screen.AllScreens` au lieu
  de toujours utiliser `SystemParameters.WorkArea` (qui ne renvoie que
  l'écran principal Windows, indépendamment d'où tourne le jeu) — voir
  `docs/audit_features.md` §1.4. Conversion pixels→unités WPF approximée par
  le ratio de mise à l'échelle de l'écran principal (correct si tous les
  écrans partagent la même échelle DPI, cas le plus courant).
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
- **Windows/StartupWindow.xaml / .xaml.cs** — écran d'accueil, `StartupUri`
  réel de `App.xaml` (remplace `MainWindow.xaml` à ce rôle depuis la Version
  11, voir Historique). Fenêtre bordée classique (comme `ControlPanelWindow`,
  même palette bleu-nuit/or), pas l'overlay in-game : sélecteur Personnage
  (`LegendComboPresets.Legends`) → sous-filtre Arme dépendant du personnage
  (même logique que `ControlPanelWindow.BuildCombosTab`, dupliquée ici plutôt
  que partagée pour garder les deux fenêtres indépendantes — voir plus bas),
  portrait du personnage choisi si connu (`Assets/Legends/<Nom>.png`), liste
  des combos filtrées (`AppState.FilteredComboIndices`) avec en tête une
  option « Aucune combo sélectionnée (juste l'overlay) », bouton d'import des
  presets (`AppState.ImportCharacterPresets`/`ImportWeaponPresets`), et 3
  boutons radio de mode (Historique/Grandes flèches/Tutoriel — sélectionner
  une vraie combo dans la liste bascule automatiquement sur Tutoriel, seul
  mode qui l'affiche). Bouton doré « Lancer en jeu » (seul bouton stylé hors
  palette de boutons par défaut, pour bien le distinguer comme CTA
  principal) : applique la combo/le mode choisis à `AppState` (`SetActiveCombo`,
  `SetMode`, persistance dans `Settings.DefaultMode`), instancie `MainWindow`,
  la pose comme `Application.Current.MainWindow` (nécessaire *avant* de fermer
  cette fenêtre-ci, sinon `ShutdownMode="OnMainWindowClose"` tue l'app puisque
  `StartupWindow` était jusque-là la `MainWindow` de l'`Application`), puis se
  ferme. Bouton secondaire « Réglages avancés… » ouvre `ControlPanelWindow`
  sans lancer l'overlay (utile pour retoucher touches/apparence avant de
  lancer). Fermer cette fenêtre sans cliquer « Lancer en jeu » (croix native)
  quitte l'app normalement, comme n'importe quelle `MainWindow` WPF.
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
  pourrait jamais être validée en jeu sans ce garde-fou). Champ "Arme
  (optionnel)" (`_weaponCombo`) qui écrit `Combo.Weapon` : absent jusqu'ici,
  ce qui rendait une combo créée/enregistrée à la main invisible dès qu'un
  filtre d'arme était actif ailleurs (`Combo.Weapon` ne se remplissait que
  via l'import des presets) — voir `docs/audit_features.md` §1.3. Aussi
  réutilisé comme **écran de relecture après un enregistrement en direct**
  (`isRecordingReview: true`, appelé depuis `MainWindow.SaveRecordedCombo`) :
  auparavant la combo capturée par `Ctrl+Alt+R` était sauvegardée directement
  sans passer par cet éditeur (`docs/plan.md §1.3.A` prévoyait un tel écran,
  jamais fait — voir `docs/audit_features.md` §1.5), donc la seule façon de
  corriger un input parasite capturé par erreur était de rouvrir l'éditeur
  après coup depuis la liste. Maintenant, arrêter l'enregistrement ouvre cet
  éditeur pré-rempli avec les étapes capturées (titre "Vérifier la combo
  enregistrée", boutons "Valider et enregistrer" / "Rejeter l'enregistrement")
  avant toute écriture dans `combos.json`.

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
| `Ctrl+Alt+H` | Suspendre / reprendre la capture globale (voir `AppState.CaptureSuspended` ci-dessous) |

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
  tolérance de récupération dans `ComboRunner` (`AttackRecoveryLockMs`, fenêtre
  de 180ms) : un mauvais bouton d'attaque pressé juste après une attaque
  précédente était ignoré plutôt que de casser la combo, pour refléter que le
  personnage est encore en animation de récupération à ce moment-là en jeu.
  **Depuis remplacé** (voir le retrait du timing comme condition d'échec
  ci-dessous) par un mécanisme sans notion de délai basé sur l'identité du
  dernier bouton validé (`_lastConsumedActionKeys`, section `ComboRunner`
  ci-dessus) — `AttackRecoveryLockMs` n'existe plus dans le code, gardé ici
  seulement comme trace historique de la transition (l'ancienne doc de cette
  section affirmait encore son existence après coup, incohérence relevée et
  corrigée via `docs/audit_features.md` §1.2).
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
- Version 10 (audit ergonomique "premier utilisateur" + corrections) : un
  agent jouant le rôle d'un tout premier utilisateur (config vierge, jamais
  lancé) a testé le parcours complet jusqu'au mode Tutoriel et relevé 9
  points de friction, tous corrigés :
  - Icône de tray générique (`SystemIcons.Application`, indiscernable des
    autres icônes système) → icône dessinée au runtime (`MainWindow.
    CreateTrayIcon`), cercle sombre + "B" doré.
  - Aucune indication au tout premier lancement → `OverlaySettingsConfig.
    WasFirstRun` (vrai si `settings.json` n'existait pas encore) déclenche un
    ballon d'info sur l'icône de tray + un affichage temporaire (12s) du
    bandeau de raccourcis normalement réservé au mode déverrouillé
    (`MainWindow.ShowFirstRunHintIfNeeded`).
  - `ControlPanelWindow` trop étroite par défaut (760×520) : l'onglet Combos
    tronquait son propre bouton d'import et cachait le bouton Supprimer
    derrière une scrollbar horizontale peu visible → agrandie à 960×640
    (`MinWidth` 900).
  - Liste de combos vide sans aucun indice → message d'état contextuel
    (`RefreshCombosList`) selon qu'aucune combo n'existe encore ou que le
    filtre d'arme actif n'en a aucune.
  - Bouton "Importer les 5 combos de cette arme" cliquable même sans arme
    choisie (menait à une MessageBox d'erreur après coup) → grisé tant que
    le filtre est sur "Toutes les armes".
  - Raccourcis clavier visibles seulement dans le 5ᵉ onglet "À propos" → même
    liste (avec l'exemple ci-dessus) désormais montrée d'office au premier
    lancement.
  - Éditeur de combo manuel (texte libre, noms d'action exacts requis) sans
    aucun exemple avant l'erreur de sauvegarde → ligne d'aide affichant un
    exemple concret et la liste des noms d'action valides, au-dessus des
    étapes.
  - Cluster ZQSD n'affichant que la lettre de la touche (pas de légende,
    illisible pour qui ne connaît pas la convention AZERTY) → nom de l'action
    ajouté en petit sous la lettre pour ce groupe, + tooltip sur tous les
    boutons.
  - Hook clavier/manette global constaté en train de réagir à des frappes
    faites dans d'autres fenêtres (navigateur, éditeur) pendant le test,
    faisant avancer silencieusement l'historique et la combo du mode
    Tutoriel hors de tout contexte de jeu → `AppState.CaptureSuspended` +
    raccourci `Ctrl+Alt+H` + entrée dans le menu du tray + bouton dans
    l'onglet Général, pour couper le suivi (touches, historique,
    `ComboRunner.Feed`) sans fermer l'app ; un bandeau rouge persistant sur
    l'overlay rappelle que la capture est suspendue tant qu'elle l'est.
- Correctif Faux (récidive du problème "Version 9") : l'utilisateur a de
  nouveau signalé des combos fausses, cette fois sur la Faux (le vrai combo
  de base est nLight > Jump > sAir > sSig, pas ce qui était codé). En
  récupérant réellement la page bluestacks.com citée comme source pour la
  Faux, son contenu actuel ("nAir>sAir", "sAir>sLight", "Rec>nAir",
  "Rec>sLight") ne correspondait à AUCUN des 5 combos codés malgré la
  citation — la correction de la Version 9 avait donc encore fabriqué la
  citation sans vérifier qu'elle correspondait vraiment à la source. Refait
  avec une page effectivement récupérée (theglobalgaming.com, "Scythe guide:
  combo strings") pour 4 des 5 combos, plus le combo de base tel que confirmé
  par l'utilisateur en jeu (marqué comme tel dans sa `Description`, sans lui
  inventer une fausse source écrite). Le `combos.json` réel de l'utilisateur
  a aussi été corrigé en direct (stats remises à 0, comme le ferait un vrai
  réimport). **Les 14 autres armes de `WeaponComboPresets.cs` n'ont pas été
  revérifiées** — vu que la Faux avait une citation fabriquée malgré la
  Version 9, il est probable qu'au moins certaines des autres armes aient le
  même problème ; à auditer arme par arme (fetch réel de la source citée,
  comparaison littérale) si l'utilisateur signale d'autres combos fausses ou
  demande un audit complet.
- Abandon de combo par inactivité : demande explicite de l'utilisateur — une
  tentative en cours (au moins une étape validée) qui reste sans input pendant
  3s doit se réinitialiser toute seule plutôt que de rester bloquée en
  attendant indéfiniment la suite. Implémenté comme un mécanisme séparé du
  timing par étape (qui, lui, reste volontairement retiré comme condition
  d'échec — voir plus haut) : `ComboRunner.CheckAbandon(now, timeout)` est un
  **poll**, pas un événement déclenché par une touche — `MainWindow` l'appelle
  toutes les 300ms via `_comboAbandonPollTimer`, indépendamment de toute
  frappe (contrairement à `_comboTimer`, qui lui se relance à chaque appui).
  Sans appel à `Feed` depuis 3s (`ComboAbandonTimeout`) et une étape déjà en
  cours (`CurrentStepIndex > 0`), la combo se réinitialise et lève un nouvel
  event `ComboAbandoned` (distinct de `ComboReset`, qui reste réservé à
  l'échec par mauvaise touche) : contrairement à un échec, il n'y a pas de
  clignotement rouge à attendre avant de rafraîchir l'affichage (pas de
  `FlashAllStepsRed` déclenché), donc `OnComboAbandoned` remet direct les
  pastilles à l'état d'attente.
- Icônes d'action du mode Tutoriel (dossier `logo/` fourni par l'utilisateur) :
  18 fichiers PNG = 6 icônes distinctes (épées croisées, couteau, silhouette
  qui court, triple chevron, flèche épaisse, éclat/griffe) déclinées chacune
  en 3 carrés pleins non détourés (fond blanc/vert/rouge). Après clarification
  avec l'utilisateur : 5 des 6 icônes remplacent les emoji Unicode des
  pastilles de combo du mode Tutoriel (Saut→chevron, Att. légère→couteau,
  Att. forte→épées croisées, Esquive→silhouette, Lancer→éclat — l'éclat
  représente le lancer d'arme, pas Taunt, qui n'a pas d'utilité réelle dans un
  combo et garde son emoji 💬) ; la flèche épaisse (`13/14/15.png`) n'est pas
  utilisée. Portée volontairement limitée aux pastilles du mode Tutoriel pour
  l'instant (l'historique et les gros boutons des modes 1/2 gardent leurs
  emoji). Fichiers copiés (pas détourés, gardés en carrés pleins tels quels,
  par choix explicite de l'utilisateur) dans `Assets/Icons/<action>_<couleur>.png`,
  déclarés `<Resource>` dans le `.csproj` (embarqués dans l'assembly, chargés
  par pack URI via `MainWindow.GetActionIcon`, pas de copie à côté de l'exe).
  Mapping action → nom de fichier de base dans `MainWindow.ActionIconBaseNames`
  (dictionnaire en dur, pas de nouveau champ dans `KeyBind`/`keybinds.json`
  pour éviter la régénération de schéma). Les 3 couleurs suivent l'état de la
  pastille (pas de variante jaune) : noir = par défaut (à venir/courante),
  vert = étape déjà réussie, rouge = flash d'échec — géré par
  `MainWindow.SetPillIconVariant`/`SetAllPillIconVariant`, appelés depuis
  `UpdateComboStepVisuals`/`FlashComboStepSuccess`/`FlashAllStepsRed`. Le `logo/`
  original reste sur le disque (pas versionné a priori, à la racine) au cas où
  d'autres icônes du lot seraient réutilisées plus tard.
  Retour immédiat de l'utilisateur, deux corrections : (1) la flèche épaisse
  finalement utilisée pour les 4 directions (Gauche/Droite/Haut/Bas), qui
  gardaient leur glyphe texte ◄►▲▼ — une seule icône `direction_<couleur>.png`
  (pointant à droite par défaut) tournée via `RenderTransform`/`RotateTransform`
  selon l'action (`ActionIconRotationDegrees` : Droite=0°, Bas=90°,
  Gauche=180°, Haut=270°), donc toutes les actions de la pastille de combo ont
  désormais une icône sauf Taunt. (2) Les pastilles étaient restées des ronds
  (`CornerRadius` = moitié de la largeur) alors que les icônes fournies sont
  des carrés pleins — ça écrasait les images au centre avec des bandes vides
  sur les côtés. Passé en carré à coins arrondis (`CornerRadius(14)`, 72×72,
  icône 58×58) pour coller à la forme réelle des designs plutôt que d'imposer
  une forme d'UI générique par-dessus.
  Nouveau retour, deux corrections supplémentaires sur ce même chantier : (1)
  le carré à coins arrondis restait un carré *avec un fond gris translucide*
  derrière l'image — toujours pas ce qui était demandé. Supprimé entièrement :
  la pastille est maintenant un `Grid` transparent (aucun `Background`) qui ne
  sert qu'à gérer l'opacité (à venir/courante) et le masque quiz ; chaque icône
  a directement ses coins légèrement arrondis via `Image.Clip` (un
  `RectangleGeometry` de rayon 8, pas de conteneur autour). Les états
  succès/échec ne sont plus indiqués par une couleur de fond mais uniquement
  par la variante de couleur de l'icône elle-même (vert/rouge, voir
  `SetPillIconVariant`) + un clignotement d'opacité. (2) Quand une étape combine
  plusieurs actions (ex. direction + attaque), les images étaient collées les
  unes aux autres dans le même `StackPanel` sans espace — séparées avec une
  marge nette (10px) entre chaque élément pour qu'elles se lisent comme deux
  icônes distinctes plutôt qu'un bloc fusionné.
- Correctif Gravity Cancel : retour de l'utilisateur (confirmé en jeu) que le
  Gravity Cancel s'exécute en appuyant sur **Esquive** au bon moment en l'air
  pour annuler la vitesse de chute — pas en sautant. `WeaponComboPresets.cs`
  codait pourtant GC comme un "Saut" partout (7 combos sur 5 armes : Marteau,
  Katars, Faux, Épée à deux mains, Canon ×3), une décision de la Version 8
  justifiée à l'époque par une concordance entre deux sources écrites
  (gamespecifications.com note littéralement "Jump" aux mêmes endroits où
  bluestacks.com note "GC") — concordance qui s'est donc révélée être une
  erreur de terminologie partagée par les deux sources, pas une confirmation
  valide. Les 7 occurrences ont été corrigées (`S("Saut")` → `S("Esquive")`
  uniquement pour l'étape représentant le GC — le combo Faux a un vrai second
  "Saut" pour un nAir juste avant, resté inchangé), et le docstring de
  traduction en tête de fichier mis à jour. Leçon : une concordance entre
  plusieurs sources écrites n'est pas une garantie contre l'erreur si aucune
  n'a été vérifiée contre le jeu réel — le test en jeu de l'utilisateur reste
  la source de vérité finale, prioritaire sur toute doc communautaire (déjà
  la leçon de "Correctif Faux" ci-dessus, qui se confirme ici sur un autre axe).
- Ctrl+Alt+U n'amenait pas toujours `ControlPanelWindow` au premier plan quand
  déclenché pendant que le jeu avait le focus (hook clavier bas niveau global,
  donc l'app n'est pas la fenêtre active au moment de l'appui) : `Activate()`
  seul ne suffit pas à cause du "foreground lock" de Windows (une fenêtre qui
  n'est pas déjà active ne peut normalement pas voler le focus toute seule).
  Corrigé en appelant explicitement `SetForegroundWindow` (P/Invoke user32,
  comme le reste des interop bas niveau de `MainWindow`) sur le handle du
  panneau juste après `Show()`/`Activate()`.
- Enregistrement de combo dans `ComboEditorWindow` (deux passes, la 1ère jugée
  insuffisante par l'utilisateur) : le texte libre par étape (taper les noms
  d'action à la main) a été entièrement remplacé par un système de puces —
  chaque étape a un bouton "Écouter" qui capture **une seule** touche/bouton à
  la fois (`ListenForNextBindAction`, même mécanique que `ListenForNextKey` de
  l'onglet Touches : un seul `KeyDown`/`ButtonDown`, résolu en nom d'action via
  `KeyBind.VirtualKeyCodes`, pas de fenêtre de temps). Une 1ère version
  utilisait une fenêtre glissante de 250ms pour regrouper automatiquement
  plusieurs touches pressées ensemble en une seule étape — rejetée : l'utilisateur
  voulait un contrôle explicite, pas une détection automatique par timing.
  Le système retenu : recliquer "Écouter" **sur la même ligne** ajoute une
  puce de plus à cette étape (simultané, ex. Droite + Att. légère) ; cliquer
  "+ Ajouter une étape" démarre une ligne séparée. Chaque puce a son propre
  ✕ pour la retirer individuellement. La liste d'actions d'une étape est
  stockée directement dans `Grid.Tag` (`List<string>` mutable, alimentée
  uniquement par clics) — `SaveAndClose` la relit telle quelle, il n'y a plus
  de texte à parser/valider pour un nom d'action mal orthographié (les puces
  ne peuvent contenir qu'une action réellement liée à une touche existante).
  Une seule écoute active à la fois : `_cancelActiveListen` coupe proprement
  celle en cours si un autre bouton "Écouter" est cliqué ou si la fenêtre se
  ferme pendant l'écoute.
- Correctifs suite à l'audit du 2026-07-25 (`docs/audit_features.md`,
  section 1 "bugs et incohérences réels") : (1) `Combo.MatchMode` était un
  réglage mort — le sélecteur "Strict / Tolérant" de `ComboEditorWindow` se
  sauvegardait mais `ComboRunner.Feed` ne le lisait jamais, les deux options
  se comportaient de façon identique (toujours tolérant au mouvement pur en
  trop). `ComboRunner.Feed` fait maintenant réellement la différence : en
  `Strict`, une direction tenue en plus de ce qui est demandé par l'étape
  fait échouer la combo (comme un mauvais bouton d'action), pas en
  `IgnoreExtraneous`. (2) La documentation de `ComboRunner` (section
  `ComboRunner` ci-dessus + historique "Version 8") citait encore
  `AttackRecoveryLockMs` (tolérance de récupération à 180ms) comme mécanisme
  actif alors qu'il avait déjà été remplacé par `_lastConsumedActionKeys`
  (tolérance sans notion de délai, basée sur l'identité du dernier bouton
  validé) — doc corrigée pour refléter le code réel. (3) Une combo créée
  manuellement dans `ComboEditorWindow` ne pouvait jamais être associée à une
  arme (`Combo.Weapon` restait toujours vide hors import de preset), la
  rendant invisible dès qu'un filtre d'arme était actif ailleurs — ajout d'un
  sélecteur d'arme optionnel dans l'éditeur. (4) L'overlay était toujours
  positionné sur `SystemParameters.WorkArea` (toujours l'écran principal
  Windows) sans moyen de cibler un écran secondaire où tournerait le jeu —
  ajout de `Settings.MonitorIndex` + sélecteur d'écran dans l'onglet
  Apparence, résolu via `System.Windows.Forms.Screen.AllScreens`
  (`MainWindow.GetTargetWorkArea`/`ApplyWorkArea`, appliqué aussi à chaud si
  changé pendant que l'overlay tourne). (5) `SaveRecordedCombo` sauvegardait
  la combo capturée par `Ctrl+Alt+R` directement dans `combos.json`, sans
  écran de relecture (pourtant prévu dans `docs/plan.md §1.3.A`, jamais fait
  jusque-là) — arrêter l'enregistrement ouvre maintenant `ComboEditorWindow`
  pré-rempli avec les étapes capturées (mode `isRecordingReview`) avant toute
  écriture sur disque, pour pouvoir corriger un input parasite ou ajuster une
  tolérance avant de valider.
- Remplacement complet des icônes du mode Tutoriel (retour utilisateur : le
  pack `logo/`/`Assets/Icons/` d'origine — voir plus haut — mélangeait en fait
  3-4 styles différents sans rapport entre eux, "immondes" une fois affiché en
  contexte). Deux passes :
  1. Une tentative de dessin maison (6 glyphes monoline vectoriels, un seul
     poids de trait par famille) proposée en aperçu via un Artifact avant tout
     code — rejetée elle aussi ("les formes elles-mêmes sont moches"), leçon
     retenue : dessiner des icônes à la main n'est pas un point fort fiable
     ici, mieux vaut partir d'un travail de designer existant.
  2. Adoption de 6 icônes de **game-icons.net** (licence CC BY 3.0, attribution
     ajoutée dans l'onglet À propos du panneau de contrôle) : *Saber Slash*
     (Att. légère), *Sword Clash* (Att. forte), *Dodging* (Esquive), *Jump
     Across* (Saut), *Thrown Daggers* (Lancer), tous par **Lorc**, et *Plain
     Arrow* (Direction, tourne selon l'action) par **Delapouite** — toutes
     issues de la même bibliothèque, donc cohérentes entre elles par
     construction. Un premier choix (*Sword Slice* pour l'attaque légère) a
     été rejeté après aperçu ("on dirait plutôt une parade") : sa silhouette
     dominée par une forme ronde se lisait comme un bouclier plutôt qu'une
     lame en mouvement — remplacé par *Saber Slash*, une lame nette en plein
     geste.
  Techniquement : les 18 PNG d'origine (3 couleurs × 6 icônes) sont retirés du
  build (`<Resource Include="Assets\Icons\*.png" />` supprimé du `.csproj`) et
  archivés (pas supprimés) dans `Assets/Icons/_archive_pack1/`. Les nouvelles
  icônes sont des `Geometry` WPF (mini-langage compatible avec le `d` SVG
  d'origine, collé quasi tel quel) dans `MainWindow.IconGeometryByBaseName`,
  affichées via `System.Windows.Shapes.Path` avec `Stretch="Uniform"` (gère la
  mise à l'échelle depuis le viewBox natif 512×512, pas de conversion de
  coordonnées à la main) — recolorées à la volée par un simple changement de
  `Fill` (`MainWindow.IconBrushForVariant`) au lieu de charger 3 fichiers par
  icône. **Bug de contraste corrigé au passage** : l'état par défaut du pack
  d'origine était un remplissage noir plein, quasi invisible sur le panneau
  bleu-nuit translucide réel du mode Tutoriel — recoloré en accent doré de la
  marque (`#E8C44A`, déjà la couleur de bordure du panneau) au lieu de noir.
  `ActionIconRotationDegrees` recalculé en conséquence : la nouvelle icône de
  direction (*Plain Arrow*) pointe vers le bas par défaut (l'ancienne
  pointait à droite), donc `Bas=0°, Gauche=90°, Haut=180°, Droite=270°` au
  lieu de l'ancien mapping.
- Réécriture complète de `WeaponComboPresets.cs` + ajout de
  `LegendComboPresets.cs` : l'utilisateur a signalé que les combos existantes
  restaient globalement fausses ("de merde") et a fourni directement un post
  Reddit de true combos vérifiés (non-esquivables, testés à 0% de dégâts,
  avec seuils de Dex) en remplacement. Tout le contenu précédent de
  `WeaponComboPresets.cs` a été supprimé plutôt que corrigé au cas par cas
  (contrairement aux correctifs "Version 9"/Faux/Gravity Cancel précédents,
  ciblés) car le signalement portait sur l'ensemble du fichier. Nouveauté
  d'architecture nécessaire pour couvrir les combos de légende du post (qui
  utilisent une attaque Signature propre à un légend, ex. "Ada Blasters") :
  ajout de `Combo.Legend` (en plus de `Combo.Weapon` existant), d'un fichier
  séparé `LegendComboPresets.cs`, et d'un filtre légend dédié dans
  `AppState`/`OverlaySettings`/`ControlPanelWindow`/`ComboEditorWindow` — voir
  les bullets `WeaponComboPresets.cs`/`LegendComboPresets.cs` plus haut pour
  le détail. Clarifié avec l'utilisateur avant de coder que Signature en
  Brawlhalla n'est **pas** un bouton séparé mais littéralement l'attaque
  forte (déjà mappée sur `Att. forte`) — pas besoin d'ajouter d'action ni de
  toucher `KeyBindConfig`. Le post Reddit listait aussi séparément une
  section "Spear" et une section "Lance" avec des entrées différentes ; comme
  le jeu actuel n'a qu'une seule arme de ce nom (Spear = "Lance" en
  français ici), les deux ont été fusionnées sous "Lance" (un doublon exact
  entre les deux dédupliqué). Seules 11 des 15 armes étaient couvertes par le
  post ; les 4 autres (Épée à deux mains, Orbe, Lance-fusée, Bottes de
  combat, Chakram) n'ont donc plus aucun combo prérégle dans l'app, plutôt
  que de laisser leur ancien contenu non vérifié à côté de combos maintenant
  vérifiés — à compléter uniquement si l'utilisateur fournit une source aussi
  fiable pour ces armes.
- Refonte du filtre Arme/Légend en sélecteur "Personnage" (retour utilisateur :
  avoir deux filtres indépendants — arme et légend — combinés en ET était
  "étrange", puisqu'un légend n'a que 2 armes précises dans le jeu réel, pas
  15 au hasard ; on pouvait choisir "Ada" + "Marteau" (une arme qu'Ada
  n'utilise pas) et se retrouver avec une liste vide sans explication.
  Tentative de récupérer une vraie table légend→2 armes pour piloter le
  nouveau sélecteur (liquipedia.net, brawlhalla.fandom.com) infructueuse
  (page sans données de poids, 402 côté fandom, un "tier list" listant des
  personnages hors-roster manifestement peu fiable) — plutôt que de
  fabriquer cette table de mémoire (même piège que "Version 9"/"Correctif
  Faux" plus haut), `LegendComboPresets.WeaponsFor(legend)` dérive les armes
  d'un personnage du contenu déjà vérifié de `Table` (les armes citées dans
  ses combos Signature), sans introduire de nouvelle donnée à sourcer. Onglet
  Combos : le sélecteur "Personnage" (ex-"Légend") vient maintenant en
  premier ; le sélecteur "Arme" devient un sous-filtre dont les options
  dépendent du personnage choisi (`RefreshWeaponFilterOptions` dans
  `ControlPanelWindow.BuildCombosTab`) — "Toutes les armes" (15 armes) sans
  personnage, ou "Toutes les armes de *Perso*" + uniquement ses armes réelles
  si un personnage est sélectionné. `AppState.FilteredComboIndices` change de
  sémantique en conséquence : un personnage actif n'est plus un simple ET
  avec l'arme, il regroupe ses combos Signature (`Combo.Legend == perso`) ET
  les combos génériques des armes qu'il utilise réellement (`Combo.Weapon`
  dans `LegendComboPresets.WeaponsFor(perso)`, `Combo.Legend` vide) — sinon
  choisir juste "Ada" n'aurait montré que ses 2 combos Signature en cachant
  les combos génériques Blasters qu'elle peut pourtant jouer.
  `AppState.ImportCharacterPresets(legend)` bundle `ImportLegendPresets` +
  `ImportWeaponPresets` pour chacune de ses armes détectées, pour importer
  tout ce qui est jouable sur ce personnage en un seul clic au lieu de devoir
  cliquer sur le bouton légend puis sur le bouton arme séparément.
  `RefreshCombosList` appelle maintenant `AppState.FilteredComboIndices()`
  directement au lieu de dupliquer la logique de filtre localement (c'est
  cette divergence entre deux implémentations du même filtre qui avait
  permis la combinaison arme/légend incohérente en premier lieu).
- Portrait de personnage dans le panneau de combo (mode Tutoriel) + icônes de
  touches cohérentes sur les modes 1/2 : demande de suite directe de la
  refonte du sélecteur "Personnage" ci-dessus. `Assets/Legends/<Clé>.png`
  (30 fichiers, un par légend de `LegendComboPresets.Legends`) contient les
  renders "Roster Pose" officiels récupérés sur `brawlhalla.com/legends/`
  (`cms.brawlhalla.com`, curl direct — `brawlhalla.fandom.com` a renvoyé 403
  même en direct, pas seulement pour l'outil de fetch). **Différence
  importante avec les icônes d'action (game-icons.net, CC BY 3.0)** : ce sont
  des illustrations officielles du jeu, pas des assets sous licence libre —
  utilisées ici en lecture seule dans un outil 100% local non redistribué,
  jamais republiées. Déclarés `<Resource Include="Assets\Legends\*.png" />`
  dans le `.csproj`, chargés par pack URI (`MainWindow.LegendPortraitFileName`
  dérive le nom de fichier depuis `LegendComboPresets.Legends`, seule source de
  la liste des légends, plutôt qu'une seconde liste à maintenir en double ;
  `_legendPortraitCache` évite de redécoder le PNG à chaque changement de
  combo). Affiché dans `BuildMode3Panel`
  (`_legendPortraitImage`, 48×48, coins légèrement arrondis) en haut à gauche
  du panneau de combo via une `Grid` à 2 colonnes (portrait en colonne Auto,
  reste du contenu centré comme avant en colonne `*`) — masqué si
  `Combo.Legend` est vide ou ne correspond à aucun fichier connu
  (`RenderComboSteps`).
  Au passage, mise à jour visuelle des modes 1 (Historique) et 2 (Grandes
  flèches) pour utiliser le même jeu d'icônes vectorielles que le mode
  Tutoriel (`IconGeometryByBaseName`/`ActionIconRotationDegrees`, jusque-là
  réservé aux pastilles de combo) au lieu du glyphe `Symbol` (emoji) ou du
  texte brut d'origine sur ces deux modes — nouvelle méthode partagée
  `BuildActionIconShape` : `BuildKeycap` (mode 1, cluster ZQSD) affiche
  l'icône de direction tournée à la place du nom d'action en petit texte,
  les boutons d'action (Saut/Att. légère/Att. forte/Esquive/Lancer) affichent
  icône + nom de touche au lieu du seul nom de touche
  (`BuildActionKeycapContent`) ; `BuildBigArrow` (mode 2, badges de direction)
  et `BuildBigKeycap` (mode 2, attaques + rangée d'icônes secondaires)
  remplacent leur glyphe `Symbol` par la même icône. Repli sur l'ancien
  rendu texte/Symbol pour Taunt (pas d'icône dédiée, comme dans le mode
  Tutoriel).
- Version 11 (écran d'accueil) : retour utilisateur que lancer l'app droit sur
  l'overlay (`App.xaml` avait `StartupUri="Windows/MainWindow.xaml"` depuis le
  tout début du projet) était contre-intuitif — un premier utilisateur se
  retrouvait face à un overlay click-through sans aucune UI de sélection, sans
  savoir qu'un panneau de contrôle existait ailleurs. Demande explicite : tout
  sélectionner (personnage/arme/combo/mode) *avant*, puis n'avoir l'overlay
  qu'une fois en jeu. Ajout de `Windows/StartupWindow.xaml(.cs)`, nouveau
  `StartupUri` de `App.xaml` — voir la section `StartupWindow` ci-dessus pour
  le détail. Point technique notable : `ShutdownMode="OnMainWindowClose"`
  (inchangé) suit la propriété `Application.MainWindow`, qui pointe par
  défaut sur la première fenêtre du `StartupUri` (donc `StartupWindow`) tant
  que rien ne la réassigne — `LaunchOverlay()` doit explicitement faire
  `Application.Current.MainWindow = overlay` *avant* de fermer `StartupWindow`,
  sinon fermer l'accueil après avoir lancé l'overlay ferme l'app entière avec.
  Le sélecteur Personnage/Arme/combo de cet écran réutilise directement l'API
  `AppState` existante (`FilteredComboIndices`, `SetTrainingLegendFilter`,
  `ImportCharacterPresets`...) déjà éprouvée par l'onglet Combos du panneau de
  contrôle — même logique de filtre dupliquée dans les deux fenêtres plutôt
  que factorisée, choix délibéré pour garder `StartupWindow` et
  `ControlPanelWindow` indépendantes (l'une n'a pas besoin d'exister pour que
  l'autre fonctionne). Le bouton « Réglages avancés » de l'accueil ouvre
  `ControlPanelWindow` directement (sans lancer l'overlay), pour le cas où
  l'utilisateur veut retoucher touches/apparence avant de rentrer en jeu.

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
