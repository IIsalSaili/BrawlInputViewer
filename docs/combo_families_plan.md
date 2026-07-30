# Familles de combos & progression par niveau de difficulté

## Contexte / problème

Certaines combos ne sont pas des enchaînements indépendants : elles sont
littéralement une combo plus courte à laquelle on a ajouté 1-2 coups à la fin
(ex. `DLight > SAir` puis `DLight > SAir > SSig`). Aujourd'hui, `combos.json`
les stocke comme deux `Combo` totalement séparées, sans aucun lien visible
dans la liste (onglet Combos / écran d'accueil) — l'utilisateur ne peut pas
deviner que l'une est une extension de l'autre sans lire les deux en détail.

Objectif : rendre cette relation visible, et permettre de s'entraîner sur la
version courte avant de passer à la version étendue, sans que ce soit
perturbant (pas de bascule surprise pendant qu'on joue).

Décisions déjà actées avec l'utilisateur (voir échange précédent) :
- La détection du lien entre deux combos doit être **automatique** (calcul à
  la volée à partir des `Steps` existants), pas un champ à maintenir à la
  main sur chaque preset/combo créée — sinon risque d'oubli/désync, comme ce
  qui s'est déjà produit avec les combos hallucinées (voir `CLAUDE.md`,
  section "Historique des décisions").
- Débloquer un niveau supérieur après avoir maîtrisé le niveau courant reste
  **une suggestion visuelle, jamais un changement automatique de combo
  active** pendant une session.
- Ce système **coexiste** avec `ChainCombos`/`ChainStreakThreshold` (la
  playlist qui avance dans toute la liste filtrée après N réussites
  consécutives) sans le modifier — deux mécanismes séparés, pas de fusion.

## Concept : "famille" de combos

Une **famille** est un groupe de combos de même `Weapon`+`Legend` où chaque
combo (sauf la première) est une extension stricte d'une autre : ses
premières étapes sont identiques, dans le même ordre, et elle en ajoute
au moins une de plus à la fin.

### Règle de préfixe

Deux `ComboStep` sont considérées identiques si leurs `RequiredActions`
contiennent le même ensemble d'actions (comparaison non ordonnée d'un
`HashSet<string>`, `FreeMovement`/`MinDelayMs`/`MaxDelayMs` ignorés — ce sont
des réglages de tolérance, pas une différence de contenu).

Une combo `B` **étend** une combo `A` si :
- `A.Weapon == B.Weapon` et `A.Legend == B.Legend` (y compris deux champs
  vides — deux combos "libres" sans arme peuvent aussi former une famille),
- `A.Steps.Count < B.Steps.Count`,
- pour chaque `i` de `0` à `A.Steps.Count - 1`, `A.Steps[i]` est identique à
  `B.Steps[i]` selon la règle ci-dessus.

### Regroupement en niveaux

Pour un ensemble de combos qui se relient deux à deux par cette règle
(directement ou en chaîne), on les trie par nombre d'étapes croissant : la
plus courte devient **Niveau 1**, la suivante **Niveau 2**, etc. Une combo
sans aucune relation de préfixe avec une autre reste seule (pas de badge de
niveau, comportement identique à aujourd'hui).

### Calcul, pas de stockage

Ce regroupement est recalculé à la demande (à l'affichage de la liste), pas
persisté dans `combos.json` — exactement comme `AppState.FilteredComboIndices`
aujourd'hui. Nouvelle méthode statique, ex. `ComboFamilies.Group(IEnumerable<Combo>)`
retournant une liste de familles (chacune = liste de combos triée par
nombre d'étapes), placée dans un nouveau fichier `Core/ComboFamilies.cs`
(logique pure, testable indépendamment de l'UI, même esprit que
`ComboRunner`).

## UI

### Liste de combos (onglet Combos + écran d'accueil)

Actuellement une liste plate. Avec les familles détectées :
- Les combos d'une même famille s'affichent groupées et indentées selon leur
  niveau (Niveau 1 en premier, sans indentation ; Niveau 2 indenté avec un
  préfixe `↳`, etc.), plutôt que dans un ordre alphabétique/d'import brut.
- Un badge textuel discret par combo de famille : `Niveau 1`, `Niveau 2`...
  (pas de mention "Facile/Moyen/Difficile" — la difficulté relative dépend
  du contexte de jeu, pas juste du nombre d'étapes, mieux vaut rester neutre
  et descriptif).
- Une combo déjà `Mastered` dont la famille a un niveau suivant affiche un
  indice visuel supplémentaire (ex. `✓ → Niveau 2 disponible`), en plus de sa
  marque `✓` existante — texte informatif seulement, aucun bouton d'action
  automatique lié.
- Les combos sans famille détectée gardent le rendu actuel à l'identique.

### Sélection / bascule de niveau

Passer au niveau suivant reste un geste explicite de l'utilisateur : cliquer
dessus dans la liste (comme choisir n'importe quelle combo aujourd'hui) pour
la rendre active via `AppState.SetActiveCombo`. Aucun nouveau mécanisme de
navigation n'est nécessaire ici — le seul ajout est l'indice visuel qui
motive ce choix.

## Ce qui NE change PAS

- `ComboRunner` : aucune modification. Chaque combo reste une séquence
  autonome validée indépendamment ; une famille n'est qu'un regroupement
  d'affichage.
- `ChainCombos`/`ChainStreakThreshold` : aucune modification, système
  totalement séparé (voir décision actée plus haut).
- `combos.json` : aucun nouveau champ, donc aucune régénération de schéma
  nécessaire (contrairement aux changements de `KeyBind`, voir `CLAUDE.md`).

## Étapes d'implémentation

1. `Core/ComboFamilies.cs` — logique pure de détection de préfixe +
   regroupement en niveaux (fonction statique prenant une liste de `Combo`
   et renvoyant, pour chaque combo, sa famille + son rang, ou `null` si
   isolée).
2. `Windows/ControlPanelWindow.RefreshCombosList` — utilise
   `ComboFamilies` pour trier/indenter/badger la liste au lieu de l'ordre
   brut de `AppState.FilteredComboIndices()`.
3. `Windows/StartupWindow` (écran d'accueil, `RefreshCombosList`) — même
   traitement, pour cohérence entre les deux écrans qui affichent une liste
   de combos.
4. Pas de changement à `AppState`, `ComboRunner`, `ComboConfig`.

## Risques / cas limites

- **Faux positif** : deux combos différentes qui partagent par coïncidence
  leurs 2 premières étapes (même arme) seraient groupées à tort comme une
  famille. Risque jugé faible vu le nombre limité de combos par arme, mais à
  garder en tête si un regroupement semble illogique après implémentation —
  solution de repli si ça arrive en pratique : ajouter un champ explicite
  d'opt-out plutôt que de complexifier la détection automatique.
- **Chaînes de préfixes ambiguës** : si `A` est préfixe de `B` et de `C`
  (deux extensions différentes de la même base, pas l'une de l'autre), la
  famille doit se représenter comme un arbre plutôt qu'une liste plate à
  plat — à traiter explicitement dans `ComboFamilies` (`B` et `C` seraient
  tous les deux "Niveau 2" sous le même `A`, affichés l'un après l'autre,
  pas fusionnés).
- **Combos sans arme/légend** (`Weapon`/`Legend` vides, ex. combos perso
  enregistrées) : la règle s'applique aussi entre elles (deux champs vides
  comptent comme "même groupe"), donc une combo perso enregistrée puis
  rejouée avec 2 coups de plus formera aussi une famille — comportement
  voulu, pas limité aux presets.
