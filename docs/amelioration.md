# Améliorations — analyse complète des features (2026-08-04)

> Document de synthèse uniquement, aucun code modifié. Basé sur une lecture
> intégrale et à jour du code (`Models/`, `Config/`, `Core/`, `Windows/`,
> ~9000 lignes) et des 4 documents de plan/audit existants (`plan.md`,
> `audit_features.md`, `combo_families_plan.md`, `plan_ux_onboarding.md`),
> recoupés avec l'historique des décisions de `CLAUDE.md`. Les pistes déjà
> listées ailleurs ne sont pas dupliquées telles quelles : elles sont
> reprises ici, réévaluées à la lumière de l'état réel du code, et rangées
> par degré d'utilité plutôt que par chantier.

Méthode de tri : « utilité » = impact concret sur l'usage réel de l'app
(pédagogie, fiabilité, confort) rapporté à l'effort, **pas** une note de
qualité de code abstraite. Un point classé « Critique » n'est pas un bug qui
plante l'app — l'app n'a jamais planté en usage réel — c'est un point qui,
laissé tel quel, coûte cher plus tard (dérive de doc, code mort qui pourrit,
capacité perdue sans remplacement).

---

## Faible

Confort ou polish, aucun impact sur la valeur centrale de l'app (validation
d'input en temps réel + pédagogie).

### 1. [FAIT — 2026-08-04] Thèmes de couleur en un clic
Chaque `KeyBind.Color` reste réglable individuellement seulement — pas de
palette pré-faite à appliquer d'un coup. *(déjà listé dans `audit_features.md` §3.2)*
- **Effort** : faible.

### 2. Métronome de rythme progressif
La barre de tolérance visuelle sous l'étape courante du mode Tutoriel
(`MainWindow.xaml.cs`, panneau mode 3) est déjà présente mais purement
indicative depuis que le timing a été retiré comme condition d'échec (choix
explicite de l'utilisateur, voir `CLAUDE.md`). L'idée d'un guide de vitesse
croissante (§4.4 de `plan_ux_onboarding.md`) reste ouverte, mais **doit
rester strictement indicative** — ne jamais redevenir une condition
d'échec, c'est ce qui a été explicitement rejeté par le passé.
- **Effort** : moyen. **Risque** : à traiter avec précaution pour ne pas
  réintroduire par la fenêtre ce qui a été retiré par la porte.

### 3. [DEVENU SANS OBJET — 2026-08-04] ~~Timestamp/delta au survol dans l'historique (mode Historique)~~
Le mode Historique dont dépendait cette piste a été **supprimé** (pas juste
désactivé) suite à la décision utilisateur sur le point Critique #17 — plus
aucun panneau d'historique visuel dans l'app. Retirée de la liste.

### 4. [DEVENU SANS OBJET — 2026-08-04] ~~Mode "compact" du layout Historique~~
Même raison que le point 3 ci-dessus : le layout dont il était question
n'existe plus.

### 5. Surveiller les faux positifs de détection de familles de combos
`docs/combo_families_plan.md` documente un risque assumé mais jamais
vérifié en pratique : deux combos différentes partageant par coïncidence
leurs 2 premières étapes (même arme) seraient groupées à tort comme une
famille. Aucun signe que ça soit arrivé, mais aucune vérification faite non
plus depuis l'implémentation. Purement un point de vigilance, pas une action
à mener maintenant.
- **Effort** : nul pour l'instant (surveillance seulement).

---

## Moyen

Améliore un usage déjà fonctionnel, sans combler de trou structurel.

### 6. Détection de tech skip (buffer de saut, wavedash)
Mesure pure sur les inputs déjà captés, cohérente avec la doctrine anti-ban
(aucune lecture de l'état du jeu nécessaire). Signalé sans changement depuis
le premier audit (`audit_features.md` §3.1).
- **Effort** : moyen.

### 7. Comparaison de sessions dans l'app
`AppState.SessionLog` + export CSV existent déjà par session, mais rien ne
stocke ces exports pour comparer une session à l'autre *dans* l'app — il
faut rouvrir les CSV dans un tableur externe aujourd'hui.
- **Effort** : moyen (stockage à ajouter + petit écran de comparaison).

### 8. [FAIT — 2026-08-04] Overlay invisible après inactivité
Opacité à 0 après N secondes sans input, réapparition au premier appui — pour
ne pas polluer l'écran pendant les phases sans combat.
- **Effort** : moyen.

### 9. [FAIT — 2026-08-04] Fichier de log de diagnostic
Aucun mécanisme de log persistant aujourd'hui pour un bug intermittent
signalé sans reproduction sous les yeux de l'utilisateur — cohérent avec la
mémoire [[feedback_diagnose_intermittent_issues]] déjà établie sur d'autres
projets de l'utilisateur. Pas de besoin identifié pour l'instant côté
BrawlhallaOverlay (aucun bug intermittent signalé à ce jour), mais peu coûteux
à poser en prévention avant qu'un tel bug survienne.
- **Effort** : faible à moyen.

### 10. Mode « entraînement sans jeu ouvert » assumé
Question ouverte n°3 de `plan_ux_onboarding.md` §8 : beaucoup de drills du
Parcours n'exigent pas Brawlhalla lancé. Assumer ce mode explicitement
changerait le cadrage de `CaptureSuspended` (actuellement pensé comme
« coupure temporaire », pas comme un vrai mode de fonctionnement). Question
de positionnement produit, pas juste de code.
- **Effort** : faible en code, nécessite une clarification de design d'abord.

### 11. [FAIT — 2026-08-04] Import/export de profil complet
Touches + combos + apparence en un seul fichier, pour changer de PC d'un
coup. Aujourd'hui seul l'export/import combo par combo existe
(`ExportSelectedCombo`/`ImportCombo`). *(déjà listé dans `audit_features.md` §2.1)*
- **Effort** : moyen.

---

## Élevé

Comble un vrai trou dans une feature déjà centrale à l'app, avec un
ratio impact/effort favorable.

### 12. [FAIT — 2026-08-04] Détection de conflit de touche en temps réel
`ControlPanelWindow` valide toujours seulement à la sauvegarde
(`docs/audit_features.md` §2.1) — un feedback immédiat pendant la frappe
dans le champ d'écoute éviterait de remplir tout un formulaire pour
découvrir un conflit à la fin. Petit chantier, gain d'ergonomie direct sur
un geste fréquent (remapper une touche).
- **Effort** : faible à moyen.

### 13. [FAIT — 2026-08-04] Raccourcis clavier non rebindables
Sept raccourcis globaux tous en `Ctrl+Alt+*` avec des `const int VK_*`
comparés en dur dans `MainWindow.xaml.cs` (confirmé par l'exploration de
code : `OnGlobalKeyDown`). Aucune collision connue signalée à ce jour, mais
c'est un point de fragilité documenté depuis `plan_ux_onboarding.md` (P2) :
une collision avec OBS/Discord/un launcher serait irréparable sans recompiler.
La mécanique « Écouter » qui capture une touche existe déjà (onglet Touches,
`ComboEditorWindow`) — la réutiliser pour un écran de réassignation des
raccourcis globaux serait cohérent avec le reste de l'app plutôt qu'un
nouveau mécanisme.
- **Effort** : moyen.

### 14. Combos de légende (Signature) absents depuis la Version 17
`Config/LegendComboPresets.cs` a sa `Table` intentionnellement vide depuis
que l'utilisateur a perdu confiance dans la source Reddit d'origine (cas
Teros, Dex physiquement impossible). La structure (`Legends`/`WeaponsFor`/
`BuildPresetCombos`) est prête à recevoir du contenu, `Combo.Legend` et tout
le filtrage par personnage fonctionnent déjà — il ne manque qu'une source de
confiance pour les vrais combos Signature par légende. Tant que ce n'est pas
fait, un pan entier de la promesse initiale (« combos utilisant une
Signature exclusive à un personnage ») reste vide pour les 69 légendes du
jeu, alors que l'infrastructure pour l'exploiter existe déjà et ne sert à
rien en l'état.
- **Effort** : élevé si fait sérieusement (sourcing arme par arme/légende
  par légende, sur le modèle de ce qui a été fait pour `WeaponComboPresets.cs`
  après le « Correctif Faux ») — mais uniquement sur demande explicite, vu le
  coût du sourcing et l'historique de deux erreurs de contenu déjà commises
  sur ce fichier précis.

### 15. [Chapitre 4 FAIT — 2026-08-04, chapitre 5 restant] Parcours — chapitres 4 et 5 jamais écrits
Les chapitres 0 à 3 du Parcours (prise en main, survivre, frapper, bouger)
sont faits et testés en conditions réelles. Le **chapitre 4 (« ton premier
vrai combo »)** est celui qui referme explicitement la boucle pédagogique
voulue par `plan_ux_onboarding.md` §4.1 : « le tutoriel de jeu et le
tutoriel de l'app fusionnés en un seul parcours » — c'est la pièce qui
amène un débutant, pas à pas, jusqu'au moteur `ComboRunner` que le reste de
l'app propose déjà. Sans lui, le Parcours s'arrête juste avant d'utiliser ce
que l'app sait faire de mieux. Contrairement au point 14 ci-dessus, ce
chapitre **réutilise du contenu déjà vérifié** (`WeaponComboPresets`,
`ComboFamilies` pour la progression de niveau) — le sourcing factuel est
donc déjà fait, il « ne reste que » l'écriture du curriculum et le
branchement UI. Le chapitre 5 (avancé : gravity cancel, chase dodge...) est
un prolongement naturel mais moins prioritaire (déblocable, jamais imposé,
public plus restreint).
- **Effort** : moyen pour le chapitre 4 (contenu déjà vérifié à réutiliser),
  élevé pour le chapitre 5 (nouveau contenu à sourcer).

---

## Critique

Pas des bugs qui cassent l'usage — l'app fonctionne, testée en conditions
réelles à chaque version. Mais deux points où laisser la situation actuelle
perdurer coûte cher : soit en répétant une erreur déjà identifiée deux fois
dans ce projet, soit en laissant pourrir silencieusement un investissement
de code déjà fait.

### 16. [FAIT — 2026-08-04] Dérive documentation/code — le point faible historique du projet, retrouvé dans le code actuel
Le projet a déjà payé cette erreur deux fois pour de vrai (combos
hallucinées « Version 9 », citation fabriquée « Correctif Faux »,
`AttackRecoveryLockMs` documenté alors que déjà remplacé — audit
`audit_features.md` §1.2). L'exploration de code menée pour ce document en a
retrouvé une nouvelle instance, plus petite mais du même type :

- **`Models/Combo.cs`** — le docstring de `MatchMode` affirme encore
  *« Only Strict is implemented for the MVP »*. C'est faux depuis le
  correctif du 2026-07-25 : `ComboRunner.Feed` implémente bien
  `IgnoreExtraneous` (comportement réel confirmé par lecture directe du
  code). Un futur travail sur `ComboRunner` qui ferait confiance à ce
  commentaire referait exactement l'erreur diagnostiquée dans
  `audit_features.md` §1.2 — sur le fichier voisin de celui qui avait déjà
  été pris en faute.
- **`ComboFailReason.Timeout`** (`Core/ComboRunner.cs`) — jamais levé en
  pratique depuis le retrait du timing comme condition d'échec, mais garde
  un paramètre vivant (`FlashAllStepsRed(ComboFailReason reason)`,
  `MainWindow.xaml.cs`) qui ne varie plus jamais. Déjà noté comme « code
  mort à nettoyer » dans `audit_features.md` §2.2 il y a plusieurs versions,
  toujours pas fait.
- **`_historySlotMode3`** (`MainWindow.xaml.cs`) — champ jamais alimenté ni
  ajouté à l'arbre visuel, vestige d'un historique retiré du mode Tutoriel.

Aucun de ces trois points ne cause de bug visible aujourd'hui. Mais le
projet a une règle explicite et durement apprise (« ne jamais faire
confiance à un commentaire/une doc sans vérifier le code réel ») — la
laisser ignorée sur son propre code source, pas seulement sur du contenu
scrapé, sape la valeur de cette règle. Correctif rapide (mise à jour de 3
commentaires/suppression d'un champ mort), à faire avant tout nouveau
chantier touchant `ComboRunner`, exactement comme la doctrine déjà actée en
section 1 de `audit_features.md`.
- **Effort** : faible (quelques lignes), mais priorité haute par principe.

### 17. [FAIT — 2026-08-04] Deux modes d'affichage sur trois bloqués sans échéance de retour
**Résolu** : l'utilisateur a tranché pour l'option 2 (suppression). Le code
des modes Historique/Grand affichage a été entièrement retiré (voir
`CLAUDE.md`, entrée Version 22) plutôt que retravaillé visuellement.

`Core/AppState.cs` : `public const bool CombosOnlyMode = true;` force le
mode Tutoriel partout depuis la Version 17 (« les modes qui affichent des
grosses flèches à l'écran c'est immonde »). Confirmé par l'exploration de
code : le code des modes Historique et Grand affichage est **toujours
présent et fonctionnellement intact** dans `MainWindow.xaml.cs`
(`ApplyModeVisuals` gère toujours les 3 branches), rien n'a été supprimé.

Le problème n'est pas que la décision de désactiver ait été mauvaise — elle
répondait à un vrai retour utilisateur sur l'esthétique. Le problème est que
c'est resté un `const bool` en l'état depuis plusieurs versions (18 à 21)
pendant lesquelles tout le reste du projet a beaucoup bougé (icônes
vectorielles, portraits de légendes, refonte Dashboard, barre de contrôle
overlay...) — **sans qu'aucun de ces changements n'ait retesté les deux
modes désactivés**. Rien ne garantit aujourd'hui qu'ils fonctionnent encore
correctement avec l'état actuel de `AppState`/`OverlaySettings` (par
exemple les nouvelles icônes vectorielles ont été branchées sur les modes
1/2 en même temps que le mode 3 d'après `CLAUDE.md`, mais jamais vérifiées
visuellement depuis puisqu'invisibles). Deux issues possibles, toutes deux
meilleures que le statu quo indéfini :
1. **Retravailler leur rendu visuel** (ce que Version 17 demandait
   implicitement) et les réactiver — probablement le choix le plus fidèle à
   l'intention d'origine du projet (3 modes complémentaires).
2. **Assumer que le Tutoriel est devenu le seul mode utile** et retirer
   proprement le code mort plutôt que de le laisser en maintenance
   silencieuse indéfinie.
Les deux sont acceptables ; ce qui ne l'est pas, c'est l'absence de décision
— chaque nouvelle feature touchant `MainWindow` doit actuellement se
demander implicitement si elle casse des modes que plus personne ne
regarde.
- **Effort** : élevé pour l'option 1 (retravail visuel réel, pas juste une
  réactivation), faible pour l'option 2 (suppression). **Nécessite une
  décision de l'utilisateur avant tout code** — c'est le point de ce
  document qui a le plus besoin d'un arbitrage humain plutôt que d'être
  simplement exécuté.

---

## Pistes déjà écartées volontairement — pour mémoire, à ne pas reproposer

Reprises de `CLAUDE.md`/`audit_features.md`, toujours valables :
- **Intégration d'API externe Brawlhalla** (stats de match) — écarté pour
  rester 100 % local, à ne faire que sur demande explicite.
- **Overlay injecté dans le renderer du jeu** — jamais, seule la lecture
  passive clavier/manette est dans le périmètre anti-ban accepté.
- **Retour au timing comme condition d'échec de combo** — retiré sur
  demande explicite, ne doit pas revenir même sous forme de métronome
  strict (voir point Faible #2 ci-dessus).
- **Réintroduire les portraits « splash art »** pour cohérence de style —
  tranché en Version 20 (une seule source de portraits pour les 69 légendes).

---

## Priorisation suggérée — état après la passe du 2026-08-04

| # | Piste | Tier | Statut |
|---|---|---|---|
| 16 | Nettoyer la dérive doc/code (`MatchMode`, `_historySlotMode3`) | Critique | ✅ Fait |
| 17 | Trancher le sort des modes Historique/Grand affichage | Critique | ✅ Fait — supprimés (choix utilisateur) |
| 12 | Détection de conflit de touche en temps réel | Élevé | ✅ Fait |
| 13 | Raccourcis clavier rebindables | Élevé | ✅ Fait |
| 15 | Parcours chapitre 4 (« premier vrai combo ») | Élevé | ✅ Fait (2 leçons, contenu déjà vérifié réutilisé) |
| 14 | Combos de légende (nouvelle source) | Élevé | ⏸ Passé — aucune source fournie, à reprendre sur demande |
| 8 | Overlay invisible après inactivité | Moyen | ✅ Fait |
| 9 | Fichier de log de diagnostic | Moyen | ✅ Fait |
| 11 | Import/export de profil complet | Moyen | ✅ Fait |
| 6 | Détection de tech skip (buffer de saut, wavedash) | Moyen | Non fait — nouvelle logique de détection à concevoir |
| 7 | Comparaison de sessions dans l'app | Moyen | Non fait — nécessite un nouveau schéma de stockage |
| 10 | Mode « entraînement sans jeu ouvert » assumé | Moyen | Non fait — nécessite une clarification de design d'abord |
| 1 | Thèmes de couleur en un clic | Faible | ✅ Fait |
| 2 | Métronome de rythme progressif | Faible | Non fait — volontairement, risque de réintroduire le timing comme condition d'échec |
| 3, 4 | Timestamp historique / mode compact | Faible | Sans objet — dépendaient du mode Historique, supprimé |
| 5 | Surveiller les faux positifs de familles de combos | Faible | Vigilance passive, rien à coder |

**Bilan** : les 2 points Critique et 3 des 4 points Élevé sont traités (le 4ᵉ,
sourcing de combos de légende, explicitement mis en pause faute de source
fiable — ne pas relancer sans qu'une soit fournie). 4 pistes Moyen/Faible
supplémentaires faites en plus. Restent 3 pistes Moyen qui demandent soit une
conception plus lourde (#6, #7) soit une décision de design préalable (#10),
et 1 piste Faible volontairement laissée de côté (#2, risque identifié). Tout
a été vérifié par `dotnet build -c Release` (0 avertissement/erreur) après
chaque changement — **pas encore testé en jeu** à ce stade (voir note dans
l'entrée Version 22 de `CLAUDE.md` sur le problème d'écran survenu pendant
cette session).
