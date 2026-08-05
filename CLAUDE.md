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
           hook clavier), ComboRunner.cs (moteur de validation du mode Tutoriel),
           ComboFamilies.cs (détection/affichage groupé des combos qui
           s'étendent les unes les autres, voir Architecture)
Windows/   DashboardWindow.xaml(.cs) (écran d'accueil, point d'entrée réel de
           l'app), MainWindow.xaml(.cs), ControlPanelWindow.xaml(.cs),
           ComboEditorWindow.xaml(.cs) — parmi les fenêtres WPF
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
- **Core/ComboFamilies.cs** — regroupement purement calculé (rien de persisté
  dans `combos.json`) des combos qui sont l'extension stricte d'une autre
  (mêmes `Weapon`/`Legend`, mêmes premières étapes dans le même ordre, plus
  au moins une étape de plus à la fin — comparaison par ensemble
  `RequiredActions` par étape, `FreeMovement`/délais ignorés). Sert
  uniquement à l'affichage groupé/indenté dans les listes de combos
  (`ControlPanelWindow`/`DashboardWindow`, via `ComboFamilies.OrderWithFamilies`,
  factorisé pour que les deux fenêtres partagent la même logique de tri sans
  la dupliquer) : les membres d'une famille se retrouvent adjacents, triés
  par nombre d'étapes croissant (`Niveau 1`, `Niveau 2`...), avec un badge de
  niveau et, pour une combo `Mastered` qui a une extension, un indice textuel
  (`→ niveau supérieur disponible ci-dessous`). Volontairement pas de bascule
  automatique de combo active à la maîtrise du niveau courant — décision
  explicite de l'utilisateur (voir `docs/combo_families_plan.md`) : l'indice
  est une suggestion, le changement de niveau reste toujours un clic manuel
  dans la liste. Ne modifie ni `ComboRunner` (chaque combo reste une séquence
  autonome validée indépendamment) ni `ChainCombos`/`ChainStreakThreshold`
  (playlist qui cycle toute la liste filtrée, système séparé et inchangé) ;
  aucun nouveau champ sur `Combo`, donc aucune régénération de schéma
  `combos.json` nécessaire.
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
  d'arme génériques (non liés à un légend), sur **9 des 15 armes du jeu**
  (Épée, Marteau, Blasters, Katars, Hache, Arc, Faux, Canon, Lance — le
  commentaire en tête de fichier disait longtemps "11 armes", incohérence
  avec le code jamais corrigée jusqu'à la Version 17 ; corrigé au passage).
  Les 6 autres (Épée à deux mains, Gantelets, Orbe, Lance-fusée, Bottes de
  combat, Chakram) n'ont volontairement aucun combo, voir plus bas. Contenu
  **entièrement réécrit**
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
  (mécanique que l'app ne modélise pas directement, voir `LegendStats.cs`)
  était indicatif seulement jusqu'à la Version 17 (texte libre dans
  `Description`) : `Combo.MinDex` (int?) l'extrait maintenant du texte
  existant via une regex (`WeaponComboPresets.ParseMinDex`, pas de
  resynchronisation manuelle sur les ~90 combos) et sert à **filtrer** les
  combos hors de portée d'un personnage — voir `AppState.FilteredComboIndices`
  et le bullet `LegendStats.cs` plus bas. Chaque combo a un `Id`
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
- **Config/LegendStats.cs** (Version 17) — stats de base (Force/Dex/Défense/
  Vitesse) des 69 légendes, sourcées sur `brawlmance.com/legends` (site
  communautaire, pas officiel mais largement utilisé par la scène
  compétitive) ; mécanique de stance (+1 sur un stat, -1 sur un autre — un
  seul swap, jamais un gros boost) confirmée sur `brawlhalla.wiki.gg/wiki/Stats`.
  `LegendStats.MaxReachableDex(legend)` = Dex de base + 1, jamais plus : sert
  à juger si un combo (`Combo.MinDex`) est humainement jouable sur le
  personnage entraîné. Dex ≠ Speed : Dex gouverne le temps de récupération
  après une attaque (donc la fenêtre pour enchaîner), Speed est la vitesse de
  déplacement, un stat totalement différent — question posée explicitement
  par l'utilisateur pendant le travail, tranchée par la doc du wiki citée
  ci-dessus avant d'écrire quoi que ce soit avec ces données.
  `AppState.FilteredComboIndices` exclut maintenant un combo dont
  `MinDex` dépasse `MaxReachableDex` du personnage actif ; `MainWindow.
  UpdateDexRequirement` affiche en plus, sous le nom de la combo active en
  mode Tutoriel, le seuil requis et si le personnage l'a déjà (vert), ne
  l'atteint qu'avec une stance (orange), ou ne peut pas du tout l'atteindre
  (rouge — ne devrait normalement plus arriver puisque filtré, mais reste
  honnête si la combo active a été choisie avant un changement de
  personnage). Demande explicite de l'utilisateur, avec un cas réel à
  l'appui : les anciens combos Teros (Dex de base 3, max 4 avec stance)
  demandaient "7+"/"9" Dex dans `LegendComboPresets.cs` — physiquement
  impossibles, pas juste non sourcés.
- **Config/LegendComboPresets.cs / Combo.Legend** — **depuis la Version 17,
  ne contient plus aucun combo** (`Table` vide intentionnellement) : les true
  combos par légende (utilisant une Signature exclusive) reposaient sur la
  même source Reddit que `WeaponComboPresets.cs`, jamais re-vérifiée arme par
  arme, et l'utilisateur ne leur fait plus confiance après un cas prouvé
  impossible (Teros, Dex de base 3, avait des combos demandant "7+"/"9" Dex
  alors que le max atteignable avec une stance est 4 — voir `LegendStats.cs`
  et Version 17 dans l'historique). La structure (`Legends`/`WeaponsFor`/
  `BuildPresetCombos`) reste en place pour pouvoir réintroduire des combos de
  légende plus tard avec une source jugée fiable.
  **`Legends` couvre maintenant les 69 légendes du jeu** (contre 30
  auparavant, qui ne couvrait par accident que les légendes citées dans les
  anciens combos) — sourcé sur `brawlhalla.com/legends/`, confirmé par
  l'utilisateur qu'aucune n'est un skin/crossover à exclure.
  **`WeaponsFor(legend)` ne dérive plus des combos** (qui sont maintenant
  vides) mais lit une table `LegendWeapons` dédiée, sourcée indépendamment
  (les 2 armes réelles de chaque légende, une page officielle par légende) —
  c'est ce qui permet à un personnage sans aucun combo dédié (donc tous,
  pour l'instant) d'afficher quand même les combos génériques de ses 2 armes
  réelles via `AppState.FilteredComboIndices`, au lieu de n'afficher que ce
  qui était accidentellement couvert par l'ancienne table de combos.
- **Models/OverlaySettings.cs / Config/OverlaySettingsConfig.cs** — réglages
  persistés dans `settings.json` : échelle, opacité, option "garder
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
- **Windows/DashboardWindow.xaml / .xaml.cs** (Version 21, remplace
  `StartupWindow`+`OnboardingWindow` — voir Historique) — fenêtre d'accueil
  unifiée, point d'entrée réel de l'app (`App.xaml.cs.OnStartup`, pas de
  `StartupUri` XAML fixe) ET rouvrable depuis une session déjà en cours
  (`Ctrl+Alt+U`/clic tray/bouton ⚙ de la barre de contrôle overlay →
  `MainWindow.OpenDashboard`, `inGameMode: true`). Fenêtre bordée classique
  (comme `ControlPanelWindow`, même palette bleu-nuit/or), pas l'overlay
  in-game.
  Deux modes de fonctionnement pilotés par le constructeur
  `DashboardWindow(bool inGameMode = false)` :
  - **`inGameMode: false`** (lancement de l'exe) : si
    `Settings.OnboardingCompleted` n'est pas vrai, affiche d'abord la fourche
    du tout premier lancement (`ShowOnboardingFork` — un seul écran commun
    "Tu es plutôt…", 3 cartes ; Expert → flux complet ci-dessous ;
    Connaisseur → grille de personnages puis import implicite des combos du
    personnage choisi ; Débutant → aucune autre question ; les trois
    convergent vers un écran "Voilà ce qui va se passer" qui prévient *avant*
    que l'overlay ne remplace la fenêtre). Sinon (ou après la fourche pour le
    profil Expert), affiche le flux complet (`BuildMainFlow`) : bandeau de
    reprise (`BuildResumeBanner`, masqué en `inGameMode`, voir plus bas),
    grille de portraits cliquables (`BuildCharacterTile`) → sélecteur Arme
    dépendant du personnage → liste de combos filtrées
    (`AppState.FilteredComboIndices`) → 3 boutons radio de mode. Bouton doré
    **« ▶ Lancer en jeu »** : applique la sélection à `AppState`
    (`SetActiveCombo`/`SetMode`), instancie `MainWindow`, la pose comme
    `Application.Current.MainWindow` (nécessaire *avant* de fermer cette
    fenêtre, sinon `ShutdownMode="OnMainWindowClose"` tue l'app), puis se
    ferme.
  - **`inGameMode: true`** (rouverte pendant qu'un `MainWindow` tourne déjà) :
    saute toujours la fourche onboarding (un revenant en jeu l'a
    nécessairement déjà passée), bandeau de reprise masqué (la session en
    cours EST déjà celle qu'on "reprendrait" — un bandeau ici n'aurait montré
    qu'une reformulation de ce qui tourne déjà). Bouton renommé
    **« ✓ Appliquer »** : applique la sélection à `AppState` exactement comme
    ci-dessus, mais **n'instancie pas de second `MainWindow`** — les mêmes
    events `ActiveComboChanged`/`ModeChanged` qu'utilisent déjà
    `Ctrl+Alt+K`/`Ctrl+Alt+P` propagent le changement en direct sur l'overlay
    déjà ouvert, puis la fenêtre se ferme. **Corrige un bug qui aurait existé
    si ce garde-fou avait manqué** : sans lui, cliquer sur ce bouton depuis
    une session en cours aurait créé un deuxième overlay superposé au
    premier — jamais expédié tel quel, détecté avant tout usage réel (voir
    Version 21).
  Sélecteur Personnage : la `ComboBox` (`_characterCombo`) reste la source de
  vérité de la sélection (hors de l'arbre visuel, `Visibility.Collapsed`),
  consommée par `RefreshWeaponOptions`/`RefreshPortrait`/`RefreshCombosList` —
  un clic sur un portrait se contente de lui réassigner `SelectedItem` pour
  redéclencher toute la chaîne sans la dupliquer. Sélecteur Arme dépendant du
  personnage : même logique que `ControlPanelWindow.BuildCombosTab`, dupliquée
  ici plutôt que partagée pour garder les deux fenêtres indépendantes. Bouton
  secondaire « Réglages avancés… » ouvre `ControlPanelWindow` (inchangé, dans
  les deux modes) pour les réglages fins (touches/apparence/...) que la
  Dashboard ne couvre pas. Fermer cette fenêtre sans cliquer le bouton
  d'action principal (croix native) ne fait rien de spécial : en
  `inGameMode: false` avant tout lancement ça quitte l'app (comportement WPF
  standard d'une `MainWindow`) ; en `inGameMode: true` ça ferme juste cette
  fenêtre secondaire, l'overlay continue de tourner.
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
  - **Un seul mode d'affichage : Tutoriel** (les modes Historique et Grand
    affichage, qui existaient jusqu'à la Version 21, ont été **supprimés**
    en Version 22 — voir l'entrée d'historique correspondante ; ce n'est pas
    juste une bascule désactivée, le code a été retiré). Bandeau centré en
    haut de l'écran (position fixe, toujours ancré en haut) affichant la
    combo active sous forme de pastilles reliées par des flèches (étape à
    venir grisée, étape courante jaune, étape réussie verte, échec = flash
    rouge si mauvaise touche / orange si trop lent, puis reset), plus le
    compteur de série. Se branche sur `ComboRunner` (voir plus haut). Sous
    la pastille courante (à partir de la 2ème étape), une fine barre de
    progression se vide en temps réel jusqu'à la fenêtre de tolérance
    (`MaxDelayMs`), pour visualiser le temps restant avant un échec par
    timeout, avec un texte en secondes en dessous (`0.4s`…) lu depuis la
    valeur animée de la barre elle-même pour rester synchronisé avec ce que
    l'œil voit. Réglages associés (onglet Général) : `SoundEnabled` (bip de
    succès/échec/complétion via `System.Media.SystemSounds`), `QuizMode`
    (masque en `?` toutes les étapes pas encore jouées — dès le début si la
    combo n'a pas démarré, façon "mode quiz inversé" : mémoriser avant
    d'exécuter plutôt que lire puis jouer — `Ctrl+Alt+I` ou le bouton
    "Révéler" du panneau de contrôle révèlent 3s), `ChainCombos` +
    `ChainStreakThreshold` (session guidée façon playlist : passe à la combo
    suivante de la liste ~900ms après avoir atteint N réussites
    *consécutives* sur la combo active, pas juste après la 1ère — une combo
    qui atteint ce seuil est marquée `Mastered` de façon persistante,
    affichée avec un ✓ dans la liste de l'onglet Combos). Chaque combo
    persiste aussi ses stats de performance cumulées
    (`BestStreak`/`TotalCompletions`/`TotalAttempts` dans `combos.json`,
    mises à jour via `AppState.SaveCombosQuiet()` qui ne lève pas
    `CombosChanged` pour ne pas reconstruire le `ComboRunner` actif — donc
    perdre sa série en cours — à chaque coup joué). Un toast générique
    (`MainWindow.ShowToast`, ~1.5s auto-fade) affiche divers feedbacks
    ponctuels (miroir détecté, révélation quiz, combo maîtrisé,
    enregistrement en cours/rejeté/validé).
  - Toujours centrée en haut de l'écran (`RepositionTopCenter`, indépendant
    de tout réglage de position — le réglage `Settings.Position`/
    `OverlayPosition`, hérité de l'ancien mode Historique déplaçable à la
    souris, a été retiré avec ce mode).
  - **Anti-répétition** : l'auto-répétition Windows (rester appuyé → rafale
    de `WM_KEYDOWN`) est filtrée via un `HashSet<int> _pressedVks` : un seul
    "coup joué" transmis au `ComboRunner`/à l'export CSV par appui physique
    réel.
  - **Icône de tray** (`System.Windows.Forms.NotifyIcon`) : clic gauche
    ouvre/donne le focus au panneau de contrôle, clic droit propose un menu
    (verrouiller, ouvrir le panneau, quitter).
- **Windows/OverlayControlBarWindow.xaml / .xaml.cs** — petite barre de
  pilotage overlay (Version 13), instanciée par `MainWindow` (champ
  `_controlBar`) en plus du panneau de contrôle et de la barre du tray :
  répond au principe "rien d'obligatoire ne passe uniquement par un
  raccourci" pour les actions les plus fréquentes en jeu. Contrairement à
  `MainWindow`, **pas** click-through — c'est le seul endroit de l'overlay où
  la souris doit pouvoir agir, donc une fenêtre séparée plutôt qu'une zone
  spéciale de la fenêtre principale (qui est click-through globalement,
  `WS_EX_TRANSPARENT` posé sur toute la fenêtre, pas par contrôle). Positionnée
  en bas à droite de la zone de travail ciblée (`Reposition`, appelé depuis
  `MainWindow.ApplyWorkArea` à chaque fois que celle-ci se recalcule), loin du
  HUD par défaut (bas-gauche) pour ne jamais le recouvrir. Masquée par défaut :
  seul un petit onglet semi-transparent ("≡", 32×32, opacité 0.45) reste
  visible en permanence comme affordance de découverte ; le survol de cette
  zone (`MouseEnter`/`MouseLeave` sur le `Grid` racine, un `DispatcherTimer`
  de 500ms retarde la refermeture pour ne pas clignoter) révèle 5 boutons
  (changer de mode, combo précédente/suivante, suspendre/reprendre la
  capture, ouvrir le panneau), chacun avec un tooltip citant son raccourci
  clavier ET manette. Vérifié visuellement (capture d'écran réelle,
  automation UI pour un survol précis) : le survol étend bien la barre, et
  elle se replie après avoir quitté la zone.
- **Models/Lesson.cs / Config/ParcoursCurriculum.cs / Models/ParcoursProgress.cs +
  Config/ParcoursProgressConfig.cs / Windows/ParcoursWindow.xaml.cs** — le "Parcours"
  (§4 du plan UX onboarding, Version 14, voir plus bas) : suite de leçons courtes qui
  enseignent une mécanique du jeu tout en faisant utiliser une fonction de l'app.
  Contrairement au reste de l'app, `ParcoursWindow` démarre elle-même `AppState.Hook`/
  `AppState.Gamepad` (idempotent, `Start()` ne fait rien si déjà démarré) : elle peut
  s'ouvrir seule (bouton "Parcours" de `StartupWindow`), sans l'overlay ni son
  tray/sa barre de contrôle. Pour la même raison elle a son **propre bouton de
  verrouillage overlay et de suspension de capture dans son en-tête** (sinon les
  raccourcis Ctrl+Alt+O/H, gérés par `MainWindow`, ne seraient interceptés par
  personne si l'overlay n'est pas ouverte à côté).
  Chaque `Lesson` a un `LessonValidationKind` qui reflète honnêtement ce que l'app
  peut vérifier sans lire l'état du jeu (§4.2 du plan) : `PressAllOnce` (chaque
  action pressée une fois, ordre libre), `Sequence` (suite ordonnée — réutilise un
  `ComboRunner` **éphémère**, jamais persisté dans `combos.json`, plutôt que
  dupliquer le moteur de matching), `AbsenceTimer` (ne PAS presser une action
  pendant N secondes — seule validation possible pour un conseil de type
  "retenue", ex. ne pas paniquer au dodge), `ToggleOnce` (un event `AppState`
  précis — `LockChanged`/`CaptureSuspendedChanged` — se déclenche une fois, pour
  les leçons sur l'app elle-même). Une leçon peut être `FullyValidatedByApp = false`
  (badge "🟡 Partiellement validé") avec un `VerifyYourselfNote` explicite plutôt
  que de prétendre confirmer un effet en jeu invérifiable. Progression persistée
  dans `parcours_progress.json` (`AppState.MarkLessonCompleted`/
  `IsLessonCompleted`/`SetParcoursCurrentLesson`), déblocage **souple** : le
  bouton "Suivant" n'est jamais bloqué par la validation (voir §4.4 du plan —
  un déblocage strict punirait le joueur expérimenté), et "Voir tout le parcours"
  permet de sauter à n'importe quelle leçon.
  Contenu : Chapitre 0 (3 leçons sur l'app elle-même, aucune donnée de jeu) +
  Chapitre 1 "Survivre" (4 leçons : ressources aériennes, chaîne de récupération,
  fast fall, ne pas paniquer au dodge) — sourcé le 2026-08-03 via
  brawlhalla.wiki.gg/wiki/Movement **et** confirmation directe de l'utilisateur
  depuis son expérience de jeu réelle (le pool de 3 actions aériennes réparties
  2 sauts+1 récup OU l'inverse, le fonctionnement par cooldown — pas par
  compteur — de l'esquive, l'absence de cooldown de la récupération). Deux
  sources (wiki vs un post de forum) donnaient des cooldowns de dodge différents
  (1s/2.7s vs 1.5s/3.5s) ; l'utilisateur a explicitement tranché en faveur du
  wiki en cas de doute. Chapitre 2 "Frapper" (4 leçons : les 3 lights, les
  aériens, Signature ≠ bouton séparé, ne pas spammer la Signature) ajouté en
  Version 15 — sourcé par question directe à l'utilisateur d'abord : il a
  explicitement rejeté une généralisation proposée sur le rôle des 3 lights
  (\"neutre pour initier, latérale pour combo\"...), confirmant qu'il n'y a pas
  de rôle universel (dépend de l'arme/du perso/du combo) — la leçon 2.1 a été
  recentrée sur le simple repérage des 3 boutons plutôt que sur un usage
  inventé. Chapitre 3 "Bouger" (3 leçons : Dash, Dash jump, Backdash) ajouté
  en Version 16 — le plan prévoyait une 4ᵉ leçon "dodge directionnel comme
  outil de déplacement" mais l'utilisateur a clarifié qu'elle n'est pas une
  mécanique séparée (déjà couverte par 1.1, l'usage "recovery bonus" n'étant
  qu'un à-côté) donc pas de leçon dédiée. Sourcé aussi par question directe :
  au sol, Esquive+direction fait un Dash sans invincibilité (cooldown
  indépendant de l'esquive aérienne — le dodge avec i-frames "n'existe pas"
  au sol), Dash jump = direction → dash → saut ~0.5s après (propulsion avec
  un peu de hauteur), Backdash = même mécanique que le Dash mais vers
  l'arrière (orientation du personnage que l'app ne modélise pas, leçon
  explicite là-dessus). Chapitres 4 et 5 du plan volontairement pas
  construits (voir Version 14/15/16).
  **Deux bugs réels trouvés en testant** (pas juste en relisant le code) : (1)
  `OnGlobalKeyDown` de `ParcoursWindow` ne vérifiait pas `AppState.CaptureSuspended`
  — une frappe réelle ailleurs sur le PC pendant un test a validé une leçon
  `PressAllOnce` sans qu'aucune touche du test n'ait été pressée ; corrigé en
  répliquant la même garde que `MainWindow.OnGlobalKeyDown`. (2) Les boutons
  d'en-tête/pied de page (Suivant/Précédent/verrouiller/suspendre/voir tout)
  gardaient le focus clavier WPF après un clic automation — un appui sur Espace
  (l'action "Saut") réactivait aussi le dernier bouton focus au lieu de
  seulement nourrir la leçon, faisant sauter des leçons de façon imprévisible ;
  corrigé avec `Focusable = false` sur ces boutons (le clic souris reste
  inchangé, seule la rétention de focus clavier est coupée).
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
| `Ctrl+Alt+O` | Verrouiller / déverrouiller l'overlay (affiche le bandeau de raccourcis) |
| `Ctrl+Alt+K` / `J` | Combo suivante / précédente (touches reconfigurables, `Settings.ComboNextVk`/`ComboPrevVk`) |
| `Ctrl+Alt+R` | Démarrer / arrêter l'enregistrement d'une combo       |
| `Ctrl+Alt+U` | Ouvrir / donner le focus à l'accueil (`DashboardWindow`, en mode in-game — voir Version 21) |
| `Ctrl+Alt+I` | Révéler temporairement (3s) la combo active en mode révision |
| `Ctrl+Alt+M` | Masquer / afficher complètement l'overlay             |
| `Ctrl+Alt+H` | Suspendre / reprendre la capture globale (voir `AppState.CaptureSuspended` ci-dessous) |

Tous affichés en toutes lettres (avec leur raccourci en suffixe) dans le menu du
tray (7/7 actions) et dans l'onglet À propos du panneau de contrôle — pour
s'apprendre passivement plutôt que de rester cachés derrière le bandeau
affiché 12s au premier lancement (voir "Version 13" ci-dessous).

## Raccourcis manette (chords "Start + bouton")

Ajoutés en Version 13 (voir plus bas) pour ne pas obliger un joueur au pad à
lâcher la manette ou alt-tabber pour piloter l'app. Détectés dans
`MainWindow.OnGlobalKeyDown/Up` via les codes synthétiques de `GamepadHook`
(mêmes constantes `GP_*`), avec un état `_padStartDown` qui joue le même rôle
que `_ctrlDown`/`_altDown` pour les chords clavier :

| Chord              | Action                                   |
|---------------------|-------------------------------------------|
| `Start + RB`        | Combo suivante (`AppState.CycleCombo`)    |
| `Start + LB`        | Combo précédente (`AppState.CyclePreviousCombo`, ajouté pour l'occasion) |
| `Start + Back`      | Suspendre/reprendre la capture            |
| `Start + X`         | Ouvrir l'accueil (`DashboardWindow`)      |

Limite assumée et documentée (onglet À propos) : si un de ces boutons
(LB/RB/X/Y/Back) est *aussi* assigné à une action de jeu dans l'onglet
Touches, tenir Start en même temps déclenche le chord plutôt que l'action —
pas de résolution de conflit plus fine pour l'instant.

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
- Version 12 (familles de combos) : retour utilisateur qu'une combo courte
  (ex. 2 étapes) et une combo plus longue qui n'est en fait que la première
  plus 1-2 coups de plus se retrouvaient traitées comme deux combos sans
  aucun rapport visible dans la liste, alors que l'une est clairement une
  extension de l'autre. Réfléchi ensemble avant d'écrire un plan
  (`docs/combo_families_plan.md`) puis de l'implémenter tel quel : détection
  **automatique** par préfixe de `Steps` (pas de champ à maintenir à la
  main — risque d'oubli/désync, déjà vécu avec les combos hallucinées, voir
  plus haut), regroupement affiché avec indentation + badge `Niveau N`, et
  suggestion textuelle (pas de bascule automatique) quand une combo maîtrisée
  a une extension. Voir `Core/ComboFamilies.cs` dans la section Architecture
  ci-dessus pour le détail technique. Vérifié avec deux combos de test
  temporaires injectées dans `combos.json` (arme vide, 2 étapes + 4 étapes
  dont les 2 premières identiques) — la famille a bien été détectée et
  affichée groupée/indentée, retirées après vérification. Au passage, la
  détection a aussi mis en évidence une vraie famille déjà présente dans les
  presets Faux existants (`NLight vers NAir` → `NLight, NAir, SAir, Gravity
  Cancel, DLight`), sans qu'elle ait été identifiée comme telle jusque-là.
- Tolérance Saut + symétrie Gauche/Droite (`Core/ComboRunner.cs`) : demande
  explicite de l'utilisateur sur deux comportements jugés trop stricts par
  rapport au jeu réel. (1) Une étape qui demande "Saut" tolère désormais
  toujours une direction tenue en plus (sauter en bougeant est normal en
  jeu), même en `MatchMode.Strict` et sans avoir à cocher `ComboStep.
  FreeMovement` à la main sur chaque étape de saut (`requiresJump` dans
  `Feed`, court-circuite `strictMovementViolation`). (2) Une combo écrite
  "vers la droite" doit marcher identiquement jouée "vers la gauche" — les
  deux orientations sont symétriques, ce n'est pas une combo différente.
  Implémenté comme une détection automatique par tentative
  (`_mirroredDirections`, verrouillée une seule fois via `UpdateMirrorLock`
  dès qu'une étape exige Gauche/Droite et que le joueur presse l'opposé
  exact) plutôt qu'un réglage à choisir dans l'éditeur : dès que l'inversion
  est détectée, `ComboRunner` attend l'opposé de ce que `Combo.Steps` décrit
  pour Gauche/Droite jusqu'à la fin de la tentative (Haut/Bas jamais
  inversés). Remis à zéro à chaque retour à l'étape 0 (échec/abandon/succès)
  via `ResetMirrorState`, pour que l'orientation soit re-détectée à chaque
  nouvelle tentative plutôt que de rester figée sur la première détectée.
  Exposé côté UI via `ComboRunner.IsMirrored`/`MirrorChanged` : `MainWindow`
  fait pivoter les icônes Gauche/Droite du panneau de combo (mode Tutoriel)
  pour afficher la flèche réellement attendue plutôt que celle écrite dans
  `Combo.Steps` (`_directionIconsByIndex`/`ApplyMirrorDisplay`), et affiche
  un badge "Direction inversée détectée" au moment où l'inversion est
  verrouillée.
- Version 13 (UX & onboarding) : audit complet de l'ergonomie
  (`docs/plan_ux_onboarding.md`) demandé après constat que le premier lancement
  restait un cul-de-sac (4 décisions d'affilée devant une liste de combos
  vide) et que tout passait par des raccourcis clavier non rebindables sans
  équivalent manette. Exécuté : les 5 chantiers P0 (fourche de premier
  lancement `OnboardingWindow`, import de combos implicite au choix du
  personnage, barre de contrôle overlay `OverlayControlBarWindow`, écran
  "voilà ce qui va se passer" avant que l'overlay ne remplace la fenêtre,
  chords manette Start+bouton) et la majorité des P1 (accueil de reprise en
  un clic, grille de portraits à la place de la `ComboBox` personnage, passe
  de vocabulaire — "Grandes flèches"→"Grand affichage", "Mode révision"→
  "Cacher les étapes (mémorisation)", "Strict/Tolérant"→"Refuser les
  directions en trop/Les ignorer" — + glossaire FR↔notation communautaire
  dans l'onglet À propos, réglages Général coupés en deux niveaux via un
  `Expander` "Réglages avancés" replié par défaut, état vide du mode Tutoriel
  explicite + explication du tout premier échec de combo affichée une fois
  par session). Menu tray complété de 4/7 à 7/7 actions, toutes avec leur
  raccourci en suffixe.
  **Volontairement non fait** : le "Parcours" (§4 du plan, mode pédagogique
  fusionnant tutoriel de jeu et tutoriel d'app) — construire ses chapitres 1
  à 5 exigerait de sourcer de nombreuses affirmations factuelles sur le jeu
  pièce par pièce, exactement le terrain où le projet s'est déjà trompé deux
  fois (combos hallucinées "Version 9", citation fabriquée "Correctif Faux").
  Un choix "Débutant" existe dans la fourche mais le dit explicitement à
  l'écran (mode Historique en attendant) plutôt que de faire semblant qu'un
  tel contenu existe. Également non faits : raccourcis clavier rebindables
  (sortir les `VK_*` vers `settings.json` + écran de réassignation) et guide
  de rythme progressif (métronome) — tous deux classés P2 dans le plan.
  Vérifié par build (`dotnet build -c Release`, 0 avertissement/erreur après
  chaque étape) et par test visuel réel : app lancée, écran de reprise
  capturé par screenshot, bouton "Lancer en jeu" invoqué via UI Automation
  (pas de simulation de frappe clavier/manette pendant que l'app tournait —
  seulement clic sur un bouton et survol souris, conformément à la prudence
  documentée plus bas sur `keybd_event`), survol de la barre de contrôle
  overlay confirmé fonctionnel par capture d'écran avant/pendant/après
  (bascule expand/collapse observée).
- Version 14 (Parcours, Chapitre 0+1) : suite directe de la Version 13,
  l'utilisateur ayant fait remarquer qu'il pouvait lui-même servir de source
  pour le contenu de jeu du "Parcours" plutôt que de s'en remettre uniquement
  à du web-scraping (déjà source de deux erreurs passées, voir "Version 9"/
  "Correctif Faux"). Contenu du Chapitre 1 "Survivre" obtenu par question
  directe à l'utilisateur (ressources aériennes : 2 sauts+1 récup OU
  l'inverse, l'esquive sur cooldown pas compteur, la récupération sans
  cooldown, le fast fall, les conseils anti-panique au dodge), recoupé avec
  brawlhalla.wiki.gg/wiki/Movement pour les chiffres précis — un désaccord
  entre le wiki et un post de forum sur le cooldown d'esquive (1s/2.7s vs
  1.5s/3.5s) a été explicitement tranché par l'utilisateur en faveur du wiki.
  Voir la section `Lesson`/`ParcoursCurriculum`/`ParcoursWindow` de
  l'Architecture ci-dessus pour le détail technique (modèle de validation par
  `LessonValidationKind`, réutilisation d'un `ComboRunner` éphémère pour les
  leçons de type séquence).
  **Deux bugs réels trouvés uniquement parce que testés en conditions
  réelles** (pas seulement en relisant le code) : `ParcoursWindow` ne
  respectait pas `AppState.CaptureSuspended` (une frappe faite ailleurs sur
  le PC pendant un test a validé une leçon toute seule), et les boutons de
  navigation gardaient le focus clavier WPF après un clic, donc taper
  "Saut" (Espace) pendant un drill réactivait aussi le dernier bouton cliqué
  — les deux corrigés (voir Architecture). Un incident distinct a eu lieu
  pendant ces tests : une tentative d'envoyer des touches à une fenêtre
  Notepad de secours a échoué silencieusement (`SetForegroundWindow` bloqué
  par la protection anti-vol-de-focus de Windows depuis un processus en
  arrière-plan) et les touches sont parties dans l'onglet Gmail réel de
  l'utilisateur à la place (rien de cassé, juste un défilement) — voir
  memory `feedback_no_input_injection_without_asking` : toute simulation de
  frappe clavier doit désormais être confirmée explicitement avant envoi, et
  la présence réelle du focus vérifiée via `GetForegroundWindow` plutôt que
  supposée après un `SetForegroundWindow`.
  Chapitres 2 à 5 du plan toujours pas construits (mécaniques de frappe,
  mouvement avancé, techniques avancées) — à reprendre de la même façon
  (interroger l'utilisateur directement plutôt que scraper le web en
  premier) si demandé.
- Version 15 (Parcours, Chapitre 2 "Frapper") : suite directe de la Version
  14, même méthode de sourcing (question directe à l'utilisateur d'abord,
  recoupement écrit ensuite si besoin). Deux réponses notables : (1) sur les
  3 variantes de light (neutre/latérale/basse), l'utilisateur a explicitement
  rejeté une généralisation proposée par erreur (\"neutre pour initier,
  latérale pour combo, basse pour poke\"...) — il n'y a pas de rôle universel,
  ça dépend de l'arme, du personnage et du combo visé. La leçon 2.1 a donc
  été écrite pour repérer les 3 boutons (Att. légère seule / +Droite / +Bas),
  sans leur inventer un usage générique — leçon retenue : quand l'utilisateur
  dit "ça dépend", ne pas quand même écrire une version édulcorée de la
  généralisation rejetée, la retirer complètement. (2) Sur les aériens
  (nAir/sAir/dAir), les usages proposés (dAir=spike/gimp, nAir=sauvetage
  rapide, sAir=edgeguard) ont été confirmés comme globalement justes mais
  explicitement qualifiés de non-exclusifs par l'utilisateur — la leçon 2.2
  le dit ("sans que ce soit exclusif"), plutôt que de présenter ces usages
  comme des règles strictes. Sur la Signature (2.3, déjà établi ailleurs dans
  ce fichier) et le spam de Signature (2.4 : animation lente qui bloque sur
  place, whiff punissable, aspect frustrant/"toxique" pour l'adversaire sans
  être illégal), confirmation directe sans correction nécessaire. 4 leçons
  ajoutées à `ParcoursCurriculum.cs`, réutilisant les mêmes `LessonValidationKind`
  que les chapitres précédents (aucun nouveau mécanisme de validation créé).
  Testé en conditions réelles (leçon 2.1, séquence Att. légère seule → +Droite
  → +Bas) : validée avec succès, mêmes précautions que la Version 14 (focus
  vérifié via `GetForegroundWindow` avant tout envoi de touches).
- Version 16 (Parcours, Chapitre 3 "Bouger") : suite directe, même méthode.
  Correction notable de l'utilisateur sur le Dash : ce n'est PAS un double-tap
  de direction comme une première hypothèse le supposait, mais littéralement
  le bouton Esquive pressé au sol — au sol l'esquive avec invincibilité
  "n'existe pas", donc le même bouton produit un Dash (déplacement rapide,
  sans i-frames), sur un cooldown complètement séparé de l'esquive aérienne.
  Le plan prévoyait une 4ᵉ leçon (dodge directionnel comme outil de
  déplacement) ; l'utilisateur a clarifié qu'elle ferait doublon avec la
  leçon 1.1 (l'esquive aérienne y est déjà couverte, son usage comme "option
  de recovery en plus" n'étant qu'un à-côté) — leçon retirée du chapitre
  plutôt que gardée en redondance. 3 leçons ajoutées (Dash, Dash jump,
  Backdash), toutes `Sequence`/`FullyValidatedByApp=false` (l'app ne peut
  jamais confirmer que le joueur était au sol, ni l'orientation du
  personnage pour le Backdash — dit explicitement dans chaque
  `VerifyYourselfNote`). Testé en conditions réelles (leçon 3.1, Droite +
  Esquive) : validée avec succès.
- Version 17 (audit de confiance sur les combos préréglés + roster complet +
  faisabilité par Dex) : l'utilisateur a exprimé un doute général sur la
  fiabilité des combos préréglés existants ("les combos sont peut être pas si
  valide qu'on le pense"), tout en reconnaissant que la source Reddit
  d'origine n'était de toute façon pas plus vérifiable qu'avant ("le scrap
  reddit est pas si viable, on va les garder pour le moment"). Plutôt que de
  laisser cette incertitude implicite, une revue complète a été montée dans
  un artifact listant tous les combos `WeaponComboPresets`/`LegendComboPresets`
  par arme/légend en notation communautaire — l'utilisateur l'a confirmée
  globalement correcte pour les combos d'arme génériques ("les true combo que
  tu as mis... me choquent pas"), mais a demandé 3 changements concrets :
  1. **Combos de légende supprimés** (`LegendComboPresets.Table` vidée) : pas
     de confiance dans cette partie spécifique de la source, contrairement
     aux combos d'arme génériques. Structure gardée pour réintroduction
     future avec une meilleure source.
  2. **Roster étendu à 69 légendes** (contre 30, qui ne couvrait par accident
     que celles citées dans les anciens combos) + une table `WeaponsFor`
     indépendante des combos (2 armes réelles par légende, sourcée sur
     `brawlhalla.com/legends/`) : un personnage sans combo de légende dédié
     (donc tous, pour l'instant) affiche quand même les combos génériques de
     ses vraies armes. Portraits ajoutés seulement pour 5 des 39 nouvelles
     légendes (Orion, Gnash, Hattori, Sir Roland, Lucien) — les autres
     utilisent des noms de code thématiques sur le CDN officiel
     (`ActualSharkM`, `BountyHunterM`...) qui ne correspondent pas de façon
     fiable aux noms de légendes (et semblent inclure des skins, pas
     seulement les personnages de base) ; deviner le mapping a été jugé trop
     risqué (attacherait silencieusement le mauvais portrait au mauvais nom)
     et laissé de côté plutôt que fait au hasard — `MainWindow.
     UpdateLegendPortrait`/`StartupWindow` gèrent déjà proprement l'absence de
     fichier (masque plutôt que planter), donc rien n'est cassé en attendant.
     Le portrait affiché dans le panneau Tutoriel suit maintenant
     `AppState.Settings.TrainingLegendFilter` (le personnage entraîné) au lieu
     de `Combo.Legend` (qui n'est presque plus jamais renseigné depuis le
     retrait des combos de légende) — sans ce changement, le portrait n'aurait
     plus jamais pu s'afficher.
  3. **Faisabilité par Dex affichée et filtrée** : voir `LegendStats.cs` et
     `Combo.MinDex` dans l'Architecture ci-dessus. Confirmé par un cas réel
     que ce n'est pas cosmétique : les anciens combos Teros (Dex 3, max 4
     avec stance) demandaient jusqu'à "9" Dex, physiquement impossible.
  Au passage, **tous les modes sauf Tutoriel désactivés temporairement**
  (`AppState.CombosOnlyMode = true`) : "les modes qui affichent des grosses
  flèches à l'écran c'est immonde". **Ce flag et le code des deux modes ont
  depuis été supprimés entièrement — voir Version 22.**
  Correction de langue notée par l'utilisateur au passage (pas encore
  appliquée à l'ensemble du code, seulement à ce qui a été écrit depuis) :
  "combo" est masculin en français dans l'usage de la communauté Brawlhalla
  ("un combo", pas "une combo") — l'app existante (UI + ce fichier) utilise
  le féminin de façon incohérente à ~67 endroits ; correction en attente
  d'arbitrage (sweep complet vs correction au fil de l'eau).
  **Sourcing** : roster + armes sur `brawlhalla.com/legends/` (officiel),
  stats sur `brawlmance.com/legends` (communautaire mais largement utilisé),
  mécanique de stance sur `brawlhalla.wiki.gg/wiki/Stats` (officiel) — tout
  confirmé/corrigé par l'utilisateur avant écriture (roster : "tous des
  persos je te confirme que c'est bon" après vérification qu'aucun n'était un
  skin/crossover). Testé en conditions réelles : import des combos Barraza
  (Dex 4, max 5) → `combos.json` inspecté directement, confirme le filtrage
  Dex correct (le seul combo Hache à 6+ Dex absent, tous les combos Blasters
  à 3+ présents, celui à 9 absent) ; capture d'écran de la grille de 69
  personnages et du mode "Tutoriel uniquement" confirmées après une session
  de dépannage de fenêtre (`AttachThreadInput` nécessaire pour reprendre le
  focus depuis un script PowerShell en arrière-plan, `SetForegroundWindow`
  seul ne suffisant pas) — accord explicite de l'utilisateur demandé avant de
  faire sauter une fenêtre par-dessus son stream en cours.
- Version 18 (finitions signalées après la Version 17) : deux corrections
  demandées ensemble ("occupe-toi des trucs laissés en attente" + "on peut
  pas scroll pour accéder aux persos suivants donc c'est nul").
  1. **Grille de personnages illisible au-delà de l'écran** : `StartupWindow`
     mettait les 70 tuiles (69 légendes + "Tous") dans un `StackPanel`
     horizontal à l'intérieur d'un `ScrollViewer` horizontal-only — la
     molette de souris (verticale) ne fait rien sur ce genre de
     `ScrollViewer` en WPF par défaut, et il n'y avait pas d'autre façon
     d'atteindre les personnages hors champ. Remplacé par un `WrapPanel`
     dans une `Grid` à colonne "Star" (largeur bornée par la fenêtre, donc le
     retour à la ligne fonctionne réellement — un `StackPanel` horizontal
     aurait donné une largeur infinie et empêché tout retour à la ligne) : la
     grille s'enroule sur plusieurs lignes et profite du défilement vertical
     de toute la page (déjà fonctionnel), sans second `ScrollViewer` imbriqué.
     Vérifié en conditions réelles (capture d'écran avant/après scroll
     molette) : les personnages en fin de liste (Sentinel, Seven, Sidra...)
     sont bien atteignables.
  2. **"une combo" → "un combo"** (accord masculin, usage de la communauté
     Brawlhalla, signalé en Version 17) : sweep complet plutôt qu'un
     arbitrage différé — tous les fichiers sous `Windows/`, `Core/` et
     `Models/` corrigés (~90 occurrences au total avec les accords
     d'adjectifs/participes qui en découlent : "cette combo" → "ce combo",
     "aucune combo" → "aucun combo", "la combo active" → "le combo actif",
     "une combo ratée/réussie/maîtrisée" → "un combo raté/réussi/maîtrisé",
     etc.). Le contenu écrit depuis la Version 17 (`LegendStats.cs`,
     `ParcoursCurriculum.cs`, `ParcoursWindow.xaml.cs`) était déjà correct,
     vérifié en le relisant plutôt que supposé.
- Version 19 (portraits — tentative puis retour en arrière) : suite au
  retour "il manque vraiment beaucoup trop d'icônes de perso", tentative de
  compléter les 33 légendes restantes (+ **Vector**, dont l'absence complète
  de la `Legends` array — armes, stats, portrait — s'est révélée être un
  vrai bug d'oubli de la Version 17, corrigé indépendamment du reste) en
  récupérant leur "splash art" officiel (`brawlhalla.com/legends/<slug>/`,
  variante `-150x150` générée par WordPress, ou recadrage carré manuel via
  `System.Drawing` en PowerShell quand cette variante n'existait pas — ex.
  Ezio, Lady Vera). **Rejeté par l'utilisateur** : les 35 portraits déjà en
  place (30 d'origine + 5 de la Version 17) sont un rendu "Roster Pose"
  statique (buste, pose neutre), alors que le "splash art" est une
  illustration d'action dynamique en plein mouvement — recadrer un carré
  dedans ne les rend pas stylistiquement cohérents avec le reste, même si le
  résultat est techniquement une image valide du bon personnage. Les 34
  fichiers ajoutés ont été supprimés ; `MainWindow.UpdateLegendPortrait`
  retombe sur son masquage propre existant pour ces personnages (voir
  Version 17), qui reste préférable à un mélange de styles. Les correctifs
  de données de Vector (`LegendComboPresets.Legends`/`LegendWeapons`,
  `LegendStats.Table`) sont conservés, seul son portrait a été retiré.
  Le "Roster Pose" ne semble plus exister comme asset séparé pour les
  légendes ajoutées après un certain point (leurs pages n'exposent que du
  splash art, contrairement aux légendes plus anciennes qui ont les deux) —
  à rouvrir seulement si une source de portraits au même gabarit que les 35
  existants est trouvée, pas en réutilisant du splash art recadré.
- Version 20 (portraits — les 69 légendes, source unique) : la Version 19
  cherchait un mapping nom-de-code→légende pour deviner des URLs de "Roster
  Pose" au cas par cas, ce qui ne marchait pas pour les légendes récentes.
  Bonne piste trouvée en récupérant la page `brawlhalla.com/legends`
  (rendue, pas juste `curl` — c'est une app SvelteKit) : chaque `<img>` de la
  grille roster a un attribut `alt="<Nom de légende>"` collé directement à
  son URL CDN, ce qui donne un mapping nom→image fiable et exhaustif en une
  seule page, sans deviner de nom de code. Contrairement à ce que Version 19
  supposait, **les 69 légendes ont bien une image dans ce style** (portrait
  serré sur le visage/tête, coins déjà arrondis, fond transparent) — les 4
  plus récentes (Loki, Seven, Imugi, Priya) utilisent juste un nom de
  fichier différent ("...-icon.png" au lieu de "a_Roster_Pose_...") mais un
  cadrage identique, vérifié visuellement avant d'appliquer à l'ensemble.
  Suite à un retour utilisateur explicite ("tu mets ça partout, oublie les
  portraits, je veux juste qu'ils aient tous le même style") : les 69
  fichiers de `Assets/Legends/` ont été **entièrement remplacés** par cette
  unique source (y compris les 35 déjà présents, pour garantir que tout le
  monde vient du même endroit plutôt que de mélanger deux origines) — plus
  aucun légend sans portrait, plus de fallback de masquage à gérer dans
  `MainWindow.UpdateLegendPortrait`.
- Version 21 (refonte UI — accueil unifié + accès depuis l'overlay) : retour
  utilisateur que l'app restait "excessivement peu facile à prendre en main"
  malgré les Versions 11-17 — demande explicite de "refonte totale" vers un
  écran d'accueil avec personnages sélectionnables par portrait. Plan détaillé
  produit et validé avant tout code (voir artifact publié en session).
  1. **Fusion `StartupWindow` + `OnboardingWindow` → `DashboardWindow`** : les
     deux fenêtres faisaient déjà l'essentiel de ce qui était demandé (grille
     de portraits, fourche premier lancement) mais vivaient séparément avec
     un choix de fenêtre décidé dans `App.xaml.cs` — fusionnées en une seule
     classe pilotée par un paramètre de constructeur (voir bullet
     `DashboardWindow` dans l'Architecture ci-dessus pour le détail complet).
     Fichiers `StartupWindow.xaml(.cs)`/`OnboardingWindow.xaml(.cs)` supprimés
     après vérification qu'aucune référence fonctionnelle n'y pointait
     ailleurs dans le code (seulement des commentaires, mis à jour au passage).
  2. **Trou trouvé par l'utilisateur en testant** : une fois l'overlay lancé
     en jeu, aucun moyen de revenir à cette Dashboard pour changer de
     personnage/combo — `Ctrl+Alt+U`/clic tray/bouton ⚙ de la barre de
     contrôle overlay n'ouvraient que l'ancien `ControlPanelWindow` (liste de
     combos à plat dans un onglet, pas la grille de portraits). Corrigé en
     rouvrant `DashboardWindow` (mode `inGameMode: true`) depuis ces trois
     points d'entrée à la place de `ControlPanelWindow` — voir le détail du
     bug évité (double overlay) et le comportement à deux modes dans le
     bullet `DashboardWindow` ci-dessus. `ControlPanelWindow` reste
     atteignable depuis le bouton « Réglages avancés » de la Dashboard, pour
     les réglages fins (touches/apparence) qu'elle ne couvre pas.
  3. **Barre de contrôle overlay** (`OverlayControlBarWindow`) : ajout d'un
     badge de mode persistant (texte doré "Tutoriel"/"Historique"/"Grand
     affichage" à côté des boutons quand la barre est dépliée) — jusque-là
     seul un flash de 1.5s sur `MainWindow` indiquait le mode juste après un
     changement, rien de permanent en cas de doute a posteriori.
  4. **Réactivation des modes Historique/Grand affichage explicitement
     refusée pour l'instant** : proposée comme étape suivante du plan, mais
     l'utilisateur a rappelé qu'ils avaient été désactivés sur signalement
     esthétique explicite (Version 17, "c'est immonde") — les remettre tels
     quels aurait juste réintroduit le même problème. Reste sur la liste des
     pistes non faites, à ne reprendre qu'après un retravail visuel dédié.
  Vérifié à chaque étape par build (`dotnet build -c Release`, 0
  avertissement/erreur imputable aux changements) et par test visuel réel :
  capture d'écran de la grille de portraits (scroll fonctionnel), clic sur un
  portrait via UI Automation (armes/combos mis à jour en direct, vérifié par
  capture avant/après), lancement de l'overlay puis réouverture de la
  Dashboard *depuis* l'overlay en cours (bouton ⚙), clic sur « ✓ Appliquer »
  suivi d'une énumération des fenêtres du processus pour confirmer qu'un seul
  `MainWindow` existe (pas de doublon).
- Version 22 (audit d'amélioration + suppression définitive des modes
  Historique/Grand affichage) : suite à la demande "analyse approfondie de
  toutes les features, pistes d'amélioration triées par utilité", un audit
  complet (`docs/amelioration.md`) a listé des pistes de Faible à Critique.
  L'utilisateur a demandé de "patcher" en priorisant les critiques. Deux
  points Critique traités :
  1. **Dérive doc/code** (le point faible historique du projet, déjà payé
     deux fois — combos hallucinées "Version 9", citation fabriquée
     "Correctif Faux") : le docstring de `Combo.MatchMode` affirmait encore
     "Only Strict is implemented for the MVP" alors que `IgnoreExtraneous`
     est bien implémenté depuis le 2026-07-25 — corrigé. Champ mort
     `_historySlotMode3` (jamais ajouté à l'arbre visuel) supprimé.
  2. **Modes Historique/Grand affichage bloqués sans échéance** depuis la
     Version 17 (`AppState.CombosOnlyMode = true`) : l'utilisateur a
     explicitement choisi de les **supprimer entièrement** plutôt que de les
     retravailler visuellement (voir option recommandée, choisie telle
     quelle). Suppression complète du code des deux modes : `AppState.
     ActiveMode`/`SetMode`/`CycleMode`/`ModeChanged`/`CombosOnlyMode`,
     `OverlaySettings.Position`/`OverlayPosition`/`FreeLeft`/`FreeTop`/
     `DefaultMode`/`FavoriteModes`, tout le code de construction/affichage
     des modes 0/1 dans `MainWindow.xaml.cs` (`BuildMode1Panel`,
     `BuildMovementCluster`, `BuildActionButtons`, `BuildKeycap`,
     `BuildActionKeycapContent`, `BuildHistoryContainer`, `BuildMode2Layer`,
     `BuildBigArrow`, `BuildBigKeycap`, `RegisterBrush`/
     `RegisterColorSwapBrush`, `RepositionPanel`, les handlers de drag
     `Mode1Panel_*`, l'historique visuel — `_historyPanel`,
     `CreateHistoryEntry`, `BindsEqual`, `Pulse`, `ResetHistoryClearTimer`,
     `ClearHistoryGraduallyAsync` — devenu sans objet puisqu'il n'était
     affiché qu'en mode Historique), ainsi que les sélecteurs de mode dans
     `ControlPanelWindow` (mode par défaut, modes favoris du cycle rapide,
     position de l'overlay) et `DashboardWindow` (3 boutons radio de mode).
     Le chord manette `Start + Y` et le raccourci `Ctrl+Alt+P` (changer de
     mode) supprimés en conséquence — plus de sens avec un seul mode.
     **Découverte en cours de route** : `ShowModeBadge` avait été supprimé
     par erreur en pensant qu'il ne servait qu'au changement de mode, alors
     que c'est un mécanisme de toast générique réutilisé pour de nombreux
     feedbacks ponctuels (miroir détecté, révélation quiz, combo maîtrisé,
     enregistrement) — restauré et renommé `ShowToast`/`_toastBadge` pour
     refléter son vrai rôle générique. `RegisterMove` (log de session pour
     l'export CSV + enregistrement de combo `Ctrl+Alt+R`) et
     `QueuePendingBind`/`_pendingBinds`/`_comboTimer` (regroupement de
     touches quasi-simultanées, alimente `ComboRunner`) sont restés intacts
     : ils ne dépendaient pas de l'affichage de l'historique visuel, contrairement
     à ce qu'un nettoyage trop rapide aurait pu supposer. Vérifié par build
     (`dotnet build -c Release`, 0 avertissement/erreur) et par grep sur tout
     le repo confirmant l'absence de référence résiduelle aux symboles
     supprimés. **Pas testé en jeu** à ce stade (l'utilisateur a signalé un
     problème d'écran pendant la session, l'app n'a donc pas été relancée par
     prudence) — à tester manuellement avant de considérer ce chantier
     entièrement clos.
  Suite de la même session, demande explicite de "patcher en priorisant les
  critiques" : les 3 autres pistes Élevé de `docs/amelioration.md` traitées
  (la 4ᵉ, combos de légende, explicitement mise en pause — aucune source
  fournie quand demandé) plus 4 pistes Moyen/Faible :
  - **Détection de conflit de touche en temps réel** (`ControlPanelWindow`,
    onglet Touches) : chaque champ Keys se borde en rouge dès qu'une touche
    saisie est aussi utilisée par une autre action, recalculé à chaque
    frappe (`RecomputeKeyConflicts`) — plus besoin d'attendre le clic sur
    "Enregistrer" pour le découvrir. Purement visuel, la validation
    bloquante existante à la sauvegarde reste inchangée.
  - **Raccourcis clavier globaux rebindables** : les six raccourcis restés
    en dur (`VK_O`/`VK_R`/`VK_U`/`VK_I`/`VK_H`/`VK_M`) sont devenus des
    champs `OverlaySettings.LockVk`/`RecordVk`/`DashboardVk`/`RevealVk`/
    `SuspendVk`/`HideVk`, sur le même modèle que `ComboNextVk`/`ComboPrevVk`
    déjà reconfigurables. Onglet Général (réglages avancés) : six lignes
    "Écouter" réutilisant la mécanique existante, avec détection de
    conflit entre les 8 raccourcis (`AllShortcutVks`) avant d'accepter une
    nouvelle touche. Bandeau de raccourcis in-overlay et onglet À propos
    mis à jour pour afficher les touches réellement configurées plutôt que
    des lettres en dur.
  - **Parcours, Chapitre 4 "Ton premier vrai combo"** (2 leçons) : à la
    différence des chapitres précédents, **aucune nouvelle donnée de jeu
    sourcée** — reprend littéralement deux combos Blasters déjà vérifiés
    dans `WeaponComboPresets.Table` (dLight>nLight puis dLight>sAir,
    3+ Dex, seuil le plus bas du fichier), c'est la pièce qui relie
    explicitement le Parcours au moteur `ComboRunner` que le reste de l'app
    utilise déjà. Chapitre 5 (technique avancées) toujours pas fait.
  - **Overlay invisible après inactivité** (`Settings.AutoHideEnabled`/
    `AutoHideIdleSeconds`, désactivé par défaut) : le panneau du mode
    Tutoriel s'estompe (opacité ~0.12, pas masqué comme
    `AppState.OverlayHidden`) après N secondes sans input, poll
    indépendant du clavier toutes les 500ms (`_autoHideCheckTimer`, même
    principe que `CheckAbandon` côté `ComboRunner`) — redevient plein
    dès le prochain appui.
  - **Thèmes de couleur en un clic** (onglet Touches) : 4 palettes
    prédéfinies (Défaut/Néon/Pastel/Doré) remplissent d'un coup les champs
    Couleur de toutes les actions, par position dans `AppState.Binds` —
    rien n'est sauvegardé tant que "Enregistrer les touches" n'est pas
    cliqué, comme n'importe quel autre changement de ce formulaire.
  - **Fichier de log de diagnostic** (`Config/DiagnosticLog.cs`,
    `diagnostic.log` à côté de l'exe) : chaque exception non gérée
    (`DispatcherUnhandledException` + `AppDomain.UnhandledException` pour
    les threads hors UI, notamment les hooks) y est journalisée en plus de
    la `MessageBox` existante — jusque-là rien ne survivait à la fermeture
    de cette boîte de dialogue, impossible à diagnostiquer après coup pour
    un bug non reproduit en direct. Écriture best-effort (jamais de
    plantage à cause du log lui-même).
  - **Import/export de profil complet** (`ControlPanelWindow`, onglet
    Général) : touches du profil actif + tous les combos + une partie de
    l'apparence (`ProfileBundle`, classe privée) en un seul fichier JSON —
    jusque-là seul l'export/import combo par combo existait. Import
    **remplace entièrement** les touches du profil actif et la liste de
    combos (`AppState.ReplaceBinds`/`ReplaceCombos`, déjà existants) après
    confirmation explicite de l'utilisateur (nombre de touches/combos
    affiché dans la boîte de dialogue).
  Trois pistes Moyen non traitées faute de scope raisonnable en une passe
  (détection de tech skip et comparaison de sessions : conception nouvelle
  nécessaire ; mode hors-jeu assumé : décision de design à clarifier
  d'abord), une piste Faible volontairement laissée de côté (métronome —
  risque déjà identifié de réintroduire le timing comme condition d'échec).
  Vérifié par `dotnet build -c Release` (0 avertissement/erreur) après
  chaque changement — **pas testé en jeu** à ce stade (voir la note sur le
  problème d'écran survenu pendant cette session, plus haut) : à tester
  manuellement avant de considérer ce chantier entièrement clos. Détail
  complet et statut à jour de chaque piste dans `docs/amelioration.md`.

## Pistes évoquées mais pas demandées/faites
- ~~Réactiver les modes Historique et Grand affichage~~ — tranché en
  Version 22 : l'utilisateur a choisi de les supprimer définitivement plutôt
  que de les retravailler visuellement. Ne plus proposer cette piste.
- Le "Parcours" pédagogique (`docs/plan_ux_onboarding.md` §4) : Chapitre 0
  (prise en main de l'app), Chapitre 1 "Survivre" (Version 14), Chapitre 2
  "Frapper" (Version 15) et Chapitre 3 "Bouger" (Version 16) faits — voir
  Architecture (`ParcoursWindow`/`ParcoursCurriculum`) et les entrées
  d'historique correspondantes. Restent les chapitres 4 et 5 (premier vrai
  combo, techniques avancées). Nécessite de sourcer chaque
  affirmation de jeu avant écriture ; la Version 14 a montré qu'interroger
  directement l'utilisateur (qui joue réellement) puis recouper avec une
  source écrite est plus fiable que scraper le web en premier — reproduire
  cette méthode plutôt que fetch seul, avec test en jeu par l'utilisateur
  comme arbitre final en cas de désaccord entre sources.
- Raccourcis clavier rebindables (sortir les `VK_*` en dur de `MainWindow`
  vers `settings.json` + écran de réassignation) — classé P2 dans le plan
  UX, pas fait en Version 13.
- Guide de rythme progressif (métronome sur la barre de tolérance) — classé
  P2 dans le plan UX, pas fait en Version 13 : le timing a été retiré comme
  condition d'échec sur demande explicite de l'utilisateur (voir plus haut),
  toute réintroduction doit rester strictement indicative.
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
