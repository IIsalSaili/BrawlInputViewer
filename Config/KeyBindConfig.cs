using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>Nom du profil de touches par défaut (celui de toujours, fichier keybinds.json).</summary>
    public const string DefaultProfileName = "Défaut";

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    // Profils multiples (ex. AZERTY vs QWERTY, solo vs équipe) : le profil "Défaut"
    // reste sur keybinds.json (compatibilité avec les installs existantes), les
    // profils additionnels vivent dans des fichiers séparés à côté de l'exe.
    private static string ProfilePath(string profileName) =>
        profileName == DefaultProfileName
            ? ConfigPath
            : Path.Combine(AppContext.BaseDirectory, $"keybinds.{SanitizeFileName(profileName)}.json");

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    /// <summary>Liste les profils existants sur disque, "Défaut" toujours inclus en premier.</summary>
    public static List<string> ListProfiles()
    {
        var names = new List<string> { DefaultProfileName };
        var dir = AppContext.BaseDirectory;
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "keybinds.*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file).Substring("keybinds.".Length);
                if (!names.Contains(name)) names.Add(name);
            }
        }
        return names;
    }

    public static List<KeyBind> LoadOrCreateDefault() => LoadProfile(DefaultProfileName);

    public static List<KeyBind> LoadProfile(string profileName)
    {
        var binds = JsonFileStore.Load<List<KeyBind>?>(ProfilePath(profileName), null);

        if (binds is null || binds.Count == 0)
        {
            binds = new List<KeyBind>(Defaults);
            SaveProfile(profileName, binds);
        }

        RecomputeVirtualKeyCodes(binds);

        return binds;
    }

    public static void SaveProfile(string profileName, List<KeyBind> binds) =>
        JsonFileStore.Save(ProfilePath(profileName), binds);

    public static void DeleteProfile(string profileName)
    {
        if (profileName == DefaultProfileName) return; // le profil par défaut n'est jamais supprimable
        var path = ProfilePath(profileName);
        if (File.Exists(path)) File.Delete(path);
    }

    public static void RecomputeVirtualKeyCodes(List<KeyBind> binds)
    {
        foreach (var bind in binds)
        {
            var codes = bind.Keys.ConvertAll(ResolveVirtualKeyCode);
            foreach (var buttonName in bind.GamepadButtons)
            {
                // Bouton manette inconnu (nom mal orthographié à la main dans le JSON) :
                // ignoré plutôt que de planter l'appli, comme les touches invalides ci-dessous
                // sont, elles, refusées explicitement à la sauvegarde par le panneau de contrôle.
                if (GamepadHook.TryResolveSyntheticCode(buttonName, out var code)) codes.Add(code);
            }
            bind.VirtualKeyCodes = codes;
        }
    }

    private static int ResolveVirtualKeyCode(string keyName)
    {
        if (!TryResolveVirtualKeyCode(keyName, out var vk))
        {
            throw new InvalidOperationException(
                $"Touche inconnue dans keybinds.json : \"{keyName}\". " +
                "Utilise un nom de touche .NET valide, ex: Q, D, Z, S, Left, Right, Up, Down, LeftShift, L, G.");
        }

        return vk;
    }

    /// <summary>Non-throwing variant used by the control panel to validate user input before saving.</summary>
    public static bool TryResolveVirtualKeyCode(string keyName, out int vk)
    {
        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
        {
            vk = 0;
            return false;
        }

        vk = KeyInterop.VirtualKeyFromKey(key);
        return true;
    }
}
