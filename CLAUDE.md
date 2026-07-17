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

## Architecture

- **KeyboardHook.cs** — hook clavier global bas niveau (`WH_KEYBOARD_LL` via
  `SetWindowsHookEx`). Capture les touches même quand Brawlhalla a le focus.
  Expose deux events `KeyDown(int vkCode)` / `KeyUp(int vkCode)`.
- **KeyBind.cs** — modèle d'une action trackée : `Action` (nom affiché),
  `Keys` (liste de noms de touches .NET, une action peut avoir plusieurs
  touches, ex. esquive sur Shift *et* flèche bas), `Label` (texte sur le
  bouton/touche), `Symbol` (glyphe affiché dans l'historique), `Color`,
  `Group` (`"Movement"` ou `"Action"`), `Slot` (position dans le cluster
  directionnel : `Up`/`Left`/`Down`/`Right`, pour `Group="Movement"` seulement).
- **KeyBindConfig.cs** — mapping par défaut (voir plus bas) + chargement/écriture
  de `keybinds.json` (généré à côté de l'exe au premier lancement, dans
  `bin\Release\net8.0-windows\`). Si tu changes le schéma du modèle `KeyBind`,
  **supprime le `keybinds.json` généré** pour qu'il se régénère avec les
  nouveaux champs (sinon `System.Text.Json` charge un objet incomplet).
- **MainWindow.xaml / .xaml.cs** — la fenêtre overlay :
  - Transparente, `Topmost`, sans bordure, cachée du alt-tab/taskbar
    (`WS_EX_TOOLWINDOW`).
  - **Click-through par défaut** (`WS_EX_TRANSPARENT`) : les clics passent au
    jeu en dessous. `Ctrl+Alt+L` bascule en mode "déverrouillé" (déplaçable à
    la souris, cesse d'être click-through) pour repositionner l'overlay, puis
    reverrouille.
  - Repositionnée en bas-gauche de l'écran à chaque changement de taille
    (`SizeChanged` → recalcule `Top` par rapport à `SystemParameters.WorkArea`).
  - Layout (de gauche à droite) : cluster directionnel ZQSD en triangle
    inversé (Haut en haut, Gauche/Bas/Droite en dessous) → gros boutons
    d'action (Saut, Att. légère, Att. forte, Esquive, Lancer, Taunt) → séparateur
    vertical → historique des coups.
  - Chaque touche/bouton s'allume (opacity 0.25 → 1.0 sur sa `SolidColorBrush`)
    tant qu'elle est physiquement enfoncée.
  - **Historique** : une ligne par appui, symbole + nom entre parenthèses
    (ex. `⚡ (Att. légère)`), les plus récentes en bas, limité à 12 lignes
    (`MaxHistoryEntries`), fondu d'entrée de 120ms.
  - **Anti-spam / anti auto-répétition** :
    - L'auto-répétition Windows (rester appuyé → rafale de `WM_KEYDOWN`) est
      filtrée via un `HashSet<int> _pressedVks` : un seul événement d'historique
      par appui physique réel.
    - Le **mash/spam volontaire** (ex. marteler attaque légère pendant un combo)
      est fusionné : si la même action revient dans les 500ms
      (`MergeWindow`), on incrémente un compteur sur la dernière ligne
      (`Att. légère ×3`) au lieu d'empiler des lignes, avec un petit flash
      visuel (`Pulse`). Une action différente entre-temps casse la fusion.

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
(`Left`, `Right`, `Up`, `Down`, `Space`, `LeftShift`, `Q`, `Z`, `S`, `D`, `G`, `L`…).

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

## Pistes évoquées mais pas demandées/faites

- Lancement automatique au démarrage de Windows.
- Icône dans la barre système (pour fermer/verrouiller sans le raccourci
  clavier `Ctrl+Alt+L`).
- Support manette (XInput) en plus du clavier.
- Pas de tests automatisés — vérifications faites manuellement via capture
  d'écran + simulation d'appuis clavier (`keybd_event` par P/Invoke depuis
  PowerShell) pendant le développement.
