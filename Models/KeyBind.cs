using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BrawlhallaOverlay;

/// <summary>
/// One tracked action: which physical key(s) trigger it, what label/color to show.
/// Each entry in "Keys" must be the name of a System.Windows.Input.Key enum value
/// (e.g. "Left", "Right", "Up", "Down", "Space", "LeftShift", "J", "K", "L", "G").
/// An action can have more than one key (e.g. dodge on both Shift and Down).
/// </summary>
public sealed class KeyBind
{
    public string Action { get; set; } = "";
    public List<string> Keys { get; set; } = new();

    /// <summary>Noms de boutons manette (voir GamepadHook.Buttons, ex. "A", "DPadUp"), en plus
    /// des touches clavier — les deux déclenchent la même action, l'un n'exclut pas l'autre.</summary>
    public List<string> GamepadButtons { get; set; } = new();

    public string Label { get; set; } = "";

    /// <summary>"Movement" (placed in the ZQSD keycap cluster) or "Action" (placed as a big button).</summary>
    public string Group { get; set; } = "Action";

    /// <summary>For Group="Movement" only: "Up", "Left", "Down" or "Right" position in the cluster.</summary>
    public string Slot { get; set; } = "";

    [JsonIgnore]
    public List<int> VirtualKeyCodes { get; set; } = new();
}
