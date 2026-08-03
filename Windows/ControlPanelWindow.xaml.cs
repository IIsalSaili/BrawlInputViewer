using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BrawlhallaOverlay;

/// <summary>
/// Fenêtre de réglages classique (bordée, dans la taskbar) séparée de l'overlay :
/// navigation latérale + panneau de contenu par onglet. Tout ce qu'elle édite
/// passe par AppState, donc les changements se répercutent immédiatement sur
/// l'overlay sans redémarrage.
/// </summary>
public partial class ControlPanelWindow : Window
{
    private static readonly string[] TabNames = { "Général", "Touches", "Combos", "Apparence", "À propos" };
    // Fond bleu-nuit/violet sombre (au lieu du gris neutre d'origine) + accent doré
    // repris de la tray icon (#E8C44A) : signature de marque cohérente avec l'overlay
    // et le motif "écusson" de l'UI Brawlhalla — voir Brawhl.md section 4/7.
    private static readonly Brush PanelBg = new SolidColorBrush(Color.FromRgb(0x18, 0x17, 0x22));
    private static readonly Brush CardBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x27, 0x35));
    private static readonly Brush TextColor = Brushes.White;
    private static readonly Brush SubtleText = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
    private static readonly Brush AccentGold = new SolidColorBrush(Color.FromRgb(0xE8, 0xC4, 0x4A));

    private ContentControl _content = null!;
    private ListBox _nav = null!;
    private Action? _unsubscribeCurrentTab;

    public ControlPanelWindow()
    {
        InitializeComponent();
        Build();
    }

    private void Build()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _nav = new ListBox
        {
            Background = PanelBg,
            BorderThickness = new Thickness(0),
            Foreground = TextColor,
            FontSize = 14,
            Padding = new Thickness(0, 12, 0, 0),
        };
        foreach (var name in TabNames)
        {
            _nav.Items.Add(new ListBoxItem { Content = name, Padding = new Thickness(16, 10, 16, 10) });
        }

        // Onglet actif marqué d'un liseré doré à gauche (accent de marque) plutôt que
        // la surbrillance système par défaut — BorderThickness posé même à l'état
        // non sélectionné (brush transparent) pour que la largeur ne saute pas au clic.
        var navItemStyle = new Style(typeof(ListBoxItem));
        navItemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(3, 0, 0, 0)));
        navItemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, AccentGold));
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, CardBg));
        navItemStyle.Triggers.Add(selectedTrigger);
        _nav.ItemContainerStyle = navItemStyle;

        _nav.SelectionChanged += (_, _) => ShowTab(_nav.SelectedIndex);
        // La nav garde le focus clavier par défaut, et ses flèches (ou même une
        // lettre comme "G", qui saute au premier onglet commençant par G via la
        // recherche incrémentale native du ListBox) sont aussi des touches de jeu
        // (Att. légère, Taunt...) : sans ça, jouer/enregistrer une combo pendant
        // que le panneau a le focus changeait d'onglet à chaque appui. Onglet =
        // souris uniquement.
        _nav.PreviewKeyDown += (_, e) => e.Handled = true;
        Grid.SetColumn(_nav, 0);
        root.Children.Add(_nav);

        _content = new ContentControl { Margin = new Thickness(20) };
        Grid.SetColumn(_content, 1);
        root.Children.Add(_content);

        RootGrid.Children.Add(root);

        _nav.SelectedIndex = 0;

        Closed += (_, _) => _unsubscribeCurrentTab?.Invoke();
    }

    private void ShowTab(int index)
    {
        _unsubscribeCurrentTab?.Invoke();
        _unsubscribeCurrentTab = null;

        _content.Content = index switch
        {
            0 => BuildGeneralTab(),
            1 => BuildKeybindsTab(),
            2 => BuildCombosTab(),
            3 => BuildAppearanceTab(),
            4 => BuildAboutTab(),
            _ => null,
        };
    }

    // --- Helpers de mise en page communs ---

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeights.Bold,
        Foreground = AccentGold,
        Margin = new Thickness(0, 0, 0, 14),
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = TextColor,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 10, 0),
    };

    private static TextBlock HelpText(string text) => new()
    {
        Text = text,
        Foreground = SubtleText,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 14),
    };

    // ================= Général =================

    private UIElement BuildGeneralTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("Général"));

        panel.Children.Add(new TextBlock { Text = "Profil de touches", Foreground = TextColor, Margin = new Thickness(0, 0, 0, 4) });
        var profileRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var profileCombo = new ComboBox { Width = 160, ItemsSource = KeyBindConfig.ListProfiles(), SelectedItem = AppState.Settings.ActiveProfile, Margin = new Thickness(0, 0, 6, 0) };
        profileCombo.SelectionChanged += (_, _) =>
        {
            if (profileCombo.SelectedItem is string name && name != AppState.Settings.ActiveProfile) AppState.SwitchProfile(name);
        };
        profileRow.Children.Add(profileCombo);

        var newProfileBtn = new Button { Content = "Nouveau (copie)", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0) };
        newProfileBtn.Click += (_, _) =>
        {
            var name = PromptForText("Nom du nouveau profil (copie du profil actif) :", "Nouveau profil");
            if (string.IsNullOrWhiteSpace(name)) return;
            AppState.DuplicateProfileAs(name.Trim());
            profileCombo.ItemsSource = KeyBindConfig.ListProfiles();
            profileCombo.SelectedItem = AppState.Settings.ActiveProfile;
        };
        profileRow.Children.Add(newProfileBtn);

        var deleteProfileBtn = new Button { Content = "Supprimer le profil actif", Padding = new Thickness(8, 4, 8, 4) };
        deleteProfileBtn.Click += (_, _) =>
        {
            if (AppState.Settings.ActiveProfile == KeyBindConfig.DefaultProfileName)
            {
                MessageBox.Show("Le profil « Défaut » ne peut pas être supprimé.", "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"Supprimer le profil « {AppState.Settings.ActiveProfile} » ?", "Confirmer", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            AppState.DeleteProfile(AppState.Settings.ActiveProfile);
            profileCombo.ItemsSource = KeyBindConfig.ListProfiles();
            profileCombo.SelectedItem = AppState.Settings.ActiveProfile;
        };
        profileRow.Children.Add(deleteProfileBtn);
        panel.Children.Add(profileRow);
        panel.Children.Add(HelpText("Utile pour un mapping AZERTY/QWERTY différent selon le PC, ou un jeu de touches distinct solo/équipe. Change les touches de l'onglet « Touches » ; les combos et l'apparence restent partagés entre profils."));

        var lockBtn = new Button { Content = LockLabel(), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        lockBtn.Click += (_, _) =>
        {
            AppState.ToggleLock();
            lockBtn.Content = LockLabel();
        };
        panel.Children.Add(lockBtn);
        panel.Children.Add(HelpText("Raccourci rapide en jeu : Ctrl+Alt+O."));

        var resetPosBtn = new Button { Content = "Réinitialiser la position (bas-gauche)", Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        resetPosBtn.Click += (_, _) =>
        {
            AppState.Settings.Position = OverlayPosition.BottomLeft;
            AppState.SaveSettings();
        };
        panel.Children.Add(resetPosBtn);
        panel.Children.Add(HelpText("Utile après avoir glissé l'overlay ailleurs à l'écran."));

        var suspendBtn = new Button { Content = SuspendLabel(), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        suspendBtn.Click += (_, _) => AppState.ToggleCaptureSuspended();
        void SuspendHandler(bool _) => Dispatcher.Invoke(() => suspendBtn.Content = SuspendLabel());
        AppState.CaptureSuspendedChanged += SuspendHandler;
        panel.Children.Add(suspendBtn);
        panel.Children.Add(HelpText("Le suivi clavier/manette capte les touches partout sur le PC, même hors jeu (nécessaire pour fonctionner par-dessus Brawlhalla). Suspends la capture quand tu utilises juste ton PC normalement, sans quoi l'historique et la combo en cours du mode Tutoriel réagissent à ce que tu tapes ailleurs. Raccourci rapide : Ctrl+Alt+H."));

        var startupCheck = new CheckBox
        {
            Content = "Lancer automatiquement au démarrage de Windows",
            Foreground = TextColor,
            IsChecked = StartupConfig.IsEnabled(),
            Margin = new Thickness(0, 4, 0, 4),
        };
        startupCheck.Checked += (_, _) => { StartupConfig.SetEnabled(true); AppState.Settings.LaunchAtStartup = true; AppState.SaveSettings(); };
        startupCheck.Unchecked += (_, _) => { StartupConfig.SetEnabled(false); AppState.Settings.LaunchAtStartup = false; AppState.SaveSettings(); };
        panel.Children.Add(startupCheck);

        panel.Children.Add(new TextBlock { Text = "Mode actif au démarrage", Foreground = TextColor, Margin = new Thickness(0, 16, 0, 4) });
        var modeCombo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = new[] { "Historique", "Grandes flèches", "Tutoriel" }, SelectedIndex = AppState.Settings.DefaultMode };
        modeCombo.SelectionChanged += (_, _) =>
        {
            AppState.Settings.DefaultMode = modeCombo.SelectedIndex;
            AppState.SaveSettings();
        };
        panel.Children.Add(modeCombo);

        panel.Children.Add(new TextBlock { Text = "Modes inclus dans le cycle rapide (Ctrl+Alt+P)", Foreground = TextColor, Margin = new Thickness(0, 16, 0, 4) });
        var favPanel = new StackPanel { Orientation = Orientation.Horizontal };
        string[] modeNames = { "Historique", "Grandes flèches", "Tutoriel" };
        for (int i = 0; i < modeNames.Length; i++)
        {
            int mode = i;
            var cb = new CheckBox
            {
                Content = modeNames[i],
                Foreground = TextColor,
                IsChecked = AppState.Settings.FavoriteModes.Contains(mode),
                Margin = new Thickness(0, 0, 16, 0),
            };
            cb.Checked += (_, _) =>
            {
                if (!AppState.Settings.FavoriteModes.Contains(mode)) AppState.Settings.FavoriteModes.Add(mode);
                AppState.SaveSettings();
            };
            cb.Unchecked += (_, _) =>
            {
                if (AppState.Settings.FavoriteModes.Count <= 1) { cb.IsChecked = true; return; } // au moins un mode actif
                AppState.Settings.FavoriteModes.Remove(mode);
                AppState.SaveSettings();
            };
            favPanel.Children.Add(cb);
        }
        panel.Children.Add(favPanel);

        var keepStreakCheck = new CheckBox
        {
            Content = "Garder la série de réussites même après une combo ratée",
            Foreground = TextColor,
            IsChecked = AppState.Settings.KeepStreakOnFail,
            Margin = new Thickness(0, 16, 0, 4),
        };
        keepStreakCheck.Checked += (_, _) => { AppState.Settings.KeepStreakOnFail = true; AppState.SaveSettings(); };
        keepStreakCheck.Unchecked += (_, _) => { AppState.Settings.KeepStreakOnFail = false; AppState.SaveSettings(); };
        panel.Children.Add(keepStreakCheck);

        var soundCheck = new CheckBox
        {
            Content = "Bips de succès/échec en mode Tutoriel",
            Foreground = TextColor,
            IsChecked = AppState.Settings.SoundEnabled,
            Margin = new Thickness(0, 4, 0, 4),
        };
        soundCheck.Checked += (_, _) => { AppState.Settings.SoundEnabled = true; AppState.SaveSettings(); };
        soundCheck.Unchecked += (_, _) => { AppState.Settings.SoundEnabled = false; AppState.SaveSettings(); };
        panel.Children.Add(soundCheck);

        var chainCheck = new CheckBox
        {
            Content = "Enchaîner automatiquement vers la combo suivante après réussite",
            Foreground = TextColor,
            IsChecked = AppState.Settings.ChainCombos,
            Margin = new Thickness(0, 4, 0, 4),
        };
        chainCheck.Checked += (_, _) => { AppState.Settings.ChainCombos = true; AppState.SaveSettings(); };
        chainCheck.Unchecked += (_, _) => { AppState.Settings.ChainCombos = false; AppState.SaveSettings(); };
        panel.Children.Add(chainCheck);

        var thresholdRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 4) };
        thresholdRow.Children.Add(Label("Réussites consécutives requises avant de passer à la suivante"));
        var thresholdBox = new TextBox { Text = AppState.Settings.ChainStreakThreshold.ToString(), Width = 40 };
        thresholdBox.LostFocus += (_, _) =>
        {
            if (int.TryParse(thresholdBox.Text, out var value) && value >= 1)
            {
                AppState.Settings.ChainStreakThreshold = value;
                AppState.SaveSettings();
            }
            else
            {
                thresholdBox.Text = AppState.Settings.ChainStreakThreshold.ToString();
            }
        };
        thresholdRow.Children.Add(thresholdBox);
        panel.Children.Add(thresholdRow);
        panel.Children.Add(HelpText("Session guidée : une combo n'est marquée « maîtrisée » et n'enchaîne vers la suivante qu'après ce nombre de réussites d'affilée (par défaut 1 = enchaîne dès la 1ère réussite)."));

        var quizCheck = new CheckBox
        {
            Content = "Mode révision (masque les étapes pas encore jouées de la combo)",
            Foreground = TextColor,
            IsChecked = AppState.Settings.QuizMode,
            Margin = new Thickness(0, 4, 0, 4),
        };
        quizCheck.Checked += (_, _) => { AppState.Settings.QuizMode = true; AppState.SaveSettings(); };
        quizCheck.Unchecked += (_, _) => { AppState.Settings.QuizMode = false; AppState.SaveSettings(); };
        panel.Children.Add(quizCheck);

        var revealRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 4) };
        var revealBtn = new Button { Content = "Révéler la combo (3s)", Padding = new Thickness(10, 4, 10, 4) };
        revealBtn.Click += (_, _) => AppState.RequestQuizReveal();
        revealRow.Children.Add(revealBtn);
        panel.Children.Add(revealRow);
        panel.Children.Add(HelpText("Force à se souvenir de la combo plutôt que de la lire. Ctrl+Alt+I (en jeu) ou ce bouton révèlent temporairement (3s) les étapes masquées."));

        var gamepadStatus = new TextBlock { Foreground = SubtleText, Margin = new Thickness(0, 8, 0, 4) };
        gamepadStatus.Text = AppState.Gamepad.Connected ? "Manette détectée." : "Aucune manette détectée (facultatif — voir l'onglet Touches pour assigner des boutons).";
        panel.Children.Add(gamepadStatus);

        panel.Children.Add(SectionTitle("Session"));
        var sessionInfo = new TextBlock { Foreground = SubtleText, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        UpdateSessionInfo(sessionInfo);
        panel.Children.Add(sessionInfo);

        var sessionRow = new StackPanel { Orientation = Orientation.Horizontal };
        var refreshBtn = new Button { Content = "Actualiser", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        refreshBtn.Click += (_, _) => UpdateSessionInfo(sessionInfo);
        sessionRow.Children.Add(refreshBtn);

        var exportSessionBtn = new Button { Content = "Exporter la session (CSV)", Padding = new Thickness(10, 4, 10, 4) };
        exportSessionBtn.Click += (_, _) => ExportSessionCsv();
        sessionRow.Children.Add(exportSessionBtn);
        panel.Children.Add(sessionRow);

        _unsubscribeCurrentTab = () => AppState.CaptureSuspendedChanged -= SuspendHandler;

        return Wrap(panel);
    }

    private static void UpdateSessionInfo(TextBlock target)
    {
        var elapsed = DateTime.UtcNow - AppState.SessionStart;
        var minutes = Math.Max(elapsed.TotalMinutes, 1.0 / 60);
        var count = AppState.SessionLog.Count;
        var perMinute = count / minutes;
        target.Text = $"{count} coup(s) joué(s) depuis {FormatElapsed(elapsed)} · {perMinute:0.0} actions/min.";
    }

    private static string FormatElapsed(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}h{span.Minutes:00}" : $"{span.Minutes}m{span.Seconds:00}";

    private static void ExportSessionCsv()
    {
        if (AppState.SessionLog.Count == 0)
        {
            MessageBox.Show("Aucun coup joué dans cette session pour l'instant.", "Session vide", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"session_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };
        if (dialog.ShowDialog() != true) return;

        var lines = new List<string> { "Horodatage,Coups" };
        foreach (var (time, actions) in AppState.SessionLog)
        {
            lines.Add($"{time:O},\"{actions.Replace("\"", "'")}\"");
        }
        File.WriteAllLines(dialog.FileName, lines);
        MessageBox.Show($"Session exportée : {AppState.SessionLog.Count} coup(s).", "Export terminé", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string LockLabel() => AppState.Locked ? "Déverrouiller l'overlay" : "Verrouiller l'overlay";
    private static string SuspendLabel() => AppState.CaptureSuspended ? "Reprendre la capture" : "Suspendre la capture (hors du jeu)";

    // ================= Touches =================

    private UIElement BuildKeybindsTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("Touches"));
        panel.Children.Add(HelpText("Modifie le nom, les touches (séparées par des « + »), le symbole et la couleur (hex) de chaque action. Clique « Écouter » puis appuie sur une touche pour la réassigner en un clic. Colonne 🎮 : boutons manette facultatifs (en plus des touches, pas à la place) — clique 🎮 puis appuie sur un bouton de la manette pour l'assigner."));

        var rows = new List<(KeyBind Bind, TextBox Label, TextBox Keys, TextBox Gamepad, TextBox Symbol, TextBox Color)>();

        var grid = new Grid();
        foreach (var bind in AppState.Binds)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            var actionText = new TextBlock { Text = bind.Action, Foreground = TextColor, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(actionText, 0);
            row.Children.Add(actionText);

            var labelBox = new TextBox { Text = bind.Label, Margin = new Thickness(2) };
            Grid.SetColumn(labelBox, 1);
            row.Children.Add(labelBox);

            var keysBox = new TextBox { Text = string.Join(" + ", bind.Keys), Margin = new Thickness(2) };
            Grid.SetColumn(keysBox, 2);
            row.Children.Add(keysBox);

            var listenBtn = new Button { Content = "Écouter", Margin = new Thickness(2) };
            Grid.SetColumn(listenBtn, 3);
            listenBtn.Click += (_, _) =>
            {
                listenBtn.Content = "…";
                ListenForNextKey(vk =>
                {
                    var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk);
                    keysBox.Text = key.ToString();
                    listenBtn.Content = "Écouter";
                });
            };
            row.Children.Add(listenBtn);

            var gamepadBox = new TextBox { Text = string.Join(" + ", bind.GamepadButtons), Margin = new Thickness(2), ToolTip = "Boutons manette (facultatif), ex: A, DPadUp" };
            Grid.SetColumn(gamepadBox, 4);
            row.Children.Add(gamepadBox);

            var listenGamepadBtn = new Button { Content = "🎮", Margin = new Thickness(2), ToolTip = "Écouter le prochain bouton manette pressé" };
            Grid.SetColumn(listenGamepadBtn, 5);
            listenGamepadBtn.Click += (_, _) =>
            {
                listenGamepadBtn.Content = "…";
                ListenForNextGamepadButton(name =>
                {
                    gamepadBox.Text = name;
                    listenGamepadBtn.Content = "🎮";
                });
            };
            row.Children.Add(listenGamepadBtn);

            var symbolBox = new TextBox { Text = bind.Symbol, Margin = new Thickness(2) };
            Grid.SetColumn(symbolBox, 6);
            row.Children.Add(symbolBox);

            var colorBox = new TextBox { Text = bind.Color, Margin = new Thickness(2) };
            Grid.SetColumn(colorBox, 7);
            row.Children.Add(colorBox);

            rows.Add((bind, labelBox, keysBox, gamepadBox, symbolBox, colorBox));

            var wrapper = new StackPanel();
            wrapper.Children.Add(row);
            panel.Children.Add(wrapper);
        }

        var saveBtn = new Button { Content = "Enregistrer les touches", Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
        saveBtn.Click += (_, _) =>
        {
            // Valide tout avant de rien muter : sinon une seule touche invalide ou
            // en double laisse AppState.Binds à moitié modifié en mémoire alors que
            // rien n'est sauvegardé sur disque (état overlay/panneau désynchronisé).
            var parsedKeys = new List<List<string>>();
            var parsedGamepad = new List<List<string>>();
            var seenKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seenGamepadButtons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (bind, _, keysBox, gamepadBox, _, _) in rows)
            {
                var keys = keysBox.Text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

                if (keys.Count == 0)
                {
                    MessageBox.Show($"« {bind.Action} » n'a aucune touche assignée.", "Touches invalides", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                foreach (var keyName in keys)
                {
                    if (!KeyBindConfig.TryResolveVirtualKeyCode(keyName, out _))
                    {
                        MessageBox.Show($"« {keyName} » (action « {bind.Action} ») n'est pas un nom de touche valide.", "Touches invalides", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (seenKeys.TryGetValue(keyName, out var otherAction) && otherAction != bind.Action)
                    {
                        MessageBox.Show($"La touche « {keyName} » est déjà utilisée par « {otherAction} ». Une même touche ne peut déclencher qu'une seule action.", "Touche en double", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    seenKeys[keyName] = bind.Action;
                }
                parsedKeys.Add(keys);

                var gamepadButtons = gamepadBox.Text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                foreach (var buttonName in gamepadButtons)
                {
                    if (!GamepadHook.TryResolveSyntheticCode(buttonName, out _))
                    {
                        var validNames = string.Join(", ", GamepadHook.Buttons.Select(b => b.Name));
                        MessageBox.Show($"« {buttonName} » (action « {bind.Action} ») n'est pas un bouton manette valide.\nBoutons valides : {validNames}", "Bouton manette invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (seenGamepadButtons.TryGetValue(buttonName, out var otherAction) && otherAction != bind.Action)
                    {
                        MessageBox.Show($"Le bouton « {buttonName} » est déjà utilisé par « {otherAction} ».", "Bouton manette en double", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    seenGamepadButtons[buttonName] = bind.Action;
                }
                parsedGamepad.Add(gamepadButtons);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var (bind, labelBox, _, _, symbolBox, colorBox) = rows[i];
                bind.Label = labelBox.Text.Trim();
                bind.Keys = parsedKeys[i];
                bind.GamepadButtons = parsedGamepad[i];
                bind.Symbol = symbolBox.Text.Trim();
                bind.Color = colorBox.Text.Trim();
            }
            AppState.NotifyBindsMutated();
            saveBtn.Content = "Enregistré ✓";
        };
        panel.Children.Add(saveBtn);

        return Wrap(panel);
    }

    private void ListenForNextKey(Action<int> onCaptured)
    {
        void Handler(int vk)
        {
            AppState.Hook.KeyDown -= Handler;
            Dispatcher.Invoke(() => onCaptured(vk));
        }
        AppState.Hook.KeyDown += Handler;
    }

    private void ListenForNextGamepadButton(Action<string> onCaptured)
    {
        void Handler(int syntheticCode)
        {
            AppState.Gamepad.ButtonDown -= Handler;
            var name = GamepadHook.Buttons.FirstOrDefault(b => GamepadHook.SyntheticCodeBase + b.Flag == syntheticCode).Name;
            if (name is not null) Dispatcher.Invoke(() => onCaptured(name));
        }
        AppState.Gamepad.ButtonDown += Handler;
    }

    // ================= Combos =================

    private ListBox _combosList = null!;
    private ComboBox _weaponFilterCombo = null!;
    private ComboBox _legendFilterCombo = null!;
    private List<int> _visibleComboIndices = new();

    private UIElement BuildCombosTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("Combos"));

        // Personnage d'abord : choisir un personnage montre directement ses combos Signature +
        // les combos génériques des armes qu'il utilise réellement (voir AppState.
        // FilteredComboIndices), au lieu de croiser arme et légend comme deux filtres indépendants
        // sans lien entre eux (un légend n'a que 2 armes précises, pas 15 au hasard).
        var characterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        characterRow.Children.Add(new TextBlock { Text = "Personnage : ", Foreground = TextColor, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

        var characterItems = new List<string> { "Tous les personnages" };
        characterItems.AddRange(LegendComboPresets.Legends);
        _legendFilterCombo = new ComboBox { Width = 220, ItemsSource = characterItems, Margin = new Thickness(0, 0, 6, 0) };
        _legendFilterCombo.SelectedItem = string.IsNullOrEmpty(AppState.Settings.TrainingLegendFilter)
            ? "Tous les personnages"
            : AppState.Settings.TrainingLegendFilter;
        characterRow.Children.Add(_legendFilterCombo);
        panel.Children.Add(characterRow);
        panel.Children.Add(HelpText("Restreint la liste ci-dessous, le sous-filtre d'arme et le cycle Ctrl+Alt+K à ce personnage. « Tous les personnages » repasse en filtre par arme seule, sans notion de perso."));

        var weaponRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        weaponRow.Children.Add(new TextBlock { Text = "Arme : ", Foreground = TextColor, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _weaponFilterCombo = new ComboBox { Width = 220, Margin = new Thickness(0, 0, 6, 0) };
        weaponRow.Children.Add(_weaponFilterCombo);

        var importPresetsBtn = new Button { Padding = new Thickness(10, 4, 10, 4) };
        weaponRow.Children.Add(importPresetsBtn);
        panel.Children.Add(weaponRow);
        var weaponHelpText = HelpText("");
        panel.Children.Add(weaponHelpText);

        // Le contenu du sous-filtre d'arme et du bouton d'import dépend du personnage choisi : sans
        // personnage, c'est un filtre pur sur les 15 armes du jeu (comportement d'origine). Avec un
        // personnage, la liste ne propose que ses armes réelles (LegendComboPresets.WeaponsFor) —
        // impossible de se retrouver sur une combinaison perso/arme qui n'existe pas dans le jeu.
        void RefreshWeaponFilterOptions()
        {
            var character = _legendFilterCombo.SelectedItem as string;
            var noCharacter = string.IsNullOrEmpty(character) || character == "Tous les personnages";

            if (noCharacter)
            {
                var weaponItems = new List<string> { "Toutes les armes" };
                weaponItems.AddRange(WeaponComboPresets.Weapons);
                _weaponFilterCombo.ItemsSource = weaponItems;
                _weaponFilterCombo.SelectedItem = string.IsNullOrEmpty(AppState.Settings.TrainingWeaponFilter)
                    ? "Toutes les armes"
                    : AppState.Settings.TrainingWeaponFilter;
                importPresetsBtn.Content = "Importer les 5 combos de cette arme";
                importPresetsBtn.IsEnabled = _weaponFilterCombo.SelectedItem as string != "Toutes les armes";
                weaponHelpText.Text = "Filtre la liste ci-dessous et les combos cyclées par Ctrl+Alt+K sur l'arme choisie. « Importer » ajoute les true combos vérifiés pour cette arme si elles n'y sont pas déjà.";
            }
            else
            {
                var weapons = LegendComboPresets.WeaponsFor(character!);
                var allLabel = $"Toutes les armes de {character}";
                var weaponItems = new List<string> { allLabel };
                weaponItems.AddRange(weapons);
                _weaponFilterCombo.ItemsSource = weaponItems;
                _weaponFilterCombo.SelectedItem = weapons.Contains(AppState.Settings.TrainingWeaponFilter)
                    ? AppState.Settings.TrainingWeaponFilter
                    : allLabel;
                importPresetsBtn.Content = $"Importer les combos de {character}";
                importPresetsBtn.IsEnabled = true;
                weaponHelpText.Text = $"Combos Signature de {character} + combos génériques de ses {weapons.Count} arme(s). « Importer » ajoute les deux d'un coup.";
            }
        }

        _legendFilterCombo.SelectionChanged += (_, _) =>
        {
            var selected = _legendFilterCombo.SelectedItem as string ?? "Tous les personnages";
            AppState.SetTrainingLegendFilter(selected == "Tous les personnages" ? "" : selected);
            // Le filtre d'arme précédent peut ne plus avoir de sens pour ce personnage (ex. "Marteau"
            // choisi puis bascule sur "Ada", qui ne joue pas Marteau) — repart sur "toutes ses armes".
            AppState.SetTrainingWeaponFilter("");
            RefreshWeaponFilterOptions();
            RefreshCombosList();
        };

        _weaponFilterCombo.SelectionChanged += (_, _) =>
        {
            var selected = _weaponFilterCombo.SelectedItem as string ?? "";
            var isAllOption = selected == "Toutes les armes" || selected.StartsWith("Toutes les armes de ");
            AppState.SetTrainingWeaponFilter(isAllOption ? "" : selected);
            RefreshCombosList();
        };

        importPresetsBtn.Click += (_, _) =>
        {
            var character = _legendFilterCombo.SelectedItem as string;
            if (!string.IsNullOrEmpty(character) && character != "Tous les personnages")
            {
                AppState.ImportCharacterPresets(character);
                RefreshCombosList();
                return;
            }

            var weapon = _weaponFilterCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(weapon) || weapon == "Toutes les armes") return;
            AppState.ImportWeaponPresets(weapon);
            RefreshCombosList();
        };

        // Créée avant le premier RefreshWeaponFilterOptions() : celui-ci fixe SelectedItem sur
        // _weaponFilterCombo, ce qui déclenche son SelectionChanged synchroniquement et donc
        // RefreshCombosList() — qui a besoin que _combosList existe déjà.
        _combosList = new ListBox { Height = 200, Background = CardBg, Foreground = TextColor, BorderThickness = new Thickness(0) };

        RefreshWeaponFilterOptions();
        RefreshCombosList();
        panel.Children.Add(_combosList);

        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };

        var recordBtn = new Button { Content = RecordLabel(), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        recordBtn.Click += (_, _) => AppState.SetRecording(!AppState.Recording);
        buttonsRow.Children.Add(recordBtn);

        var manualBtn = new Button { Content = "Créer manuellement", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        manualBtn.Click += (_, _) => OpenComboEditor(null);
        buttonsRow.Children.Add(manualBtn);

        var editBtn = new Button { Content = "Modifier", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        editBtn.Click += (_, _) =>
        {
            var idx = SelectedAbsoluteIndex();
            if (idx >= 0) OpenComboEditor(AppState.Combos[idx]);
        };
        buttonsRow.Children.Add(editBtn);

        var dupBtn = new Button { Content = "Dupliquer", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        dupBtn.Click += (_, _) =>
        {
            var idx = SelectedAbsoluteIndex();
            if (idx < 0) return;
            var original = AppState.Combos[idx];
            var json = JsonSerializer.Serialize(original);
            var copy = JsonSerializer.Deserialize<Combo>(json)!;
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = $"{original.Name} (copie)";
            AppState.Combos.Add(copy);
            AppState.NotifyCombosMutated();
            RefreshCombosList();
        };
        buttonsRow.Children.Add(dupBtn);

        var upBtn = new Button { Content = "▲", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 2, 0) };
        upBtn.Click += (_, _) => MoveSelectedCombo(-1);
        buttonsRow.Children.Add(upBtn);

        var downBtn = new Button { Content = "▼", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0) };
        downBtn.Click += (_, _) => MoveSelectedCombo(1);
        buttonsRow.Children.Add(downBtn);

        var deleteBtn = new Button { Content = "Supprimer", Padding = new Thickness(10, 4, 10, 4) };
        deleteBtn.Click += (_, _) =>
        {
            var idx = SelectedAbsoluteIndex();
            if (idx < 0) return;
            AppState.Combos.RemoveAt(idx);
            AppState.NotifyCombosMutated();
            RefreshCombosList();
        };
        buttonsRow.Children.Add(deleteBtn);

        panel.Children.Add(buttonsRow);
        panel.Children.Add(HelpText("« Enregistrer » capture tes prochains appuis en direct (même raccourci que Ctrl+Alt+R) ; reclique pour arrêter et sauvegarder. « Créer manuellement » ouvre un éditeur d'étapes."));

        var ioRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };

        var exportBtn = new Button { Content = "Exporter la combo sélectionnée", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        exportBtn.Click += (_, _) => ExportSelectedCombo();
        ioRow.Children.Add(exportBtn);

        var importBtn = new Button { Content = "Importer une combo", Padding = new Thickness(10, 4, 10, 4) };
        importBtn.Click += (_, _) => ImportCombo();
        ioRow.Children.Add(importBtn);

        panel.Children.Add(ioRow);
        panel.Children.Add(HelpText("Permet de partager une combo (fichier .json) entre deux installations ou avec quelqu'un d'autre."));

        var statsBtn = new Button { Content = "Voir les stats de précision par action", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        statsBtn.Click += (_, _) => ShowActionStats();
        panel.Children.Add(statsBtn);
        panel.Children.Add(HelpText("Cumulé sur toutes les combos jouées en mode Tutoriel, persiste entre les sessions."));

        void RecordingHandler(bool _) => Dispatcher.Invoke(() => recordBtn.Content = RecordLabel());
        AppState.RecordingChanged += RecordingHandler;
        AppState.CombosChanged += RefreshCombosList;

        _unsubscribeCurrentTab = () =>
        {
            AppState.RecordingChanged -= RecordingHandler;
            AppState.CombosChanged -= RefreshCombosList;
        };

        return Wrap(panel);
    }

    private void ExportSelectedCombo()
    {
        var idx = SelectedAbsoluteIndex();
        if (idx < 0)
        {
            MessageBox.Show("Sélectionne d'abord une combo dans la liste.", "Aucune combo sélectionnée", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var combo = AppState.Combos[idx];
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Combo JSON (*.json)|*.json",
            FileName = $"{combo.Name}.json",
        };
        if (dialog.ShowDialog() != true) return;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(combo, options));
    }

    private void ImportCombo()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Combo JSON (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;

        Combo? imported;
        try
        {
            imported = JsonSerializer.Deserialize<Combo>(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fichier invalide : {ex.Message}", "Import impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (imported is null || imported.Steps.Count == 0)
        {
            MessageBox.Show("Ce fichier ne contient pas de combo valide.", "Import impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var knownActions = new HashSet<string>(AppState.Binds.Select(b => b.Action));
        var unknownActions = imported.Steps.SelectMany(s => s.RequiredActions).Where(a => !knownActions.Contains(a)).Distinct().ToList();
        if (unknownActions.Count > 0)
        {
            MessageBox.Show(
                $"Cette combo référence des actions qui n'existent pas dans ta configuration de touches : {string.Join(", ", unknownActions)}.\n" +
                "Importée quand même, mais elle ne pourra pas être validée en jeu tant que ces actions n'existent pas (onglet Touches).",
                "Actions inconnues", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        imported.Id = Guid.NewGuid().ToString("N");
        AppState.Combos.Add(imported);
        AppState.NotifyCombosMutated();
        RefreshCombosList();
    }

    private static void ShowActionStats()
    {
        var stats = AppState.Stats.OrderByDescending(s => s.Failures == 0 ? 0 : (double)s.Failures / (s.Successes + s.Failures)).ToList();
        if (stats.Count == 0)
        {
            MessageBox.Show("Pas encore de données — joue une combo en mode Tutoriel pour commencer à accumuler des stats.", "Stats de précision", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lines = stats.Select(s =>
        {
            var total = s.Successes + s.Failures;
            var rate = total == 0 ? 0 : 100.0 * s.Successes / total;
            return $"{s.Action,-16} {s.Successes,4} réussite(s) / {s.Failures,4} échec(s)  ({rate:0}% de réussite)";
        });
        MessageBox.Show(string.Join("\n", lines), "Stats de précision par action", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string RecordLabel() => AppState.Recording ? "■ Arrêter l'enregistrement" : "● Enregistrer une combo";

    /// <summary>Résout la sélection de la ListBox (indexée sur la liste filtrée par arme) en
    /// index absolu dans AppState.Combos.</summary>
    private int SelectedAbsoluteIndex() =>
        _combosList.SelectedIndex >= 0 && _combosList.SelectedIndex < _visibleComboIndices.Count
            ? _visibleComboIndices[_combosList.SelectedIndex]
            : -1;

    private void RefreshCombosList()
    {
        var previouslySelectedAbsolute = SelectedAbsoluteIndex();

        _combosList.Items.Clear();
        _visibleComboIndices.Clear();

        var filter = AppState.Settings.TrainingWeaponFilter;
        var legendFilter = AppState.Settings.TrainingLegendFilter;
        // Réutilise le même calcul que AppState.CycleCombo/ActiveComboFilteredPosition au lieu de
        // ré-implémenter la logique de filtre ici — sinon les deux finissent par diverger (c'est
        // exactement ce qui s'était passé avec l'ancien ET arme/légend indépendant).
        var filteredAbsIndices = AppState.FilteredComboIndices();
        foreach (var absIndex in ComboFamilies.OrderWithFamilies(filteredAbsIndices, i => AppState.Combos[i]))
        {
            var combo = AppState.Combos[absIndex.Index];
            _visibleComboIndices.Add(absIndex.Index);
            var legendTag = string.IsNullOrEmpty(combo.Legend) ? "" : $"{combo.Legend} ";
            var weaponTag = string.IsNullOrEmpty(combo.Weapon) ? "" : $"[{legendTag}{combo.Weapon}] ";
            var masteredMark = combo.Mastered ? " ✓" : "";
            var perf = combo.TotalAttempts > 0
                ? $" — meilleure série {combo.BestStreak}, {combo.TotalCompletions}/{combo.TotalAttempts} réussie(s)"
                : "";
            var damageNote = string.IsNullOrEmpty(combo.DamageNote) ? "" : $"  ⚠ {combo.DamageNote}";
            var indent = absIndex.Level > 1 ? new string(' ', (absIndex.Level - 1) * 3) + "↳ " : "";
            var levelTag = absIndex.Level > 0 && absIndex.FamilySize > 1 ? $" [Niveau {absIndex.Level}]" : "";
            var followUpHint = combo.Mastered && absIndex.HasFollowUp ? "  → niveau supérieur disponible ci-dessous" : "";
            _combosList.Items.Add($"{indent}{weaponTag}{combo.Name}{masteredMark}{levelTag}  ({combo.Steps.Count} étapes){perf}{damageNote}{followUpHint}");
        }

        if (_combosList.Items.Count == 0)
        {
            // Sans ce message, une liste vide (cas du tout premier lancement, ou
            // d'un filtre d'arme/légend sans combo importée) ressemble à un bug
            // plutôt qu'à un état normal — rien n'indiquait quoi faire ensuite.
            string hint;
            if (string.IsNullOrEmpty(filter) && string.IsNullOrEmpty(legendFilter))
                hint = "Aucune combo pour l'instant. Choisis une arme ci-dessus puis « Importer les 5 combos de cette arme », ou clique « Créer manuellement » / « Enregistrer une combo » plus bas.";
            else if (!string.IsNullOrEmpty(legendFilter))
                hint = $"Aucune combo pour « {legendFilter} »{(string.IsNullOrEmpty(filter) ? "" : $" sur {filter}")}. Clique « Importer les combos de {legendFilter} » ci-dessus (certains personnages n'ont pas de combo listé), ou choisis « Tous les personnages ».";
            else
                hint = $"Aucune combo pour « {filter} ». Clique « Importer les 5 combos de cette arme » ci-dessus, ou choisis « Toutes les armes » pour voir les autres combos.";
            _combosList.Items.Add(new ListBoxItem
            {
                Content = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Foreground = SubtleText },
                IsEnabled = false,
                Padding = new Thickness(6),
            });
        }

        var restored = _visibleComboIndices.IndexOf(previouslySelectedAbsolute);
        if (restored >= 0) _combosList.SelectedIndex = restored;
        else if (_combosList.Items.Count > 0) _combosList.SelectedIndex = 0;
    }

    private void MoveSelectedCombo(int direction)
    {
        var visibleIndex = _combosList.SelectedIndex;
        var visibleTarget = visibleIndex + direction;
        if (visibleIndex < 0 || visibleTarget < 0 || visibleTarget >= _visibleComboIndices.Count) return;

        var absIndex = _visibleComboIndices[visibleIndex];
        var absTarget = _visibleComboIndices[visibleTarget];
        (AppState.Combos[absIndex], AppState.Combos[absTarget]) = (AppState.Combos[absTarget], AppState.Combos[absIndex]);
        AppState.NotifyCombosMutated();
        RefreshCombosList();
        _combosList.SelectedIndex = visibleTarget;
    }

    private void OpenComboEditor(Combo? existing)
    {
        var editor = new ComboEditorWindow(existing) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is not null)
        {
            if (existing is null)
            {
                AppState.Combos.Add(editor.Result);
            }
            else
            {
                var idx = AppState.Combos.IndexOf(existing);
                if (idx >= 0) AppState.Combos[idx] = editor.Result;
            }
            AppState.NotifyCombosMutated();
            RefreshCombosList();
        }
    }

    // ================= Apparence =================

    private UIElement BuildAppearanceTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("Apparence"));

        panel.Children.Add(Label("Échelle"));
        var scaleSlider = new Slider { Minimum = 0.5, Maximum = 2.0, Value = AppState.Settings.Scale, Width = 300, HorizontalAlignment = HorizontalAlignment.Left, TickFrequency = 0.1 };
        var scaleValue = new TextBlock { Foreground = TextColor, Margin = new Thickness(10, 0, 0, 0), Text = $"{AppState.Settings.Scale:0.00}x" };
        var scaleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 14) };
        scaleRow.Children.Add(scaleSlider);
        scaleRow.Children.Add(scaleValue);
        scaleSlider.ValueChanged += (_, e) =>
        {
            AppState.Settings.Scale = e.NewValue;
            scaleValue.Text = $"{e.NewValue:0.00}x";
            AppState.SaveSettings();
        };
        panel.Children.Add(scaleRow);

        panel.Children.Add(Label("Opacité"));
        var opacitySlider = new Slider { Minimum = 0.2, Maximum = 1.0, Value = AppState.Settings.Opacity, Width = 300, HorizontalAlignment = HorizontalAlignment.Left, TickFrequency = 0.05 };
        var opacityValue = new TextBlock { Foreground = TextColor, Margin = new Thickness(10, 0, 0, 0), Text = $"{AppState.Settings.Opacity * 100:0}%" };
        var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 14) };
        opacityRow.Children.Add(opacitySlider);
        opacityRow.Children.Add(opacityValue);
        opacitySlider.ValueChanged += (_, e) =>
        {
            AppState.Settings.Opacity = e.NewValue;
            opacityValue.Text = $"{e.NewValue * 100:0}%";
            AppState.SaveSettings();
        };
        panel.Children.Add(opacityRow);

        panel.Children.Add(Label("Position"));
        var positionCombo = new ComboBox
        {
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = new[] { "Bas gauche", "Bas droite", "Haut gauche", "Haut droite", "Libre (glisser en jeu)" },
            SelectedIndex = (int)AppState.Settings.Position,
            Margin = new Thickness(0, 4, 0, 14),
        };
        positionCombo.SelectionChanged += (_, _) =>
        {
            AppState.Settings.Position = (OverlayPosition)positionCombo.SelectedIndex;
            AppState.SaveSettings();
        };
        panel.Children.Add(positionCombo);

        panel.Children.Add(HelpText("« Libre » est aussi choisi automatiquement dès que tu glisses le panneau à la souris en jeu (overlay déverrouillé)."));

        panel.Children.Add(Label("Écran"));
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screenItems = new List<string> { "Écran principal" };
        for (var i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            screenItems.Add($"Écran {i + 1} ({s.Bounds.Width}x{s.Bounds.Height}){(s.Primary ? " — principal" : "")}");
        }
        var screenCombo = new ComboBox
        {
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = screenItems,
            SelectedIndex = AppState.Settings.MonitorIndex < 0 || AppState.Settings.MonitorIndex >= screens.Length ? 0 : AppState.Settings.MonitorIndex + 1,
            Margin = new Thickness(0, 4, 0, 14),
        };
        screenCombo.SelectionChanged += (_, _) =>
        {
            AppState.Settings.MonitorIndex = screenCombo.SelectedIndex - 1;
            AppState.SaveSettings();
        };
        panel.Children.Add(screenCombo);
        panel.Children.Add(HelpText("Utile si Brawlhalla tourne sur un moniteur secondaire : sans ce réglage l'overlay reste toujours calé sur l'écran principal Windows."));

        panel.Children.Add(Label("Longueur de l'historique de coups"));
        var historySlider = new Slider { Minimum = 4, Maximum = 30, Value = AppState.Settings.MaxHistoryEntries, Width = 300, HorizontalAlignment = HorizontalAlignment.Left, TickFrequency = 1, IsSnapToTickEnabled = true };
        var historyValue = new TextBlock { Foreground = TextColor, Margin = new Thickness(10, 0, 0, 0), Text = $"{AppState.Settings.MaxHistoryEntries} lignes" };
        var historyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 14) };
        historyRow.Children.Add(historySlider);
        historyRow.Children.Add(historyValue);
        historySlider.ValueChanged += (_, e) =>
        {
            var value = (int)e.NewValue;
            AppState.Settings.MaxHistoryEntries = value;
            historyValue.Text = $"{value} lignes";
            AppState.SaveSettings();
        };
        panel.Children.Add(historyRow);

        return Wrap(panel);
    }

    // ================= À propos =================

    private UIElement BuildAboutTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("À propos"));
        panel.Children.Add(new TextBlock { Text = "Brawlhalla Input Overlay", Foreground = TextColor, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(new TextBlock { Text = "Mode Tutoriel + Panneau de contrôle", Foreground = SubtleText, Margin = new Thickness(0, 0, 0, 20) });

        panel.Children.Add(new TextBlock { Text = "Raccourcis clavier", Foreground = TextColor, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var (keys, desc) in new[]
        {
            ("Ctrl+Alt+O", "Verrouiller / déverrouiller l'overlay"),
            ("Ctrl+Alt+P", "Changer de mode (parmi les favoris)"),
            ("Ctrl+Alt+K", "Changer la combo active (mode Tutoriel)"),
            ("Ctrl+Alt+R", "Démarrer / arrêter l'enregistrement d'une combo"),
            ("Ctrl+Alt+U", "Ouvrir / donner le focus au panneau de contrôle"),
            ("Ctrl+Alt+I", "Révéler temporairement la combo (mode révision)"),
            ("Ctrl+Alt+H", "Suspendre / reprendre la capture (utile hors du jeu)"),
        })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = keys, Foreground = TextColor, FontFamily = new FontFamily("Consolas"), Width = 110 });
            row.Children.Add(new TextBlock { Text = desc, Foreground = SubtleText });
            panel.Children.Add(row);
        }

        panel.Children.Add(new TextBlock { Text = "Icônes", Foreground = TextColor, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 20, 0, 6) });
        panel.Children.Add(new TextBlock
        {
            Text = "Icônes du mode Tutoriel par Lorc et Delapouite, sous licence CC BY 3.0. Disponibles sur game-icons.net.",
            Foreground = SubtleText,
            TextWrapping = TextWrapping.Wrap,
        });

        return Wrap(panel);
    }

    /// <summary>Petite boîte de dialogue modale à un champ texte (pas d'équivalent WPF natif
    /// à InputBox VB) — utilisée pour nommer un nouveau profil de touches.</summary>
    private string? PromptForText(string message, string title)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = PanelBg,
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = message, Foreground = TextColor, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var input = new TextBox { Margin = new Thickness(0, 0, 0, 14) };
        root.Children.Add(input);

        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "Annuler", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
        var okBtn = new Button { Content = "OK", Padding = new Thickness(14, 5, 14, 5), IsDefault = true };
        okBtn.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        buttonsRow.Children.Add(cancelBtn);
        buttonsRow.Children.Add(okBtn);
        root.Children.Add(buttonsRow);

        dialog.Content = root;
        input.Focus();

        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private static UIElement Wrap(UIElement content) =>
        new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
}
