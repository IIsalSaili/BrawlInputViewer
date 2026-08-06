# Plan — séparation UX « true combo » vs « string »

> Réflexion de conception, **aucun code écrit**. À valider avant implémentation.
> Rédigé le 2026-08-05.

---

## 1. Le problème, énoncé proprement

Aujourd'hui l'app ne connaît qu'un seul type d'objet : `Combo` (une suite de
`ComboStep`). Elle affiche, entraîne et « fait maîtriser » de la même façon :

- un **true combo** — l'adversaire ne peut pas s'échapper entre les coups, les
  dégâts sont garantis si l'exécution est correcte ;
- un **string** — la suite est correcte en exécution mais laisse une fenêtre
  où l'adversaire peut esquiver ; ça touche parce que l'adversaire n'a pas
  réagi, pas parce que c'était garanti.

C'est une distinction **de fond, pas cosmétique** : les deux ne s'entraînent
pas pour la même raison et ne se jouent pas dans la même situation. Un string
appris comme un true combo produit un joueur qui balance la suite en aveugle et
se fait punir sur la fenêtre d'esquive. Aujourd'hui rien dans l'app ne permet
de faire la différence — pas même de savoir laquelle des deux on est en train
de répéter.

Le contenu actuel de `Config/WeaponComboPresets.cs` est, d'après sa source
(post Reddit fourni par l'utilisateur), une liste de **true combos** testés à
0 % — mais ce fait n'existe nulle part dans le modèle de données, seulement
dans un docstring de fichier. Un combo perso enregistré via `Ctrl+Alt+R` se
retrouve à côté, visuellement indiscernable.

### Ce que la distinction n'est pas

Un piège à éviter dès la modélisation : « true combo » n'est **pas une
propriété absolue d'une séquence**. Une même suite est vraie ou fausse selon :

- le **% de dégâts** de l'adversaire (le knockback scale — déjà modélisé,
  partiellement, par `Combo.DamageNote`) ;
- le **Dex** du personnage (déjà modélisé par `Combo.MinDex` + `LegendStats`) ;
- le poids/la gravité du personnage adverse, sa DI, la position sur la map —
  **non modélisés, et à ne pas chercher à modéliser**.

Conséquence directe sur le design : le champ ne doit pas être un `bool`. Un
`bool` force une réponse pour tous les combos existants et pour tous les combos
perso, alors que la bonne réponse est très souvent « je ne sais pas ». Voir §2.

---

## 2. Modèle de données

### 2.1 Le champ

```csharp
public enum ComboKind
{
    Unknown = 0,   // défaut : statut non renseigné
    True,          // true combo — inéchappable dans les conditions décrites
    String,        // string — fenêtre d'esquive, ça passe sur une lecture
}
```

```csharp
// dans Combo.cs
public ComboKind Kind { get; set; } = ComboKind.Unknown;
```

**Trois états, pas deux.** `Unknown` est un état de première classe, pas une
case à remplir plus tard : c'est l'état honnête de tout combo perso créé ou
enregistré par l'utilisateur, et il doit rester affichable sans culpabilité.
L'alternative (`bool IsTrueCombo`) obligerait à choisir par défaut, donc à
mentir par défaut.

### 2.2 Migration : il n'y en a pas

Deux propriétés du code existant font que ce champ s'ajoute sans script de
migration ni régénération de `combos.json` :

1. `System.Text.Json` donne la valeur par défaut (`0` = `Unknown`) à une
   propriété absente du JSON — tous les combos déjà présents dans le
   `combos.json` de l'utilisateur se chargent donc en `Unknown`, ce qui est
   exactement le bon résultat pour ses combos perso.
2. Le constructeur statique de `AppState` réimporte **tous** les presets
   d'arme à chaque lancement (`foreach (var weapon in WeaponComboPresets.Weapons)
   ImportWeaponPresets(weapon)`), en upsert par `Id` stable. Les ~90 combos
   préréglés récupéreront donc leur `Kind` au premier lancement suivant, sans
   rien demander à l'utilisateur.

> ⚠️ Effet de bord connu de cet upsert : il remplace l'objet `Combo` entier,
> donc remet à 0 les stats de performance (`BestStreak`/`TotalCompletions`/
> `TotalAttempts`/`Mastered`) des combos préréglés. C'est déjà le comportement
> actuel à chaque correction de preset (documenté dans `CLAUDE.md`), mais ici
> ça se déclencherait sur **tous** les presets d'un coup, pour un changement
> purement déclaratif. **À trancher avant de coder** : soit on l'accepte
> (l'utilisateur perd ses séries records sur les presets), soit
> `ImportWeaponPresets` recopie les compteurs de l'ancien objet vers le
> nouveau quand les `Steps` sont identiques. La deuxième option est ~10 lignes
> et évite une perte de données visible ; c'est celle que je recommande, et
> elle profite à toutes les corrections de presets futures.

### 2.3 Règle de remplissage — la partie la plus importante

Le projet s'est trompé **deux fois** sur des données de jeu inventées (combos
hallucinés « Version 9 », citation fabriquée « Correctif Faux »). Le statut
true/string est exactement le même type de donnée factuelle invérifiable depuis
le code. Règles :

- **Ne jamais déduire `Kind` de la structure du combo.** Ni du nombre
  d'étapes, ni de la présence d'une esquive, ni de l'arme, ni du `MinDex`.
  Aucune heuristique. Une heuristique ici produirait une affirmation fausse
  présentée avec la même autorité qu'une affirmation sourcée — le scénario
  exact des deux erreurs passées.
- **Les presets d'arme passent en `True`** parce que leur source unique le
  revendique explicitement (« true combos, non-esquivables, testés à 0 % ») et
  que cette revendication est déjà tracée dans le docstring de
  `WeaponComboPresets.cs`. C'est le seul remplissage automatique légitime, et
  il tient en une ligne dans `BuildPresetCombos`.
- **`LegendComboPresets.Table` est vide** et le reste : rien à marquer.
- **Tout le reste part et reste en `Unknown`** jusqu'à ce que l'utilisateur
  décide lui-même dans l'éditeur.
- Pas de nouveau champ de provenance (`KindSource`) : `Combo.Description`
  porte déjà la source des presets, et un champ de plus serait vide sur 100 %
  des combos perso.

### 2.4 Ce qu'on ne modélise pas (v1)

**Sur quelle étape précise se situe la fenêtre d'esquive.** Ce serait la donnée
la plus pédagogiquement utile (« ici il peut sortir »), et techniquement c'est
juste un `bool EscapableAfter` sur `ComboStep`. Mais cette donnée n'existe
dans aucune source dont on dispose, et la remplir « au jugé » retomberait
exactement dans le piège du §2.3. Donc : **pas de champ par étape en v1**, à
rouvrir seulement si une source frame-data par arme est fournie. Noté en §8.

---

## 3. Principe directeur : la distinction est informationnelle, jamais mécanique

**`Combo.Kind` ne doit toucher ni `ComboRunner`, ni la validation, ni le
timing.** C'est la règle qui garde tout le reste simple.

Raison : `ComboRunner` valide *les entrées du joueur*. Or les entrées à
produire sont rigoureusement identiques que l'adversaire ait pu esquiver ou
non — la différence se joue en face, dans un état de jeu que l'app ne lit pas
(et ne doit pas lire, doctrine anti-ban). Un string mal exécuté échoue pour la
même raison qu'un true combo mal exécuté : une mauvaise touche.

Corollaires à tenir :

- pas de tolérance différente, pas de fenêtre de timing différente (le timing
  a été retiré comme condition d'échec sur demande explicite — ne pas le
  réintroduire par cette porte) ;
- pas de comportement d'abandon différent ;
- pas de « mode string » séparé dans l'overlay.

Tout ce que fait `Kind`, c'est **étiqueter, filtrer et expliquer**.

---

## 4. Vocabulaire

L'app parle français mais son utilisateur lit la scène en notation anglaise
(Reddit/YouTube), et l'onglet À propos a déjà un glossaire FR↔notation
communautaire. Donc : **terme communautaire en étiquette, explication en
français**.

| État | Étiquette courte (UI) | Libellé long (éditeur, glossaire) |
|---|---|---|
| `True` | `TRUE` | **True combo** — garanti, l'adversaire ne peut pas s'échapper |
| `String` | `STRING` | **String** — esquivable sur certaines frames, ça passe sur une lecture |
| `Unknown` | `?` | **Statut non vérifié** — on ne sait pas si l'adversaire peut sortir |

Accord masculin (« un combo », « un string ») conformément à la passe de la
Version 18.

Formulation à garder pour `String` : jamais dévalorisante. Un string n'est pas
un combo raté, c'est un outil différent (conditionnement, punition d'une
esquive prévisible, mixup). Si l'UI laisse entendre « string = mauvais », le
joueur les évitera au lieu d'apprendre quand les placer. C'est un vrai risque
de formulation, pas un détail de ton.

---

## 5. Encodage visuel

### 5.1 Contrainte forte : les couleurs d'état sont déjà prises

Dans le panneau du mode Tutoriel, vert / rouge / orange ont déjà un sens
fonctionnel et immédiat (étape réussie / mauvaise touche / trop lent). Utiliser
vert pour « true » et rouge/orange pour « string » créerait une collision
sémantique **dans le même bandeau, à quelques pixels d'écart**. À exclure.

Palette proposée :

| État | Couleur | Justification |
|---|---|---|
| `TRUE` | or de marque `#E8C44A` | déjà l'accent « élément valorisé » (bordure du panneau, tray icon), aucun rôle d'état |
| `STRING` | bleu-gris désaturé `~#8FA9BF` | lisible, neutre, ne crie pas « erreur » |
| `?` | gris `~#888` | présence discrète mais présence quand même |

### 5.2 Étiquette texte, pas glyphe seul

Un glyphe (🔒 / ⚠) demande d'avoir appris la convention, et `⚠` est **déjà
utilisé** dans l'overlay pour `DamageNote`. Donc petite pastille texte
(`TRUE` / `STRING` / `?`), 9-10 px, majuscules, fond translucide de la couleur
ci-dessus, coins arrondis. Une pastille de ~44 px de large tient sans problème
dans la ligne unique du bandeau, refondue justement pour être compacte.

### 5.3 L'absence d'étiquette ne doit jamais signifier « true »

Tentation naturelle : n'afficher la pastille que pour les strings (« pas de
pastille = c'est bon »). À rejeter — ça transforme `Unknown` en affirmation
implicite « true », soit précisément le mensonge par défaut qu'on évite au
§2.1. **La pastille est toujours affichée**, y compris `?`. Coût : une
pastille de plus. Bénéfice : « non vérifié » devient visible, donc corrigeable.

### 5.4 Où elle apparaît

1. **Overlay, mode Tutoriel** — dans la ligne principale, collée au nom du
   combo (`_comboNameText`, via `BuildComboNameRow`).
2. **Liste de combos du Dashboard** (`RefreshCombosList`) — préfixe de ligne,
   à côté du `[Arme]` déjà présent.
3. **Liste de l'onglet Combos du panneau de contrôle** — idem.
4. **Éditeur de combo** — le sélecteur lui-même (§6).

---

## 6. Édition — comment l'utilisateur renseigne le statut

Dans `ComboEditorWindow`, juste **au-dessus** du champ « Limite de % de
dégâts » (les deux se lisent ensemble, voir §7) :

```
Type de combo
[ Je ne sais pas ▾ ]   ( Je ne sais pas / True combo (garanti) / String (esquivable) )

Un true combo ne peut pas être esquivé une fois le premier coup touché. Un string
laisse une fenêtre où l'adversaire peut sortir : il passe sur une lecture, pas
en garanti. Laisse « Je ne sais pas » si tu n'es pas sûr — mieux vaut inconnu
que faux.
```

La dernière phrase reprend mot pour mot le registre du texte d'aide déjà
présent sous `DamageNote` (« Laisse vide si tu ne sais pas — mieux vaut vide
que faux »), pour que la règle de prudence se lise comme une constante de
l'app et pas comme un avertissement ponctuel.

Le même éditeur servant d'écran de relecture après `Ctrl+Alt+R`
(`isRecordingReview`), un combo enregistré en direct passe donc naturellement
par ce choix, avec « Je ne sais pas » présélectionné.

---

## 7. Articulation avec `DamageNote` et `MinDex`

Il y a un recouvrement réel à clarifier, sinon l'UI devient redondante :

- **`Kind`** = la nature de l'affirmation (garanti / lecture / inconnu) ;
- **`DamageNote`** = *sous quelle condition de %* cette affirmation tient ;
- **`MinDex`** = *sur quel personnage* elle est atteignable.

Donc ils se composent au lieu de se répéter. Affichage recommandé dans
l'overlay quand les deux sont renseignés :

```
[TRUE]  Nom du combo          ⚠ true combo jusqu'à ~40 %
```

et surtout : **`Kind = True` avec `DamageNote` vide ne veut pas dire « marche à
tout % »** — c'est déjà écrit dans le docstring de `DamageNote`, à répéter dans
l'infobulle de la pastille `TRUE` (« garanti dans les conditions décrites par
la source ; l'absence de limite de % ne veut pas dire qu'il n'y en a pas »).

---

## 8. Filtrage et navigation — la décision structurante

Trois options ont été envisagées.

**(a) Sections dans la liste** (« True combos » / « Strings » / « Non
vérifiés »).
**(b) Filtre persistant** qui restreint la liste affichée *et* le cycle.
**(c) Deux modes d'entraînement séparés.**

### Pourquoi (a) est rejeté : conflit avec les familles de combos

`Core/ComboFamilies.cs` regroupe et indente les combos qui étendent un autre
combo (`Niveau 1` → `Niveau 2`). Or le cas le plus fréquent et le plus
instructif est précisément : **un true combo court dont l'extension plus longue
devient un string** (la partie garantie, puis le prolongement qui est une
lecture). Sectionner par type couperait cette famille en deux endroits de la
liste et détruirait exactement la lecture qui a le plus de valeur pédagogique.

**(c)** est surdimensionné : la mécanique d'entraînement est identique (§3),
un mode séparé n'aurait aucun comportement propre à justifier son existence.

### Décision : (b), branché sur le funnel existant

`AppState.FilteredComboIndices()` est déjà le point de passage unique du
filtrage arme / personnage / Dex, consommé par le Dashboard, le panneau de
contrôle, l'overlay et le cycle `Ctrl+Alt+K`. Ajouter le filtre de type là
donne la cohérence de toutes ces surfaces gratuitement.

- nouveau `OverlaySettings.TrainingKindFilter` (string : `""` = tous,
  `"True"`, `"String"`, `"Unknown"` — même convention de chaîne vide que
  `TrainingWeaponFilter`/`TrainingLegendFilter`, plutôt que d'introduire un
  type de filtre différent des deux autres) ;
- nouveau `AppState.SetTrainingKindFilter(...)` calqué **à l'identique** sur
  `SetTrainingWeaponFilter` — y compris le repli
  `if (!FilteredComboIndices().Contains(ActiveComboIndex))
  SetActiveCombo(FirstFilteredIndex())`, qui gère déjà le cas « le combo actif
  vient de sortir du filtre » ;
- une ligne de 4 puces au-dessus de la liste, dans le Dashboard **et** dans
  l'onglet Combos : `Tous · TRUE · STRING · ?`.

La 4ᵉ puce (`?`, non vérifiés) n'est pas un luxe : sans elle, un utilisateur
qui filtre sur `TRUE` voit tous ses combos perso disparaître sans chemin
évident pour les retrouver.

### Effet de bord à connaître (acceptable)

`ComboFamilies.OrderWithFamilies` reçoit la liste **déjà filtrée**. Filtrer un
membre intermédiaire d'une famille peut donc afficher « Niveau 1 / Niveau 2 »
là où il s'agit en réalité du 1ᵉʳ et du 3ᵉ niveau. Le phénomène existe déjà
avec le filtre Dex et n'a jamais gêné ; à ne corriger que s'il devient visible
en pratique.

### États vides

`RefreshCombosList` a déjà des messages d'état contextuels selon qu'aucun combo
n'existe ou que le filtre d'arme ne rend rien. Ajouter le cas du type, avec un
message qui explique au lieu de constater :

> Aucun string pour ce personnage — les combos préréglés de l'app sont tous
> des true combos. Les strings, c'est à toi de les ajouter (bouton
> « Créer manuellement »).

---

## 9. Ce que ça change dans l'entraînement lui-même

C'est là que la séparation cesse d'être un badge et devient utile.

### 9.1 Message d'activation d'un string

À l'activation d'un combo `String` (changement de combo actif), afficher via le
mécanisme de toast existant (`MainWindow.ShowToast`, ~1,5 s) :

> String — l'adversaire peut esquiver. À placer sur une lecture, pas en
> automatique.

Une fois par activation, pas en permanence : la pastille `STRING` reste, elle,
affichée en continu. Ne pas occuper durablement la ligne secondaire du
bandeau, déjà partagée entre `DamageNote`, seuil de Dex et explication du
premier échec.

### 9.2 Nuance sur la « maîtrise »

`Mastered` (N réussites consécutives) mesure la **consistance d'exécution**.
Pour un true combo, « maîtrisé » veut effectivement dire « je peux le sortir en
match ». Pour un string, ça ne veut dire que « je sais l'exécuter » — la partie
difficile (choisir le moment) n'est pas mesurable par l'app.

Correctif minimal et honnête : conserver la même mécanique, changer le libellé
selon le type — « maîtrisé ✓ » pour un true combo, « **exécution** maîtrisée ✓ »
pour un string. Zéro logique nouvelle, une affirmation qui cesse d'être trop
forte.

### 9.3 Enchaînement automatique (playlist)

`ChainCombos` cycle toute la liste filtrée. Comme le filtre de type entre dans
`FilteredComboIndices`, une session « true combos uniquement » devient
possible sans une ligne de code supplémentaire dans le moteur de playlist —
c'est le bénéfice concret d'avoir branché le filtre sur le funnel existant
plutôt qu'à côté.

---

## 10. Pédagogie — où la distinction s'apprend

Un débutant ne connaît pas ces deux mots. Trois points d'entrée, par coût
croissant :

1. **Glossaire de l'onglet À propos** — deux entrées à ajouter au glossaire
   FR↔notation communautaire existant. Coût quasi nul.
2. **Infobulle de la pastille** — la définition complète au survol, partout où
   la pastille apparaît hors de l'overlay.
3. **Une leçon dans le Parcours, chapitre 4** (« Ton premier vrai combo »).
   C'est le bon endroit : le chapitre utilise déjà deux combos Blasters
   préréglés, et une leçon « pourquoi celui-ci est garanti et pas celui-là »
   s'appuierait sur des faits **déjà sourcés et validés par l'utilisateur** au
   chapitre 1 (l'esquive fonctionne par cooldown, pas par compteur ; valeurs
   du wiki tranchées explicitement par l'utilisateur). Sourcing à risque
   faible, contrairement au chapitre 5 encore ouvert.
   Type de validation : `PressAllOnce`/`Sequence` sur un combo existant +
   `FullyValidatedByApp = false` — l'app ne peut évidemment pas vérifier
   qu'un adversaire a esquivé.

---

## 11. Ce qui est explicitement écarté

À écrire ici pour ne pas être reproposé plus tard :

- **Déduire automatiquement le type** d'un combo depuis ses étapes — impossible
  correctement, et c'est la mécanique exacte des deux erreurs de contenu
  passées.
- **Faire réagir `ComboRunner` différemment** selon le type (tolérance,
  timing, abandon) — voir §3, ça n'a pas de sens et ça rouvrirait la question
  du timing comme condition d'échec, retirée sur demande explicite.
- **Rouge/orange pour les strings** — collision avec les couleurs d'échec du
  même bandeau (§5.1).
- **Deux listes ou deux fenêtres séparées** — casse les familles de combos
  (§8).
- **Marquer la frame/l'étape exacte esquivable** — v2, uniquement si une source
  frame-data est fournie (§2.4).
- **Lire l'état du jeu pour savoir si l'adversaire a esquivé** — hors doctrine
  anti-ban, définitivement hors sujet.

---

## 12. Découpage d'implémentation

| # | Lot | Fichiers | Effort |
|---|---|---|---|
| 1 | `ComboKind` + `Combo.Kind` | `Models/Combo.cs` | trivial |
| 2 | Presets marqués `True` + préservation des compteurs à l'upsert (§2.2) | `Config/WeaponComboPresets.cs`, `Core/AppState.cs` | faible |
| 3 | Sélecteur de type dans l'éditeur + texte d'aide | `Windows/ComboEditorWindow.xaml.cs` | faible |
| 4 | Pastille réutilisable + affichage dans les 3 listes/overlay | `Windows/MainWindow.xaml.cs`, `DashboardWindow.xaml.cs`, `ControlPanelWindow.xaml.cs` | moyen |
| 5 | Filtre : `Settings.TrainingKindFilter`, `SetTrainingKindFilter`, `FilteredComboIndices`, puces dans 2 fenêtres, états vides | `Models/OverlaySettings.cs`, `Core/AppState.cs`, 2 fenêtres | moyen |
| 6 | Toast d'activation d'un string + libellé de maîtrise nuancé | `Windows/MainWindow.xaml.cs` | faible |
| 7 | Glossaire À propos | `Windows/ControlPanelWindow.xaml.cs` | trivial |
| 8 | Leçon Parcours ch. 4 (optionnel, après validation du contenu) | `Config/ParcoursCurriculum.cs` | moyen |

Les lots 1→4 forment un premier incrément livrable et utile à eux seuls
(l'information existe et est visible partout) ; le 5 ajoute la navigation ; les
6→8 la pédagogie.

## 13. Vérification

Pas de tests automatisés dans ce projet — vérification manuelle, en respectant
la règle « ne jamais simuler de frappes clavier sans confirmation » :

1. `dotnet build -c Release` sans avertissement, app relancée.
2. **Chargement d'un `combos.json` existant** : tous les combos perso doivent
   apparaître en `?`, aucun en `TRUE` — c'est le test qui prouve qu'aucune
   inférence ne s'est glissée quelque part.
3. **Presets après premier lancement** : inspecter `combos.json` directement,
   tous les `preset-*` doivent avoir `"Kind": "True"` et **avoir conservé**
   leurs `BestStreak`/`Mastered` (si le lot 2 est fait avec préservation).
4. **Filtre** : `STRING` sur un personnage sans string → message d'état
   explicatif, pas une liste vide muette ; `Ctrl+Alt+K` ne doit cycler que dans
   le sous-ensemble filtré ; le combo actif doit basculer proprement s'il sort
   du filtre.
5. **Overlay** : capture d'écran réelle du bandeau — vérifier que la pastille
   ne casse pas la mise en page sur une seule ligne, et qu'elle reste lisible
   par-dessus un fond clair.
6. **Cas famille mixte** : créer temporairement un combo `True` de 2 étapes et
   son extension `String` de 4 étapes (mêmes 2 premières), vérifier qu'ils
   restent groupés/indentés `Niveau 1` → `Niveau 2` avec deux pastilles
   différentes — c'est le scénario qui justifie le rejet du sectionnement
   (§8). Supprimer les combos de test après vérification.

---

## 14. Annexe — icône de replacement (« free movement »)

> Chantier **distinct** de la séparation true/string, ajouté ici sur demande
> explicite de l'utilisateur : « y'a plein de combos où faut se replacer pour
> toucher, c'est un des points qui bloque le plus ». Se traite indépendamment,
> mais partage les mêmes surfaces d'affichage (pastilles du bandeau Tutoriel,
> éditeur), donc autant le concevoir dans la même passe.

### 14.1 Le constat : la donnée existe déjà, elle n'est juste jamais montrée

`ComboStep.FreeMovement` existe depuis longtemps, est **réellement honoré** par
`ComboRunner.Feed` (il court-circuite `strictMovementViolation` en
`MatchMode.Strict`) et est **éditable** — case à cocher en colonne 3 de chaque
ligne d'étape dans `ComboEditorWindow`.

Mais : il n'est affiché **nulle part** dans l'overlay. `RenderComboSteps` lit
`step.RequiredActions` et rien d'autre du `ComboStep`. Le joueur en jeu n'a donc
aucun moyen de savoir qu'une étape tolère/appelle un replacement — l'information
existe dans le modèle, elle s'arrête à l'éditeur.

C'est une situation confortable pour une fois : **aucune donnée de jeu nouvelle
à sourcer**, donc aucun risque du §2.3. On rend visible ce qui est déjà là.

### 14.2 Le piège de conception : deux sens différents sous un même drapeau

À ne pas confondre, parce que ce sont deux affirmations de nature différente :

1. **Permissif** — « tenir une direction en plus ici ne casse pas le combo ».
   C'est ce que `FreeMovement` veut dire aujourd'hui, c'est une propriété du
   *validateur*, et l'app peut l'affirmer avec certitude (c'est son propre
   comportement).
2. **Prescriptif** — « ici tu **dois** te replacer, sinon le coup suivant ne
   touche pas ». C'est ce que décrit l'utilisateur, c'est une propriété du
   *jeu*, et l'app ne peut pas l'affirmer sans source.

Les deux se recoupent très souvent en pratique (on coche `FreeMovement`
précisément sur les étapes où on bouge), mais ce ne sont pas les mêmes
garanties. Décision proposée :

- **v1 : n'afficher que le sens (1)**, avec une formulation qui n'affirme pas
  le (2) — « replacement libre ici » et non « replace-toi ici ». Zéro donnée à
  sourcer, zéro risque de mentir.
- **v2 éventuelle** : un champ prescriptif séparé, rempli **uniquement par
  l'utilisateur** dans l'éditeur, jamais par un preset ni par une inférence —
  même règle que `Combo.Kind` (§2.3). À ne faire que si le (1) se révèle
  insuffisant à l'usage.

### 14.3 Choix de l'icône

Contraintes issues de l'existant :

- **Même bibliothèque que les autres** : game-icons.net, CC BY 3.0, attribution
  déjà en place dans l'onglet À propos, rendue en `Geometry` WPF dans
  `IconGeometryByBaseName`. Le projet a déjà rejeté deux fois des icônes
  dessinées à la main ou d'un autre style — ne pas rouvrir ce débat.
- **Collision à éviter absolument** : les pastilles affichent déjà *Plain
  Arrow* (Delapouite) pour les directions, avec le sens « appuie sur cette
  direction ». Une icône de replacement en forme de flèche simple serait lue
  comme un input à presser. Il faut une silhouette clairement différente.

Candidat recommandé : une **croix directionnelle à 4 branches** (type
« move » / « four directions », Delapouite, même auteur que la flèche
existante donc cohérent de trait). Une croix ne peut pas se confondre avec une
flèche unique, et le motif « 4 directions » dit visuellement « bouge », pas
« appuie ici ».

Couleur : gris-blanc translucide (~50 %) ou le bleu-gris du §5.1. **Ni or**
(réservé à l'accent/`TRUE`), **ni vert/rouge** — et surtout : cette icône ne
doit **pas** suivre les variantes de couleur de la pastille
(`SetPillIconVariant`), sinon elle passerait au vert « réussi » et se lirait
comme une action validée alors que ce n'est pas un input.

### 14.4 Placement — sur la flèche de transition, pas dans la pastille

Ne **pas** l'ajouter dans le `contentPanel` de la pastille à côté des icônes
d'action : tout ce qui est dans cette rangée est un bouton à presser, y glisser
un marqueur qui n'en est pas un recrée exactement la confusion du §14.2.

Deux placements possibles :

- **(A) sur le séparateur `→` qui précède l'étape** — recommandé. Le
  replacement se fait *entre* le coup précédent et celui-ci ; la flèche de
  transition est donc le porteur sémantiquement juste. Concrètement : petite
  croix sous (ou superposée à) le `→` rendu en boucle dans `RenderComboSteps`.
- **(B) badge discret en coin de pastille** — plus simple à poser, mais
  suggère que le replacement fait partie de l'étape elle-même.

Note : le `→` n'est rendu que pour `i > 0`. Une première étape cochée
`FreeMovement` n'aurait donc pas de support en (A) — cas de repli : ne rien
afficher (un replacement avant le premier coup n'a de toute façon pas de sens
dans une notation de combo), ou basculer sur (B) pour cette seule étape.

### 14.5 L'éditeur — la case existe mais n'est pas identifiée

Dans `ComboEditorWindow`, le `stepsHeader` ne pose des libellés que sur les
colonnes 1 et 2 (« Max (indicatif) », « Min (indicatif) »). La colonne 3, celle
de la case `FreeMovement`, **n'a aucun en-tête** : la case n'est découvrable
qu'en survolant pour lire son infobulle. À corriger dans la même passe, en y
mettant **la même icône** que dans l'overlay — c'est ce qui fait le lien entre
« je coche cette case » et « je vois ce symbole en jeu ».

### 14.6 Le vrai blocage : aucun preset ne coche la case

`WeaponComboPresets` construit chaque étape via
`S(params string[] actions)`, qui ne renseigne que `RequiredActions`. **Aucune
étape préréglée n'a donc `FreeMovement = true`** — l'icône n'apparaîtrait sur
aucun des ~90 combos préréglés, c'est-à-dire précisément là où l'utilisateur
dit que le replacement bloque.

Rendre l'icône visible sans rien qui la déclenche ne résout donc pas le
problème posé. Options, par ordre de risque croissant :

1. **L'utilisateur coche lui-même** sur les combos qu'il connaît. Le plus sûr
   (il est la source de vérité du jeu, méthode déjà retenue pour le Parcours).
   ⚠️ **Mais** : `ImportWeaponPresets` s'exécute à chaque démarrage en upsert et
   **remplace l'objet `Combo` entier** — toute modification faite par
   l'utilisateur sur un combo préréglé est **effacée au lancement suivant**.
   C'est le même problème de fond que le §2.2 sur les compteurs, et il rend
   cette option inutilisable telle quelle. Chemin de contournement existant :
   « Dupliquer » le preset (le doublon a un `Id` neuf, donc n'est pas écrasé) —
   à documenter dans l'aide, mais c'est une réponse en creux.
2. **Sourcer étape par étape** quelles étapes demandent un replacement →
   nouvelle donnée de jeu sur ~90 combos, exactement le terrain des deux
   erreurs passées. À écarter sauf source fournie.
3. **Déduire** (ex. « toute étape avec une direction = replacement ») → non,
   même raison qu'au §2.3, et ce serait factuellement faux (une direction fait
   partie de l'input `sLight`, ce n'est pas un déplacement).

**Recommandation** : traiter d'abord le §2.2/§14.6-1 (faire survivre les
modifications utilisateur à l'upsert des presets, ou au minimum ne pas écraser
un preset modifié), sinon l'option la plus saine des trois reste hors d'usage.
C'est la question à poser à l'utilisateur avant de coder cette partie.

### 14.7 Découpage

| # | Lot | Fichiers | Effort |
|---|---|---|---|
| A1 | Icône ajoutée à `IconGeometryByBaseName` (game-icons.net, croix 4 directions) + attribution À propos | `Windows/MainWindow.xaml.cs`, `ControlPanelWindow.xaml.cs` | trivial |
| A2 | Rendu sur le séparateur `→` des étapes `FreeMovement` | `Windows/MainWindow.xaml.cs` (`RenderComboSteps`) | faible |
| A3 | En-tête + icône sur la colonne de la case dans l'éditeur | `Windows/ComboEditorWindow.xaml.cs` | trivial |
| A4 | Survie des modifications utilisateur à l'upsert des presets (§14.6) | `Core/AppState.cs` | moyen, **à trancher d'abord** |

Vérification : cocher `FreeMovement` sur une étape d'un combo perso, relancer,
capture d'écran du bandeau → l'icône doit apparaître sur la bonne transition,
rester grise quand l'étape passe au vert, et ne pas décaler la mise en page sur
une seule ligne.

---

## 15. Questions ouvertes à trancher avant de coder

1. **Que fait l'upsert des presets au démarrage des données de l'utilisateur ?**
   La question se pose deux fois dans ce document et mérite d'être tranchée une
   seule fois pour les deux : préservation des compteurs de performance (§2.2)
   et survie des modifications faites sur un combo préréglé, dont la case
   « replacement » (§14.6). C'est un changement de comportement de
   `ImportWeaponPresets` qui dépasse le périmètre strict de ce chantier, mais
   sans lui l'option la plus saine du §14.6 reste inutilisable.
2. **Étiquettes en anglais (`TRUE`/`STRING`) ou en français** — je recommande
   l'anglais, ce sont les termes que la scène et les guides utilisent, et
   l'explication française reste dans l'infobulle et le glossaire.
3. **Les presets sont-ils vraiment tous des true combos ?** Le champ va
   afficher, en toutes lettres et sur ~90 combos, une affirmation qui repose
   entièrement sur un post Reddit — le même niveau de confiance que le contenu
   lui-même, ni plus ni moins. Ce n'est pas une régression (l'app le sous-entend
   déjà en les présentant sans nuance), mais l'affirmer explicitement la rend
   falsifiable, donc corrigeable. À signaler à l'utilisateur, pas à décider
   seul.
