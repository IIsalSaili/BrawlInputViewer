using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private const int VK_R = 0x52;
    private const int VK_K = 0x4B;
    private const int VK_U = 0x55;
    private const int VK_I = 0x49;

    private static bool IsCtrl(int vkCode) => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL;
    private static bool IsAlt(int vkCode) => vkCode is VK_MENU or VK_LMENU or VK_RMENU;

    private static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(500);
    // Fenêtre pendant laquelle des touches enfoncées quasi en même temps sont
    // regroupées dans un seul "coup joué" ("A + B") au lieu de deux. Reste courte
    // exprès : le cas "direction tenue depuis un moment puis attaque pressée plus
    // tard" est déjà géré par QueuePendingBind qui relit l'état *actuellement*
    // enfoncé (_pressedVks) à chaque nouvel appui, donc il n'a pas besoin d'une
    // fenêtre large pour ça. Une fenêtre large ici casse le mash (appuis répétés
    // rapprochés sur l'attaque) : chaque nouvel appui relance le timer, donc un
    // mash plus rapide que la fenêtre l'empêche indéfiniment de se déclencher et
    // plus aucun coup n'est jamais transmis au ComboRunner.
    private static readonly TimeSpan ComboWindow = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan HistoryClearDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HistoryClearStep = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan HistoryClearStagger = TimeSpan.FromMilliseconds(40);

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
    private IntPtr _hwnd;
    private DispatcherTimer? _historyClearTimer;
    private CancellationTokenSource? _historyClearCts;

    // Regroupement des touches pressées quasi simultanément (voir ComboWindow) avant
    // de les logguer comme une seule entrée combinée dans l'historique.
    private readonly List<KeyBind> _pendingBinds = new();
    private DispatcherTimer? _comboTimer;

    private bool _ctrlDown;
    private bool _altDown;

    // Fusion des appuis répétés (spam) sur la même action/combo en une seule ligne d'historique.
    private List<KeyBind>? _lastLoggedBinds;
    private TextBlock? _lastLoggedText;
    private Border? _lastLoggedEntry;
    private int _lastLoggedCount;
    private DateTime _lastLoggedTime;

    // --- Trois modes d'affichage (voir AppState.ActiveMode) : historique+cluster,
    // grandes flèches, tutoriel de combos. Ctrl+Alt+P cycle parmi les favoris. ---
    private Border _mode1Panel = null!;
    private Grid _mode2Layer = null!;
    private Border _mode3Panel = null!;
    private TextBlock _hintText = null!;
    private Point? _dragStart;
    private double _canvasWidth;
    private double _canvasHeight;

    // --- Mode 3 : Tutoriel de combos ---
    private ComboRunner? _comboRunner;
    private StackPanel _comboStepsPanel = null!;
    private TextBlock _comboNameText = null!;
    private TextBlock _comboStreakText = null!;
    private ContentControl _historySlotMode1 = null!;
    private ContentControl _historySlotMode3 = null!;
    private readonly Dictionary<int, TextBlock> _pillTextsByIndex = new();
    private readonly Dictionary<int, string> _pillSymbolsByIndex = new();
    private readonly Dictionary<int, ProgressBar> _pillBarsByIndex = new();
    private readonly Dictionary<int, TextBlock> _pillCountdownsByIndex = new();
    private bool _quizRevealed;
    private DispatcherTimer? _quizRevealTimer;
    private DispatcherTimer? _chainComboTimer;
    private DispatcherTimer? _toleranceCountdownTimer;
    private DispatcherTimer? _comboCompletedResetTimer;

    // --- Enregistrement de combo (Ctrl+Alt+R) ---
    private readonly List<(List<KeyBind> Binds, DateTime Time)> _recordedMoves = new();

    // --- Badge de changement de mode (auto-fade) ---
    private TextBlock _modeBadge = null!;
    private DispatcherTimer? _modeBadgeTimer;

    // --- Icône dans la zone de notification (tray) ---
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    // --- Panneau de contrôle (fenêtre séparée, ouverte à la demande) ---
    private ControlPanelWindow? _controlPanel;

    public MainWindow()
    {
        InitializeComponent();

        RebuildBindMaps();
        BuildLayout();

        AppState.LockChanged += OnLockChanged;
        AppState.ModeChanged += OnModeChanged;
        AppState.BindsChanged += OnBindsChanged;
        AppState.CombosChanged += OnCombosOrActiveComboChanged;
        AppState.ActiveComboChanged += _ => OnCombosOrActiveComboChanged();
        AppState.SettingsChanged += OnSettingsChanged;
        AppState.RecordingChanged += OnRecordingChanged;

        Loaded += MainWindow_Loaded;
        Closed += (_, _) =>
        {
            AppState.Hook.KeyDown -= OnGlobalKeyDown;
            AppState.Hook.KeyUp -= OnGlobalKeyUp;
            AppState.Hook.Dispose();
            AppState.Gamepad.ButtonDown -= OnGlobalKeyDown;
            AppState.Gamepad.ButtonUp -= OnGlobalKeyUp;
            AppState.Gamepad.Dispose();
            AppState.QuizRevealRequested -= RevealQuizStepsTemporarily;
            if (_trayIcon is not null) _trayIcon.Visible = false;
            _trayIcon?.Dispose();
            _controlPanel?.Close();
        };
    }

    private void RebuildBindMaps()
    {
        _bindsByVk.Clear();
        foreach (var bind in AppState.Binds)
        {
            foreach (var vk in bind.VirtualKeyCodes)
            {
                _bindsByVk[vk] = bind;
            }
        }
    }

    private void BuildLayout()
    {
        _brushesByVk.Clear();
        _restOpacityByBrush.Clear();
        _colorSwapByVk.Clear();
        RootCanvas.Children.Clear();

        _hintText = new TextBlock
        {
            Text = "Ctrl+Alt+O : verrouiller/déverrouiller · Ctrl+Alt+P : changer de mode · Ctrl+Alt+K : changer de combo · Ctrl+Alt+R : enregistrer une combo · Ctrl+Alt+U : panneau de contrôle · Ctrl+Alt+I : révéler la combo (mode révision) · glisser pour déplacer (mode 1)",
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

        _mode3Panel = BuildMode3Panel();
        _mode3Panel.Visibility = Visibility.Collapsed;
        Canvas.SetLeft(_mode3Panel, 24);
        Canvas.SetTop(_mode3Panel, 24);
        RootCanvas.Children.Add(_mode3Panel);

        _modeBadge = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)),
            Padding = new Thickness(10, 5, 10, 5),
            Opacity = 0,
        };
        Canvas.SetTop(_modeBadge, 10);
        RootCanvas.Children.Add(_modeBadge);

        ApplyScale();
        SetActiveComboRunner();
        ApplyModeVisuals(AppState.ActiveMode, showBadge: false);
        ApplyLockVisuals(AppState.Locked);
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
            if (AppState.Settings.Position != OverlayPosition.Free) RepositionPanel(panel);
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

        foreach (var bind in AppState.Binds)
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

        foreach (var bind in AppState.Binds)
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
        _historySlotMode1 = new ContentControl { Content = _historyPanel };
        return _historySlotMode1;
    }

    // --- Mode 3 : Tutoriel de combos, en gros bandeau centré en haut de l'écran
    // (façon "notation de combo" des jeux de baston) : contrairement au mode 1,
    // ce panneau ignore le réglage de position général et reste toujours ancré
    // en haut, pour rester lisible d'un coup d'œil pendant l'action. ---
    private Border BuildMode3Panel()
    {
        _comboNameText = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };

        _comboStreakText = new TextBlock
        {
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        _comboStepsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        _historySlotMode3 = new ContentControl();

        var content = new StackPanel { Orientation = Orientation.Vertical };
        content.Children.Add(_comboNameText);
        content.Children.Add(_comboStepsPanel);
        content.Children.Add(_comboStreakText);
        content.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 12, 0, 12),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        });
        content.Children.Add(_historySlotMode3);

        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x77, 0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24, 18, 24, 18),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Child = content,
        };

        panel.SizeChanged += (_, _) =>
        {
            if (AppState.ActiveMode == 2) RepositionTopCenter(panel);
        };

        return panel;
    }

    private void RenderComboSteps()
    {
        _comboStepsPanel.Children.Clear();
        _pillTextsByIndex.Clear();
        _pillSymbolsByIndex.Clear();
        _pillBarsByIndex.Clear();
        _pillCountdownsByIndex.Clear();
        _quizRevealed = false;
        _quizRevealTimer?.Stop();
        _toleranceCountdownTimer?.Stop();

        var combos = AppState.Combos;
        var activeIndex = AppState.ActiveComboIndex;

        if (activeIndex < 0 || combos.Count == 0)
        {
            _comboNameText.Text = "Aucune combo — crée-en une (Ctrl+Alt+R ou le panneau de contrôle)";
            _comboStreakText.Text = "";
            return;
        }

        var combo = combos[activeIndex];
        var (filteredPos, filteredCount) = AppState.ActiveComboFilteredPosition();
        var weaponTag = string.IsNullOrEmpty(combo.Weapon) ? "" : $"[{combo.Weapon}] ";
        _comboNameText.Text = $"{weaponTag}{combo.Name}  ({filteredPos + 1}/{filteredCount} · Ctrl+Alt+K pour changer)";
        _comboStreakText.Text = $"Série réussie : {_comboRunner?.Streak ?? 0}";

        for (int i = 0; i < combo.Steps.Count; i++)
        {
            if (i > 0)
            {
                _comboStepsPanel.Children.Add(new TextBlock
                {
                    Text = "→",
                    Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                    FontSize = 30,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0),
                });
            }

            var step = combo.Steps[i];
            var symbol = string.Join("", step.RequiredActions.Select(a =>
                AppState.Binds.FirstOrDefault(b => b.Action == a)?.Symbol ?? "?"));
            _pillSymbolsByIndex[i] = symbol;

            var symbolText = new TextBlock
            {
                Text = symbol,
                FontSize = 34,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _pillTextsByIndex[i] = symbolText;

            var pill = new Border
            {
                Width = 82,
                Height = 82,
                CornerRadius = new CornerRadius(41),
                Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(2),
                Child = symbolText,
                Opacity = i == 0 ? 1.0 : 0.4,
                Tag = i,
            };

            // Barre fine sous la pastille : visible seulement pour l'étape courante
            // quand elle a une fenêtre de tolérance (pas la 1ère étape), se vide au
            // fil du temps restant avant que l'input soit jugé "trop lent".
            var bar = new ProgressBar
            {
                Width = 82,
                Height = 4,
                Margin = new Thickness(0, 4, 0, 0),
                Minimum = 0,
                Visibility = Visibility.Collapsed,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xD0, 0x3F)),
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0),
            };
            _pillBarsByIndex[i] = bar;

            var countdown = new TextBlock
            {
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            _pillCountdownsByIndex[i] = countdown;

            var stepColumn = new StackPanel { Orientation = Orientation.Vertical };
            stepColumn.Children.Add(pill);
            stepColumn.Children.Add(bar);
            stepColumn.Children.Add(countdown);
            _comboStepsPanel.Children.Add(stepColumn);
        }

        UpdateComboStepVisuals();
    }

    private Border? PillAt(int index)
    {
        foreach (var child in _comboStepsPanel.Children)
        {
            if (child is not StackPanel column) continue;
            if (column.Children.Count > 0 && column.Children[0] is Border b && b.Tag is int i && i == index) return b;
        }
        return null;
    }

    private void ApplyQuizMask()
    {
        if (_comboRunner is null) return;

        foreach (var (index, text) in _pillTextsByIndex)
        {
            var hidden = AppState.Settings.QuizMode && !_quizRevealed && index >= _comboRunner.CurrentStepIndex;
            text.Text = hidden ? "?" : _pillSymbolsByIndex.GetValueOrDefault(index, "?");
        }
    }

    private void RevealQuizStepsTemporarily()
    {
        if (!AppState.Settings.QuizMode || _comboRunner is null) return;

        _quizRevealed = true;
        ApplyQuizMask();
        ShowModeBadge("Combo révélée (3s)");

        _quizRevealTimer?.Stop();
        _quizRevealTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _quizRevealTimer.Tick += (_, _) =>
        {
            _quizRevealTimer!.Stop();
            _quizRevealed = false;
            ApplyQuizMask();
        };
        _quizRevealTimer.Start();
    }

    private void UpdateComboStepVisuals()
    {
        if (_comboRunner is null) return;

        foreach (var child in _comboStepsPanel.Children)
        {
            if (child is not StackPanel column) continue;
            if (column.Children.Count == 0 || column.Children[0] is not Border pill || pill.Tag is not int index) continue;

            if (index < _comboRunner.CurrentStepIndex)
            {
                pill.Background = new SolidColorBrush(Color.FromArgb(0xAA, 0x2E, 0xCC, 0x71)); // vert
                pill.Opacity = 1.0;
            }
            else if (index == _comboRunner.CurrentStepIndex)
            {
                pill.Background = new SolidColorBrush(Color.FromArgb(0xAA, 0xF4, 0xD0, 0x3F)); // jaune
                pill.Opacity = 1.0;
            }
            else
            {
                pill.Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                pill.Opacity = 0.4;
            }
        }

        ApplyQuizMask();
    }

    private void HideAllToleranceBars()
    {
        _toleranceCountdownTimer?.Stop();
        foreach (var bar in _pillBarsByIndex.Values)
        {
            bar.BeginAnimation(RangeBase.ValueProperty, null);
            bar.Visibility = Visibility.Collapsed;
        }
        foreach (var countdown in _pillCountdownsByIndex.Values)
        {
            countdown.Visibility = Visibility.Collapsed;
        }
    }

    // Démarre le compte à rebours visuel (barre + texte en secondes) de la
    // fenêtre de tolérance pour l'étape qui vient de devenir courante (pas
    // d'effet sur la 1ère étape, qui n'a pas de délai à respecter — même
    // règle que ComboRunner.Feed). Le texte lit la valeur *animée* de la
    // barre à intervalle régulier plutôt que de dupliquer le calcul de temps
    // écoulé, pour rester strictement synchronisé avec ce que l'œil voit.
    private void StartToleranceBar(int newCurrentIndex)
    {
        HideAllToleranceBars();
        if (_comboRunner is null || newCurrentIndex <= 0 || newCurrentIndex >= _comboRunner.Combo.Steps.Count) return;
        if (!_pillBarsByIndex.TryGetValue(newCurrentIndex, out var bar)) return;
        if (!_pillCountdownsByIndex.TryGetValue(newCurrentIndex, out var countdown)) return;

        var step = _comboRunner.Combo.Steps[newCurrentIndex];
        var maxDelay = step.MaxDelayMs ?? _comboRunner.Combo.DefaultToleranceMs;
        if (maxDelay <= 0) return;

        bar.Maximum = maxDelay;
        bar.Value = maxDelay;
        bar.Visibility = Visibility.Visible;
        bar.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(maxDelay, 0, TimeSpan.FromMilliseconds(maxDelay)));

        countdown.Visibility = Visibility.Visible;
        countdown.Text = $"{maxDelay / 1000.0:0.0}s";

        _toleranceCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _toleranceCountdownTimer.Tick += (_, _) =>
        {
            var remaining = Math.Max(0, bar.Value);
            countdown.Text = $"{remaining / 1000.0:0.0}s";
            if (remaining <= 0) _toleranceCountdownTimer!.Stop();
        };
        _toleranceCountdownTimer.Start();
    }

    private void FlashComboStepSuccess(int index)
    {
        var pill = PillAt(index);
        if (pill is null) return;

        pill.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
        var flash = new DoubleAnimation(1.0, 0.4, TimeSpan.FromMilliseconds(120)) { AutoReverse = true };
        pill.BeginAnimation(OpacityProperty, flash);
    }

    private static readonly TimeSpan FailBlinkStep = TimeSpan.FromMilliseconds(110);
    private const int FailBlinkCount = 3;

    // Sur un échec : toute la rangée clignote puis se réinitialise. Couleur
    // différente selon la cause (mauvaise touche = rouge, trop lent = orange)
    // pour un diagnostic immédiat sans devoir recouper avec l'historique.
    private void FlashAllStepsRed(ComboFailReason reason)
    {
        HideAllToleranceBars();

        var color = reason == ComboFailReason.Timeout
            ? Color.FromRgb(0xF3, 0x9C, 0x12) // orange : trop lent
            : Color.FromRgb(0xE7, 0x4C, 0x3C); // rouge : mauvaise touche
        var brush = new SolidColorBrush(color);

        foreach (var child in _comboStepsPanel.Children)
        {
            if (child is not StackPanel column || column.Children.Count == 0 || column.Children[0] is not Border pill) continue;

            pill.Background = brush;
            pill.Opacity = 1.0;

            var blink = new DoubleAnimation(1.0, 0.15, FailBlinkStep)
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(FailBlinkCount),
            };
            pill.BeginAnimation(OpacityProperty, blink);
        }

        var resetDelay = TimeSpan.FromMilliseconds(FailBlinkStep.TotalMilliseconds * 2 * FailBlinkCount + 40);
        var resetTimer = new DispatcherTimer { Interval = resetDelay };
        resetTimer.Tick += (_, _) =>
        {
            resetTimer.Stop();
            UpdateComboStepVisuals();
        };
        resetTimer.Start();
    }

    private void SetActiveComboRunner()
    {
        _chainComboTimer?.Stop();

        if (_comboRunner is not null)
        {
            _comboRunner.StepSucceeded -= OnComboStepSucceeded;
            _comboRunner.StepFailed -= OnComboStepFailed;
            _comboRunner.ComboCompleted -= OnComboCompleted;
            _comboRunner.ComboReset -= OnComboReset;
        }

        var index = AppState.ActiveComboIndex;
        var combos = AppState.Combos;
        _comboRunner = index >= 0 && index < combos.Count
            ? new ComboRunner(combos[index], AppState.Settings.KeepStreakOnFail)
            : null;

        if (_comboRunner is not null)
        {
            _comboRunner.StepSucceeded += OnComboStepSucceeded;
            _comboRunner.StepFailed += OnComboStepFailed;
            _comboRunner.ComboCompleted += OnComboCompleted;
            _comboRunner.ComboReset += OnComboReset;
        }

        RenderComboSteps();
    }

    private void OnCombosOrActiveComboChanged()
    {
        Dispatcher.Invoke(SetActiveComboRunner);
    }

    private void OnComboStepSucceeded(int index)
    {
        Dispatcher.Invoke(() =>
        {
            if (AppState.Settings.SoundEnabled) SystemSounds.Asterisk.Play();

            var actions = _comboRunner?.Combo.Steps[index].RequiredActions ?? new List<string>();
            AppState.RecordStepResult(actions, success: true);

            FlashComboStepSuccess(index);
            UpdateComboStepVisuals();
            StartToleranceBar(index + 1);
        });
    }

    private void OnComboStepFailed(int index, ComboFailReason reason)
    {
        Dispatcher.Invoke(() =>
        {
            if (AppState.Settings.SoundEnabled) SystemSounds.Hand.Play();

            var actions = _comboRunner?.Combo.Steps[index].RequiredActions ?? new List<string>();
            AppState.RecordStepResult(actions, success: false);

            FlashAllStepsRed(reason);
        });
    }

    private void OnComboCompleted()
    {
        Dispatcher.Invoke(() =>
        {
            if (AppState.Settings.SoundEnabled) SystemSounds.Exclamation.Play();

            HideAllToleranceBars();
            var streak = _comboRunner?.Streak ?? 0;
            _comboStreakText.Text = $"Série réussie : {streak}";
            UpdateComboStepVisuals();

            // Reste tout vert 1s après une réussite (ComboRunner.Feed remet déjà
            // CurrentStepIndex à 0 en interne juste après avoir levé cet event, donc
            // rien ne raffiche l'état "en attente" tant qu'aucun nouvel input n'arrive) :
            // si on rejoue la 1ère étape entre-temps, StepSucceeded rafraîchit l'affichage
            // immédiatement de toute façon, ce timer sert juste de filet en cas d'inaction.
            _comboCompletedResetTimer?.Stop();
            _comboCompletedResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _comboCompletedResetTimer.Tick += (_, _) =>
            {
                _comboCompletedResetTimer!.Stop();
                UpdateComboStepVisuals();
            };
            _comboCompletedResetTimer.Start();

            // Historique de performance persisté par combo (survit au redémarrage,
            // contrairement à Streak qui vit dans le ComboRunner en mémoire) :
            // sauvegarde "silencieuse" pour ne pas déclencher CombosChanged (qui
            // reconstruirait le ComboRunner actif et perdrait la série en cours).
            var combo = _comboRunner?.Combo;
            if (combo is not null)
            {
                combo.TotalCompletions++;
                combo.TotalAttempts++;
                if (streak > combo.BestStreak) combo.BestStreak = streak;

                var threshold = Math.Max(1, AppState.Settings.ChainStreakThreshold);
                var justMastered = !combo.Mastered && streak >= threshold;
                if (justMastered)
                {
                    combo.Mastered = true;
                    ShowModeBadge($"Combo maîtrisée : {combo.Name} !");
                }
                AppState.SaveCombosQuiet();

                // Enchaînement façon "session guidée" : ne passe à la combo suivante
                // de la liste qu'une fois le seuil de réussites consécutives atteint
                // (pas juste après la 1ère réussite), pour forcer une vraie répétition
                // avant de progresser — cf. section "progression multi-combos" de
                // docs/audit_features.md.
                if (AppState.Settings.ChainCombos && AppState.Combos.Count > 1 && streak >= threshold)
                {
                    _chainComboTimer?.Stop();
                    _chainComboTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                    _chainComboTimer.Tick += (_, _) =>
                    {
                        _chainComboTimer!.Stop();
                        AppState.CycleCombo();
                    };
                    _chainComboTimer.Start();
                }
            }
        });
    }

    // Ne remet pas l'affichage à zéro tout de suite : FlashAllStepsRed (déclenché
    // juste avant par StepFailed) s'en charge une fois son clignotement terminé,
    // sinon le reset immédiat couperait l'animation rouge.
    private void OnComboReset()
    {
        Dispatcher.Invoke(() =>
        {
            _comboStreakText.Text = $"Série réussie : {_comboRunner?.Streak ?? 0}";

            var combo = _comboRunner?.Combo;
            if (combo is not null)
            {
                combo.TotalAttempts++;
                AppState.SaveCombosQuiet();
            }
        });
    }

    // --- Mode 2 : grosses flèches collées aux bords de l'écran (gauche/droite/
    // haut/bas) + attaques légère/forte bien visibles au centre en haut. Pensé
    // pour être lu d'un coup d'œil, sans avoir à lire du texte. ---
    private Grid BuildMode2Layer()
    {
        var grid = new Grid();

        var left = AppState.Binds.First(b => b.Group == "Movement" && b.Slot == "Left");
        var right = AppState.Binds.First(b => b.Group == "Movement" && b.Slot == "Right");
        var up = AppState.Binds.First(b => b.Group == "Movement" && b.Slot == "Up");
        var down = AppState.Binds.First(b => b.Group == "Movement" && b.Slot == "Down");

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
        var light = AppState.Binds.First(b => b.Action == "Att. légère");
        var heavy = AppState.Binds.First(b => b.Action == "Att. forte");
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
        // Toutes les actions du groupe "Action" sauf les deux déjà affichées en
        // gros au centre (attaques légère/forte) — pas de liste de noms en dur,
        // pour ne pas casser silencieusement si l'utilisateur renomme/ajoute une
        // action dans l'onglet Touches.
        foreach (var bind in AppState.Binds.Where(b => b.Group == "Action" && b != light && b != heavy))
        {
            extrasPanel.Children.Add(BuildBigKeycap(bind, 70, 70, 30, showLabel: false));
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
        ApplyClickThrough(AppState.Locked);

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

        RepositionPanel(_mode1Panel);

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

        AppState.Hook.KeyDown += OnGlobalKeyDown;
        AppState.Hook.KeyUp += OnGlobalKeyUp;
        AppState.Hook.Start();

        // Manette : mêmes handlers que le clavier (codes synthétiques hors de la
        // plage VK réelle, voir GamepadHook), donc l'historique/les combos/les
        // raccourcis Ctrl+Alt+* fonctionnent à l'identique quelle que soit la
        // source d'input.
        AppState.Gamepad.ButtonDown += OnGlobalKeyDown;
        AppState.Gamepad.ButtonUp += OnGlobalKeyUp;
        AppState.Gamepad.Start();

        AppState.QuizRevealRequested += RevealQuizStepsTemporarily;

        BuildTrayIcon();

        // Applique le mode de démarrage configuré (par défaut 0, déjà affiché par
        // BuildLayout, donc pas de badge parasite si rien ne change).
        if (AppState.Settings.DefaultMode != AppState.ActiveMode)
        {
            AppState.SetMode(AppState.Settings.DefaultMode);
        }

        ApplyOpacity();
    }

    private void BuildTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        var lockItem = new System.Windows.Forms.ToolStripMenuItem("Verrouiller / Déverrouiller");
        lockItem.Click += (_, _) => Dispatcher.Invoke(AppState.ToggleLock);
        menu.Items.Add(lockItem);

        var modeItem = new System.Windows.Forms.ToolStripMenuItem("Changer de mode");
        modeItem.Click += (_, _) => Dispatcher.Invoke(AppState.CycleMode);
        menu.Items.Add(modeItem);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var panelItem = new System.Windows.Forms.ToolStripMenuItem("Ouvrir le panneau de contrôle");
        panelItem.Click += (_, _) => Dispatcher.Invoke(OpenControlPanel);
        menu.Items.Add(panelItem);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var quitItem = new System.Windows.Forms.ToolStripMenuItem("Quitter");
        quitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(quitItem);

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Brawlhalla Input Overlay",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(OpenControlPanel);
            }
        };
    }

    private void OpenControlPanel()
    {
        if (_controlPanel is null || !_controlPanel.IsLoaded)
        {
            _controlPanel = new ControlPanelWindow();
            _controlPanel.Closed += (_, _) => _controlPanel = null;
            _controlPanel.Show();
        }
        else
        {
            if (_controlPanel.WindowState == WindowState.Minimized)
                _controlPanel.WindowState = WindowState.Normal;
            _controlPanel.Activate();
        }
    }

    private void RepositionPanel(Border panel)
    {
        double left, top;
        switch (AppState.Settings.Position)
        {
            case OverlayPosition.BottomRight:
                left = _canvasWidth - panel.ActualWidth - 24;
                top = _canvasHeight - panel.ActualHeight - 24;
                break;
            case OverlayPosition.TopLeft:
                left = 24;
                top = 24;
                break;
            case OverlayPosition.TopRight:
                left = _canvasWidth - panel.ActualWidth - 24;
                top = 24;
                break;
            case OverlayPosition.Free:
                left = AppState.Settings.FreeLeft;
                top = AppState.Settings.FreeTop;
                break;
            case OverlayPosition.BottomLeft:
            default:
                left = 24;
                top = _canvasHeight - panel.ActualHeight - 24;
                break;
        }

        Canvas.SetLeft(panel, left);
        Canvas.SetTop(panel, top);
    }

    // Le mode Tutoriel (mode 3) ignore le réglage de position général : il reste
    // toujours un gros bandeau centré en haut de l'écran, pour rester lisible
    // pendant l'action sans dépendre d'où l'utilisateur a placé le panneau 1.
    private void RepositionTopCenter(Border panel)
    {
        Canvas.SetLeft(panel, (_canvasWidth - panel.ActualWidth) / 2);
        Canvas.SetTop(panel, 30);
    }

    private void ApplyScale()
    {
        var scale = AppState.Settings.Scale;
        var transform = new ScaleTransform(scale, scale);
        _mode1Panel.LayoutTransform = transform;
        _mode3Panel.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void ApplyOpacity()
    {
        Opacity = AppState.Settings.Opacity;
    }

    private void OnGlobalKeyDown(int vkCode)
    {
        ResetHistoryClearTimer();

        if (IsCtrl(vkCode)) _ctrlDown = true;
        if (IsAlt(vkCode)) _altDown = true;

        if (_ctrlDown && _altDown && vkCode == VK_O)
        {
            Dispatcher.Invoke(AppState.ToggleLock);
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_P)
        {
            Dispatcher.Invoke(AppState.CycleMode);
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_K)
        {
            Dispatcher.Invoke(AppState.CycleCombo);
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_R)
        {
            Dispatcher.Invoke(() => AppState.SetRecording(!AppState.Recording));
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_U)
        {
            Dispatcher.Invoke(OpenControlPanel);
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_I)
        {
            Dispatcher.Invoke(RevealQuizStepsTemporarily);
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
    // est pressée quasi en même temps, afin de les regrouper en un seul "coup joué"
    // ("A + B"). Recalcule à chaque nouvel appui l'ensemble des touches *actuellement*
    // enfoncées (pas seulement celles pressées pendant la fenêtre) : une direction déjà
    // tenue avant que l'attaque soit pressée doit quand même compter comme tenue au
    // moment du coup — sinon "se placer puis attaquer" ne matcherait jamais une étape
    // combinée du mode Tutoriel.
    private void QueuePendingBind(KeyBind bind)
    {
        _pendingBinds.Clear();
        foreach (var vk in _pressedVks)
        {
            if (_bindsByVk.TryGetValue(vk, out var heldBind) && !_pendingBinds.Contains(heldBind))
            {
                _pendingBinds.Add(heldBind);
            }
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

        _comboRunner?.Feed(binds, now);
        if (AppState.Recording) _recordedMoves.Add((binds, now));
        AppState.LogSessionMove(HistoryText(binds));

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

        while (_historyPanel.Children.Count > AppState.Settings.MaxHistoryEntries)
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

    private void ApplyLockVisuals(bool locked)
    {
        ApplyClickThrough(locked);
        _hintText.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnLockChanged(bool locked) => Dispatcher.Invoke(() => ApplyLockVisuals(locked));

    private static readonly string[] ModeNames = { "Historique", "Grandes flèches", "Tutoriel" };

    private void ApplyModeVisuals(int mode, bool showBadge)
    {
        _mode1Panel.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        _mode2Layer.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        _mode3Panel.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;

        // L'historique est un seul StackPanel partagé entre le mode 1 et le mode 3 :
        // on le déplace vers le conteneur du mode actif plutôt que d'en dupliquer l'état.
        _historySlotMode1.Content = mode == 0 ? _historyPanel : null;
        _historySlotMode3.Content = mode == 2 ? _historyPanel : null;

        if (mode == 2)
        {
            _mode3Panel.UpdateLayout();
            RepositionTopCenter(_mode3Panel);
        }
        else if (mode == 0)
        {
            _mode1Panel.UpdateLayout();
            RepositionPanel(_mode1Panel);
        }

        if (showBadge) ShowModeBadge(ModeNames[mode]);
    }

    private void OnModeChanged(int mode) => Dispatcher.Invoke(() => ApplyModeVisuals(mode, showBadge: true));

    private void ShowModeBadge(string text)
    {
        _modeBadge.Text = $"Mode : {text}";
        Canvas.SetLeft(_modeBadge, (_canvasWidth - _modeBadge.ActualWidth) / 2);

        _modeBadgeTimer?.Stop();
        _modeBadge.BeginAnimation(OpacityProperty, null);
        _modeBadge.Opacity = 1.0;

        _modeBadgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _modeBadgeTimer.Tick += (_, _) =>
        {
            _modeBadgeTimer!.Stop();
            _modeBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(400)));
        };
        _modeBadgeTimer.Start();
    }

    private void OnRecordingChanged(bool recording)
    {
        Dispatcher.Invoke(() =>
        {
            if (recording)
            {
                _recordedMoves.Clear();
                ShowModeBadge("Enregistrement combo… (Ctrl+Alt+R pour arrêter)");
            }
            else
            {
                SaveRecordedCombo();
            }
        });
    }

    private void SaveRecordedCombo()
    {
        if (_recordedMoves.Count == 0)
        {
            ShowModeBadge("Enregistrement annulé (aucun coup capturé)");
            return;
        }

        var steps = new List<ComboStep>();
        for (int i = 0; i < _recordedMoves.Count; i++)
        {
            var (binds, time) = _recordedMoves[i];
            int? maxDelay = null;
            if (i > 0)
            {
                var gapMs = (time - _recordedMoves[i - 1].Time).TotalMilliseconds;
                maxDelay = (int)Math.Max(150, gapMs * 1.6);
            }

            steps.Add(new ComboStep
            {
                RequiredActions = binds.Select(b => b.Action).ToList(),
                MaxDelayMs = maxDelay,
            });
        }

        var combo = new Combo
        {
            Name = $"Combo {AppState.Combos.Count + 1}",
            Steps = steps,
        };

        AppState.Combos.Add(combo);
        AppState.NotifyCombosMutated();
        ShowModeBadge($"Combo enregistrée : {combo.Name} ({steps.Count} étapes)");
        AppState.SetActiveCombo(AppState.Combos.Count - 1);
    }

    private void OnBindsChanged()
    {
        Dispatcher.Invoke(() =>
        {
            RebuildBindMaps();
            BuildLayout();
        });
    }

    private void OnSettingsChanged()
    {
        Dispatcher.Invoke(() =>
        {
            ApplyScale();
            ApplyOpacity();
            RepositionPanel(_mode1Panel);
            RepositionTopCenter(_mode3Panel);
            if (_comboRunner is not null) _comboRunner.KeepStreakOnFail = AppState.Settings.KeepStreakOnFail;
        });
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
        if (AppState.Locked) return;
        _dragStart = e.GetPosition(RootCanvas);
        _mode1Panel.CaptureMouse();
    }

    private void Mode1Panel_MouseMove(object sender, MouseEventArgs e)
    {
        if (AppState.Locked || _dragStart is null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(RootCanvas);
        var delta = pos - _dragStart.Value;
        Canvas.SetLeft(_mode1Panel, Canvas.GetLeft(_mode1Panel) + delta.X);
        Canvas.SetTop(_mode1Panel, Canvas.GetTop(_mode1Panel) + delta.Y);
        _dragStart = pos;
    }

    private void Mode1Panel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not null)
        {
            AppState.Settings.Position = OverlayPosition.Free;
            AppState.Settings.FreeLeft = Canvas.GetLeft(_mode1Panel);
            AppState.Settings.FreeTop = Canvas.GetTop(_mode1Panel);
            AppState.SaveSettings();
        }

        _dragStart = null;
        _mode1Panel.ReleaseMouseCapture();
    }
}
