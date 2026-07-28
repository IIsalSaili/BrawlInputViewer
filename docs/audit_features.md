# Audit complet — Features actuelles & pistes d'évolution (2026-07-25)

> Remplace l'audit du 2026-07-18 : la quasi-totalité de ses pistes ont depuis
> été implémentées (voir `CLAUDE.md`, sections Version 8/9/10 et les
> correctifs qui suivent) — sons, barre de tolérance, `ComboFailReason`,
> enchaînement de combos, stats de précision, import/export de combo, profils
> multiples, manette XInput, session/CSV, etc. Cet audit repart d'une lecture
> **intégrale** du code actuel (`Core/*.cs`, `Config/*.cs`, `Models/*.cs`,
> `Windows/*.xaml.cs`, ~4700 lignes au total) plutôt que de la doc, pour
> distinguer ce qui est *vraiment* branché de ce qui est seulement documenté
> comme tel.

---

## 0. Doctrine anti-ban (rappel, inchangée)

Hook clavier bas niveau (`WH_KEYBOARD_LL`) + polling manette (`XInputGetState`)
= lecture passive user-mode, aucune injection, aucune automatisation. Même
catégorie que OBS/Keypress Hero, hors radar Easy Anti-Cheat. Grille de
décision complète conservée de l'audit précédent : ✅ lecture passive
clavier/manette/process-liste, ✅ overlay WPF classique ; ❌ lecture mémoire du
process du jeu, ❌ injection DLL/hook du renderer, ❌ automatisation d'input,
❌ modification de fichiers du jeu. Toutes les idées ci-dessous ont été
passées par cette grille.

---

## 1. Constats — bugs et incohérences réels trouvés à la lecture du code

> **Statut (2026-07-25, même jour) : les 5 points de cette section sont
> corrigés.** Conservés ci-dessous tels quels (diagnostic d'origine) pour
> garder la trace de ce qui a été trouvé et pourquoi — voir l'entrée
> "Correctifs suite à l'audit du 2026-07-25" dans l'historique des décisions
> de `CLAUDE.md` pour le détail de chaque correctif.

Ce sont des faits vérifiés dans le code au moment de l'audit, pas des pistes
d'amélioration — traités en priorité, avant toute nouvelle feature.

### 1.1 [CORRIGÉ] `Combo.MatchMode` est un réglage mort — `ComboRunner.Feed` ne le lit jamais

`ComboEditorWindow.xaml.cs:69-76` expose un vrai sélecteur "Strict / Tolérant
(ignore le mouvement pur)" qui écrit dans `Combo.MatchMode`
(`ComboEditorWindow.xaml.cs:333`). Mais **`ComboRunner.Feed`
(`Core/ComboRunner.cs:78-169`) ne référence `Combo.MatchMode` nulle part** —
`grep MatchMode` sur tout `Core/` ne remonte aucune occurrence en dehors du
modèle et de l'éditeur. La tolérance au mouvement pur (`Group == "Movement"`
jamais bloquant, voir docstring de classe `ComboRunner.cs:32-36`) est en fait
**déjà le comportement systématique**, quel que soit le choix fait dans
l'éditeur — donc choisir "Strict" ou "Tolérant" produit exactement le même
résultat en jeu. L'utilisateur qui bascule ce menu croit changer un
comportement qui ne bouge pas.
- **Impact** : confusion pure (le réglage semble fonctionner : il se
  sauvegarde, se recharge), aucun risque de sécurité/anti-ban, mais c'est un
  gaspillage d'UI et une source de rapport de bug "ça ne change rien".
- **Correctif appliqué** : option (b) — `ComboRunner.Feed` calcule maintenant
  un `extraMovement` (directions tenues en plus de ce qui est requis) et,
  en `MatchMode.Strict`, le traite comme un mauvais input (échec/reset). En
  `IgnoreExtraneous`, comportement inchangé (mouvement pur toujours toléré).
  Le sélecteur de l'éditeur a donc désormais un effet réel.

### 1.2 [CORRIGÉ] `CLAUDE.md` documentait une feature (`AttackRecoveryLockMs`, 180ms) qui n'existe plus dans le code

`CLAUDE.md:124-129` (section `ComboRunner`) et `:429` (historique "Version 8")
décrivent une tolérance de récupération basée sur un délai de 180ms
(`AttackRecoveryLockMs`) après une attaque. **Ce symbole n'existe nulle part
dans `Core/ComboRunner.cs`** (confirmé par grep sur tout le repo — seules les
mentions dans `CLAUDE.md` remontent). Le mécanisme réellement présent
aujourd'hui est différent et plus simple : `_lastConsumedActionKeys`
(`ComboRunner.cs:67`, `:100`, `:151`) tolère un mash/répétition du **même**
bouton d'action qui vient de valider l'étape précédente, **sans aucune
fenêtre de temps** — cohérent avec le retrait du timing comme condition
d'échec (déjà documenté dans `CLAUDE.md` juste au-dessus). Le passage de
"tolérance par délai de 180ms" à "tolérance par identité du dernier bouton
validé, sans délai" est un vrai changement de comportement qui n'a jamais été
consigné dans l'historique des décisions — `CLAUDE.md` décrit donc à la fois
l'ancien et laisse croire qu'il est toujours actif.
- **Impact** : aucun sur le comportement de l'app (le code fait foi, pas la
  doc), mais un futur travail sur `ComboRunner` risque de "corriger" un
  symbole qui n'existe déjà plus, ou de mal comprendre le mécanisme réel de
  tolérance en le cherchant sous le nom `AttackRecoveryLockMs`.
- **Correctif appliqué** : `CLAUDE.md` (section `ComboRunner` + historique
  Version 8) mis à jour pour décrire `_lastConsumedActionKeys` tel qu'il
  existe réellement, avec une note explicite que `AttackRecoveryLockMs` a été
  remplacé et n'existe plus.

### 1.3 [CORRIGÉ] Une combo créée manuellement ne pouvait jamais être associée à une arme

`ComboEditorWindow.SaveAndClose` (`ComboEditorWindow.xaml.cs:330`) fixe
`Weapon = _existing?.Weapon ?? ""` — aucun contrôle dans l'éditeur ne permet
de choisir l'arme. Le champ `Combo.Weapon` n'est donc rempli que par
`WeaponComboPresets`/`AppState.ImportWeaponPresets`. Une combo personnelle
enregistrée en direct ou créée à la main reste **structurellement invisible**
dès qu'un filtre d'arme est actif dans l'onglet Combos ou en mode Tutoriel
(`AppState.FilteredComboIndices`, `ControlPanelWindow.RefreshCombosList`) —
elle n'apparaît que sous "Toutes les armes". Pas un crash, mais une combo
perso peut sembler "disparue" pour un utilisateur qui a un filtre d'arme actif
au moment de la créer, sans message expliquant pourquoi.
- **Correctif appliqué** : `ComboBox` "Arme (optionnel)" ajouté dans
  `ComboEditorWindow`, pré-rempli avec `WeaponComboPresets.Weapons` + une
  option "(aucune)", entre Description et la ligne Tolérance/Mode.

### 1.4 [CORRIGÉ] Overlay limité à l'écran principal (`SystemParameters.WorkArea`)

`MainWindow_Loaded` (`MainWindow.xaml.cs:1207-1219`) utilisait
`SystemParameters.WorkArea`, qui renvoie toujours la zone de travail de
l'écran **principal** Windows. Sur un setup multi-écran où Brawlhalla tourne
sur un écran secondaire (fréquent avec un moniteur de jeu dédié), l'overlay
restait positionné/dimensionné par rapport au moniteur principal et ne
suivait pas la fenêtre du jeu. Déjà signalé dans l'audit précédent (section
2.2).
- **Correctif appliqué** : `Settings.MonitorIndex` (-1 = écran principal,
  défaut) + sélecteur "Écran" dans l'onglet Apparence. `MainWindow
  .GetTargetWorkArea`/`ApplyWorkArea` résolvent l'écran choisi via
  `System.Windows.Forms.Screen.AllScreens`, appliqué au chargement et à
  chaud si changé pendant que l'overlay tourne. Conversion pixels→unités WPF
  approximée par le ratio d'échelle de l'écran principal — correcte sur la
  plupart des setups (même DPI partout), pas garantie si les écrans ont des
  échelles différentes (limite documentée dans le commentaire du code).

### 1.5 [CORRIGÉ] Aucune revue/annulation après un enregistrement de combo

`SaveRecordedCombo` (`MainWindow.xaml.cs:1802-1838`) sauvegardait directement
dans `AppState.Combos` dès l'arrêt de l'enregistrement (`Ctrl+Alt+R`), sans
écran de relecture — contrairement à ce que prévoyait `docs/plan.md §1.3.A`
("écran de relecture/édition : liste des étapes, suppression d'une étape
parasite..."). Une combo mal enregistrée (input parasite, mauvais timing
capturé comme tolérance) ne pouvait être corrigée qu'en rouvrant
`ComboEditorWindow` après coup depuis la liste.
- **Correctif appliqué** : arrêter l'enregistrement ouvre maintenant
  `ComboEditorWindow` (nouveau paramètre `isRecordingReview: true`) pré-rempli
  avec les étapes capturées, titre "Vérifier la combo enregistrée", boutons
  "Valider et enregistrer" / "Rejeter l'enregistrement" — rien n'est écrit
  dans `combos.json` tant que l'utilisateur n'a pas validé cet écran.

---

## 2. Améliorations restantes sur les features existantes

Ce qui restait de pertinent de l'audit précédent, purgé de tout ce qui a été
implémenté depuis.

### 2.1 Panneau de contrôle / Touches
- **Détection de conflit de touche en temps réel** — toujours seulement
  validée à la sauvegarde (`ControlPanelWindow.xaml.cs:470-525`). Un feedback
  immédiat pendant la frappe dans `keysBox` (avant même de cliquer
  "Enregistrer") éviterait de remplir tout un formulaire pour découvrir un
  conflit à la fin.
- **Import/export de profil complet** (touches + combos + apparence en un
  seul fichier) — toujours absent. Seul l'export/import combo par combo
  existe (`ExportSelectedCombo`/`ImportCombo`). Utile pour changer de PC
  d'un coup plutôt que de recréer profils et combos séparément.

### 2.2 Mode Tutoriel
- Voir 1.1 et 1.2 ci-dessus (priorité avant toute nouvelle feature dans ce
  module).
- **`ComboFailReason.Timeout` mort dans les faits mais toujours dans
  l'enum** — cohérent avec le choix documenté de ne plus jamais échouer sur
  le timing, mais `FlashAllStepsRed(ComboFailReason reason)`
  (`MainWindow.xaml.cs:879-906`) garde un paramètre `reason` qui ne varie
  plus jamais en pratique (`ComboRunner.Feed` ne lève plus que
  `WrongInput`, `ComboRunner.cs:162`). Pas un bug, juste du code mort à
  documenter ou nettoyer si `ComboRunner` est retouché un jour.

### 2.3 Historique / Mode 1
- **Timestamp/delta au survol** — toujours absent. Comme noté dans le premier
  audit : utile en pratique libre pour juger son propre rythme entre deux
  coups, sans dépendre du mode Tutoriel.
- **Mode "compact"** (cluster + boutons sans historique) — toujours pas de
  layout alternatif pour libérer de l'espace écran.

### 2.4 Robustesse
- `JsonFileStore.Load` (`Config/JsonFileStore.cs:14-29`) retombe proprement
  sur la valeur par défaut si le JSON est corrompu — **ce point du premier
  audit est résolu**, vérifié à la lecture.
- Toujours aucun fichier de log de diagnostic (cf. mémoire
  [[feedback_diagnose_intermittent_issues]]) — à envisager si un bug
  intermittent est un jour signalé sans reproduction possible sous les yeux
  de l'utilisateur.

---

## 3. Nouvelles pistes (au-delà de ce qui était déjà listé)

### 3.1 Entraînement / pédagogie
- **Sélecteur d'arme manquant à la création manuelle** — voir 1.3, à traiter
  comme un bug plutôt qu'une feature.
- **Mode "quiz" inversé** listé dans l'ancien audit (2.1) est en fait déjà
  fait : `Settings.QuizMode` + `Ctrl+Alt+I`/bouton "Révéler" existent
  (`OverlaySettings.cs:58`, `MainWindow.RevealQuizStepsTemporarily`). À
  retirer de la liste des idées, c'est livré.
- **Détection de tech skip** (buffer de saut, wavedash) — toujours une piste
  ouverte et safe (mesure pure), pas de changement depuis le premier audit.
- **Historique de session comparé entre elles** — `AppState.SessionLog` +
  export CSV existent déjà par session, mais rien ne stocke ces exports pour
  comparer session à session dans l'app elle-même (l'utilisateur doit rouvrir
  ses CSV dans un tableur externe). Piste "confort" seulement si l'usage
  s'avère fréquent.

### 3.2 Personnalisation visuelle
- **Thèmes de couleur en un clic** — toujours à faire, chaque `KeyBind.Color`
  reste réglable individuellement seulement.
- **Overlay "invisible après inactivité"** — toujours à faire (opacité à 0
  après N secondes sans input, réapparition au premier appui).
- ~~**Choix explicite d'écran**~~ — fait, voir 1.4 [CORRIGÉ] (réglage
  `Settings.MonitorIndex` + liste des écrans détectés dans l'onglet
  Apparence).

### 3.3 Zone grise (rappel, inchangé)
- API externe Brawlhalla (stats de match) — toujours écarté du principe
  "outil 100% local", à ne faire que sur demande explicite.
- Overlay injecté dans le renderer du jeu — toujours à ne jamais faire,
  l'overlay WPF actuel suffit.

---

## 4. Priorisation suggérée

1. ~~**À corriger avant tout nouveau chantier** (bugs/incohérences réels,
   section 1)~~ — **fait le 2026-07-25**, voir les 5 sous-sections [CORRIGÉ]
   de la section 1 et l'entrée d'historique correspondante dans `CLAUDE.md`.
2. **Rapide, haute valeur** : détection de conflit de touche en temps réel
   pendant la réassignation, timestamp/delta dans l'historique.
3. **Confort/personnalisation** : import/export de profil complet, thèmes de
   couleur, mode "compact" du layout 1.
4. **Chantiers plus lourds** : overlay invisible après inactivité, détection
   de tech skip dédiée (le multi-écran, lui, est maintenant traité — voir 1.4
   [CORRIGÉ] — même si la conversion DPI reste approximative).
5. **Sur demande explicite seulement** : intégration API externe.

---

## Sources

Audit basé sur une lecture intégrale (pas partielle) au 2026-07-25 de :
`Core/AppState.cs`, `Core/ComboRunner.cs`, `Core/KeyboardHook.cs`,
`Core/GamepadHook.cs`, `Config/JsonFileStore.cs`, `Config/KeyBindConfig.cs`,
`Config/ComboConfig.cs`, `Config/OverlaySettingsConfig.cs`,
`Config/StatsConfig.cs`, `Config/StartupConfig.cs`, `Config/WeaponComboPresets.cs`
(en-tête), `Models/*.cs`, `Windows/MainWindow.xaml.cs` (intégral, 1900
lignes), `Windows/ControlPanelWindow.xaml.cs` (intégral, 1004 lignes),
`Windows/ComboEditorWindow.xaml.cs` (intégral), `App.xaml.cs`, `CLAUDE.md`,
`docs/plan.md`, et l'audit précédent (`docs/audit_features.md` du
2026-07-18, remplacé par ce document). Vérifications croisées par `grep` sur
tout le dépôt pour `MatchMode` et `AttackRecoveryLockMs`. Pas de changement de
code fonctionnel effectué — document de synthèse uniquement.
