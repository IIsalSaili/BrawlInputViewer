using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrawlhallaOverlay;

/// <summary>
/// Fourche du tout premier lancement (§5.1 du plan UX onboarding, docs/plan_ux_onboarding.md) :
/// remplace l'ancien comportement où le tout premier écran vu par l'utilisateur était déjà
/// StartupWindow au complet (4 décisions d'affilée, combos vides, aucune explication de ce qui va
/// se passer). Cette fenêtre ne s'affiche qu'une fois — App.xaml.cs choisit entre elle et
/// StartupWindow selon AppState.Settings.OnboardingCompleted.
///
/// Trois écrans possibles, jamais tous les trois pour un même utilisateur :
///   A. "Tu es plutôt…" — la seule question posée à tout le monde.
///   B. Grille de personnages (profils Débutant/Connaisseur uniquement).
///   C. "Voilà ce qui va se passer" — prévient AVANT que l'overlay ne remplace cette fenêtre par
///      un overlay transparent click-through, au lieu d'un ballon de notification après coup.
///
/// Volontairement pas de contenu pédagogique de jeu ici (le "Parcours" du §4 du plan reste à
/// construire — il manipule beaucoup de données factuelles sur Brawlhalla, qui doivent être
/// sourcées pièce par pièce avant d'être écrites, voir §3 et l'historique "Version 9"/"Correctif
/// Faux" de CLAUDE.md). Le choix "Débutant" mène donc pour l'instant au mode Historique (pas de
/// pression de combo) plutôt qu'à un Parcours qui n'existe pas encore, en le disant explicitement
/// à l'écran plutôt que de laisser croire qu'un contenu pédagogique complet est prêt.
/// </summary>
public partial class OnboardingWindow : Window
{
    private static readonly Brush CardBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x27, 0x35));
    private static readonly Brush CardHoverBg = new SolidColorBrush(Color.FromRgb(0x35, 0x31, 0x42));
    private static readonly Brush TextColor = Brushes.White;
    private static readonly Brush SubtleText = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
    private static readonly Brush AccentGold = new SolidColorBrush(Color.FromRgb(0xE8, 0xC4, 0x4A));

    private readonly Dictionary<string, BitmapImage> _portraitCache = new();
    private string _profile = "";
    private string? _chosenCharacter;

    public OnboardingWindow()
    {
        InitializeComponent();
        ShowForkScreen();
    }

    private void SetContent(UIElement content)
    {
        RootGrid.Children.Clear();
        RootGrid.Children.Add(content);
    }

    private static Border Card(string emoji, string title, string subtitle)
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

    // ------------------------------------------------------------------
    // Écran A — "Tu es plutôt…"
    // ------------------------------------------------------------------
    private void ShowForkScreen()
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

        var beginner = Card("🌱", "Je débute à Brawlhalla", "Aucune autre question — on te montre juste l'essentiel de l'app pour commencer.");
        beginner.MouseLeftButtonUp += (_, _) => ChooseProfile("Débutant");
        cards.Children.Add(beginner);

        var knower = Card("🎮", "Je connais le jeu, pas l'app", "Choisis ton personnage, ses combos vérifiées sont importées automatiquement.");
        knower.MouseLeftButtonUp += (_, _) => ChooseProfile("Connaisseur");
        cards.Children.Add(knower);

        var expert = Card("⚡", "Je sais ce que je fais", "Direct vers l'écran complet : personnage, arme, combo, mode d'affichage.");
        expert.MouseLeftButtonUp += (_, _) => ChooseProfile("Expert");
        cards.Children.Add(expert);

        root.Children.Add(cards);
        SetContent(root);
    }

    private void ChooseProfile(string profile)
    {
        _profile = profile;
        AppState.Settings.OnboardingProfile = profile;

        if (profile == "Expert")
        {
            CompleteOnboardingToStartupWindow();
            return;
        }

        if (profile == "Connaisseur")
        {
            ShowCharacterGrid();
            return;
        }

        // Débutant : pas de question supplémentaire (voir doc en tête de fichier).
        ShowWhatHappensNext();
    }

    // ------------------------------------------------------------------
    // Écran B — grille de personnages (profil Connaisseur uniquement)
    // ------------------------------------------------------------------
    private void ShowCharacterGrid()
    {
        var root = new DockPanel();

        var header = new StackPanel { Margin = new Thickness(24, 20, 24, 8) };
        header.Children.Add(new TextBlock { Text = "Ton personnage", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = AccentGold });
        header.Children.Add(new TextBlock
        {
            Text = "Ses combos vérifiées seront importées automatiquement — pas besoin de cliquer sur un bouton \"Importer\" séparé.",
            FontSize = 12,
            Foreground = SubtleText,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Border { Padding = new Thickness(24, 12, 24, 16) };
        var skipBtn = new Button { Content = "Je ne sais pas encore →", Padding = new Thickness(10, 6, 10, 6), HorizontalAlignment = HorizontalAlignment.Right };
        skipBtn.Click += (_, _) => { _chosenCharacter = null; ShowWhatHappensNext(); };
        footer.Child = skipBtn;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var wrap = new WrapPanel { Margin = new Thickness(24, 0, 24, 0) };
        foreach (var legend in LegendComboPresets.Legends)
        {
            wrap.Children.Add(CharacterTile(legend));
        }
        scroll.Content = wrap;
        root.Children.Add(scroll);

        SetContent(root);
    }

    private Border CharacterTile(string legend)
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
        tile.MouseLeftButtonUp += (_, _) => { _chosenCharacter = legend; ShowWhatHappensNext(); };
        return tile;
    }

    // ------------------------------------------------------------------
    // Écran C — "Voilà ce qui va se passer"
    // ------------------------------------------------------------------
    private void ShowWhatHappensNext()
    {
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
        Bullet("Pour la retrouver ensuite : l'icône dorée \"B\" en bas à droite de l'écran (zone de notification Windows), ou le raccourci Ctrl+Alt+U depuis n'importe où.");
        Bullet("Une petite barre discrète (≡) reste aussi accessible en survolant le coin bas-droit de l'overlay en jeu : elle permet de changer de mode/combo ou de suspendre la capture à la souris, sans raccourci clavier à retenir.");

        if (_profile == "Débutant")
        {
            Bullet("Tu démarres en mode \"Tutoriel\" (Historique et Grand affichage sont temporairement désactivés) — sans combo sélectionné pour l'instant, choisis-en un quand tu veux depuis le panneau de contrôle.");
            Bullet("Une fenêtre \"Leçons\" s'ouvre à côté : des leçons courtes (tes touches, puis survivre — sauts/esquive/récupération) validées en temps réel pendant que tu joues. Chapitre 0+1 seulement pour l'instant, la suite arrivera plus tard.");
        }
        else if (_chosenCharacter is not null)
        {
            Bullet($"Tu démarres directement en mode \"Tutoriel\" avec les combos de {_chosenCharacter}, déjà importées et prêtes à être validées en jeu.");
        }
        else
        {
            Bullet("Tu démarres en mode \"Tutoriel\" — tu pourras choisir un personnage et ses combos plus tard depuis le panneau de contrôle (Ctrl+Alt+U).");
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

        SetContent(panel);
    }

    // ------------------------------------------------------------------
    // Sorties
    // ------------------------------------------------------------------
    private void MarkOnboardingCompleted()
    {
        AppState.Settings.OnboardingCompleted = true;
        AppState.SaveSettings();
    }

    /// <summary>Profil "Expert" : ferme la fourche et rouvre le flux complet existant
    /// (StartupWindow), sans rien présélectionner de plus que ce qu'il fait déjà.</summary>
    private void CompleteOnboardingToStartupWindow()
    {
        MarkOnboardingCompleted();
        var startup = new StartupWindow();
        Application.Current.MainWindow = startup;
        startup.Show();
        Close();
    }

    /// <summary>Profils Débutant/Connaisseur : applique le choix de personnage/mode et lance
    /// directement l'overlay, comme StartupWindow.LaunchOverlay — sans repasser par l'écran
    /// complet, puisque la fourche a déjà posé les questions pertinentes pour ce profil.</summary>
    private void CompleteOnboardingAndLaunch()
    {
        MarkOnboardingCompleted();

        if (_profile == "Connaisseur" && _chosenCharacter is not null)
        {
            AppState.SetTrainingLegendFilter(_chosenCharacter);
            AppState.ImportCharacterPresets(_chosenCharacter);
            var filtered = AppState.FilteredComboIndices();
            if (filtered.Count > 0) AppState.SetActiveCombo(filtered[0]);
            AppState.Settings.DefaultMode = 2; // Tutoriel
        }
        else
        {
            AppState.Settings.DefaultMode = 0; // Historique
        }

        AppState.SaveSettings();
        AppState.SetMode(AppState.Settings.DefaultMode);

        var overlay = new MainWindow();
        Application.Current.MainWindow = overlay;
        overlay.Show();

        // Le profil Débutant n'a pas encore de combo à s'entraîner : le Parcours (Chapitre 0+1,
        // voir docs/plan_ux_onboarding.md §4) lui donne quand même quelque chose à faire tout de
        // suite, plutôt que de le laisser devant un simple mode Historique sans autre indication.
        if (_profile == "Débutant")
        {
            var parcours = new ParcoursWindow();
            parcours.Show();
        }

        Close();
    }
}
