using System.Collections.Generic;
using System.Linq;

namespace BrawlhallaOverlay;

/// <summary>
/// Détection purement calculée (rien de persisté dans combos.json) des "familles" de
/// combos : un groupe où chaque combo (sauf le premier) est une extension stricte
/// d'une autre — mêmes premières étapes, dans le même ordre, plus au moins une étape
/// en plus à la fin. Sert uniquement à l'affichage groupé/indenté dans les listes de
/// combos (ControlPanelWindow, DashboardWindow) — voir docs/combo_families_plan.md.
/// Ne touche ni ComboRunner ni ChainCombos : chaque combo reste une séquence
/// autonome, une famille n'est qu'un regroupement de présentation.
/// </summary>
public static class ComboFamilies
{
    public sealed class ComboFamily
    {
        public int FamilyId;

        /// <summary>Membres triés par niveau croissant (1 = le plus court), avec
        /// l'ordre d'origine comme départage entre combos de même niveau (siblings).</summary>
        public List<(Combo Combo, int Level)> MembersByLevel = new();

        /// <summary>Id des combos qui ont au moins une extension directe dans cette
        /// famille — sert à savoir si un combo "Mastered" a un niveau supérieur à
        /// suggérer.</summary>
        public HashSet<string> ComboIdsWithFollowUp = new();
    }

    private static bool StepEquals(ComboStep a, ComboStep b) =>
        a.RequiredActions.Count == b.RequiredActions.Count
        && new HashSet<string>(a.RequiredActions).SetEquals(b.RequiredActions);

    /// <summary>Vrai si `shorter` est un préfixe strict de `longer` (mêmes arme/légend,
    /// strictement moins d'étapes, et chaque étape commune identique).</summary>
    private static bool IsPrefixOf(Combo shorter, Combo longer)
    {
        if (shorter.Weapon != longer.Weapon || shorter.Legend != longer.Legend) return false;
        if (shorter.Steps.Count >= longer.Steps.Count) return false;
        for (var i = 0; i < shorter.Steps.Count; i++)
        {
            if (!StepEquals(shorter.Steps[i], longer.Steps[i])) return false;
        }
        return true;
    }

    /// <summary>Regroupe les combos en familles à partir de la liste fournie (typiquement
    /// déjà filtrée par arme/personnage). Les combos sans aucune relation de préfixe avec
    /// une autre de la liste n'apparaissent dans aucune famille retournée — l'appelant les
    /// affiche telles quelles, sans changement.</summary>
    public static List<ComboFamily> Group(IReadOnlyList<Combo> combos)
    {
        // Prédécesseur immédiat = le préfixe le plus long parmi ceux qui préfixent cette
        // combo (le "parent" direct, pas juste n'importe quel ancêtre plus court).
        var immediatePredecessor = new Dictionary<string, Combo>();
        foreach (var x in combos)
        {
            Combo? best = null;
            foreach (var y in combos)
            {
                if (ReferenceEquals(x, y)) continue;
                if (!IsPrefixOf(y, x)) continue;
                if (best is null || y.Steps.Count > best.Steps.Count) best = y;
            }
            if (best is not null) immediatePredecessor[x.Id] = best;
        }

        var hasSuccessor = immediatePredecessor.Values.Select(c => c.Id).ToHashSet();

        var levelCache = new Dictionary<string, int>();
        int LevelOf(Combo c)
        {
            if (levelCache.TryGetValue(c.Id, out var cached)) return cached;
            var level = immediatePredecessor.TryGetValue(c.Id, out var pred) ? LevelOf(pred) + 1 : 1;
            levelCache[c.Id] = level;
            return level;
        }

        Combo RootOf(Combo c)
        {
            var cur = c;
            while (immediatePredecessor.TryGetValue(cur.Id, out var pred)) cur = pred;
            return cur;
        }

        var familyIdByRootId = new Dictionary<string, int>();
        var familiesByRootId = new Dictionary<string, ComboFamily>();
        var nextFamilyId = 1;

        for (var i = 0; i < combos.Count; i++)
        {
            var combo = combos[i];
            var inFamily = immediatePredecessor.ContainsKey(combo.Id) || hasSuccessor.Contains(combo.Id);
            if (!inFamily) continue;

            var root = RootOf(combo);
            if (!familyIdByRootId.TryGetValue(root.Id, out var famId))
            {
                famId = nextFamilyId++;
                familyIdByRootId[root.Id] = famId;
                familiesByRootId[root.Id] = new ComboFamily { FamilyId = famId };
            }

            var family = familiesByRootId[root.Id];
            family.MembersByLevel.Add((combo, LevelOf(combo)));
            if (hasSuccessor.Contains(combo.Id)) family.ComboIdsWithFollowUp.Add(combo.Id);
        }

        // Départage des combos de même niveau (siblings) par ordre d'apparition d'origine,
        // pas par Id, pour rester stable visuellement d'un rafraîchissement à l'autre.
        var originalOrder = combos.Select((c, i) => (c.Id, i)).ToDictionary(t => t.Id, t => t.i);
        foreach (var family in familiesByRootId.Values)
        {
            family.MembersByLevel.Sort((a, b) =>
            {
                var byLevel = a.Level.CompareTo(b.Level);
                return byLevel != 0 ? byLevel : originalOrder[a.Combo.Id].CompareTo(originalOrder[b.Combo.Id]);
            });
        }

        return familiesByRootId.Values.ToList();
    }

    /// <summary>Un combo prêt à être affiché, avec sa position dans une famille si
    /// elle en fait partie (Level 0 = pas de famille détectée, rendu inchangé).</summary>
    public readonly struct OrderedCombo
    {
        public required int Index { get; init; }
        public required int Level { get; init; }
        public required int FamilySize { get; init; }
        public required bool HasFollowUp { get; init; }
    }

    /// <summary>Réordonne une liste d'index de combos (typiquement déjà filtrée par
    /// arme/personnage) pour que les membres d'une même famille se retrouvent adjacents,
    /// triés par niveau croissant — au lieu de leur ordre d'origine dans `combos.json`, qui
    /// peut les avoir dispersés. Les combos sans famille gardent leur position d'origine.
    /// `resolve` traduit un index (absolu dans AppState.Combos, ou autre selon l'appelant)
    /// en Combo — factorisé ici pour que ControlPanelWindow et DashboardWindow affichent la
    /// même logique de regroupement sans la dupliquer.</summary>
    public static List<OrderedCombo> OrderWithFamilies(IReadOnlyList<int> indices, System.Func<int, Combo> resolve)
    {
        var combos = indices.Select(resolve).ToList();
        var families = Group(combos);

        var familyByComboId = new Dictionary<string, ComboFamily>();
        var levelByComboId = new Dictionary<string, int>();
        foreach (var family in families)
        {
            foreach (var (combo, level) in family.MembersByLevel)
            {
                familyByComboId[combo.Id] = family;
                levelByComboId[combo.Id] = level;
            }
        }

        var indexByComboId = new Dictionary<string, int>();
        for (var k = 0; k < indices.Count; k++) indexByComboId[combos[k].Id] = indices[k];

        var emitted = new HashSet<string>();
        var result = new List<OrderedCombo>();
        foreach (var combo in combos)
        {
            if (!emitted.Add(combo.Id)) continue;

            if (familyByComboId.TryGetValue(combo.Id, out var family))
            {
                // Le combo courant fait partie d'une famille : on émet toute la famille
                // d'un coup (triée par niveau), pas seulement ce combo, pour que les
                // membres restent groupés même si l'un d'eux est rencontré en premier.
                foreach (var (member, level) in family.MembersByLevel)
                {
                    if (member.Id != combo.Id && !emitted.Add(member.Id)) continue;
                    result.Add(new OrderedCombo
                    {
                        Index = indexByComboId[member.Id],
                        Level = level,
                        FamilySize = family.MembersByLevel.Count,
                        HasFollowUp = family.ComboIdsWithFollowUp.Contains(member.Id),
                    });
                }
            }
            else
            {
                result.Add(new OrderedCombo { Index = indexByComboId[combo.Id], Level = 0, FamilySize = 0, HasFollowUp = false });
            }
        }

        return result;
    }
}
