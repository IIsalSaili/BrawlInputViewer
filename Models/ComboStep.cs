using System.Collections.Generic;

namespace BrawlhallaOverlay;

/// <summary>
/// One step of a combo: a set of KeyBind.Action names that must be pressed
/// together (simultaneous combination, e.g. dodge = "Esquive" alone, or two
/// actions at once), plus a timing tolerance window relative to the previous
/// step's success.
/// </summary>
public sealed class ComboStep
{
    public List<string> RequiredActions { get; set; } = new();

    /// <summary>Max delay (ms) after the previous step succeeded. Null = no limit.</summary>
    public int? MaxDelayMs { get; set; }

    /// <summary>Min delay (ms) after the previous step succeeded. Null = no minimum.</summary>
    public int? MinDelayMs { get; set; }

    /// <summary>Si vrai, cette étape tolère n'importe quelle direction tenue en plus
    /// de ce qui est requis, même en MatchMode.Strict (ex. un coup qui demande de se
    /// décaler pour toucher la hitbox, sans que ce décalage fasse partie du combo
    /// lui-même). Sans effet en IgnoreExtraneous, qui tolère déjà tout excédent de
    /// mouvement sur toutes les étapes.</summary>
    public bool FreeMovement { get; set; }
}
