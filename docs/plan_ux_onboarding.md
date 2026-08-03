# Plan UX & Onboarding — rendre l'app utilisable par un vrai joueur

> **Document de plan uniquement. Aucun code modifié.**
> Objectif : passer d'un outil « orienté dev » (raccourcis clavier obligatoires,
> vocabulaire interne, aucune prise en main) à une app que trois profils très
> différents peuvent lancer et comprendre sans explication extérieure :
> un **débutant Brawlhalla**, un **joueur qui connaît le jeu mais pas l'app**,
> et un **joueur expérimenté** qui veut aller vite.
>
> Ce plan intègre une proposition centrale : **fusionner le tutoriel du jeu et
> le tutoriel de l'app en un seul parcours** (voir §4). C'est la réponse à la
> question posée — oui, les deux en un, mais pas n'importe comment : l'app
> n'apprend pas le jeu *à côté* de son propre fonctionnement, elle **utilise
> l'apprentissage du jeu comme véhicule de sa propre découverte**.

---

## 1. Diagnostic factuel de l'état actuel

Établi en lisant le code, pas en supposant. Chaque point est vérifiable.

### 1.1 Le premier lancement est un cul-de-sac

Parcours réel d'une installation vierge (`combos.json` inexistant) :

1. `App.xaml` ouvre `StartupWindow` — bien, c'est déjà un progrès (Version 11).
2. L'écran demande **4 décisions d'affilée** : Personnage, Arme, Combo, Mode
   d'affichage (`Windows/StartupWindow.xaml.cs:143-320`).
3. Or à ce stade la liste de combos est **vide** : `ComboConfig` n'a aucune
   valeur par défaut (choix documenté dans `CLAUDE.md`), donc l'utilisateur voit
   `— Aucune combo sélectionnée (juste l'overlay) —` et rien d'autre. Il faut
   deviner qu'un bouton « Importer les combos de X » existe et qu'il faut le
   cliquer **avant** que la liste serve à quelque chose.
4. Les 3 modes sont des boutons radio nus : « Historique », « Grandes flèches »,
   « Tutoriel ». Aucun aperçu, aucune image, aucune phrase disant *à quoi ça
   ressemble en jeu*. Un nom comme « Grandes flèches » ne veut rien dire tant
   qu'on ne l'a pas vu.
5. « Lancer en jeu ▶ » ferme la fenêtre et affiche un overlay **transparent,
   click-through, sans aucune UI**. Pour un débutant, l'app vient littéralement
   de disparaître. Le seul filet de sécurité est un ballon de notification de
   6 secondes sur l'icône de tray (`MainWindow.xaml.cs:1584`) et un bandeau de
   raccourcis affiché 12 s (`ShowFirstRunHintIfNeeded`, ligne 1527).

**Conclusion** : le pire moment de l'app est aussi le premier. Le nouvel
utilisateur doit avoir déjà compris le produit pour réussir l'écran qui est
censé le lui expliquer.

### 1.2 Tout passe par des raccourcis clavier, non rebindables, clavier-only

Sept raccourcis globaux, tous en `Ctrl+Alt+*`, avec des lettres arbitraires
(`O`, `P`, `K`, `R`, `U`, `I`, `H` — `MainWindow.xaml.cs:51-57`) :

| Raccourci | Rôle | Équivalent souris existant ? |
|---|---|---|
| `Ctrl+Alt+O` | verrouiller/déplacer l'overlay | menu tray |
| `Ctrl+Alt+P` | changer de mode | menu tray |
| `Ctrl+Alt+K` | changer de combo | **aucun en jeu** (panneau seulement) |
| `Ctrl+Alt+R` | enregistrer une combo | panneau seulement |
| `Ctrl+Alt+U` | ouvrir le panneau | clic tray |
| `Ctrl+Alt+I` | révéler la combo (mode révision) | bouton dans le panneau |
| `Ctrl+Alt+H` | suspendre la capture | menu tray |

Trois problèmes cumulés :

- **Non rebindables** : ce sont des `const int VK_*` comparés en dur dans
  `OnKeyDown` (lignes 1701-1740). Aucun réglage, aucun `settings.json`. Une
  collision avec un autre logiciel (OBS, Discord, un launcher) est irréparable.
- **Aucun équivalent manette.** L'app sait *lire* une manette
  (`Core/GamepadHook.cs`) et se vend comme un outil d'entraînement Brawlhalla —
  un jeu massivement joué au pad. Mais **chaque action de pilotage exige un
  chord clavier à deux modificateurs.** Un joueur au pad doit lâcher la manette
  ou alt-tab pour changer de combo. C'est le trou le plus grave de l'ergonomie
  actuelle, et il est structurel, pas cosmétique.
- **Non découvrables** : la liste complète n'existe qu'à deux endroits — un
  bandeau affiché 12 s au tout premier lancement, et le 5ᵉ onglet « À propos ».
  Après ces 12 secondes, l'information est perdue pour toujours à moins de
  fouiller.

La littérature UX est nette là-dessus : les raccourcis doivent être un
**accélérateur**, jamais le seul chemin, et doivent être affichés *à côté* de
l'action qu'ils déclenchent (menus, tooltips) pour s'apprendre passivement.

### 1.3 Vocabulaire interne exposé tel quel

Termes affichés à l'utilisateur qui n'ont de sens que pour qui a écrit le code :

- « Mode révision » (= masquer les étapes pour tester sa mémoire)
- « Modes inclus dans le cycle rapide (Ctrl+Alt+P) »
- « Grandes flèches » (nom de layout, pas d'usage)
- « Niveau 2 », « ↳ », « familles » (regroupement de combos par extension)
- « Strict / Tolérant » (`MatchMode`, dans l'éditeur)
- « Att. légère » / « Att. forte » (le jeu et toute la communauté disent
  *light* / *signature*, et les guides notent `dLight`, `sAir`, `nSig`…)
- « Suspendre la capture » (= arrêter d'écouter le clavier)

Un joueur qui lit un guide Brawlhalla verra `dLight > nAir > sSig`. L'app lui
parle un autre langage — sans jamais faire le pont.

### 1.4 L'onglet « Général » est un dépotoir

12 contrôles sans rapport entre eux, dans l'ordre où ils ont été codés :
profils de touches, reset position, lancement Windows, mode par défaut, modes
favoris, garder la série, bips, enchaînement de combos, mode révision, bouton
révéler, actions/minute, export CSV
(`ControlPanelWindow.xaml.cs:140-345`). Aucun regroupement, aucun réglage
masqué par défaut. Un débutant doit trier 12 options dont 9 ne le concernent
pas encore.

### 1.5 L'app n'enseigne que des combos — pas le jeu

C'est le point le plus important, et il est confirmé par la recherche :
**les nouveaux joueurs ne meurent pas par manque de combos, ils meurent par
mauvaise récupération et par dodge paniqué.** Les guides débutants placent
dans l'ordre : recovery (enchaîner saut → 2 sauts aériens → dodge → recovery),
usage du dodge, mouvement — *puis* seulement les true combos, généralement à
partir du rang Or.

Or l'app ne sait faire qu'une chose : valider une séquence de true combo. Elle
sert donc, aujourd'hui, exclusivement le joueur qui a déjà dépassé le stade où
il en a le plus besoin. Le débutant — cible explicite de la demande — n'a
littéralement rien à faire dedans.

### 1.6 Aucune boucle de progression

Il existe des stats (`stats.json`, `BestStreak`, `Mastered`) mais elles sont
enfouies derrière un bouton « Voir les stats de précision par action » et ne
pilotent **rien**. L'app ne dit jamais : *voilà où tu en es, voilà quoi
travailler maintenant*. Même le seul mécanisme qui s'en approche (familles de
combos, `ComboFamilies.cs`) a été délibérément conçu pour ne **pas** proposer
le passage au niveau suivant automatiquement — juste un texte discret.

---

## 2. Les profils, et ce que chacun exige

| | **Débutant Brawlhalla** | **Joueur confirmé, app inconnue** | **Joueur expérimenté / habitué** |
|---|---|---|---|
| Sait ce qu'est un `dLight` | non | oui | oui |
| Sait ce qu'il doit travailler | **non** | à peu près | oui |
| Tolérance à la config | très faible | moyenne | élevée, veut du contrôle |
| Objectif en lançant l'app | « progresser », flou | « m'entraîner sur mon perso » | « refaire ma routine, vite » |
| Ce qui le fait abandonner | ne rien comprendre en 60 s | devoir configurer avant de jouer | friction sur un geste répété |
| **Ce qu'il lui faut** | un **parcours guidé** qui décide à sa place | un **démarrage en 2 clics** sur son perso | des **presets, une reprise instantanée, des raccourcis** |

Un quatrième profil implicite, à ne pas oublier : **le revenant** (a utilisé
l'app il y a 3 semaines, a tout oublié). Il a besoin de la même chose que le
profil 3 — reprendre où il en était — mais avec un rappel du contexte.

**Conséquence de design :** un seul écran d'accueil ne peut pas servir les
trois. Il faut une **fourche explicite au premier lancement**, puis un accueil
qui change de forme selon l'historique (voir §5.1 et §5.2).

---

## 3. Principes directeurs

Cinq règles pour arbitrer toutes les décisions qui suivent.

1. **Rien d'obligatoire ne passe uniquement par un raccourci.** Tout raccourci
   doit avoir un chemin souris/manette équivalent, et être affiché à côté de
   l'action qu'il déclenche.
2. **Divulgation progressive.** L'écran par défaut ne montre que ce qui sert au
   premier usage ; le reste vit derrière « Avancé ». Les réglages puissants ne
   disparaissent pas, ils cessent d'être un péage.
3. **Parler le langage du joueur, pas celui du code.** Le vocabulaire de
   référence est celui de la communauté Brawlhalla (`dLight`, `sig`, `recovery`,
   `GC`), avec traduction FR affichée à côté, jamais un jargon inventé.
4. **Toujours un « et maintenant ? ».** Chaque écran, chaque fin de drill,
   chaque réussite propose l'action suivante. L'app ne laisse jamais
   l'utilisateur devant un état terminal sans issue.
5. **La doctrine anti-ban ne bouge pas.** 100 % local, lecture passive, aucune
   injection d'input, aucune automatisation, aucune dépendance réseau. Tout ce
   qui suit est compatible : on ajoute de l'UI et de la pédagogie, jamais un
   comportement qui touche au jeu.

Et une règle de contenu, héritée des erreurs passées (voir « Version 9 » et
« Correctif Faux » dans `CLAUDE.md`) : **aucune donnée factuelle sur le jeu
n'est écrite sans source réelle et vérifiable.** Le tutoriel proposé en §4
manipule beaucoup de contenu Brawlhalla — c'est exactement le terrain où le
projet s'est déjà planté deux fois.

---

## 4. La pièce centrale : le Parcours (tutoriel jeu + app fusionnés)

### 4.1 L'idée

Un mode **« Parcours »**, quatrième mode aux côtés des trois existants, mais
surtout **nouveau point d'entrée par défaut pour un profil débutant**.

Le principe : une suite de **leçons courtes**, chacune composée de

> **objectif en une phrase → explication en 3 lignes → drill à exécuter →
> validation en temps réel par l'overlay → critère de réussite → leçon suivante
> débloquée.**

Et le pont recherché : **chaque leçon enseigne une mécanique du jeu tout en
faisant utiliser une fonction de l'app.** La leçon « Chaîne de récupération »
apprend la recovery *et* apprend au passage à lire les pastilles du mode
Tutoriel. La leçon « Ton premier true combo » apprend un combo réel *et*
apprend le sélecteur de personnage. Le tutoriel de l'app n'est jamais un écran
séparé qu'on subit avant de jouer : il est **dissous dans le contenu**.

C'est la réponse directe à « peut-être les deux en un ». Un tutoriel d'app pur
serait une visite guidée que personne ne lit ; un tutoriel de jeu pur ferait
doublon avec le tutoriel intégré de Brawlhalla. La combinaison est le seul
angle où l'app apporte quelque chose que ni le jeu ni un guide YouTube
n'apportent : **une validation d'input en temps réel, à la maison, sur ton
propre clavier/pad.**

### 4.2 La contrainte dure à assumer (et à dire à l'utilisateur)

**L'app ne voit que les inputs. Elle ne voit pas l'état du jeu.** Pas de
position du personnage, pas de dégâts, pas de hitbox, pas de « tu es revenu sur
le stage ». C'est structurel et non contournable sans lire la mémoire du jeu —
ce qui est exactement la ligne rouge anti-ban.

Conséquence sur la conception des leçons :

- ✅ Validable : *séquences d'inputs*, ordre, simultanéité, répétition, rythme
  approximatif. (« Fais saut, saut, esquive, recovery dans cet ordre. »)
- ❌ Non validable : *effets en jeu*. (« Reviens sur le stage », « touche
  l'adversaire », « ne te fais pas edgeguard ».)
- 🟡 Contournement honnête : pour tout ce qui n'est pas validable, la leçon
  devient **explication + consigne à vérifier soi-même en Training Room**, avec
  une case « j'ai réussi » cochée manuellement. Il faut l'assumer clairement à
  l'écran (« l'app ne peut pas vérifier ça pour toi — regarde ton perso ») au
  lieu de faire semblant.

Cette distinction doit être visible dans l'UI de chaque leçon (badge « validé
par l'app » vs « à vérifier en jeu »). Mentir dessus ferait perdre la confiance
qui est tout l'intérêt d'un outil de validation.

### 4.3 Le curriculum proposé

Ordre dérivé de la recherche (récupération avant tout, dodge ensuite,
mouvement, puis combos ; les techniques avancées type gravity cancel sont
explicitement classées « avancé » par le wiki officiel).

> ⚠️ Le contenu ci-dessous est une **structure proposée**. Chaque affirmation
> factuelle sur le jeu (frames, ordre exact des inputs, propriétés) devra être
> re-sourcée pièce par pièce avant écriture, conformément au principe §3.

**Chapitre 0 — Prise en main de l'app** (3 leçons, ~4 min)
| Leçon | Contenu | Ce que l'app enseigne d'elle-même |
|---|---|---|
| 0.1 Tes touches | Appuie sur chaque touche demandée ; l'app confirme qu'elle la voit | lecture de l'overlay, vérification du mapping — **remplace la config de touches à l'aveugle** |
| 0.2 L'overlay | Placement à l'écran, opacité, click-through | déplacement de l'overlay sans avoir à connaître `Ctrl+Alt+O` |
| 0.3 Suspendre | « L'app écoute ton clavier partout, même hors du jeu » | `CaptureSuspended` expliqué *au bon moment*, pas dans un onglet |

**Chapitre 1 — Survivre** (le plus important pour un débutant)
| Leçon | Mécanique | Validable ? |
|---|---|---|
| 1.1 Tes ressources en l'air | 1 saut + 2 sauts aériens + 1 dodge + 1 recovery | ✅ séquence |
| 1.2 La chaîne de récupération | enchaîner ces ressources dans le bon ordre au lieu de tout cramer d'un coup | ✅ séquence, ❌ résultat |
| 1.3 Fast fall | tenir Bas en l'air pour tomber plus vite | 🟡 |
| 1.4 Ne pas paniquer au dodge | garder le dodge pour un vrai besoin | ❌ → explication + drill de retenue |

**Chapitre 2 — Frapper**
| 2.1 Les 3 lights | neutre / latéral / bas, et à quoi sert chacun | ✅ |
| 2.2 Les aériens | nAir / sAir / dAir depuis un saut | ✅ |
| 2.3 Signature ≠ bouton séparé | la sig, c'est l'attaque forte + direction | ✅ — **corrige une confusion réelle**, déjà rencontrée dans ce projet |
| 2.4 Ne pas spammer la sig | pourquoi c'est puni | ❌ explication |

**Chapitre 3 — Bouger**
| 3.1 Dash | ✅ | 3.2 Dash jump | ✅ | 3.3 Backdash | ✅ | 3.4 Dodge directionnel (8 directions) | ✅ |

**Chapitre 4 — Ton premier vrai combo**
Réutilise **tel quel** le contenu vérifié existant (`WeaponComboPresets`,
`LegendComboPresets`) et le moteur `ComboRunner`. C'est ici que l'app actuelle
commence — et c'est ici que le débutant doit arriver, pas démarrer.
Progression naturelle via `ComboFamilies` : niveau 1 → niveau 2 → …

**Chapitre 5 — Avancé** (déblocable, jamais imposé)
Gravity cancel, chase dodge, ledge/floor cancel, DI. Explicitement marqué
« difficile » — le wiki officiel classe le GC comme volontairement plus dur que
la moyenne des techniques du jeu.

### 4.4 Mécanique de progression

- **Critère de réussite** : N répétitions **consécutives** propres. Le moteur
  existe déjà (`ChainStreakThreshold`, `Mastered`) — il suffit de le brancher
  sur des leçons au lieu de combos.
- **Déblocage souple** : la leçon suivante se débloque à la réussite, mais
  **tout reste accessible** via « voir tout le parcours » pour un joueur
  expérimenté qui refuse d'être tenu en laisse. Un déblocage strict punirait le
  profil 3.
- **Reprise** : l'accueil affiche « Reprendre : Chapitre 1.2 » en bouton
  principal. C'est ce qui règle le cas du revenant.
- **Métronome / vitesse progressive** (idée issue de la recherche sur les
  trainers de jeux de combat) : commencer lent puis accélérer est la méthode
  reconnue pour la mémoire musculaire. L'app a déjà une barre de tolérance
  visuelle inutilisée comme couperet — elle pourrait devenir un **guide de
  rythme optionnel** avec vitesse cible croissante. À traiter comme piste P2,
  pas comme fondation : le timing a été retiré comme condition d'échec sur
  demande explicite de l'utilisateur, et ça ne doit pas revenir par la fenêtre.

---

## 5. Le reste du plan, par chantier

### 5.1 Premier lancement : une fourche, pas un formulaire

Remplacer l'écran actuel *au premier lancement uniquement* par 3 écrans max.

**Écran A — « Tu es plutôt… »** (une seule question, 3 grosses cartes)
- 🌱 *Je débute à Brawlhalla* → Parcours, chapitre 0. Aucune autre question.
- 🎮 *Je connais le jeu, pas l'app* → écran B (choix perso), puis mode Tutoriel
  avec les combos de son perso **déjà importées automatiquement**.
- ⚡ *Je sais ce que je fais* → l'écran d'accueil actuel, complet.

Ce seul écran désamorce l'essentiel du problème : il n'y a plus « une app dans
laquelle on ne comprend rien », il y a trois entrées assumées.

**Écran B — Personnage** (profils 1 et 2 seulement)
Grille de portraits cliquables (les 30 PNG existent déjà dans
`Assets/Legends/`) au lieu d'une `ComboBox` de 30 lignes de texte. Option
« Je ne sais pas encore » → l'app propose un legend débutant sourcé.
**L'import des combos devient implicite** : choisir un perso importe ses
combos. Le bouton « Importer » disparaît du chemin nominal (il reste en
avancé) — c'était une étape purement technique exposée à l'utilisateur.

**Écran C — « Voilà ce que tu vas voir »**
Aperçu **visuel** des modes (une capture/maquette par mode, pas trois radios
textuelles), et surtout : *« l'app va se réduire en overlay transparent.
Pour la retrouver : icône en bas à droite, ou Ctrl+Alt+U. »* dit **avant** que
ça arrive, pas dans un ballon de 6 s après.

### 5.2 Accueil des lancements suivants

Écran de reprise, pas de configuration :
- **Bouton principal** contextuel : « Reprendre — Chapitre 2.1 » ou
  « Relancer : Faux · niveau 2 ».
- Ligne de contexte : dernière session, série record, combos maîtrisées.
- Un lien discret « Changer de perso / de combo » qui mène à l'écran complet
  actuel.

Le profil expérimenté relance sa session en **un clic**. C'est aussi ce qui
transforme les stats existantes (déjà persistées, aujourd'hui invisibles) en
quelque chose d'utile.

### 5.3 Tuer la dépendance aux raccourcis

Par ordre d'importance :

1. **Barre de contrôle overlay.** Une petite barre d'icônes (changer de mode /
   combo suivante / précédente / pause capture / ouvrir le panneau), attachée à
   l'overlay, **masquée par défaut**, qui apparaît au survol souris de la zone
   ou par le raccourci existant. Click-through désactivé sur cette barre
   uniquement. Résout d'un coup la non-découvrabilité *et* le fait que l'app
   « disparaît » après le lancement.
2. **Raccourcis manette.** Un chord pad configurable (ex. `Start + LB` = combo
   suivante). Techniquement gratuit : `GamepadHook` lit déjà l'état des boutons,
   il suffit de traiter des combinaisons au lieu de boutons isolés. **C'est le
   correctif à plus fort ratio impact/effort de tout ce document** pour un jeu
   joué au pad.
3. **Raccourcis rebindables.** Sortir les `VK_*` en dur vers `settings.json` +
   un écran de réassignation (réutiliser la mécanique « Écouter » qui existe
   déjà dans l'onglet Touches). Règle les collisions avec OBS/Discord.
4. **Afficher le raccourci partout où l'action existe.** Chaque bouton du
   panneau, chaque entrée du menu tray affiche son raccourci en suffixe grisé.
   C'est comme ça qu'un raccourci s'apprend : passivement, en faisant.
5. **Menu tray complet.** Aujourd'hui il couvre 4 des 7 actions ; il doit
   couvrir les 7.

### 5.4 Passe de vocabulaire

- Glossaire intégré (une page, FR ↔ notation communautaire) accessible partout.
- Renommages : « Mode révision » → **« Cacher les étapes (mémorisation) »** ;
  « Grandes flèches » → **« Grand affichage »** avec aperçu ; « Strict /
  Tolérant » → **« Refuser les directions en trop / Les ignorer »**.
- Afficher la **notation communautaire à côté** du nom d'action dans les
  pastilles et l'éditeur : `Att. légère + Bas` → aussi `dLight`. C'est le pont
  qui manque entre l'app et tous les guides que le joueur lira ailleurs.

### 5.5 Réglages : deux niveaux

Découper l'onglet Général actuel (12 contrôles à plat) en sections avec un
interrupteur **« Réglages avancés »** replié par défaut :

- **Visible** : mode par défaut, son, position/taille de l'overlay, suspendre.
- **Avancé** : profils de touches, modes favoris du cycle, garder la série,
  enchaînement auto, seuil de maîtrise, export CSV, lancement Windows,
  choix d'écran.

### 5.6 Overlay auto-explicatif

- **Légende repliable** en mode 1/2 : ce que représente chaque icône (le pack
  d'icônes est joli mais muet).
- **État vide parlant** : en mode Tutoriel sans combo, afficher « Choisis une
  combo → clic sur l'icône en bas à droite », pas juste « Aucune combo ».
- **Premier échec = explication** : la première fois qu'une combo casse,
  afficher une ligne « mauvaise touche : tu as fait X, l'étape demandait Y »
  plutôt qu'un simple flash rouge à décoder.

---

## 6. Priorisation

| # | Chantier | Profil servi | Impact | Effort | Priorité |
|---|---|---|---|---|---|
| 1 | Fourche « tu es plutôt… » au 1er lancement (§5.1 A) | 1, 2 | énorme | faible | **P0** |
| 2 | Import de combos implicite au choix du perso | 1, 2 | énorme | faible | **P0** |
| 3 | Barre de contrôle overlay (§5.3.1) | tous | énorme | moyen | **P0** |
| 4 | Écran C « voilà ce qui va se passer » | 1, 2 | fort | faible | **P0** |
| 5 | Raccourcis manette (§5.3.2) | tous (pad) | fort | faible | **P0** |
| 6 | Accueil de reprise (§5.2) | 3, revenant | fort | moyen | P1 |
| 7 | Grille de portraits au lieu de ComboBox | 1, 2 | moyen | faible | P1 |
| 8 | Passe de vocabulaire + glossaire (§5.4) | 1, 2 | fort | moyen | P1 |
| 9 | Réglages à deux niveaux (§5.5) | 1, 2 | moyen | faible | P1 |
| 10 | **Parcours — Chapitre 0 + Chapitre 1** (§4) | 1 | énorme | **élevé** | P1 |
| 11 | Overlay auto-explicatif (§5.6) | 1, 2 | moyen | faible | P1 |
| 12 | Raccourcis rebindables | 3 | moyen | moyen | P2 |
| 13 | Parcours — chapitres 2 à 5 | 1, 2 | fort | élevé | P2 |
| 14 | Guide de rythme progressif (métronome) | 3 | moyen | moyen | P2 |

**Lecture de cette table :** les 5 P0 valent, à eux seuls, plus que le Parcours
complet en ratio impact/effort, et ne demandent aucune donnée de jeu à sourcer.
Ils devraient précéder toute écriture de contenu pédagogique. Le Parcours est
le gros morceau — il mérite son propre plan détaillé une fois la structure
validée.

---

## 7. Risques et non-objectifs

- **Risque de contenu faux.** Le Parcours multiplie par 10 la surface de
  données factuelles sur le jeu. Le projet s'est déjà trompé deux fois là-dessus
  (combos hallucinées, citation de source fabriquée, gravity cancel codé comme
  un saut). **Règle : aucune leçon écrite sans source récupérée réellement, et
  test en jeu par l'utilisateur comme arbitre final.**
- **Risque de doublon avec le tutoriel intégré de Brawlhalla.** Le jeu a son
  propre tutoriel et une Training Room. L'angle de l'app n'est pas de refaire
  ça : c'est la **validation d'input en temps réel** et le **suivi de
  progression**, que le jeu ne fournit pas.
- **Risque de trop guider.** Un déblocage strict ferait fuir le profil 3. D'où
  la fourche de §5.1 et le « voir tout le parcours ».
- **Non-objectif : lire l'état du jeu.** Jamais. Toute leçon qui l'exigerait est
  reclassée en « à vérifier toi-même » (§4.2).
- **Non-objectif : API externe / réseau.** Inchangé (voir
  `docs/audit_features.md` §2.5).

---

## 8. Questions ouvertes

1. **Le Parcours doit-il être un 4ᵉ mode d'overlay, ou une fenêtre à part ?**
   Une fenêtre (type `ControlPanelWindow`) est plus simple à faire et plus
   lisible pour lire des explications ; un mode overlay permet de s'entraîner
   *dans* la Training Room du jeu. Probablement : **explication en fenêtre,
   drill en overlay**, avec passage automatique de l'une à l'autre.
2. **Combien de leçons avant que ça devienne un projet à part entière ?** Le
   chapitre 0 + 1 (7 leçons) est un périmètre raisonnable pour valider le
   format avant d'écrire les 20 suivantes.
3. **Faut-il un mode « je m'entraîne sans le jeu ouvert » assumé ?** Beaucoup de
   drills d'input n'exigent pas Brawlhalla lancé. Ce serait un argument fort —
   et ça change le cadrage de `CaptureSuspended`.

---

## Sources

Recherche menée pour ce plan (contenu de jeu à re-vérifier pièce par pièce
avant écriture des leçons, cf. §3) :

- [Movement — Official Brawlhalla Wiki](https://brawlhalla.wiki.gg/wiki/Movement) — liste et classification basique/intermédiaire/avancé des techniques de mouvement
- [Brawlhalla Beginner's Guide – Tips to Improve Fast (Steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=3510421353) — ordre d'apprentissage, erreurs classiques, focus par rang
- [Gravity Canceling For Beginners (Steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=880809718)
- [Training — Brawlhalla Wiki](https://brawlhalla.fandom.com/wiki/Training) — capacités réelles de la Training Room
- [Brawlhalla Beginner's Guide — EarlyGuides](https://earlyguides.com/brawlhalla/beginners-guide)
- [Progressive Disclosure in UX — UXPin](https://www.uxpin.com/studio/blog/what-is-progressive-disclosure/) et [Userpilot](https://userpilot.com/blog/progressive-disclosure-examples/) — onboarding en couches
- [Hotkey vs. Context menu — Icons8](https://icons8.com/blog/articles/the-ux-dilemma-hotkeys-vs-context-menus/) — raccourci comme accélérateur, jamais chemin unique
- [The UX of Keyboard Shortcuts](https://medium.com/design-bootcamp/the-art-of-keyboard-shortcuts-designing-for-speed-and-efficiency-9afd717fc7ed) — affichage des raccourcis dans les menus/tooltips
- [FightFlow — Fight Camps & training systems](https://fightflow.app/features) et [Developing the training mindset in fighting games](https://pattheflip.medium.com/developing-the-training-mindset-in-fighting-games-2c0d167adcb5) — structure de drills, progression lente→rapide
