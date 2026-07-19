namespace BrawlhallaOverlay;

/// <summary>
/// Cumulative precision counters for one KeyBind.Action across all combo
/// attempts (all combos combined), persisted so it survives app restarts.
/// </summary>
public sealed class ActionStat
{
    public string Action { get; set; } = "";
    public int Successes { get; set; }
    public int Failures { get; set; }
}
