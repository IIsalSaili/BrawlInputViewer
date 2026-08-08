using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BrawlhallaOverlay;

/// <summary>
/// Fenêtre plein écran (sur le moniteur ciblé par Settings.MonitorIndex) qui laisse
/// l'utilisateur dessiner au clic-glisser un rectangle autour de la zone de dégâts adverse
/// du HUD Brawlhalla. Le rectangle choisi est
/// converti en pixels physiques d'écran (coordonnées System.Windows.Forms.Screen) et exposé
/// via <see cref="Result"/> pour que l'appelant l'écrive dans OverlaySettings.
/// </summary>
public partial class HudCalibrationWindow : Window
{
    public (int X, int Y, int Width, int Height)? Result { get; private set; }

    private readonly double _scaleX;
    private readonly double _scaleY;
    private readonly System.Drawing.Rectangle _screenBounds;

    private readonly string _instructions;
    private TextBlock _hint = null!;
    private Rectangle _selection = null!;
    private Point? _dragStart;
    private bool _hasSelection;

    public HudCalibrationWindow(string? instructions = null)
    {
        _instructions = instructions ?? "Dessine un rectangle autour de la zone de dégâts de l'ADVERSAIRE dans le HUD (clic-glisse). Entrée pour valider, Échap pour annuler.";
        InitializeComponent();

        var index = AppState.Settings.MonitorIndex;
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen = index >= 0 && index < screens.Length ? screens[index] : System.Windows.Forms.Screen.PrimaryScreen!;
        _screenBounds = screen.Bounds;

        var primary = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        _scaleX = SystemParameters.PrimaryScreenWidth / primary.Width;
        _scaleY = SystemParameters.PrimaryScreenHeight / primary.Height;

        Left = _screenBounds.Left * _scaleX;
        Top = _screenBounds.Top * _scaleY;
        Width = _screenBounds.Width * _scaleX;
        Height = _screenBounds.Height * _scaleY;

        Build();
    }

    private void Build()
    {
        _hint = new TextBlock
        {
            Text = _instructions,
            Foreground = Theme.TextPrimary,
            // Alpha plus opaque que Theme.OverlayPanelBg : ce texte doit rester lisible en plein
            // écran par-dessus n'importe quel fond de jeu pendant le calibrage, reste local.
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x18, 0x17, 0x22)),
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
        };
        Canvas.SetLeft(_hint, 24);
        Canvas.SetTop(_hint, 24);
        RootCanvas.Children.Add(_hint);

        _selection = new Rectangle
        {
            Stroke = Theme.AccentGold,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xE8, 0xC4, 0x4A)),
            Visibility = Visibility.Collapsed,
        };
        RootCanvas.Children.Add(_selection);

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += OnKeyDown;

        Loaded += (_, _) => Activate();
        Focusable = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(RootCanvas);
        _selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(_selection, _dragStart.Value.X);
        Canvas.SetTop(_selection, _dragStart.Value.Y);
        _selection.Width = 0;
        _selection.Height = 0;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is null) return;

        var current = e.GetPosition(RootCanvas);
        var left = Math.Min(_dragStart.Value.X, current.X);
        var top = Math.Min(_dragStart.Value.Y, current.Y);
        var width = Math.Abs(current.X - _dragStart.Value.X);
        var height = Math.Abs(current.Y - _dragStart.Value.Y);

        Canvas.SetLeft(_selection, left);
        Canvas.SetTop(_selection, top);
        _selection.Width = width;
        _selection.Height = height;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null) return;
        _dragStart = null;
        _hasSelection = _selection.Width > 4 && _selection.Height > 4;
        _hint.Text = _hasSelection
            ? "Entrée pour valider cette zone, Échap pour annuler, ou redessine un nouveau rectangle."
            : "Zone trop petite — redessine un rectangle plus grand.";
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Result = null;
            Close();
            return;
        }

        if (e.Key == Key.Enter && _hasSelection)
        {
            var left = Canvas.GetLeft(_selection);
            var top = Canvas.GetTop(_selection);
            var physicalX = _screenBounds.Left + (int)Math.Round(left / _scaleX);
            var physicalY = _screenBounds.Top + (int)Math.Round(top / _scaleY);
            var physicalW = (int)Math.Round(_selection.Width / _scaleX);
            var physicalH = (int)Math.Round(_selection.Height / _scaleY);
            Result = (physicalX, physicalY, physicalW, physicalH);
            Close();
        }
    }
}
