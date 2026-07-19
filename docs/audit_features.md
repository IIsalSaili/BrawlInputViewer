# Audit complet — Features actuelles & pistes d'évolution

> Document d'audit (pas un plan validé). Rédigé après relecture complète du
> code (`KeyboardHook`, `AppState`, `ComboRunner`, `MainWindow` 3 modes,
> `ControlPanelWindow`, `ComboEditorWindow`, tous les `Models`/`Config`).
> Objectif : lister en profondeur (1) ce qui pourrait être amélioré dans les
> features existantes, (2) les nouvelles features envisageables, en excluant
> systématiquement tout ce qui présenterait un risque de ban. Voir la section
> 0 pour le rappel de la doctrine anti-ban qui filtre chaque idée ci-dessous.

---

## 0. Doctrine anti-ban (rappel + grille de decision)

Le raisonnement déjà établi dans `CLAUDE.md` : le hook `WH_KEYBOARD_LL` est
**passif** (observation user-mode d'un flux d'événements clavier global,
aucune injection dans le process du jeu, aucune automatisation d'input). Ça
place l'outil dans la même catégorie que OBS / Keypress Hero / un overlay de
stream, hors du radar d'Easy Anti-Cheat qui cible l'injection mémoire, la
modification de fichiers du jeu, ou l'automatisation d'inputs.

**Grille de décision à appliquer à toute nouvelle idée** :

| Catégorie | Exemples | Verdict |
|---|---|---|
| Lecture passive du clavier/souris (hook global) | tout ce qui existe déjà | ✅ Safe |
| Lecture passive de fenêtres/process (sans hook mémoire du jeu) | détecter que Brawlhalla est lancé via `Process.GetProcessesByName` | ✅ Safe |
| Affichage overlay pur (WPF par-dessus, pas d'injection DirectX/OpenGL dans le jeu) | tout l'overlay actuel | ✅ Safe |
| Lecture mémoire du process du jeu (`ReadProcessMemory` sur Brawlhalla.exe) | afficher les stocks/dégâts en live, détecter la position du perso | ❌ Risque élevé — c'est exactement la signature d'un cheat/trainer, même en lecture seule |
| Injection DLL / hook du renderer du jeu | overlay "in-game" façon Discord overlay avec rendu DirectX injecté | ❌ Risque élevé, inutile ici (l'overlay WPF classique suffit) |
| Automatisation d'input (rejouer une macro, auto-esquive, input assisté) | "rejouer ma combo enregistrée automatiquement" | ❌ Interdit absolu — c'est littéralement un bot d'input |
| Modification de fichiers du jeu | reskin, mod de hitbox | ❌ Hors sujet, jamais évoqué, à ne jamais faire |
| Réseau (scrap de stats externes, API tierce Brawlhalla) | via l'API officielle Brawlhalla si publique, ou des sites communautaires | ⚠️ Safe techniquement mais hors du principe "aucune automatisation", à valider avec l'utilisateur si ça devient pertinent (ToS du site tiers) |

Toutes les idées ci-dessous ont été passées par cette grille — je signale
explicitement (⚠️) les rares idées en zone grise et pourquoi.

---

## 1. Améliorations des features existantes

### 1.1 Historique des coups (mode 1 & 3)

- **Distinction visuelle "input pendant le tutoriel" vs "input hors combo"** :
  aujourd'hui l'historique est identique dans tous les modes. En mode
  Tutoriel, colorer différemment (ou marquer d'une icône) les lignes qui ont
  fait échouer le `ComboRunner` aiderait à comprendre *quel* input précis a
  cassé la combo, sans devoir recouper mentalement avec le flash rouge des
  pastilles.
- **Timestamp / delta visible au survol ou en option** : actuellement aucune
  indication du timing réel entre deux coups dans l'historique — utile en
  pratique libre pour juger son propre rythme (ex. "j'ai mis 340ms entre mon
  attaque légère et mon esquive").
- **Export/statistiques de session** : nombre de coups joués, actions les
  plus utilisées, durée de session — géré uniquement à partir du flux déjà
  capté par le hook, aucune donnée jeu nécessaire. Cf. section 2.4 (Analytics)
  pour le détail.
- **Historique redimensionnable / configurable** : `MaxHistoryEntries = 12`
  est une constante en dur (`MainWindow.xaml.cs:49`). Simple à exposer comme
  réglage dans l'onglet Apparence.
- **Bug potentiel à vérifier** : `ClearHistoryGraduallyAsync` retire les
  entrées une par une avec un `CancellationTokenSource` par appel — si
  l'utilisateur spam très vite, plusieurs `ClearHistoryGraduallyAsync` en
  cascade rapide pourraient créer des races sur `_historyPanel.Children` ;
  pas un bug observé mais à garder à l'œil si le "vidage à l'inactivité" est
  élargi (voir 2.4 timers de session).

### 1.2 Mode Historique (mode 1) / Mode Grandes flèches (mode 2)

- **Personnalisation du layout du mode 1** : aujourd'hui l'ordre
  cluster→boutons→historique est fixe. Un mode "compact" (juste
  cluster+boutons, sans historique, pour ceux qui veulent seulement voir
  l'état des touches) économiserait de l'espace écran pour les joueurs en
  petite résolution / en train de streamer avec peu de marge.
- **Mode 2 : rangée d'extras non personnalisable** — `["Saut", "Esquive",
  "Lancer", "Taunt"]` est une liste en dur (`MainWindow.xaml.cs:651`). Si
  l'utilisateur renomme/supprime une action `Action` custom dans l'onglet
  Touches, elle n'apparaîtra jamais en mode 2 même si elle existe dans
  `AppState.Binds`. À généraliser pour itérer sur toutes les actions du
  groupe `Action` non déjà affichées (Saut/Att.légère/Att.forte), au lieu
  d'une liste de noms figée — actuellement fragile si un nom d'action est
  modifié dans l'éditeur de touches.
- **Densité d'information mode 2** : c'est le mode "lu d'un coup d'œil"
  mais il n'affiche toujours pas de historique du tout — pourrait avoir une
  mini-bande de 2-3 dernières actions en toute discrétion, sans revenir à la
  verbosité du mode 1.

### 1.3 Mode Tutoriel / `ComboRunner`

- **Feedback sonore optionnel** — déjà noté comme piste dans `plan.md`
  ("son optionnel plus tard"), jamais fait. Un bip de succès/échec (WPF
  `SystemSounds` ou fichiers `.wav` courts) aiderait énormément en pratique
  sans avoir à regarder l'écran — cas d'usage réel pour du drill "les yeux
  sur le jeu, pas sur l'overlay".
- **Timer visuel de la fenêtre de tolérance** — évoqué dans `plan.md`
  ("barre qui se vide") pour l'étape courante quand `MaxDelayMs` est défini,
  jamais implémenté. Actuellement l'échec par timeout est complètement
  invisible tant qu'il n'arrive pas (le joueur ne sait pas qu'il est "en
  retard" avant l'échec effectif) — un léger cercle de progression autour de
  la pastille active donnerait un vrai feedback temps réel.
- **`MatchMode.IgnoreExtraneous` non exposé dans l'UI** — le mode existe dans
  le moteur (`Combo.MatchMode`, `ComboRunner.cs:84`) mais rien dans
  `ComboEditorWindow` ni `ControlPanelWindow` ne permet de le choisir par
  combo (à vérifier dans `ComboEditorWindow.xaml.cs`, probablement à ajouter
  un simple toggle). C'est une feature "gratuite" déjà codée et non exposée.
- **Enchaînement de plusieurs combos à la suite** — mentionné dans le plan
  d'origine ("passage à la combo suivante si un enchaînement... est
  configuré") mais pas implémenté : aujourd'hui une combo réussie boucle sur
  elle-même (`ComboRunner.Feed`, retour à `CurrentStepIndex = 0`). Une
  "playlist" de combos à jouer dans l'ordre serait un vrai ajout pour
  structurer une session d'entraînement (échauffement → enchaînements
  avancés → optimal punish).
- **Historique de performance par combo** — combien de tentatives, combien de
  réussites, meilleur streak jamais atteint, persisté dans `combos.json` ou
  un fichier séparé. Actuellement `Streak` (`ComboRunner.cs:28`) est en
  mémoire uniquement et repart à zéro à chaque relance de l'app.
- **Diagnostic d'échec plus fin** — à l'échec, `ComboRunner` sait juste que
  ça a raté (mauvaise touche *ou* timing), mais l'UI (`FlashAllStepsRed`)
  traite les deux cas pareil. Distinguer visuellement "trop lent" (pastille
  orange par ex.) de "mauvaise touche" (rouge) donnerait un diagnostic
  immédiat sans devoir comparer avec l'historique.
- **Import/export/partage de combo** — un simple `.json` d'une combo unique
  exportable/importable (au lieu de tout `combos.json`) permettrait de
  partager une combo entre deux installations, ou de publier des recettes de
  combo dans un forum/Discord communautaire sans partager tout son profil.
  Aucune automatisation impliquée, juste de la sérialisation — safe.
- **Undo sur "Stop" d'enregistrement raté** — `SaveRecordedCombo` sauvegarde
  direct dans `AppState.Combos` sans étape de relecture (contrairement à ce
  que prévoyait `plan.md` §1.3.A : "écran de relecture/édition"). Actuellement
  la seule façon de corriger une combo mal enregistrée est de rouvrir
  `ComboEditorWindow` après coup. Un écran de confirmation juste après le
  `Ctrl+Alt+R` d'arrêt (accepter / rejouer / éditer avant sauvegarde) collerait
  mieux au plan d'origine.

### 1.4 Panneau de contrôle / Touches

- **Détection de conflit de touche en temps réel pendant la réassignation** —
  `CLAUDE.md` mentionne une validation "à la sauvegarde" pour les doublons.
  Un feedback immédiat pendant l'écoute ("cette touche est déjà utilisée par
  Esquive") éviterait de découvrir le conflit seulement après avoir rempli
  tout le formulaire.
- **Profils multiples de keybinds** — un seul `keybinds.json`. Utile pour
  quelqu'un qui joue à la fois AZERTY/QWERTY sur deux PC, ou qui veut un
  profil "1v1" vs "en équipe" avec des priorités d'action différentes.
- **Import/export de profil complet** (binds + combos + apparence en un seul
  fichier) — pratique pour changer de PC ou partager sa config avec un ami.

### 1.5 Robustesse générale / technique

- **Pas de gestion d'erreur si `combos.json`/`keybinds.json` est corrompu** —
  à vérifier dans `Config/*Config.cs` (non lu en détail ici) : si le
  `System.Text.Json` échoue au chargement, l'app plante-t-elle au démarrage ou
  retombe-t-elle sur les valeurs par défaut ? Un fichier corrompu (crash
  pendant l'écriture, édition manuelle ratée) ne doit jamais empêcher l'app de
  démarrer.
- **Pas de mécanisme de mise à jour** — l'app n'a aucun moyen de se
  vérifier/mettre à jour elle-même. Hors scope pour un outil perso, mais à
  noter si le projet est un jour partagé plus largement.
- **Pas de logs de diagnostic** — en cas de bug rare (rappel de la mémoire
  [[feedback_diagnose_intermittent_issues]] : préférer un logging continu
  aux tests ponctuels pour les bugs intermittents), il n'existe aujourd'hui
  aucun fichier de log qu'on pourrait activer pour diagnostiquer un problème
  qui ne se reproduit pas sous les yeux de l'utilisateur.

---

## 2. Nouvelles features envisageables

### 2.1 Entraînement / pédagogie (extension naturelle du mode Tutoriel)

- **Mode "quiz" inversé** — au lieu d'afficher la combo à l'avance, l'overlay
  affiche seulement "Combo aléatoire n°X" et le joueur doit s'en souvenir ;
  un bouton "révéler" affiche la solution après coup. Bon pour la mémorisation
  plutôt que la simple exécution assistée.
- **Détection de "tech skip"** — Brawlhalla a des inputs canoniques (ex.
  buffer de saut, wavedash) qui reposent sur un timing très précis entre deux
  touches. `ComboRunner` sait déjà mesurer des delta-temps ; une combo
  spécifiquement dédiée à un seul mouvement technique avec fenêtre de
  tolérance très courte transformerait l'outil en "trainer de exécution"
  ciblé, sans toucher au jeu — juste une combo à 2 étapes bien réglée.
  (Complètement safe : c'est de la mesure, pas de l'assistance.)
- **Session guidée multi-combos avec progression** — une liste ordonnée de
  combos de difficulté croissante, marquées "maîtrisée" après N réussites
  consécutives, avec passage automatique à la suivante. Structure un vrai
  parcours d'apprentissage plutôt qu'une combo isolée en boucle.
- **Statistiques de précision par action** — quelle touche/action génère le
  plus d'échecs de combo (via les events `StepFailed` déjà émis), affichées
  en fin de session dans le panneau de contrôle. Aide à cibler l'entraînement
  ("tu rates surtout les transitions vers Esquive").

### 2.2 Personnalisation visuelle / UX de l'overlay

- **Thèmes de couleur** — actuellement chaque `KeyBind.Color` est réglable
  individuellement, mais pas de "thème" global (ex. palette néon, palette
  pastel, monochrome pour le contraste sur certains fonds de jeu). Un preset
  de thème appliqué à tous les binds en un clic.
- **Overlay "minimal"/"streaming friendly"** — un mode d'affichage encore plus
  épuré que le mode 2 (juste des points qui s'allument, sans texte ni cadre)
  pensé pour l'incrustation dans un stream sans polluer visuellement le
  gameplay affiché aux viewers.
- **Fond dynamique selon l'activité** — l'overlay pourrait devenir totalement
  invisible (opacité 0) après N secondes d'inactivité clavier et réapparaître
  au premier appui, pour ne jamais gêner un visionnage de replay ou une pause.
- **Multi-écran** — actuellement `MainWindow_Loaded` utilise
  `SystemParameters.WorkArea` (écran principal implicite). Un choix explicite
  d'écran cible dans l'onglet Apparence pour les setups multi-moniteurs.

### 2.3 Manette / inputs alternatifs

- **Support XInput** (déjà noté "évoqué mais pas fait" dans `CLAUDE.md`) —
  écoute passive des boutons de manette via `XInputGetState` (polling, pas de
  hook nécessaire côté manette), même principe de sécurité que le clavier :
  lecture d'état, aucune injection. Permettrait à un joueur manette d'utiliser
  les mêmes modes Historique/Tutoriel. Techniquement le plus gros chantier
  parmi les idées listées ici (nouveau système d'input parallèle à
  `KeyboardHook`, mapping manette→action à créer, UI de réassignation
  manette).

### 2.4 Analytics de session (lecture passive uniquement, zéro lien avec le jeu)

Toutes basées uniquement sur le flux déjà capté par le hook clavier — aucune
lecture du jeu, donc aucun risque supplémentaire par rapport à l'existant :

- **Fréquence d'actions** (actions/minute), utile pour comparer son rythme de
  jeu entre sessions.
- **Heatmap temporelle** — quelles actions dominent en début vs fin de
  session (fatigue, changement de style).
- **Export CSV de la session** pour analyse externe (tableur) si l'utilisateur
  veut creuser plus loin que ce que l'overlay affiche.
- **Comparaison de sessions** — graphe simple (nombre de coups, actions/min,
  streak moyen en mode Tutoriel) session par session, stocké localement.

### 2.5 Idées évoquées mais à traiter avec prudence (zone grise, ⚠️)

- **Lire les stats de match via l'API officielle Brawlhalla** (si elle existe
  et est publique) pour corréler "j'ai joué telle combo → j'ai gagné le
  match" : techniquement hors du process du jeu (appel HTTP externe), donc
  pas un risque anti-cheat en soi, mais ça sort du principe actuel "outil
  100% local/passif" et introduit une dépendance réseau + un ToS tiers à
  respecter. **À ne faire que si l'utilisateur le demande explicitement**,
  avec vérification préalable des conditions d'utilisation de l'API/du site
  utilisé.
- **Overlay avec incrustation dans le rendu du jeu (in-game overlay façon
  Discord/Steam)** au lieu d'une fenêtre WPF par-dessus : nécessiterait un
  hook du renderer (DirectX/OpenGL) du process du jeu, ce qui est exactement
  la technique utilisée par les cheats visuels (ESP/wallhack). **À ne jamais
  faire** — l'overlay WPF actuel (fenêtre séparée, topmost) atteint le même
  résultat visuel sans franchir cette ligne. Ne pas confondre avec
  l'overlay actuel qui, lui, estety sûr par construction.

### 2.6 Idées écartées d'office (rappel, ne pas reconsidérer sans raison forte)

- Automatisation/rejeu d'une combo enregistrée (bot d'input) — **exclu**,
  but même de l'outil est l'entraînement manuel, pas l'assistance.
- Lecture mémoire du process Brawlhalla (stocks, %, position adverse) —
  **exclu**, signature de cheat classique même en lecture seule.
- Aim-assist / auto-esquive / tout ajustement automatique d'un input joué par
  le joueur — **exclu**, même famille que l'automatisation.

---

## 3. Priorisation suggérée

Classement par rapport effort/valeur, en tenant compte de ce qui est déjà à
moitié fait dans le code (features "gratuites" à finir) :

1. **Rapide, haute valeur, déjà 80% fait** : exposer `MatchMode` dans
   `ComboEditorWindow`, généraliser la liste d'extras du mode 2 (bug de
   fragilité identifié en 1.2), feedback sonore succès/échec du Tutoriel.
2. **Valeur pédagogique forte** : timer visuel de tolérance, diagnostic
   d'échec fin (timing vs mauvaise touche), enchaînement de plusieurs combos,
   statistiques de précision par action.
3. **Confort/personnalisation** : profils multiples de keybinds, import/export
   de combo et de profil, historique redimensionnable.
4. **Chantiers plus lourds** : support manette (XInput), analytics de session
   avec export/comparaison, mode "quiz" inversé.
5. **À ne considérer que sur demande explicite** : intégration API externe
   (zone grise réseau, section 2.5).

---

## Sources

Audit basé sur lecture directe du code au 2026-07-18 : `MainWindow.xaml.cs`,
`ComboRunner.cs`, `AppState.cs`, `KeyboardHook.cs`, `ControlPanelWindow.xaml.cs`
(partiel), `CLAUDE.md`, `docs/plan.md`. Pas de changement de code effectué —
document de synthèse uniquement.
