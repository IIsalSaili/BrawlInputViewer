using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrawlhallaOverlay;

/// <summary>
/// Écran d'accueil (point d'entrée de l'app, StartupUri dans App.xaml) : demande
/// personnage/arme/combo/mode AVANT d'afficher l'overlay, plutôt que de lancer
/// directement l'overlay click-through sans aucun contexte (retour utilisateur :
/// l'ancien comportement — StartupUri pointant droit sur MainWindow — était perçu
/// comme contre-intuitif au premier lancement). Fenêtre bordée classique (comme
/// ControlPanelWindow), pas l'overlay in-game. Une fois "Lancer en jeu" cliqué,
/// elle applique la sélection à AppState puis instancie MainWindow et se ferme —
/// voir LaunchOverlay().
/// </summary>
public partial class StartupWindow : Window
{
    // Même palette que ControlPanelWindow (voir sa doc en tête de fichier) : accent
    // doré de marque repris de la tray icon, pas de couleur neuve inventée ici.
    private static readonly Brush PanelBg = new SolidColorBrush(Color.FromRgb(0x18, 0x17, 0x22));
    private static readonly Brush CardBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x27, 0x35));
    private static readonly Brush TextColor = Brushes.White;
    private static readonly Brush SubtleText = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
    private static readonly Brush AccentGold = new SolidColorBrush(Color.FromRgb(0xE8, 0xC4, 0x4A));

    private ComboBox _characterCombo = null!;
    private ComboBox _weaponCombo = null!;
    private ListBox _combosList = null!;
    private readonly List<int> _visibleIndices = new(); // -1 = "aucune combo (juste l'overlay)"
    private Image _portraitImage = null!;
    private RadioButton _modeHistorique = null!;
    private RadioButton _modeArrows = null!;
    private RadioButton _modeTutorial = null!;
    private readonly Dictionary<string, BitmapImage> _portraitCache = new();

    private ControlPanelWindow? _controlPanel;

    public StartupWindow()
    {
        InitializeComponent();
        Build();
    }

    private void Build()
    {
        var root = new DockPanel();

        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.Content = BuildBody();
        root.Children.Add(scroll);

        RootGrid.Children.Add(root);
    }

    private UIElement BuildHeader()
    {
        var border = new Border
        {
            Background = CardBg,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xE8, 0xC4, 0x4A)),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(24, 20, 24, 20),
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        // Même dessin que la tray icon (cercle sombre + "B" doré) pour une identité
        // de marque cohérente entre la barre système et cet écran d'accueil.
        var logo = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderBrush = AccentGold,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 0, 16, 0),
            Child = new TextBlock
            {
                Text = "B",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = AccentGold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        row.Children.Add(logo);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = "Brawlhalla Input Overlay",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = TextColor,
        });
        titles.Children.Add(new TextBlock
        {
            Text = "Choisis ton personnage, ta combo et ton mode d'affichage, puis lance l'overlay en jeu.",
            FontSize = 12,
            Foreground = SubtleText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });
        row.Children.Add(titles);

        border.Child = row;
        return border;
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        Foreground = AccentGold,
        Margin = new Thickness(0, 14, 0, 6),
    };

    private static TextBlock HelpText(string text) => new()
    {
        Text = text,
        Foreground = SubtleText,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private UIElement BuildBody()
    {
        var panel = new StackPanel { Margin = new Thickness(24, 8, 24, 24) };

        panel.Children.Add(SectionTitle("Personnage"));
        var characterRow = new StackPanel { Orientation = Orientation.Horizontal };

        _portraitImage = new Image
        {
            Width = 48,
            Height = 48,
            Margin = new Thickness(0, 0, 12, 0),
            Visibility = Visibility.Collapsed,
            Clip = new RectangleGeometry(new Rect(0, 0, 48, 48), 6, 6),
        };
        characterRow.Children.Add(_portraitImage);

        var characterItems = new List<string> { "Tous les personnages" };
        characterItems.AddRange(LegendComboPresets.Legends);
        _characterCombo = new ComboBox { Width = 260, ItemsSource = characterItems, VerticalAlignment = VerticalAlignment.Center };
        _characterCombo.SelectedItem = string.IsNullOrEmpty(AppState.Settings.TrainingLegendFilter)
            ? "Tous les personnages"
            : AppState.Settings.TrainingLegendFilter;
        characterRow.Children.Add(_characterCombo);
        panel.Children.Add(characterRow);

        panel.Children.Add(SectionTitle("Arme"));
        var weaponRow = new StackPanel { Orientation = Orientation.Horizontal };
        _weaponCombo = new ComboBox { Width = 260, Margin = new Thickness(0, 0, 8, 0) };
        weaponRow.Children.Add(_weaponCombo);
        var importBtn = new Button { Padding = new Thickness(10, 4, 10, 4) };
        weaponRow.Children.Add(importBtn);
        panel.Children.Add(weaponRow);
        var weaponHelp = HelpText("");
        panel.Children.Add(weaponHelp);

        void RefreshWeaponOptions()
        {
            var character = _characterCombo.SelectedItem as string;
            var noCharacter = string.IsNullOrEmpty(character) || character == "Tous les personnages";

            if (noCharacter)
            {
                var items = new List<string> { "Toutes les armes" };
                items.AddRange(WeaponComboPresets.Weapons);
                _weaponCombo.ItemsSource = items;
                _weaponCombo.SelectedItem = string.IsNullOrEmpty(AppState.Settings.TrainingWeaponFilter)
                    ? "Toutes les armes"
                    : AppState.Settings.TrainingWeaponFilter;
                importBtn.Content = "Importer les 5 combos de cette arme";
                importBtn.IsEnabled = _weaponCombo.SelectedItem as string != "Toutes les armes";
                weaponHelp.Text = "Filtre la liste de combos ci-dessous sur l'arme choisie. « Importer » ajoute les true combos vérifiés pour cette arme si elles n'y sont pas déjà.";
            }
            else
            {
                var weapons = LegendComboPresets.WeaponsFor(character!);
                var allLabel = $"Toutes les armes de {character}";
                var items = new List<string> { allLabel };
                items.AddRange(weapons);
                _weaponCombo.ItemsSource = items;
                _weaponCombo.SelectedItem = weapons.Contains(AppState.Settings.TrainingWeaponFilter)
                    ? AppState.Settings.TrainingWeaponFilter
                    : allLabel;
                importBtn.Content = $"Importer les combos de {character}";
                importBtn.IsEnabled = true;
                weaponHelp.Text = $"Combos Signature de {character} + combos génériques de ses {weapons.Count} arme(s). « Importer » ajoute les deux d'un coup.";
            }
        }

        void RefreshPortrait()
        {
            var character = _characterCombo.SelectedItem as string;
            if (!string.IsNullOrEmpty(character) && LegendComboPresets.Legends.Contains(character))
            {
                var fileName = character.Replace(" ", "") + ".png";
                if (!_portraitCache.TryGetValue(fileName, out var portrait))
                {
                    try
                    {
                        portrait = new BitmapImage(new Uri($"pack://application:,,,/Assets/Legends/{fileName}", UriKind.Absolute));
                        _portraitCache[fileName] = portrait;
                    }
                    catch (IOException)
                    {
                        portrait = null;
                    }
                }
                if (portrait is not null)
                {
                    _portraitImage.Source = portrait;
                    _portraitImage.Visibility = Visibility.Visible;
                }
                else
                {
                    _portraitImage.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                _portraitImage.Visibility = Visibility.Collapsed;
            }
        }

        _characterCombo.SelectionChanged += (_, _) =>
        {
            var selected = _characterCombo.SelectedItem as string ?? "Tous les personnages";
            AppState.SetTrainingLegendFilter(selected == "Tous les personnages" ? "" : selected);
            AppState.SetTrainingWeaponFilter("");
            RefreshWeaponOptions();
            RefreshPortrait();
            RefreshCombosList();
        };

        _weaponCombo.SelectionChanged += (_, _) =>
        {
            var selected = _weaponCombo.SelectedItem as string ?? "";
            var isAllOption = selected == "Toutes les armes" || selected.StartsWith("Toutes les armes de ");
            AppState.SetTrainingWeaponFilter(isAllOption ? "" : selected);
            RefreshCombosList();
        };

        importBtn.Click += (_, _) =>
        {
            var character = _characterCombo.SelectedItem as string;
            if (!string.IsNullOrEmpty(character) && character != "Tous les personnages")
            {
                AppState.ImportCharacterPresets(character);
                RefreshCombosList();
                return;
            }

            var weapon = _weaponCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(weapon) || weapon == "Toutes les armes") return;
            AppState.ImportWeaponPresets(weapon);
            RefreshCombosList();
        };

        panel.Children.Add(SectionTitle("Combo à afficher"));
        // Créée avant le 1er RefreshWeaponOptions()/RefreshCombosList() : ceux-ci fixent
        // SelectedItem sur les ComboBox, ce qui déclenche leur SelectionChanged
        // synchroniquement et donc RefreshCombosList(), qui a besoin que la liste existe déjà.
        _combosList = new ListBox { Height = 150, Background = CardBg, Foreground = TextColor, BorderThickness = new Thickness(0) };

        RefreshWeaponOptions();
        RefreshPortrait();
        RefreshCombosList();
        panel.Children.Add(_combosList);

        _combosList.SelectionChanged += (_, _) =>
        {
            // Choisir une vraie combo n'a d'intérêt qu'en mode Tutoriel (seul mode qui
            // l'affiche) — bascule automatiquement dessus pour éviter de lancer en mode
            // Historique après avoir pourtant choisi une combo précise.
            var idx = _combosList.SelectedIndex;
            if (idx >= 0 && idx < _visibleIndices.Count && _visibleIndices[idx] >= 0)
            {
                _modeTutorial.IsChecked = true;
            }
        };

        panel.Children.Add(SectionTitle("Mode d'affichage en jeu"));
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal };
        _modeHistorique = new RadioButton { Content = "Historique", GroupName = "mode", Foreground = TextColor, Margin = new Thickness(0, 0, 20, 0) };
        _modeArrows = new RadioButton { Content = "Grandes flèches", GroupName = "mode", Foreground = TextColor, Margin = new Thickness(0, 0, 20, 0) };
        _modeTutorial = new RadioButton { Content = "Tutoriel", GroupName = "mode", Foreground = TextColor };
        modeRow.Children.Add(_modeHistorique);
        modeRow.Children.Add(_modeArrows);
        modeRow.Children.Add(_modeTutorial);
        switch (AppState.Settings.DefaultMode)
        {
            case 1: _modeArrows.IsChecked = true; break;
            case 2: _modeTutorial.IsChecked = true; break;
            default: _modeHistorique.IsChecked = true; break;
        }
        panel.Children.Add(modeRow);
        panel.Children.Add(HelpText("Rappel : le mode Tutoriel affiche la combo choisie ci-dessus, validée en temps réel pendant que tu joues. Changeable en jeu avec Ctrl+Alt+P."));

        return panel;
    }

    private int SelectedComboIndex() =>
        _combosList.SelectedIndex >= 0 && _combosList.SelectedIndex < _visibleIndices.Count
            ? _visibleIndices[_combosList.SelectedIndex]
            : -1;

    private void RefreshCombosList()
    {
        var previouslySelected = SelectedComboIndex();

        _combosList.Items.Clear();
        _visibleIndices.Clear();

        _combosList.Items.Add("— Aucune combo sélectionnée (juste l'overlay) —");
        _visibleIndices.Add(-1);

        foreach (var i in AppState.FilteredComboIndices())
        {
            var combo = AppState.Combos[i];
            _visibleIndices.Add(i);
            var legendTag = string.IsNullOrEmpty(combo.Legend) ? "" : $"{combo.Legend} ";
            var weaponTag = string.IsNullOrEmpty(combo.Weapon) ? "" : $"[{legendTag}{combo.Weapon}] ";
            _combosList.Items.Add($"{weaponTag}{combo.Name}  ({combo.Steps.Count} étapes)");
        }

        var restored = _visibleIndices.IndexOf(previouslySelected);
        _combosList.SelectedIndex = restored >= 0 ? restored : (AppState.ActiveComboIndex >= 0 ? _visibleIndices.IndexOf(AppState.ActiveComboIndex) : 0);
        if (_combosList.SelectedIndex < 0) _combosList.SelectedIndex = 0;
    }

    private UIElement BuildFooter()
    {
        var border = new Border
        {
            Background = CardBg,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xE8, 0xC4, 0x4A)),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(24, 14, 24, 14),
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var advancedBtn = new Button
        {
            Content = "Réglages avancés…",
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        advancedBtn.Click += (_, _) => OpenAdvancedSettings();
        Grid.SetColumn(advancedBtn, 0);
        row.Children.Add(advancedBtn);

        var launchBtn = new Button
        {
            Content = "Lancer en jeu ▶",
            Padding = new Thickness(20, 8, 20, 8),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Background = AccentGold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x17, 0x22)),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        launchBtn.Click += (_, _) => LaunchOverlay();
        Grid.SetColumn(launchBtn, 2);
        row.Children.Add(launchBtn);

        border.Child = row;
        return border;
    }

    private void OpenAdvancedSettings()
    {
        if (_controlPanel is null || !_controlPanel.IsLoaded)
        {
            _controlPanel = new ControlPanelWindow();
            _controlPanel.Closed += (_, _) => _controlPanel = null;
            _controlPanel.Show();
        }
        else
        {
            if (_controlPanel.WindowState == WindowState.Minimized) _controlPanel.WindowState = WindowState.Normal;
            _controlPanel.Activate();
        }
    }

    private void LaunchOverlay()
    {
        var chosen = SelectedComboIndex();
        if (chosen >= 0) AppState.SetActiveCombo(chosen);

        var mode = _modeTutorial.IsChecked == true ? 2 : _modeArrows.IsChecked == true ? 1 : 0;
        AppState.Settings.DefaultMode = mode;
        AppState.SaveSettings();
        AppState.SetMode(mode);

        var overlay = new MainWindow();
        Application.Current.MainWindow = overlay;
        overlay.Show();

        _controlPanel?.Close();
        Close();
    }
}
