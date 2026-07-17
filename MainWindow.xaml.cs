using System;
using System.Collections.Generic;
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

    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_L = 0x4C;

    private const int MaxHistoryEntries = 12;
    private static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HistoryClearDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HistoryClearStep = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan HistoryClearStagger = TimeSpan.FromMilliseconds(40);

    private readonly List<KeyBind> _binds;
    private readonly Dictionary<int, KeyBind> _bindsByVk = new();
    private readonly Dictionary<int, SolidColorBrush> _brushesByVk = new();
    private readonly HashSet<int> _pressedVks = new();
    private StackPanel _historyPanel = null!;
    private KeyboardHook? _hook;
    private IntPtr _hwnd;
    private DispatcherTimer? _historyClearTimer;
    private CancellationTokenSource? _historyClearCts;

    private bool _ctrlDown;
    private bool _altDown;
    private bool _locked = true;

    // Fusion des appuis répétés (spam) sur la même action en une seule ligne d'historique.
    private KeyBind? _lastLoggedBind;
    private TextBlock? _lastLoggedText;
    private Border? _lastLoggedEntry;
    private int _lastLoggedCount;
    private DateTime _lastLoggedTime;

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
        SizeChanged += (_, _) => RepositionBottomLeft();
        Closed += (_, _) => _hook?.Dispose();
    }

    private void BuildLayout()
    {
        MainRow.Children.Add(BuildMovementCluster());
        MainRow.Children.Add(BuildActionButtons());
        MainRow.Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(12, 2, 12, 2),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        });
        MainRow.Children.Add(BuildHistoryContainer());
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

    private Border BuildKeycap(KeyBind bind, double width, double height, double fontSize)
    {
        var color = (Color)ColorConverter.ConvertFromString(bind.Color);
        var brush = new SolidColorBrush(color) { Opacity = 0.25 };
        foreach (var vk in bind.VirtualKeyCodes)
        {
            _brushesByVk[vk] = brush;
        }

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

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        ApplyClickThrough(_locked);

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + 24;
        RepositionBottomLeft();

        _historyClearTimer = new DispatcherTimer { Interval = HistoryClearDelay };
        _historyClearTimer.Tick += (_, _) =>
        {
            _historyClearTimer.Stop();
            _ = ClearHistoryGraduallyAsync();
        };

        _hook = new KeyboardHook();
        _hook.KeyDown += OnGlobalKeyDown;
        _hook.KeyUp += OnGlobalKeyUp;
        _hook.Start();
    }

    private void RepositionBottomLeft()
    {
        var workArea = SystemParameters.WorkArea;
        Top = workArea.Bottom - ActualHeight - 24;
    }

    private void OnGlobalKeyDown(int vkCode)
    {
        ResetHistoryClearTimer();

        if (vkCode == VK_CONTROL) _ctrlDown = true;
        if (vkCode == VK_MENU) _altDown = true;

        if (_ctrlDown && _altDown && vkCode == VK_L)
        {
            Dispatcher.Invoke(ToggleLock);
            return;
        }

        if (_brushesByVk.TryGetValue(vkCode, out var brush))
        {
            Dispatcher.Invoke(() => brush.Opacity = 1.0);
        }

        // Ignore l'auto-répétition OS : un seul événement d'historique par appui,
        // pas une rafale tant que la touche reste enfoncée.
        if (_pressedVks.Add(vkCode) && _bindsByVk.TryGetValue(vkCode, out var bind))
        {
            Dispatcher.Invoke(() => RegisterMove(bind));
        }
    }

    private void OnGlobalKeyUp(int vkCode)
    {
        ResetHistoryClearTimer();

        if (vkCode == VK_CONTROL) _ctrlDown = false;
        if (vkCode == VK_MENU) _altDown = false;

        _pressedVks.Remove(vkCode);

        if (_brushesByVk.TryGetValue(vkCode, out var brush))
        {
            Dispatcher.Invoke(() => brush.Opacity = 0.25);
        }
    }

    private void RegisterMove(KeyBind bind)
    {
        var now = DateTime.UtcNow;

        // Spam de la même action (ex: attaque légère martelée) : on ne veut qu'une
        // seule ligne dans l'historique, avec un compteur, puisque seul le premier
        // appui compte vraiment dans le combo.
        if (bind == _lastLoggedBind && _lastLoggedText is not null && (now - _lastLoggedTime) <= MergeWindow)
        {
            _lastLoggedCount++;
            _lastLoggedText.Text = $"{HistoryText(bind)} ×{_lastLoggedCount}";
            Pulse(_lastLoggedEntry!);
            _lastLoggedTime = now;
            return;
        }

        var (entry, text) = CreateHistoryEntry(bind);
        _historyPanel.Children.Add(entry);
        entry.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));

        while (_historyPanel.Children.Count > MaxHistoryEntries)
        {
            _historyPanel.Children.RemoveAt(0);
        }

        _lastLoggedBind = bind;
        _lastLoggedEntry = entry;
        _lastLoggedText = text;
        _lastLoggedCount = 1;
        _lastLoggedTime = now;
    }

    private static (Border entry, TextBlock text) CreateHistoryEntry(KeyBind bind)
    {
        var color = (Color)ColorConverter.ConvertFromString(bind.Color);

        var text = new TextBlock
        {
            Text = HistoryText(bind),
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

    private static string HistoryText(KeyBind bind) =>
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
            // à _lastLoggedBind/Entry/Text, RegisterMove vient de les fixer pour
            // cette nouvelle touche (sinon son mash-merge serait cassé).
            return;
        }
        finally
        {
            if (_historyClearCts == cts)
                _historyClearCts = null;
        }

        _lastLoggedBind = null;
        _lastLoggedEntry = null;
        _lastLoggedText = null;
        _lastLoggedCount = 0;
    }

    private void ToggleLock()
    {
        _locked = !_locked;
        ApplyClickThrough(_locked);
        HintText.Visibility = _locked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyClickThrough(bool clickThrough)
    {
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
        exStyle = clickThrough ? (exStyle | WS_EX_TRANSPARENT) : (exStyle & ~WS_EX_TRANSPARENT);
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_locked)
        {
            DragMove();
        }
    }
}
