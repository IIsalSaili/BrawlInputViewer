using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BrawlhallaOverlay;

public partial class MainWindow : Window
{
    // --- Win32 interop pour rendre la fenêtre "click-through" (les clics passent
    // au jeu en dessous) et invisible au alt-tab. ---
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // Le hook WH_KEYBOARD_LL rapporte parfois le code générique (0x11/0x12)
    // et parfois le code spécifique gauche/droite (0xA2-0xA5) selon le contexte
    // (observé en jeu plein écran : uniquement 0xA2/0xA4) — il faut couvrir les deux.
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_O = 0x4F;
    private const int VK_P = 0x50;

    private static bool IsCtrl(int vkCode) => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL;
    private static bool IsAlt(int vkCode) => vkCode is VK_MENU or VK_LMENU or VK_RMENU;

    private const int MaxHistoryEntries = 12;
    private static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(500);
    // Fenêtre pendant laquelle des touches enfoncées quasi en même temps sont
    // regroupées dans une seule ligne d'historique ("A + B") au lieu de deux.
    private static readonly TimeSpan ComboWindow = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan HistoryClearDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HistoryClearStep = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan HistoryClearStagger = TimeSpan.FromMilliseconds(40);

    private readonly List<KeyBind> _binds;
    private readonly Dictionary<int, KeyBind> _bindsByVk = new();
    private readonly Dictionary<int, List<SolidColorBrush>> _brushesByVk = new();
    private readonly Dictionary<SolidColorBrush, double> _restOpacityByBrush = new();

    // Flèches du mode 2 : au lieu de varier l'opacité, on change carrément la
    // couleur (bleu au repos → rouge quand la touche est appuyée) pour que ce
    // soit lisible d'un coup d'œil.
    private static readonly Color ArrowRestColor = (Color)ColorConverter.ConvertFromString("#3498DB");
    private static readonly Color ArrowPressedColor = (Color)ColorConverter.ConvertFromString("#E74C3C");
    private readonly Dictionary<int, List<SolidColorBrush>> _colorSwapByVk = new();
    private readonly HashSet<int> _pressedVks = new();
    private StackPanel _historyPanel = null!;
    private KeyboardHook? _hook;
    private IntPtr _hwnd;
    private DispatcherTimer? _historyClearTimer;
    private CancellationTokenSource? _historyClearCts;

    // Regroupement des touches pressées quasi simultanément (voir ComboWindow) avant
    // de les logguer comme une seule entrée combinée dans l'historique.
    private readonly List<KeyBind> _pendingBinds = new();
    private DispatcherTimer? _comboTimer;

    private bool _ctrlDown;
    private bool _altDown;
    private bool _locked = true;

    // Fusion des appuis répétés (spam) sur la même action/combo en une seule ligne d'historique.
    private List<KeyBind>? _lastLoggedBinds;
    private TextBlock? _lastLoggedText;
    private Border? _lastLoggedEntry;
    private int _lastLoggedCount;
    private DateTime _lastLoggedTime;

    // --- Deux modes d'affichage : le mode 1 (historique + cluster ZQSD compact,
    // en bas à gauche) et le mode 2 (grosses flèches collées aux bords de
    // l'écran + attaques au centre en haut, pensé pour être lisible d'un coup
    // d'œil). Ctrl+Alt+M bascule de l'un à l'autre. ---
    private Border _mode1Panel = null!;
    private Grid _mode2Layer = null!;
    private TextBlock _hintText = null!;
    private bool _mode2Active;
    private bool _mode1Moved;
    private Point? _dragStart;
    private double _canvasWidth;
    private double _canvasHeight;

    public MainWindow()
    {
        InitializeComponent();

        _binds = KeyBindConfig.LoadOrCreateDefault();
        foreach (var bind in _binds)
        {
            foreach (var vk in bind.VirtualKeyCodes)
            {
                _bindsByVk[vk] = bind;
            }
        }

        BuildLayout();

        Loaded += MainWindow_Loaded;
        Closed += (_, _) => _hook?.Dispose();
    }

    private void BuildLayout()
    {
        _hintText = new TextBlock
        {
            Text = "Ctrl+Alt+O : verrouiller / déverrouiller · Ctrl+Alt+P : changer de mode · glisser pour déplacer (mode 1)",
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
            Padding = new Thickness(6, 3, 6, 3),
            TextWrapping = TextWrapping.Wrap,
            Width = 280,
            Visibility = Visibility.Collapsed,
        };
        Canvas.SetLeft(_hintText, 10);
        Canvas.SetTop(_hintText, 10);
        RootCanvas.Children.Add(_hintText);

        _mode1Panel = BuildMode1Panel();
        Canvas.SetLeft(_mode1Panel, 24);
        Canvas.SetTop(_mode1Panel, 24);
        RootCanvas.Children.Add(_mode1Panel);

        _mode2Layer = BuildMode2Layer();
        _mode2Layer.Visibility = Visibility.Collapsed;
        Canvas.SetLeft(_mode2Layer, 0);
        Canvas.SetTop(_mode2Layer, 0);
        RootCanvas.Children.Add(_mode2Layer);
    }

    private Border BuildMode1Panel()
    {
        var mainRow = new StackPanel { Orientation = Orientation.Horizontal };
        mainRow.Children.Add(BuildMovementCluster());
        mainRow.Children.Add(BuildActionButtons());
        mainRow.Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(12, 2, 12, 2),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        });
        mainRow.Children.Add(BuildHistoryContainer());

        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Child = mainRow,
        };

        panel.MouseLeftButtonDown += Mode1Panel_MouseLeftButtonDown;
        panel.MouseMove += Mode1Panel_MouseMove;
        panel.MouseLeftButtonUp += Mode1Panel_MouseLeftButtonUp;
        panel.SizeChanged += (_, _) =>
        {
            if (!_mode1Moved) RepositionMode1Bottom();
        };

        return panel;
    }

    private UIElement BuildMovementCluster()
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        foreach (var bind in _binds)
        {
            if (bind.Group != "Movement") continue;

            var (row, col) = bind.Slot switch
            {
                "Up" => (0, 1),
                "Left" => (1, 0),
                "Down" => (1, 1),
                "Right" => (1, 2),
                _ => (0, 0),
            };

            var keycap = BuildKeycap(bind, width: 42, height: 42, fontSize: 15);
            Grid.SetRow(keycap, row);
            Grid.SetColumn(keycap, col);
            grid.Children.Add(keycap);
        }

        return grid;
    }

    private UIElement BuildActionButtons()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

        foreach (var bind in _binds)
        {
            if (bind.Group != "Action") continue;
            panel.Children.Add(BuildKeycap(bind, width: 130, height: 30, fontSize: 12));
        }

        return panel;
    }

    private void RegisterBrush(KeyBind bind, SolidColorBrush brush)
    {
        _restOpacityByBrush[brush] = brush.Opacity;
        foreach (var vk in bind.VirtualKeyCodes)
        {
            if (!_brushesByVk.TryGetValue(vk, out var list))
            {
                list = new List<SolidColorBrush>();
                _brushesByVk[vk] = list;
            }
            list.Add(brush);
        }
    }

    private void RegisterColorSwapBrush(KeyBind bind, SolidColorBrush brush)
    {
        foreach (var vk in bind.VirtualKeyCodes)
        {
            if (!_colorSwapByVk.TryGetValue(vk, out var list))
            {
                list = new List<SolidColorBrush>();
                _colorSwapByVk[vk] = list;
            }
            list.Add(brush);
        }
    }

    private Border BuildKeycap(KeyBind bind, double width, double height, double fontSize)
    {
        var color = (Color)ColorConverter.ConvertFromString(bind.Color);
        var brush = new SolidColorBrush(color) { Opacity = 0.25 };
        RegisterBrush(bind, brush);

        return new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(6),
            Width = width,
            Height = height,
            Margin = new Thickness(3),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = bind.Label,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private UIElement BuildHistoryContainer()
    {
        _historyPanel = new StackPanel { Orientation = Orientation.Vertical, Width = 200, VerticalAlignment = VerticalAlignment.Bottom };
        return _historyPanel;
    }

    // --- Mode 2 : grosses flèches collées aux bords de l'écran (gauche/droite/
    // haut/bas) + attaques légère/forte bien visibles au centre en haut. Pensé
    // pour être lu d'un coup d'œil, sans avoir à lire du texte. ---
    private Grid BuildMode2Layer()
    {
        var grid = new Grid();

        var left = _binds.First(b => b.Group == "Movement" && b.Slot == "Left");
        var right = _binds.First(b => b.Group == "Movement" && b.Slot == "Right");
        var up = _binds.First(b => b.Group == "Movement" && b.Slot == "Up");
        var down = _binds.First(b => b.Group == "Movement" && b.Slot == "Down");

        grid.Children.Add(BuildBigArrow(left, HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(180, 0, 0, 0)));
        grid.Children.Add(BuildBigArrow(right, HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, 180, 0)));
        grid.Children.Add(BuildBigArrow(up, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, 60, 0, 0), fontSize: 110));
        grid.Children.Add(BuildBigArrow(down, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, 160)));

        var attackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 190, 0, 0),
        };
        var light = _binds.First(b => b.Action == "Att. légère");
        var heavy = _binds.First(b => b.Action == "Att. forte");
        attackPanel.Children.Add(BuildBigKeycap(light, 170, 170, 58));
        attackPanel.Children.Add(BuildBigKeycap(heavy, 170, 170, 58));
        grid.Children.Add(attackPanel);

        var extrasPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 380, 0, 0),
        };
        foreach (var action in new[] { "Saut", "Esquive", "Lancer", "Taunt" })
        {
            var bind = _binds.FirstOrDefault(b => b.Action == action);
            if (bind is not null)
            {
                extrasPanel.Children.Add(BuildBigKeycap(bind, 70, 70, 30, showLabel: false));
            }
        }
        grid.Children.Add(extrasPanel);

        return grid;
    }

    private FrameworkElement BuildBigArrow(KeyBind bind, HorizontalAlignment h, VerticalAlignment v, Thickness margin, double fontSize = 150)
    {
        var badgeBrush = new SolidColorBrush(ArrowRestColor) { Opacity = 0.75 };
        RegisterColorSwapBrush(bind, badgeBrush);

        double size = fontSize * 1.55;

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = badgeBrush,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(3),
            HorizontalAlignment = h,
            VerticalAlignment = v,
            Margin = margin,
            Child = new TextBlock
            {
                Text = bind.Symbol,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private Border BuildBigKeycap(KeyBind bind, double width, double height, double fontSize, bool showLabel = true)
    {
        var color = (Color)ColorConverter.ConvertFromString(bind.Color);
        var brush = new SolidColorBrush(color) { Opacity = 0.25 };
        RegisterBrush(bind, brush);

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(new TextBlock
        {
            Text = bind.Symbol,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (showLabel)
        {
            content.Children.Add(new TextBlock
            {
                Text = bind.Action,
                FontSize = fontSize * 0.28,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        return new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(10),
            Width = width,
            Height = height,
            Margin = new Thickness(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(2),
            Child = content,
        };
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        ApplyClickThrough(_locked);

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;

        _canvasWidth = workArea.Width;
        _canvasHeight = workArea.Height;
        RootCanvas.Width = _canvasWidth;
        RootCanvas.Height = _canvasHeight;

        _mode2Layer.Width = _canvasWidth;
        _mode2Layer.Height = _canvasHeight;

        RepositionMode1Bottom();

        _historyClearTimer = new DispatcherTimer { Interval = HistoryClearDelay };
        _historyClearTimer.Tick += (_, _) =>
        {
            _historyClearTimer.Stop();
            _ = ClearHistoryGraduallyAsync();
        };

        _comboTimer = new DispatcherTimer { Interval = ComboWindow };
        _comboTimer.Tick += (_, _) =>
        {
            _comboTimer!.Stop();
            FlushPendingBinds();
        };

        _hook = new KeyboardHook();
        _hook.KeyDown += OnGlobalKeyDown;
        _hook.KeyUp += OnGlobalKeyUp;
        _hook.Start();
    }

    private void RepositionMode1Bottom()
    {
        Canvas.SetLeft(_mode1Panel, 24);
        Canvas.SetTop(_mode1Panel, _canvasHeight - _mode1Panel.ActualHeight - 24);
    }

    private void OnGlobalKeyDown(int vkCode)
    {
        ResetHistoryClearTimer();

        if (IsCtrl(vkCode)) _ctrlDown = true;
        if (IsAlt(vkCode)) _altDown = true;

        if (_ctrlDown && _altDown && vkCode == VK_O)
        {
            Dispatcher.Invoke(ToggleLock);
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_P)
        {
            Dispatcher.Invoke(ToggleMode2);
            return;
        }

        if (_brushesByVk.TryGetValue(vkCode, out var brushes))
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var brush in brushes) brush.Opacity = 1.0;
            });
        }

        if (_colorSwapByVk.TryGetValue(vkCode, out var swapBrushes))
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var brush in swapBrushes) brush.Color = ArrowPressedColor;
            });
        }

        // Ignore l'auto-répétition OS : un seul événement d'historique par appui,
        // pas une rafale tant que la touche reste enfoncée.
        if (_pressedVks.Add(vkCode) && _bindsByVk.TryGetValue(vkCode, out var bind))
        {
            Dispatcher.Invoke(() => QueuePendingBind(bind));
        }
    }

    // Ne loggue pas immédiatement : attend ComboWindow pour voir si une autre touche
    // est pressée quasi en même temps, afin de les afficher groupées ("A + B").
    private void QueuePendingBind(KeyBind bind)
    {
        if (!_pendingBinds.Contains(bind))
        {
            _pendingBinds.Add(bind);
        }

        _comboTimer?.Stop();
        _comboTimer?.Start();
    }

    private void FlushPendingBinds()
    {
        if (_pendingBinds.Count == 0) return;

        var binds = new List<KeyBind>(_pendingBinds);
        _pendingBinds.Clear();
        RegisterMove(binds);
    }

    private void OnGlobalKeyUp(int vkCode)
    {
        ResetHistoryClearTimer();

        if (IsCtrl(vkCode)) _ctrlDown = false;
        if (IsAlt(vkCode)) _altDown = false;

        _pressedVks.Remove(vkCode);

        if (_brushesByVk.TryGetValue(vkCode, out var brushes))
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var brush in brushes) brush.Opacity = _restOpacityByBrush[brush];
            });
        }

        if (_colorSwapByVk.TryGetValue(vkCode, out var swapBrushes))
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var brush in swapBrushes) brush.Color = ArrowRestColor;
            });
        }
    }

    private void RegisterMove(List<KeyBind> binds)
    {
        var now = DateTime.UtcNow;

        // Spam de la même action/combo (ex: attaque légère martelée) : on ne veut
        // qu'une seule ligne dans l'historique, avec un compteur, puisque seul le
        // premier appui compte vraiment dans le combo.
        if (_lastLoggedBinds is not null && BindsEqual(_lastLoggedBinds, binds) && _lastLoggedText is not null && (now - _lastLoggedTime) <= MergeWindow)
        {
            _lastLoggedCount++;
            _lastLoggedText.Text = $"{HistoryText(binds)} ×{_lastLoggedCount}";
            Pulse(_lastLoggedEntry!);
            _lastLoggedTime = now;
            return;
        }

        var (entry, text) = CreateHistoryEntry(binds);
        _historyPanel.Children.Add(entry);
        entry.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));

        while (_historyPanel.Children.Count > MaxHistoryEntries)
        {
            _historyPanel.Children.RemoveAt(0);
        }

        _lastLoggedBinds = binds;
        _lastLoggedEntry = entry;
        _lastLoggedText = text;
        _lastLoggedCount = 1;
        _lastLoggedTime = now;
    }

    private static bool BindsEqual(List<KeyBind> a, List<KeyBind> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    private static (Border entry, TextBlock text) CreateHistoryEntry(List<KeyBind> binds)
    {
        var color = (Color)ColorConverter.ConvertFromString(binds[0].Color);

        var text = new TextBlock
        {
            Text = HistoryText(binds),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        };

        var entry = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(8, 4, 8, 4),
            Opacity = 0,
            Child = text,
        };

        return (entry, text);
    }

    private static string HistoryText(List<KeyBind> binds) =>
        string.Join(" + ", binds.Select(HistoryTextSingle));

    private static string HistoryTextSingle(KeyBind bind) =>
        string.IsNullOrEmpty(bind.Symbol) ? bind.Action : $"{bind.Symbol} ({bind.Action})";

    private static void Pulse(Border entry)
    {
        var flash = new DoubleAnimation(1.0, 0.55, TimeSpan.FromMilliseconds(80))
        {
            AutoReverse = true,
        };
        entry.BeginAnimation(OpacityProperty, flash);
    }

    private void ResetHistoryClearTimer()
    {
        _historyClearTimer?.Stop();
        _historyClearCts?.Cancel();
        _historyClearTimer?.Start();
    }

    private async Task ClearHistoryGraduallyAsync()
    {
        _historyClearCts?.Cancel();
        var cts = new CancellationTokenSource();
        _historyClearCts = cts;

        try
        {
            while (_historyPanel.Children.Count > 0)
            {
                cts.Token.ThrowIfCancellationRequested();

                if (_historyPanel.Children[0] is Border entry)
                {
                    var fade = new DoubleAnimation(1.0, 0.0, HistoryClearStep);
                    entry.BeginAnimation(OpacityProperty, fade);
                    await Task.Delay(HistoryClearStep + HistoryClearStagger, cts.Token);
                }
                else
                {
                    await Task.Delay(HistoryClearStagger, cts.Token);
                }

                if (cts.Token.IsCancellationRequested)
                    break;

                if (_historyPanel.Children.Count > 0)
                    _historyPanel.Children.RemoveAt(0);
            }
        }
        catch (OperationCanceledException)
        {
            // Le timer a été réinitialisé par une nouvelle entrée : ne pas toucher
            // à _lastLoggedBinds/Entry/Text, RegisterMove vient de les fixer pour
            // cette nouvelle touche (sinon son mash-merge serait cassé).
            return;
        }
        finally
        {
            if (_historyClearCts == cts)
                _historyClearCts = null;
        }

        _lastLoggedBinds = null;
        _lastLoggedEntry = null;
        _lastLoggedText = null;
        _lastLoggedCount = 0;
    }

    private void ToggleLock()
    {
        _locked = !_locked;
        ApplyClickThrough(_locked);
        _hintText.Visibility = _locked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ToggleMode2()
    {
        _mode2Active = !_mode2Active;
        _mode1Panel.Visibility = _mode2Active ? Visibility.Collapsed : Visibility.Visible;
        _mode2Layer.Visibility = _mode2Active ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyClickThrough(bool clickThrough)
    {
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
        exStyle = clickThrough ? (exStyle | WS_EX_TRANSPARENT) : (exStyle & ~WS_EX_TRANSPARENT);
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
    }

    private void Mode1Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_locked) return;
        _dragStart = e.GetPosition(RootCanvas);
        _mode1Panel.CaptureMouse();
    }

    private void Mode1Panel_MouseMove(object sender, MouseEventArgs e)
    {
        if (_locked || _dragStart is null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(RootCanvas);
        var delta = pos - _dragStart.Value;
        Canvas.SetLeft(_mode1Panel, Canvas.GetLeft(_mode1Panel) + delta.X);
        Canvas.SetTop(_mode1Panel, Canvas.GetTop(_mode1Panel) + delta.Y);
        _dragStart = pos;
        _mode1Moved = true;
    }

    private void Mode1Panel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _mode1Panel.ReleaseMouseCapture();
    }
}
