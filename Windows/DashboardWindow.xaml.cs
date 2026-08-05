using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrawlhallaOverlay;

/// <summary>
/// Tableau de bord unifié (Phase 1 de la refonte UX) — fusion de StartupWindow (v11) et OnboardingWindow (v13).
/// Point d'entrée principal de l'app : sélection personnage/arme/combo/mode avant de lancer l'overlay.
///
/// Trois chemins possibles selon le profil utilisateur :
///   - Débutant : mode Tutoriel, puis Leçons en parallèle
///   - Connaisseur : grille persos → combo → Tutoriel directement
///   - Expert : flux complet (accueil classique)
///   - Revenant : bandeau "Reprendre" au top si session active
///
/// Tous convergent sur un écran "Voilà ce qui va se passer" (explique ce qui advient quand l'overlay remplace cette fenêtre).
/// </summary>
public partial class DashboardWindow : Window
{
    // Palette = ControlPanelWindow + StartupWindow (accent doré cohérent)
    private static readonly Brush PanelBg = new SolidColorBrush(Color.FromRgb(0x18, 0x17, 0x22));
    private static readonly Brush CardBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x27, 0x35));
    private static readonly Brush CardHoverBg = new SolidColorBrush(Color.FromRgb(0x35, 0x31, 0x42));
    private static readonly Brush TextColor = Brushes.White;
    private static readonly Brush SubtleText = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
    private static readonly Brush AccentGold = new SolidColorBrush(Color.FromRgb(0xE8, 0xC4, 0x4A));

    // Si vraie, une session overlay tourne déjà (ouverte depuis MainWindow via Ctrl+Alt+U/tray/
    // barre de contrôle) : la fourche d'onboarding n'a plus lieu d'être (un revenant en jeu a déjà
    // forcément complété l'onboarding), et "Lancer en jeu" doit juste appliquer la sélection à
    // l'overlay déjà ouvert (AppState.SetActiveCombo, qui propage en direct via
    // ActiveComboChanged, déjà écouté par MainWindow pour Ctrl+Alt+K) plutôt que
    // d'instancier un second MainWindow par-dessus celui qui tourne.
    private readonly bool _inGameMode;

    // État de navigation
    private string _onboardingProfile = "";
    private string? _onboardingChosenCharacter;

    // Contrôles du flux principal
    private ComboBox _characterCombo = null!;
    private ComboBox _weaponCombo = null!;
    private ListBox _combosList = null!;
    private readonly List<int> _visibleIndices = new();
    private Image _portraitImage = null!;
    private readonly Dictionary<string, BitmapImage> _portraitCache = new();

    private ControlPanelWindow? _controlPanel;

    public DashboardWindow(bool inGameMode = false)
    {
        InitializeComponent();
        _inGameMode = inGameMode;

        // Montrer la fourche d'onboarding seulement au tout premier lancement de l'app (jamais
        // en mode in-game : ouvrir cette fenêtre depuis une session déjà en cours signifie que
        // l'onboarding est nécessairement déjà passé).
        if (!_inGameMode && AppState.Settings.OnboardingCompleted != true)
        {
            ShowOnboardingFork();
        }
        else
        {
            BuildMainFlow();
        }
    }

    private void ShowOnboardingFork()
    {
        var root = new StackPanel { Margin = new Thickness(32), VerticalAlignment = VerticalAlignment.Center };

        root.Children.Add(new TextBlock
        {
            Text = "Bienvenue !",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = AccentGold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        root.Children.Add(new TextBlock
        {
            Text = "Une seule question pour commencer : tu es plutôt…",
            FontSize = 13,
            Foreground = SubtleText,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 28),
        });

        var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var beginner = BuildForkCard("🌱", "Je débute à Brawlhalla", "Aucune autre question — on te montre juste l'essentiel de l'app pour commencer.");
        beginner.MouseLeftButtonUp += (_, _) => ChooseOnboardingProfile("Débutant");
        cards.Children.Add(beginner);

        var knower = BuildForkCard("🎮", "Je connais le jeu, pas l'app", "Choisis ton personnage, ses combos vérifiées sont importées automatiquement.");
        knower.MouseLeftButtonUp += (_, _) => ChooseOnboardingProfile("Connaisseur");
        cards.Children.Add(knower);

        var expert = BuildForkCard("⚡", "Je sais ce que je fais", "Direct vers l'écran complet : personnage, arme, combo, mode d'affichage.");
        expert.MouseLeftButtonUp += (_, _) => ChooseOnboardingProfile("Expert");
        cards.Children.Add(expert);

        root.Children.Add(cards);
        RootGrid.Children.Add(root);
    }

    private static Border BuildForkCard(string emoji, string title, string subtitle)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = emoji, FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        stack.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = TextColor, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = SubtleText, Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 190 });

        var border = new Border
        {
            Background = CardBg,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xE8, 0xC4, 0x4A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(10),
            Width = 210,
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = stack,
        };
        border.MouseEnter += (_, _) => border.Background = CardHoverBg;
        border.MouseLeave += (_, _) => border.Background = CardBg;
        return border;
    }

    private void ChooseOnboardingProfile(string profile)
    {
        _onboardingProfile = profile;

        if (profile == "Expert")
        {
            MarkOnboardingCompleted();
            BuildMainFlow();
            return;
        }

        if (profile == "Connaisseur")
        {
            ShowOnboardingCharacterGrid();
            return;
        }

        // Débutant : pas de question supplémentaire
        ShowWhatHappensNext();
    }

    private void ShowOnboardingCharacterGrid()
    {
        RootGrid.Children.Clear();
        var root = new DockPanel();

        var header = new StackPanel { Margin = new Thickness(24, 20, 24, 8) };
        header.Children.Add(new TextBlock { Text = "Ton personnage", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = AccentGold });
        header.Children.Add(new TextBlock
        {
            Text = "Ses combos vérifiées seront importées automatiquement — pas besoin de cliquer sur un bouton « Importer » séparé.",
            FontSize = 12,
            Foreground = SubtleText,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Border { Padding = new Thickness(24, 12, 24, 16) };
        var skipBtn = new Button { Content = "Je ne sais pas encore →", Padding = new Thickness(10, 6, 10, 6), HorizontalAlignment = HorizontalAlignment.Right };
        skipBtn.Click += (_, _) => { _onboardingChosenCharacter = null; ShowWhatHappensNext(); };
        footer.Child = skipBtn;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var wrap = new WrapPanel { Margin = new Thickness(24, 0, 24, 0) };
        foreach (var legend in LegendComboPresets.Legends)
        {
            wrap.Children.Add(BuildOnboardingCharacterTile(legend));
        }
        scroll.Content = wrap;
        root.Children.Add(scroll);

        RootGrid.Children.Add(root);
    }

    private Border BuildOnboardingCharacterTile(string legend)
    {
        var image = new Image { Width = 64, Height = 64, Clip = new RectangleGeometry(new Rect(0, 0, 64, 64), 6, 6) };
        var fileName = legend.Replace(" ", "") + ".png";
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
        if (portrait is not null) image.Source = portrait;

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(image);
        stack.Children.Add(new TextBlock { Text = legend, FontSize = 11, Foreground = TextColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });

        var tile = new Border
        {
            Background = CardBg,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(4),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = stack,
        };
        tile.MouseEnter += (_, _) => tile.Background = CardHoverBg;
        tile.MouseLeave += (_, _) => tile.Background = CardBg;
        tile.MouseLeftButtonUp += (_, _) => { _onboardingChosenCharacter = legend; ShowWhatHappensNext(); };
        return tile;
    }

    private void ShowWhatHappensNext()
    {
        RootGrid.Children.Clear();
        var panel = new StackPanel { Margin = new Thickness(32), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 560 };

        panel.Children.Add(new TextBlock { Text = "Voilà ce qui va se passer", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = AccentGold, HorizontalAlignment = HorizontalAlignment.Center });

        void Bullet(string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(new TextBlock { Text = "•", Foreground = AccentGold, FontSize = 14, Margin = new Thickness(0, 0, 10, 0) });
            row.Children.Add(new TextBlock { Text = text, Foreground = TextColor, FontSize = 13, TextWrapping = TextWrapping.Wrap, Width = 480 });
            panel.Children.Add(row);
        }

        Bullet("Cette fenêtre va se fermer et laisser place à un overlay transparent, sans bordure ni bouton — normal, c'est fait pour se superposer au jeu sans le gêner.");
        Bullet("Pour la retrouver ensuite : l'icône dorée « B » en bas à droite de l'écran (zone de notification Windows), ou le raccourci Ctrl+Alt+U depuis n'importe où.");
        Bullet("Une petite barre discrète (≡) reste aussi accessible en survolant le coin bas-droit de l'overlay en jeu : elle permet de suspendre la capture ou de rouvrir cet accueil à la souris, sans raccourci clavier à retenir.");

        if (_onboardingProfile == "Débutant")
        {
            Bullet("Tu démarres en mode « Tutoriel » (le seul mode d'affichage de l'app) — sans combo sélectionné pour l'instant, choisis-en un quand tu veux depuis le panneau de contrôle.");
            Bullet("Une fenêtre « Leçons » s'ouvre à côté : des leçons courtes (tes touches, puis survivre — sauts/esquive/récupération) validées en temps réel pendant que tu joues. Chapitre 0+1 seulement pour l'instant, la suite arrivera plus tard.");
        }
        else if (_onboardingChosenCharacter is not null)
        {
            Bullet($"Tu démarres directement en mode « Tutoriel » avec les combos de {_onboardingChosenCharacter}, déjà importées et prêtes à être validées en jeu.");
        }
        else
        {
            Bullet("Tu démarres en mode « Tutoriel » — tu pourras choisir un personnage et ses combos plus tard depuis le panneau de contrôle (Ctrl+Alt+U).");
        }

        var launchBtn = new Button
        {
            Content = "C'est parti ▶",
            Padding = new Thickness(24, 10, 24, 10),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Background = AccentGold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x17, 0x22)),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 28, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        launchBtn.Click += (_, _) => CompleteOnboardingAndLaunch();
        panel.Children.Add(launchBtn);

        RootGrid.Children.Add(panel);
    }

    // ========== FLUX PRINCIPAL (après onboarding) ==========

    private void BuildMainFlow()
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

        RootGrid.Children.Clear();
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

        // Logo officiel de l'app (Assets/AppLogo.png, déjà utilisé pour l'icône de tray et les
        // icônes de fenêtre — voir MainWindow.CreateTrayIcon) — remplace un ancien badge "B"
        // dessiné en texte brut, resté ici sans avoir été mis à jour en même temps que le tray.
        var logo = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderBrush = AccentGold,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 0, 16, 0),
            Child = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/AppLogo.png", UriKind.Absolute)),
                Width = 34,
                Height = 34,
                Stretch = Stretch.Uniform,
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
            Text = _inGameMode
                ? "Change de personnage, d'arme ou de combo — appliqué en direct sur l'overlay en cours."
                : "Choisis ton personnage, ton arme, un combo et lance l'overlay en jeu.",
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

        panel.Children.Add(BuildTutorialBanner());

        var resumeBanner = BuildResumeBanner();
        if (resumeBanner is not null) panel.Children.Add(resumeBanner);

        panel.Children.Add(SectionTitle("Personnage"));

        _portraitImage = new Image
        {
            Width = 48,
            Height = 48,
            Margin = new Thickness(0, 0, 12, 0),
            Visibility = Visibility.Collapsed,
            Clip = new RectangleGeometry(new Rect(0, 0, 48, 48), 6, 6),
        };

        var characterItems = new List<string> { "Tous les personnages" };
        characterItems.AddRange(LegendComboPresets.Legends);
        _characterCombo = new ComboBox { ItemsSource = characterItems, Visibility = Visibility.Collapsed };
        _characterCombo.SelectedItem = string.IsNullOrEmpty(AppState.Settings.TrainingLegendFilter)
            ? "Tous les personnages"
            : AppState.Settings.TrainingLegendFilter;

        var characterSection = new Grid();
        characterSection.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        characterSection.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_portraitImage, 0);
        var characterGrid = new WrapPanel();
        Grid.SetColumn(characterGrid, 1);
        characterSection.Children.Add(_portraitImage);
        characterSection.Children.Add(characterGrid);

        characterGrid.Children.Add(BuildCharacterTile(null, "Tous"));
        foreach (var legend in LegendComboPresets.Legends) characterGrid.Children.Add(BuildCharacterTile(legend, legend));

        panel.Children.Add(characterSection);

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
                importBtn.Visibility = Visibility.Visible;
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
                importBtn.Visibility = Visibility.Collapsed;
                weaponHelp.Text = $"Combos Signature de {character} + combos génériques de ses {weapons.Count} arme(s), importés automatiquement.";
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
            if (selected != "Tous les personnages") AppState.ImportCharacterPresets(selected);
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
        _combosList = new ListBox { Height = 150, Background = CardBg, Foreground = TextColor, BorderThickness = new Thickness(0) };

        RefreshWeaponOptions();
        RefreshPortrait();
        RefreshCombosList();
        panel.Children.Add(_combosList);

        panel.Children.Add(HelpText("Le mode Tutoriel affiche le combo choisi ci-dessus, validé en temps réel pendant que tu joues."));

        return panel;
    }

    private UIElement BuildTutorialBanner()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "🎓 Leçons — apprends les bases du jeu et de l'app",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = TextColor,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Recommandé avant de te lancer : leçons courtes validées en temps réel (sauts, esquive, récupération...).",
            FontSize = 11,
            Foreground = SubtleText,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var tutorialBtn = new Button
        {
            Content = "Ouvrir les leçons ▶",
            Padding = new Thickness(16, 8, 16, 8),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Background = AccentGold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x17, 0x22)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        tutorialBtn.Click += (_, _) => new ParcoursWindow().Show();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(stack, 0);
        Grid.SetColumn(tutorialBtn, 1);
        grid.Children.Add(stack);
        grid.Children.Add(tutorialBtn);

        return new Border
        {
            Background = CardBg,
            BorderBrush = AccentGold,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 16),
            Child = grid,
        };
    }

    private UIElement? BuildResumeBanner()
    {
        // Sans intérêt en mode in-game : la session en cours EST déjà celle qu'on "reprendrait",
        // pas une session passée — le bandeau n'aurait montré qu'une reformulation de ce qui
        // tourne déjà, pas une action utile.
        if (_inGameMode) return null;
        if (AppState.ActiveComboIndex < 0 || AppState.ActiveComboIndex >= AppState.Combos.Count) return null;

        var combo = AppState.Combos[AppState.ActiveComboIndex];

        var contextParts = new List<string>();
        if (combo.BestStreak > 0) contextParts.Add($"série record {combo.BestStreak}");
        if (combo.Mastered) contextParts.Add("maîtrisée ✓");

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = $"Reprendre — {combo.Name}",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = TextColor,
        });
        stack.Children.Add(new TextBlock
        {
            Text = contextParts.Count > 0 ? string.Join(" · ", contextParts) + " — dernière session" : "Dernière session",
            FontSize = 11,
            Foreground = SubtleText,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var resumeBtn = new Button
        {
            Content = "Reprendre ▶",
            Padding = new Thickness(16, 8, 16, 8),
            FontWeight = FontWeights.Bold,
            Background = AccentGold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x17, 0x22)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        resumeBtn.Click += (_, _) => LaunchOverlay();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(stack, 0);
        Grid.SetColumn(resumeBtn, 1);
        grid.Children.Add(stack);
        grid.Children.Add(resumeBtn);

        return new Border
        {
            Background = CardBg,
            BorderBrush = AccentGold,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 16),
            Child = grid,
        };
    }

    private Border BuildCharacterTile(string? legend, string label)
    {
        var image = new Image { Width = 40, Height = 40, Clip = new RectangleGeometry(new Rect(0, 0, 40, 40), 5, 5) };
        if (legend is not null)
        {
            var fileName = legend.Replace(" ", "") + ".png";
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
            if (portrait is not null) image.Source = portrait;
        }

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Width = 60 };
        stack.Children.Add(image);
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            Foreground = TextColor,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var tile = new Border
        {
            Background = CardBg,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Margin = new Thickness(3),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = label,
            Child = stack,
        };
        tile.MouseLeftButtonUp += (_, _) => _characterCombo.SelectedItem = legend ?? "Tous les personnages";
        return tile;
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

        _combosList.Items.Add("— Aucun combo sélectionné (juste l'overlay) —");
        _visibleIndices.Add(-1);

        var filteredAbsIndices = AppState.FilteredComboIndices();
        foreach (var ordered in ComboFamilies.OrderWithFamilies(filteredAbsIndices, i => AppState.Combos[i]))
        {
            var combo = AppState.Combos[ordered.Index];
            _visibleIndices.Add(ordered.Index);
            var legendTag = string.IsNullOrEmpty(combo.Legend) ? "" : $"{combo.Legend} ";
            var weaponTag = string.IsNullOrEmpty(combo.Weapon) ? "" : $"[{legendTag}{combo.Weapon}] ";
            var masteredMark = combo.Mastered ? " ✓" : "";
            var indent = ordered.Level > 1 ? new string(' ', (ordered.Level - 1) * 3) + "↳ " : "";
            var levelTag = ordered.Level > 0 && ordered.FamilySize > 1 ? $" [Niveau {ordered.Level}]" : "";
            var followUpHint = combo.Mastered && ordered.HasFollowUp ? "  → niveau supérieur disponible ci-dessous" : "";
            _combosList.Items.Add($"{indent}{weaponTag}{combo.Name}{masteredMark}{levelTag}  ({combo.Steps.Count} étapes){followUpHint}");
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

        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };

        var advancedBtn = new Button
        {
            Content = "Réglages avancés…",
            Padding = new Thickness(10, 6, 10, 6),
        };
        advancedBtn.Click += (_, _) => OpenAdvancedSettings();
        leftButtons.Children.Add(advancedBtn);

        Grid.SetColumn(leftButtons, 0);
        row.Children.Add(leftButtons);

        var launchBtn = new Button
        {
            Content = _inGameMode ? "✓ Appliquer" : "▶ Lancer en jeu",
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

        // En mode in-game, un MainWindow tourne déjà : SetActiveCombo ci-dessus lui est
        // propagé en direct (ActiveComboChanged, le même event qu'utilise Ctrl+Alt+K) —
        // inutile et faux d'en instancier un second par-dessus.
        if (!_inGameMode)
        {
            var overlay = new MainWindow();
            Application.Current.MainWindow = overlay;
            overlay.Show();
        }

        _controlPanel?.Close();
        Close();
    }

    private void MarkOnboardingCompleted()
    {
        AppState.Settings.OnboardingCompleted = true;
        AppState.SaveSettings();
    }

    private void CompleteOnboardingAndLaunch()
    {
        MarkOnboardingCompleted();

        if (_onboardingProfile == "Connaisseur" && _onboardingChosenCharacter is not null)
        {
            AppState.SetTrainingLegendFilter(_onboardingChosenCharacter);
            AppState.ImportCharacterPresets(_onboardingChosenCharacter);
            var filtered = AppState.FilteredComboIndices();
            if (filtered.Count > 0) AppState.SetActiveCombo(filtered[0]);
        }

        var overlay = new MainWindow();
        Application.Current.MainWindow = overlay;
        overlay.Show();

        if (_onboardingProfile == "Débutant")
        {
            var parcours = new ParcoursWindow();
            parcours.Show();
        }

        Close();
    }
}
