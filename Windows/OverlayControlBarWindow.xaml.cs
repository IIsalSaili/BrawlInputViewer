using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BrawlhallaOverlay;

/// <summary>
/// Petite barre de pilotage attachée à l'overlay (§5.3.1 du plan UX onboarding), pour ne plus
/// dépendre uniquement des raccourcis clavier Ctrl+Alt+* pour les actions les plus fréquentes en
/// jeu (suspendre la capture, ouvrir l'accueil). Contrairement à
/// MainWindow, cette fenêtre n'est PAS click-through : c'est un choix délibéré, c'est justement
/// le seul endroit de l'overlay où la souris doit pouvoir agir. Elle vit en bas à droite de
/// l'écran ciblé, une zone volontairement éloignée du HUD par défaut (bas-gauche) pour ne jamais
/// le recouvrir.
///
/// Masquée par défaut : seul un petit onglet semi-transparent (≡) reste visible en permanence
/// (l'affordance de découverte), et la survol de la zone révèle la barre complète. Une fenêtre
/// entièrement invisible tant qu'on ne l'a pas trouvée ne serait pas différente de "aucune barre
/// du tout" — d'où ce compromis (voir §5.3 du plan : "masquée par défaut... apparaît au survol").
/// </summary>
public partial class OverlayControlBarWindow : Window
{
    private const double WindowWidth = 220;
    private const double WindowHeight = 40;
    private static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(500);

    private static readonly Brush BarBg = new SolidColorBrush(Color.FromArgb(0xE0, 0x18, 0x17, 0x22));
    private static readonly Brush AccentGold = new SolidColorBrush(Color.FromRgb(0xE8, 0xC4, 0x4A));

    private readonly Action _openPanel;
    private Border _collapsedTab = null!;
    private Border _expandedBorder = null!;
    private StackPanel _expandedBar = null!;
    private TextBlock _pauseGlyph = null!;
    private DispatcherTimer? _collapseTimer;

    public OverlayControlBarWindow(Action openPanel)
    {
        InitializeComponent();
        _openPanel = openPanel;

        Width = WindowWidth;
        Height = WindowHeight;

        Build();

        AppState.CaptureSuspendedChanged += OnCaptureSuspendedChanged;
        Closed += (_, _) =>
        {
            AppState.CaptureSuspendedChanged -= OnCaptureSuspendedChanged;
        };
    }

    /// <summary>Positionne la barre en bas à droite de la zone de travail donnée (voir
    /// MainWindow.GetTargetWorkArea, appelé à chaque ApplyWorkArea côté overlay pour rester sur
    /// le même écran, y compris si Settings.MonitorIndex change pendant que l'overlay tourne).</summary>
    public void Reposition(Rect workArea)
    {
        Left = workArea.Right - WindowWidth - 16;
        Top = workArea.Bottom - WindowHeight - 16;
    }

    private void Build()
    {
        var root = new Grid();
        root.MouseEnter += (_, _) => Expand();
        root.MouseLeave += (_, _) => ScheduleCollapse();

        _collapsedTab = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = BarBg,
            BorderBrush = AccentGold,
            BorderThickness = new Thickness(1),
            Opacity = 0.45,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Barre de contrôle de l'overlay (survole pour l'ouvrir)",
            Child = new TextBlock
            {
                Text = "≡",
                FontSize = 16,
                Foreground = AccentGold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        root.Children.Add(_collapsedTab);

        _expandedBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _expandedBorder = new Border
        {
            Background = BarBg,
            BorderBrush = AccentGold,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(6, 4, 6, 4),
            Child = _expandedBar,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        root.Children.Add(_expandedBorder);

        // Boutons combo précédente/suivante déplacés dans le panneau de combo lui-même
        // (MainWindow.BuildComboNameRow, mode Tutoriel) — plus logique juste à côté du nom du
        // combo qu'ils affectent que dans cette barre séparée.
        _pauseGlyph = new TextBlock();
        _expandedBar.Children.Add(BarButton(_pauseGlyph, "Suspendre/reprendre la capture\nCtrl+Alt+H · manette : Start + Back", () => AppState.ToggleCaptureSuspended()));
        _expandedBar.Children.Add(BarButton("⚙", "Ouvrir l'accueil (perso/combo/mode)\nCtrl+Alt+U · manette : Start + X", () => _openPanel()));
        UpdatePauseGlyph();

        RootGrid.Children.Add(root);
    }

    private Button BarButton(string glyph, string tooltip, Action onClick)
    {
        var text = new TextBlock { Text = glyph, FontSize = 14 };
        return BarButton(text, tooltip, onClick);
    }

    private Button BarButton(TextBlock content, string tooltip, Action onClick)
    {
        content.Foreground = Brushes.White;
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;

        var button = new Button
        {
            Content = content,
            Width = 30,
            Height = 30,
            Margin = new Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void UpdatePauseGlyph()
    {
        _pauseGlyph.Text = AppState.CaptureSuspended ? "▶" : "⏸";
    }

    private void OnCaptureSuspendedChanged(bool _) => Dispatcher.Invoke(UpdatePauseGlyph);

    private void Expand()
    {
        _collapseTimer?.Stop();
        _collapsedTab.Visibility = Visibility.Collapsed;
        _expandedBorder.Opacity = 1;
        _expandedBorder.IsHitTestVisible = true;
    }

    private void ScheduleCollapse()
    {
        _collapseTimer?.Stop();
        _collapseTimer = new DispatcherTimer { Interval = CollapseDelay };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer!.Stop();
            _expandedBorder.Opacity = 0;
            _expandedBorder.IsHitTestVisible = false;
            _collapsedTab.Visibility = Visibility.Visible;
        };
        _collapseTimer.Start();
    }
}
