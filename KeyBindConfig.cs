using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace BrawlhallaOverlay;

public static class KeyBindConfig
{
    private const string FileName = "keybinds.json";

    // Mapping clavier perso (ZQSD + flèches + Shift).
    // Édite keybinds.json (créé à côté de l'exe au premier lancement) si tu
    // changes encore tes touches en jeu.
    private static readonly List<KeyBind> Defaults = new()
    {
        new KeyBind { Action = "Gauche", Keys = new() { "Q" }, Label = "Q", Symbol = "◄", Color = "#5DADE2", Group = "Movement", Slot = "Left" },
        new KeyBind { Action = "Droite", Keys = new() { "D" }, Label = "D", Symbol = "►", Color = "#2E86C1", Group = "Movement", Slot = "Right" },
        new KeyBind { Action = "Haut",   Keys = new() { "Z" }, Label = "Z", Symbol = "▲", Color = "#48C9B0", Group = "Movement", Slot = "Up" },
        new KeyBind { Action = "Bas",    Keys = new() { "S" }, Label = "S", Symbol = "▼", Color = "#229954", Group = "Movement", Slot = "Down" },

        new KeyBind { Action = "Saut",        Keys = new() { "Space" },            Label = "Saut",        Symbol = "⇧", Color = "#58D68D", Group = "Action" },
        new KeyBind { Action = "Att. légère", Keys = new() { "Left" },              Label = "Att. légère", Symbol = "⚡", Color = "#F4D03F", Group = "Action" },
        new KeyBind { Action = "Att. forte",  Keys = new() { "Right" },             Label = "Att. forte",  Symbol = "💥", Color = "#E67E22", Group = "Action" },
        new KeyBind { Action = "Esquive",     Keys = new() { "LeftShift", "Down" }, Label = "Esquive",     Symbol = "💨", Color = "#E74C3C", Group = "Action" },
        new KeyBind { Action = "Lancer",      Keys = new() { "Up" },                Label = "Lancer",      Symbol = "🎯", Color = "#9B59B6", Group = "Action" },
        new KeyBind { Action = "Taunt",       Keys = new() { "G" },                 Label = "Taunt",       Symbol = "💬", Color = "#85929E", Group = "Action" },
    };

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static List<KeyBind> LoadOrCreateDefault()
    {
        List<KeyBind>? binds = null;

        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                binds = JsonSerializer.Deserialize<List<KeyBind>>(json);
            }
            catch
            {
                // Fichier corrompu ou mal formé : on retombe sur les valeurs par défaut.
                binds = null;
            }
        }

        if (binds is null || binds.Count == 0)
        {
            binds = new List<KeyBind>(Defaults);
            Save(binds);
        }

        foreach (var bind in binds)
        {
            bind.VirtualKeyCodes = bind.Keys.ConvertAll(ResolveVirtualKeyCode);
        }

        return binds;
    }

    private static void Save(List<KeyBind> binds)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(binds, options));
    }

    private static int ResolveVirtualKeyCode(string keyName)
    {
        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
        {
            throw new InvalidOperationException(
                $"Touche inconnue dans keybinds.json : \"{keyName}\". " +
                "Utilise un nom de touche .NET valide, ex: Q, D, Z, S, Left, Right, Up, Down, LeftShift, L, G.");
        }

        return KeyInterop.VirtualKeyFromKey(key);
    }
}
