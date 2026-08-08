using System.Collections.Generic;

namespace BrawlhallaOverlay;

/// <summary>
/// Contenu du "Parcours" (docs/plan_ux_onboarding.md §4) : chapitre 0 (prise en main de l'app,
/// aucune donnée de jeu — pas de risque de citation fabriquée), chapitre 1 (survivre), chapitre 2
/// (frapper), chapitre 3 (bouger) et chapitre 4 (premier vrai combo), mécaniques sourcées
/// explicitement — voir SourceNote sur chaque leçon concernée. Chapitre 3 : le plan prévoyait
/// 4 leçons (dash, dash jump, backdash, dodge directionnel comme outil de déplacement) mais
/// l'utilisateur a clarifié que la 4ᵉ n'est pas une mécanique séparée — l'esquive directionnelle
/// aérienne est déjà couverte par la leçon 1.1, son usage comme "option de recover en plus" est un
/// simple à-côté, pas de leçon dédiée pour éviter la redondance. Chapitre 4 (Version 22) : ne
/// source aucune nouvelle donnée de jeu — reprend littéralement deux combos Blasters déjà vérifiés
/// dans WeaponComboPresets.Table (seuil de Dex le plus bas du fichier, 3+, donc jouables par la
/// quasi-totalité des légendes), c'est la pièce qui relie le Parcours au moteur ComboRunner que le
/// reste de l'app utilise déjà. Chapitre 5 du plan (techniques avancées) reste volontairement pas
/// fait — à sourcer progressivement de la même façon (voir CLAUDE.md, "Version 13"/"Version
/// 14"/"Version 15"/"Version 16"/"Version 22").
///
/// Méthode de sourcing (Chapitres 1 et 2, 2026-08-03) : question directe posée à l'utilisateur
/// (qui joue réellement) en premier, recoupée avec une source écrite (brawlhalla.wiki.gg/wiki/Movement)
/// pour les chiffres précis — pas l'inverse. Deux corrections notables de l'utilisateur pendant ce
/// travail : (1) le pool de sauts/récupérations est "2+1 ou 1+2", pas un simple "1 saut + 2 sauts
/// aériens" comme une première lecture pourrait le suggérer ; (2) il n'existe PAS de rôle universel
/// pour les 3 variantes de light (neutre/latérale/basse) — ça dépend de l'arme/du perso/du combo, une
/// généralisation proposée par erreur a été rejetée avant d'être écrite. Un chiffre a aussi été
/// explicitement arbitré par l'utilisateur en cas de désaccord entre sources (le wiki officiel —
/// 1s/2.7s de cooldown d'esquive — plutôt qu'un post de forum donnant 1.5s/3.5s). Cf. la règle du §3
/// du plan et l'historique "Version 9"/"Correctif Faux" de CLAUDE.md (ne jamais écrire un fait de jeu
/// sans vérification réelle).
/// </summary>
public static class ParcoursCurriculum
{
    public static List<Lesson> BuildLessons() => new()
    {
        // ================= Chapitre 0 — Prise en main de l'app =================
        new Lesson
        {
            Id = "0.1",
            Chapter = 0,
            ChapterTitle = "Prise en main de l'app",
            Title = "Tes touches",
            Objective = "Vérifie que l'app voit bien chacune de tes touches.",
            Explanation = "Appuie une fois sur chacune des actions ci-dessous. Dès que l'app détecte l'appui, l'action passe en vert — ça confirme que le mapping clavier/manette (onglet Touches du panneau de contrôle) fonctionne, avant même d'aller en jeu.",
            Kind = LessonValidationKind.PressAllOnce,
            RequiredActionsOnce = new() { "Gauche", "Droite", "Haut", "Bas", "Saut", "Att. légère", "Att. forte", "Esquive" },
        },
        new Lesson
        {
            Id = "0.2",
            Chapter = 0,
            ChapterTitle = "Prise en main de l'app",
            Title = "L'overlay",
            Objective = "Déverrouille l'overlay une fois pour savoir comment le repositionner.",
            Explanation = "L'overlay est \"verrouillé\" par défaut (les clics passent au jeu en dessous, il ne bouge pas). Déverrouille-le une fois — via Ctrl+Alt+O, le menu de l'icône en bas à droite de l'écran (barre de contrôle overlay), ou l'icône dans la zone de notification Windows — pour voir qu'il devient déplaçable à la souris. Reverrouille-le ensuite si tu veux.",
            Kind = LessonValidationKind.ToggleOnce,
            ToggleEventName = "Lock",
        },
        new Lesson
        {
            Id = "0.3",
            Chapter = 0,
            ChapterTitle = "Prise en main de l'app",
            Title = "Suspendre la capture",
            Objective = "Comprends pourquoi l'app peut réagir même hors du jeu, et comment couper ça.",
            Explanation = "L'app écoute ton clavier/ta manette partout sur ton PC (pas seulement dans Brawlhalla) — nécessaire pour fonctionner par-dessus le jeu. Si tu utilises ton PC normalement (navigateur, autre jeu...), suspends la capture (Ctrl+Alt+H, ou le bouton ⏸ de la barre de contrôle overlay) pour que l'historique et le mode Tutoriel arrêtent de réagir à ce que tu tapes ailleurs.",
            Kind = LessonValidationKind.ToggleOnce,
            ToggleEventName = "CaptureSuspended",
        },

        // ================= Chapitre 1 — Survivre =================
        new Lesson
        {
            Id = "1.1",
            Chapter = 1,
            ChapterTitle = "Survivre",
            Title = "Tes ressources en l'air",
            Objective = "Situe où sont tes sauts, ton esquive et ta récupération.",
            Explanation = "Une fois en l'air, tu as droit à un total de 3 actions avant de retoucher le sol ou un mur : soit 2 sauts aériens + 1 récupération, soit l'inverse (1 saut + 2 récupérations, la 2ᵉ étant une \"Exhausted Recovery\" qui monte beaucoup moins). L'esquive est séparée de ce total : elle marche à un cooldown (pas un nombre fixe), tu la récupères automatiquement après un temps donné. La récupération, elle, n'a pas de cooldown : une fois utilisée (les fois que tu en as), c'est fini jusqu'au prochain sol/mur. Ici, fais dans l'ordre : Saut, Saut, Esquive, puis Récupération (Haut + Att. forte) — juste pour repérer chaque bouton, l'ordre réel en jeu est libre.",
            Kind = LessonValidationKind.Sequence,
            Sequence = new()
            {
                new LessonStep { RequiredActions = new() { "Saut" } },
                new LessonStep { RequiredActions = new() { "Saut" } },
                new LessonStep { RequiredActions = new() { "Esquive" } },
                new LessonStep { RequiredActions = new() { "Haut", "Att. forte" } },
            },
            SourceNote = "Pool de 3 actions (sauts/récupérations) confirmé sur brawlhalla.wiki.gg/wiki/Movement, subtilité \"2+1 ou 1+2\" et fonctionnement de l'esquive/récupération confirmés par l'utilisateur (test en jeu), 2026-08-03.",
        },
        new Lesson
        {
            Id = "1.2",
            Chapter = 1,
            ChapterTitle = "Survivre",
            Title = "La chaîne de récupération",
            Objective = "Enchaîne tes ressources au lieu de tout cramer d'un coup.",
            Explanation = "Le plus gros piège en tant que débutant : paniquer et tout dépenser (sauts + esquive + récupération) dès la 1ère seconde en l'air. Mieux vaut garder de quoi réagir à ce qui vient ensuite. Enchaîne ici Saut, Esquive, puis Récupération, sans trop traîner entre les deux — l'app valide que tu enchaînes les bons boutons dans l'ordre, mais ne peut pas savoir si tu es vraiment revenu sur le stage en jeu : à vérifier toi-même en Training Room.",
            Kind = LessonValidationKind.Sequence,
            Sequence = new()
            {
                new LessonStep { RequiredActions = new() { "Saut" } },
                new LessonStep { RequiredActions = new() { "Esquive" } },
                new LessonStep { RequiredActions = new() { "Haut", "Att. forte" } },
            },
            FullyValidatedByApp = false,
            VerifyYourselfNote = "L'app valide la séquence d'appuis, pas le résultat en jeu — va vérifier en Training Room que cet enchaînement te ramène bien sur le stage depuis une position difficile.",
        },
        new Lesson
        {
            Id = "1.3",
            Chapter = 1,
            ChapterTitle = "Survivre",
            Title = "Fast fall",
            Objective = "Tombe plus vite pour te déplacer plus librement en l'air.",
            Explanation = "Tenir Bas en l'air (fast fall) accélère ta chute — utile pour revenir plus vite au sol ou punir un adversaire en dessous, plutôt que de planer lentement et rester une cible facile. Tiens Bas un instant pour t'entraîner au geste.",
            Kind = LessonValidationKind.PressAllOnce,
            RequiredActionsOnce = new() { "Bas" },
            FullyValidatedByApp = false,
            VerifyYourselfNote = "L'app confirme juste que tu tiens Bas — elle ne peut pas savoir si tu étais réellement en l'air au bon moment : à sentir toi-même en jeu.",
            SourceNote = "Confirmé par l'utilisateur (test en jeu) et par brawlhalla.wiki.gg/wiki/Movement, 2026-08-03.",
        },
        new Lesson
        {
            Id = "1.4",
            Chapter = 1,
            ChapterTitle = "Survivre",
            Title = "Ne pas paniquer au dodge",
            Objective = "Résiste à l'envie d'esquiver au moindre stress.",
            Explanation = "Le pire réflexe : esquiver toujours dans la même direction, ou dès qu'un adversaire approche — ça devient lisible et punissable. Sois imprévisible : parfois esquiver sur place (invincibilité plus longue mais tu restes sur place), parfois dans une direction (tu bouges, mais l'invincibilité est plus courte). Ici, pas de bouton à presser : le but est de tenir sans esquiver pendant quelques secondes, comme si tu réfléchissais avant d'agir plutôt que de mashé le dodge.",
            Kind = LessonValidationKind.AbsenceTimer,
            AbsenceActions = new() { "Esquive" },
            AbsenceSeconds = 8,
            FullyValidatedByApp = false,
            VerifyYourselfNote = "Ce \"drill de retenue\" ne prouve pas que tu esquives moins bêtement en vrai combat, juste que tu peux tenir sans presser le bouton par réflexe — le vrai test, c'est en jeu.",
            SourceNote = "Conseil de l'utilisateur (expérience de jeu personnelle), 2026-08-03 — pas une citation externe.",
        },

        // ================= Chapitre 2 — Frapper =================
        new Lesson
        {
            Id = "2.1",
            Chapter = 2,
            ChapterTitle = "Frapper",
            Title = "Les 3 lights",
            Objective = "Repère les 3 variantes de l'attaque légère.",
            Explanation = "Att. légère seule = light neutre. Avec une direction en plus (Gauche/Droite ou Bas), tu obtiens la variante latérale ou basse — même bouton, juste une direction tenue avec. Contrairement à ce qu'un guide générique pourrait laisser penser, il n'y a pas un rôle fixe et universel pour chacune (\"la neutre pour initier, la latérale pour combo\"...) — ça dépend vraiment de l'arme, du personnage et du combo visé. Ici, on repère juste les 3 variantes, pas leur usage.",
            Kind = LessonValidationKind.Sequence,
            Sequence = new()
            {
                new LessonStep { RequiredActions = new() { "Att. légère" } },
                new LessonStep { RequiredActions = new() { "Droite", "Att. légère" } },
                new LessonStep { RequiredActions = new() { "Bas", "Att. légère" } },
            },
            SourceNote = "L'utilisateur a explicitement corrigé une généralisation proposée (\"neutre pour initier, latérale pour combo...\") : pas de rôle universel, ça dépend de l'arme/perso/combo — leçon volontairement recentrée sur le repérage des boutons, pas leur usage, 2026-08-03.",
        },
        new Lesson
        {
            Id = "2.2",
            Chapter = 2,
            ChapterTitle = "Frapper",
            Title = "Les aériens",
            Objective = "Les mêmes boutons, mais en l'air après un saut.",
            Explanation = "nAir/sAir/dAir utilisent exactement les mêmes boutons que les lights au sol (Att. légère + direction), simplement pressés en l'air après un saut. Elles ont souvent un usage assez situationnel — dAir pour toucher un adversaire en dessous (spike/gimp), nAir comme sauvetage rapide, sAir en edgeguard — sans que ce soit exclusif : elles restent utilisables dans d'autres situations selon l'arme et le personnage. Ici : Saut, puis les 3 variantes.",
            Kind = LessonValidationKind.Sequence,
            Sequence = new()
            {
                new LessonStep { RequiredActions = new() { "Saut" } },
                new LessonStep { RequiredActions = new() { "Att. légère" } },
                new LessonStep { RequiredActions = new() { "Droite", "Att. légère" } },
                new LessonStep { RequiredActions = new() { "Bas", "Att. légère" } },
            },
            FullyValidatedByApp = false,
            VerifyYourselfNote = "L'app valide juste la séquence de boutons — elle ne sait pas si tu étais vraiment en l'air au bon moment.",
            SourceNote = "Usages (dAir=spike/gimp, nAir=sauvetage, sAir=edgeguard) confirmés par l'utilisateur comme globalement justes mais non exclusifs — ces attaques restent utilisables ailleurs aussi, 2026-08-03.",
        },
        new Lesson
        {
            Id = "2.3",
            Chapter = 2,
            ChapterTitle = "Frapper",
            Title = "Signature ≠ bouton séparé",
            Objective = "La Signature, c'est juste Att. forte (+ une direction).",
            Explanation = "Erreur fréquente en découvrant l'app : chercher un bouton \"Signature\" séparé. Il n'existe pas — la Signature EST l'attaque forte (nSig neutre, sSig latérale avec une direction, dSig basse). Même logique que les lights : direction + bouton.",
            Kind = LessonValidationKind.Sequence,
            Sequence = new()
            {
                new LessonStep { RequiredActions = new() { "Att. forte" } },
                new LessonStep { RequiredActions = new() { "Droite", "Att. forte" } },
                new LessonStep { RequiredActions = new() { "Bas", "Att. forte" } },
            },
        },
        new Lesson
        {
            Id = "2.4",
            Chapter = 2,
            ChapterTitle = "Frapper",
            Title = "Ne pas spammer la Signature",
            Objective = "La Signature punit celui qui la rate, pas que l'adversaire.",
            Explanation = "Spammer la Signature est une mauvaise idée très concrète : l'animation est lente et te bloque sur place, donc un whiff (un raté) se punit sévèrement. C'est aussi un style de jeu que beaucoup trouvent frustrant à affronter — pas illégal, juste peu apprécié. Ici, pas de bouton à presser : tiens sans presser Att. forte pendant quelques secondes.",
            Kind = LessonValidationKind.AbsenceTimer,
            AbsenceActions = new() { "Att. forte" },
            AbsenceSeconds = 8,
            FullyValidatedByApp = false,
            VerifyYourselfNote = "Ce drill de retenue ne prouve pas que tu ne spammeras pas en vrai combat sous pression — juste que tu peux t'en empêcher hors contexte.",
            SourceNote = "Confirmé par l'utilisateur (expérience de jeu personnelle) : animation lente qui bloque sur place, whiff punissable, et aspect \"toxique\"/frustrant pour l'adversaire sans être illégal, 2026-08-03.",
        },

        // ================= Chapitre 3 — Bouger =================
        new Lesson
        {
            Id = "3.1",
            Chapter = 3,
            ChapterTitle = "Bouger",
            Title = "Dash",
            Objective = "Le même bouton que l'esquive, mais au sol ça ne dodge pas.",
            Explanation = "Au sol, presser Esquive + une direction ne donne PAS d'invincibilité (contrairement à l'esquive aérienne) — ça fait un Dash, un déplacement rapide au sol. Comme ce n'est pas vraiment une esquive, ça ne touche pas au cooldown de l'esquive aérienne : les deux sont indépendants. Utile pour couvrir de la distance vite ou se replacer.",
            Kind = LessonValidationKind.Sequence,
            Sequence = new()
            {
                new LessonStep { RequiredActions = new() { "Droite", "Esquive" } },
            },
            FullyValidatedByApp = false,
            VerifyYourselfNote = "L'app valide juste l'appui Esquive+direction — elle ne sait pas si tu étais au sol (donc en train de dasher) ou en l'air (donc en train d'esquiver avec invincibilité).",
            SourceNote = "Confirmé par l'utilisateur (expérience de jeu personnelle) : au sol Esquive = Dash sans invincibilité, cooldown séparé de l'esquive aérienne, 2026-08-03.",
        },
        new Lesson
        {
            Id = "3.2",
            Chapter = 3,
            ChapterTitle = "Bouger",
            Title = "Dash jump",
            Objective = "Enchaîne direction, Dash, puis Saut pour un saut qui couvre plus de distance.",
            Explanation = "Presse une direction, dashe dans cette direction (Esquive + direction), puis saute environ une demi-seconde après — presque enchaîné. Ça te propulse dans la direction du dash avec un peu de hauteur en plus, au lieu d'un saut vertical classique.",
            Kind = LessonValidationKind.Sequence,
            Sequence = new()
            {
                new LessonStep { RequiredActions = new() { "Droite" } },
                new LessonStep { RequiredActions = new() { "Droite", "Esquive" } },
                new LessonStep { RequiredActions = new() { "Saut" } },
            },
            FullyValidatedByApp = false,
            VerifyYourselfNote = "L'app valide l'ordre des boutons, pas le timing exact (~0.5s) ni si tu étais bien au sol au moment du dash.",
            SourceNote = "Confirmé par l'utilisateur (expérience de jeu personnelle) : direction → dash → saut ~0.5s après, propulsion avec un peu de hauteur, 2026-08-03.",
        },
        new Lesson
        {
            Id = "3.3",
            Chapter = 3,
            ChapterTitle = "Bouger",
            Title = "Backdash",
            Objective = "Le même Dash, mais vers l'arrière — pour créer de la distance.",
            Explanation = "Même mécanique que le Dash (Esquive + direction au sol, sans invincibilité), mais vers l'arrière plutôt que vers l'avant. Sert surtout à créer de la distance ou punir un adversaire qui rate son attaque (whiff punish). En jeu, \"arrière\" dépend du sens où tu regardes — ici on prend juste Gauche comme exemple.",
            Kind = LessonValidationKind.Sequence,
            Sequence = new()
            {
                new LessonStep { RequiredActions = new() { "Gauche", "Esquive" } },
            },
            FullyValidatedByApp = false,
            // Note complétée lors de l'audit 2026-08-07 (§F4) : ComboRunner.CanonicalizeHorizontal
            // fusionne Gauche et Droite en un seul jeton pour TOUS ses consommateurs, y compris le
            // ComboRunner éphémère de cette leçon. Cette leçon-ci valide donc les deux sens
            // indifféremment — conséquence directe et assumée de la règle produit "gauche et droite
            // reviennent au même", mais qui mérite d'être dite ici plutôt que de laisser croire que
            // le drill vérifie l'orientation.
            VerifyYourselfNote = "L'app ne connaît pas le sens où ton personnage regarde — en jeu, backdash veut dire dasher vers l'arrière par rapport à ton orientation, pas forcément vers la gauche. Concrètement, ce drill se valide avec Gauche OU Droite : c'est à toi de vérifier en jeu que tu pars bien vers l'arrière.",
            SourceNote = "Confirmé par l'utilisateur (expérience de jeu personnelle) : même mécanique que le Dash, vers l'arrière, pour distance/whiff punish, 2026-08-03.",
        },

        // ================= Chapitre 4 — Ton premier vrai combo =================
        // Contrairement aux chapitres précédents, aucune nouvelle affirmation de jeu n'est
        // introduite ici : les deux séquences ci-dessous sont recopiées littéralement des combos
        // Blasters déjà vérifiés dans WeaponComboPresets.Table ("DLight vers NLight" / "DLight vers
        // SAir"), pas de nouvelle donnée à sourcer. Choisies pour leur seuil de Dex le plus bas (3+)
        // de tout WeaponComboPresets — jouables par la quasi-totalité des légendes du jeu, contexte
        // idéal pour une toute première combo.
        new Lesson
        {
            Id = "4.1",
            Chapter = 4,
            ChapterTitle = "Ton premier vrai combo",
            Title = "dLight > nLight",
            Objective = "Ta première combo réelle : attaque légère basse, puis attaque légère neutre.",
            Explanation = "Un « true combo » enchaîne deux coups sans que l'adversaire puisse esquiver entre les deux — contrairement à un simple enchaînement de boutons, ça touche vraiment en match. Celle-ci (dLight > nLight, aux Blasters) est l'une des plus accessibles du jeu : jouable dès 3 de Dex, donc par la quasi-totalité des légendes.",
            Kind = LessonValidationKind.Sequence,
            Sequence = new()
            {
                new LessonStep { RequiredActions = new() { "Bas", "Att. légère" } },
                new LessonStep { RequiredActions = new() { "Att. légère" } },
            },
            FullyValidatedByApp = false,
            VerifyYourselfNote = "L'app valide l'ordre des boutons, pas si le premier coup a réellement touché l'adversaire en jeu (condition réelle pour que le second connecte comme un vrai true combo).",
            SourceNote = "Combo « DLight vers NLight » (Blasters, 3+ Dex) de WeaponComboPresets.cs, elle-même sourcée sur des true combos testés à 0% de dégâts fournis par l'utilisateur (voir CLAUDE.md) — reprise ici telle quelle, aucune nouvelle donnée introduite.",
        },
        new Lesson
        {
            Id = "4.2",
            Chapter = 4,
            ChapterTitle = "Ton premier vrai combo",
            Title = "dLight > sAir",
            Objective = "Une combo un peu plus longue : la même ouverture, suivie d'un saut vers un aérien.",
            Explanation = "Toujours aux Blasters et toujours 3+ Dex : dLight, puis un saut, puis une attaque légère latérale en l'air (sAir). Une fois dLight > nLight à l'aise, ce genre de variante à 3 étapes est le pas suivant naturel — dans l'onglet Combos du panneau de contrôle, les combos qui partagent ainsi un début commun sont regroupées et indentées (« familles de combos ») pour repérer ces enchaînements facilement.",
            Kind = LessonValidationKind.Sequence,
            Sequence = new()
            {
                new LessonStep { RequiredActions = new() { "Bas", "Att. légère" } },
                new LessonStep { RequiredActions = new() { "Saut" } },
                new LessonStep { RequiredActions = new() { "Droite", "Att. légère" } },
            },
            FullyValidatedByApp = false,
            VerifyYourselfNote = "Comme pour 4.1, l'app valide la séquence d'inputs, pas si chaque coup a réellement touché l'adversaire en jeu.",
            SourceNote = "Combo « DLight vers SAir » (Blasters, 3+ Dex) de WeaponComboPresets.cs, même source que la leçon 4.1 — reprise ici telle quelle.",
        },
    };
}
