# Plan — Mode Tutoriel (combos) & Refonte UI

> **Statut : implémenté.** Ce document décrit le design d'origine (utile pour
> comprendre les choix), mais l'état actuel du code fait foi — voir
> `CLAUDE.md` (sections "Structure des dossiers", "Architecture",
> "Historique des décisions") pour ce qui existe réellement aujourd'hui,
> y compris les ajustements faits depuis (raccourci de verrouillage passé à
> `Ctrl+Alt+O`, garde-fous de validation ajoutés, fichiers réorganisés en
> dossiers).

Document de design pour deux chantiers liés :
1. Un **mode Tutoriel** avec des combos préenregistrés, validation d'input en temps réel, échec → reprise au début.
2. Une **refonte de l'UI** de l'overlay pour supporter proprement plusieurs modes, un accès aux paramètres, etc.

Contexte technique de départ (voir `CLAUDE.md`) : app WPF .NET 8, hook clavier bas niveau
(`KeyboardHook.cs`), modèle d'action `KeyBind` (`KeyBind.cs` / `KeyBindConfig.cs`), overlay
actuel avec 2 modes (historique+cluster / grosses flèches) basculés au clavier, tout construit
en C# dans `MainWindow.xaml.cs` (pas de vrai système de vues/pages).

---

## 1. Vision du mode Tutoriel

### 1.1 Principe

- Une **combo** = une séquence ordonnée d'inputs (touches ou combinaisons de touches déjà
  modélisées par `KeyBind`), avec une fenêtre de tolérance de timing par étape.
- L'utilisateur **crée et enregistre ses propres combos** (pas celles de Brawlhalla par
  défaut — l'idée de départ "scrap les combos Brawlhalla" est abandonnée : on ne les
  intègre pas du tout, seulement des combos custom définies par l'utilisateur).
- En mode Tutoriel, l'overlay affiche la combo sélectionnée comme une **partition
  d'inputs** à jouer : étape courante surlignée, étapes suivantes grisées, étapes
  réussies en vert.
- À chaque input du joueur :
  - Si l'input correspond à l'étape attendue → elle passe en "réussie", on avance à
    l'étape suivante, petit feedback positif (flash vert, son optionnel plus tard).
  - Si l'input ne correspond pas (mauvaise touche, ou bonne touche mais hors fenêtre de
    timing) → échec, feedback rouge, **la combo se réinitialise et repart de l'étape 1**.
  - Une fois la dernière étape validée → feedback de réussite (combo complète), petite
    pause, puis reset automatique pour rejouer (drill en boucle) ou passage à la combo
    suivante si un enchaînement de plusieurs combos est configuré.

### 1.2 Modèle de données

Nouveau fichier `ComboStep.cs` / `Combo.cs`, persistés dans `combos.json` à côté de l'exe
(même pattern que `keybinds.json`).

```csharp
public sealed class ComboStep
{
    // Une étape peut nécessiter une seule touche ou une combinaison simultanée
    // (ex: Esquive = Shift+Bas), donc on référence des KeyBind.Action, pas des vk bruts.
    public List<string> RequiredActions { get; set; } = new(); // ex: ["Esquive"] ou ["Gauche","Att. légère"]

    // Fenêtre de tolérance après la fin de l'étape précédente pour jouer cette étape.
    // Null = pas de limite de temps (attend indéfiniment).
    public int? MaxDelayMs { get; set; }

    // Fenêtre de tolérance minimale (pour forcer un délai, ex: un buffer volontaire).
    public int? MinDelayMs { get; set; }
}

public sealed class Combo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";           // ex: "Combo Neutral Light x3"
    public string Description { get; set; } = "";     // optionnel, note libre
    public List<ComboStep> Steps { get; set; } = new();
    public int DefaultToleranceMs { get; set; } = 400; // valeur par défaut si un step ne précise pas MaxDelayMs
}
```

`combos.json` = `List<Combo>`. Fichier vide/absent au premier lancement → l'app démarre
avec zéro combo et invite l'utilisateur à en créer une (pas de valeurs "par défaut"
puisqu'on ne veut justement pas des combos Brawlhalla préfaites).

### 1.3 Comment on enregistre une combo (UX de création)

Deux façons, à proposer toutes les deux (la 1 est indispensable pour le MVP, la 2 est un
"nice to have") :

**A. Enregistrement en direct ("Record")** — le plus naturel pour ce projet puisque le
hook clavier existe déjà :
1. Depuis l'écran Paramètres → onglet Combos → "Nouvelle combo" → "Enregistrer".
2. L'utilisateur joue la séquence de touches en vrai (dans l'overlay, pas en jeu — ou en
   jeu, peu importe puisque le hook est global).
3. Chaque frappe (ou combinaison quasi simultanée, réutilisant la logique `ComboWindow`
   déjà présente dans `MainWindow.xaml.cs`) devient une `ComboStep`, avec le délai réel
   mesuré par rapport à l'étape précédente, converti en fenêtre de tolérance
   (`MaxDelayMs = délai mesuré × marge, ex. 1.6`, éditable ensuite).
4. "Stop" pour terminer l'enregistrement, puis un écran de relecture/édition (liste des
   étapes, possibilité de supprimer une étape parasite, ajuster les tolérances, renommer).

**B. Édition manuelle (liste déroulante d'actions)** — pour créer/corriger une combo sans
  avoir à la rejouer physiquement : un éditeur de liste où chaque ligne = un dropdown
  d'actions disponibles (tirées de `KeyBindConfig`) + un champ de tolérance en ms.
  Utile pour ajuster une combo enregistrée en mode A, ou en construire une de zéro.

### 1.4 Moteur de validation en jeu

Nouvelle classe `ComboRunner.cs` (logique pure, testable indépendamment de l'UI) :

```csharp
public sealed class ComboRunner
{
    public Combo Combo { get; }
    public int CurrentStepIndex { get; private set; }
    public ComboRunState State { get; private set; } // Waiting, InProgress, Success, Failed

    public event Action<int>? StepSucceeded;   // index de l'étape validée
    public event Action<int>? StepFailed;      // index de l'étape ratée
    public event Action? ComboCompleted;
    public event Action? ComboReset;

    public void Feed(List<KeyBind> pressedBindsThisTick, DateTime timestamp) { ... }
    public void Reset() { ... }
}
```

- Se branche sur le même flux d'événements que `RegisterMove` dans `MainWindow.xaml.cs`
  (réutilise le regroupement "combo quasi simultané" déjà en place via `_pendingBinds` /
  `ComboWindow`) : chaque "coup joué" détecté par l'overlay est envoyé à `ComboRunner.Feed`.
- Logique de correspondance : compare l'ensemble d'actions jouées à `RequiredActions` de
  l'étape courante (ordre des touches dans une combinaison simultanée non significatif,
  mais l'ensemble doit correspondre exactement — un input avec une touche en trop est un
  échec, pour éviter les faux positifs de spam).
- Gestion du timing : à chaque `StepSucceeded`, on horodate ; si l'input suivant arrive
  après `MaxDelayMs`, il est automatiquement traité comme un échec même s'il matchait, et
  on reset — évite de valider une combo "trop lente" comme si elle était correcte.
- Un input totalement hors combo (aucune correspondance avec l'étape courante ni la
  suivante) : si le tutoriel est censé être strict, ça compte comme un échec (reset). Un
  mode "tolérant" pourra ignorer les touches de mouvement pur pendant qu'on attend une
  attaque, configurable plus tard — **hors scope du MVP**, mais prévoir un enum
  `MatchMode { Strict, IgnoreExtraneous }` sur `Combo` pour ne pas se fermer la porte.

### 1.5 Affichage du mode Tutoriel

Nouveau panneau dédié (mode 3, mais repensé proprement dans la refonte UI ci-dessous),
affiché en overlay comme les modes existants :

- **Rangée horizontale d'étapes** (façon "notation de combo" des jeux de baston/fighting
  games) : chaque étape = une pastille avec le `Symbol` (et les symboles combinés si
  plusieurs actions simultanées, ex. `💨` pour Esquive), reliées par des petites flèches
  `→`.
  - Étape à venir : gris/opacité faible.
  - Étape courante : surlignée (bordure pulsante ou couleur vive), éventuellement avec un
    petit timer visuel (barre qui se vide) si `MaxDelayMs` est défini, pour donner un
    signal visuel de la fenêtre de tolérance qui se referme.
  - Étape réussie : verte, coche.
  - Étape ratée : rouge, flash bref, puis toute la rangée se réinitialise (léger shake).
- **Compteur de séries** : nombre de réussites consécutives de la combo entière (pour la
  dimension "drill"/practice), remis à zéro à l'échec ou conservé au choix (option "garder
  le score même après un échec" pour ne pas décourager le joueur qui s'entraîne).
- **Sélecteur de combo actif** : petite liste déroulante/onglets en haut du panneau
  Tutoriel pour changer de combo sans repasser par les paramètres.
- **Historique de touches classique** (déjà existant) reste visible en dessous ou à côté,
  pour que l'utilisateur comprenne *pourquoi* une étape a été jugée ratée (voir ce qu'il a
  réellement pressé).

### 1.6 Où ça vit dans le layout

Le mode Tutoriel devient un troisième mode d'affichage à part entière, cohérent avec la
bascule Mode 1 / Mode 2 déjà en place, mais géré proprement par le système d'onglets décrit
dans la partie UI (plus de bascule uniquement au raccourci clavier — accessible aussi via
un vrai menu, cf. section 2).

---

## 2. Refonte de l'UI

### 2.1 Constat sur l'existant

Aujourd'hui :
- Toute l'interface (mode 1, mode 2, hint text, drag) est construite à la main en C# dans
  `MainWindow.xaml.cs`, avec un seul `Window`/`Canvas` — pas de fenêtre de paramètres, pas
  de navigation, tout passe par des raccourcis clavier globaux (`Ctrl+Alt+O` verrouiller,
  `Ctrl+Alt+P` changer de mode).
- Éditer les touches ou créer des combos suppose actuellement d'éditer `keybinds.json` à
  la main, texte brut, sans validation ni retour visuel.
- Pas moyen de savoir "dans quel mode je suis" ou "comment changer" sans avoir lu le hint
  text ou la doc.

Avec l'ajout du mode Tutoriel + de la gestion de combos, ce modèle ne passe plus à
l'échelle : il faut un vrai système de paramètres et une navigation claire, tout en
gardant l'overlay lui-même minimaliste et non intrusif pendant le jeu (c'est tout l'intérêt
de l'outil).

### 2.2 Principe directeur : séparer "Overlay" et "Panneau de contrôle"

Deux fenêtres distinctes, avec des responsabilités différentes :

1. **Fenêtre Overlay** (ce qui existe déjà, épurée) :
   - Reste transparente, click-through par défaut, topmost, sans bordure.
   - N'affiche QUE le contenu du mode actif (touches qui s'allument, grosses flèches, ou
     panneau combo du tutoriel) + éventuellement un petit badge discret en overlay
     (ex. coin de l'écran) indiquant le mode actif et un raccourci pour ouvrir le panneau
     de contrôle, visible seulement en mode déverrouillé.
   - Ne contient plus aucune UI de configuration.

2. **Fenêtre Panneau de contrôle** ("Control Panel" / Paramètres) — **nouvelle fenêtre**,
   normale (pas transparente, bordée, redimensionnable, dans la taskbar), ouverte via :
   - Une icône dans la **zone de notification (system tray)** — clic gauche ouvre/donne le
     focus au panneau, clic droit propose un menu contextuel rapide (Verrouiller/
     Déverrouiller, Changer de mode, Quitter). C'était listé comme "évoqué mais pas fait"
     dans `CLAUDE.md` — cette refonte est l'occasion de l'ajouter, car c'est le point
     d'entrée naturel pour tout le reste.
   - Le raccourci existant `Ctrl+Alt+O` reste pour déverrouiller/verrouiller rapidement
     l'overlay pendant une partie (workflow rapide en jeu), mais **n'ouvre plus** de
     réglages en overlay — pour ça il faut passer par le tray.
   - Un nouveau raccourci dédié, ex. `Ctrl+Alt+U` ("U" comme UI), pour ouvrir/fermer
     directement le panneau de contrôle sans toucher à la souris.

   Structure du panneau de contrôle (navigation latérale simple, style Settings classique) :
   - **Général** : verrouillage, position de l'overlay (reset position), lancement auto
     Windows (case à cocher — item "évoqué mais pas fait", à activer ici), choix du mode
     actif par défaut au démarrage.
   - **Touches** : éditeur visuel des `KeyBind` (remplace l'édition manuelle de
     `keybinds.json`) — liste des actions, pour chacune un bouton "réassigner" qui écoute
     la prochaine touche pressée (réutilise le hook existant), choix de couleur/symbole.
   - **Combos** : la gestion décrite en 1.3 — liste des combos enregistrées, bouton
     "Nouvelle combo" (Enregistrer / Éditer manuellement), édition/suppression/duplication,
     réordonnancement si on veut enchaîner plusieurs combos à la suite.
   - **Apparence** : taille/échelle de l'overlay, opacité globale, position (bas-gauche
     actuellement fixe → proposer bas-droite/haut-gauche/haut-droite ou position libre déjà
     permise par le drag).
   - **À propos** : version, rappel des raccourcis clavier.

### 2.3 Switch de mode "proprement"

Remplacer le seul raccourci `Ctrl+Alt+P` qui fait défiler les modes en boucle par :
- Un **sélecteur explicite** dans le panneau de contrôle (onglets ou boutons radio :
  "Historique", "Grandes flèches", "Tutoriel") — l'utilisateur voit le mode actif nommé,
  pas juste "mode 1/2".
- Conserver `Ctrl+Alt+P` en jeu comme raccourci de bascule rapide, mais en boucle sur
  uniquement les modes marqués "actifs/favoris" par l'utilisateur dans les paramètres (par
  défaut les 3), pour rester utilisable sans souris pendant une partie.
- Un badge discret et temporaire (auto-fade après ~1.5s) affiché dans l'overlay au moment
  du changement de mode, indiquant le nom du mode ("Mode : Tutoriel"), pour un feedback
  clair sans devoir ouvrir le panneau.

### 2.4 Impact technique (haut niveau, pas de code à ce stade)

- Introduire une séparation nette : `OverlayWindow` (renommage conceptuel de
  `MainWindow`) reste responsable de l'affichage in-game ; nouvelle `ControlPanelWindow`
  (WPF classique, XAML+bindings plutôt que tout construit en C#) héberge les réglages.
  Les deux partagent un état applicatif commun (liste de `KeyBind`, liste de `Combo`, mode
  actif, position/échelle) — prévoir un petit objet `AppState`/service partagé (singleton
  injecté ou simple classe statique au vu de la taille du projet) plutôt que de dupliquer
  l'état, pour que les changements faits dans le panneau se répercutent immédiatement sur
  l'overlay sans redémarrage.
- Ajouter `System.Windows.Forms.NotifyIcon` (ou équivalent WPF via un petit wrapper) pour
  l'icône de tray — nécessite de référencer `System.Windows.Forms` en plus de WPF, ce qui
  est courant et sans risque pour ce type d'app.
- `KeyBindConfig` et le futur `ComboConfig` (même pattern que `KeyBindConfig` mais pour
  `combos.json`) restent la source de vérité sur disque ; le panneau de contrôle
  écrit/relit via ces classes, l'overlay recharge en mémoire à chaque sauvegarde (pas
  besoin de watcher de fichier si tout passe par l'`AppState` partagé en mémoire).

### 2.5 Ce qui ne change pas

- Le comportement click-through / topmost / caché du alt-tab de l'overlay lui-même.
- Le hook clavier bas niveau global, seule source d'input (aucune raison de changer ça
  pour le mode Tutoriel — même mécanisme de capture, juste un nouveau consommateur du flux
  d'événements).
- Pas d'automatisation d'input à aucun moment (le mode Tutoriel ne fait qu'observer et
  comparer, jamais n'injecte quoi que ce soit) — donc aucun changement au raisonnement
  déjà établi sur l'absence de risque anti-cheat.

---

## 3. Découpage en étapes de réalisation (proposition, à valider avant de coder)

1. **Fondations données** : `Combo.cs`, `ComboStep.cs`, `ComboConfig.cs` (persistance
   `combos.json`, même pattern que `KeyBindConfig`).
2. **Moteur** : `ComboRunner.cs` + tests manuels via simulation clavier (comme le reste du
   projet, pas de suite de tests automatisés prévue) pour valider la logique de séquence/
   timing/reset indépendamment de tout affichage.
3. **Affichage Tutoriel minimal** : nouveau panneau dans l'overlay existant (rangée
   d'étapes + branchement sur `ComboRunner`), accessible via le raccourci de bascule de
   mode existant, sans encore de vraie UI de création de combo (combos créées à la main
   dans `combos.json` pour cette étape, comme `keybinds.json` aujourd'hui).
4. **Enregistrement de combo** (option A de 1.3) : UI minimale de capture, pas encore
   intégrée au panneau de contrôle complet — peut être une fenêtre simple dédiée en
   attendant.
5. **Refonte UI complète** : `ControlPanelWindow`, tray icon, `AppState` partagé, éditeur
   de touches visuel, éditeur de combos manuel, sélecteur de mode explicite. C'est le plus
   gros chantier — probablement à découper lui-même en sous-étapes (tray + panneau vide →
   onglet Général → onglet Touches → onglet Combos → onglet Apparence).
6. **Polish** : badge de changement de mode, compteur de séries persistant, lancement auto
   Windows, mode `IgnoreExtraneous` si le mode strict s'avère trop frustrant à l'usage.

Chaque étape reste testable/jouable indépendamment (aligné avec la méthode de vérif
manuelle déjà utilisée sur ce projet — capture d'écran + simulation clavier).
