using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BrawlhallaOverlay;

/// <summary>
/// Le "Parcours" (docs/plan_ux_onboarding.md §4) : suite de leçons courtes qui enseignent une
/// mécanique du jeu tout en faisant utiliser une fonction de l'app. Fenêtre bordée classique
/// (explication + progression), le drill se joue via les mêmes hooks globaux que l'overlay
/// (AppState.Hook/AppState.Gamepad, démarrés ici s'ils ne le sont pas déjà — ils sont idempotents,
/// donc cette fenêtre marche même sans MainWindow ouverte à côté).
///
/// Contrainte dure (§4.2 du plan) : l'app ne voit que les inputs, jamais l'état du jeu — donc
/// chaque leçon expose honnêtement ce qu'elle peut vraiment vérifier (badge "Validé"/"Partiellement
/// validé par l'app") plutôt que de prétendre confirmer un effet en jeu qu'elle ne peut pas voir.
/// </summary>
public partial class ParcoursWindow : Window
{
    // Palette centralisée dans Core/Theme.cs

    private readonly List<Lesson> _lessons = ParcoursCurriculum.BuildLessons();
    private int _currentIndex;
    private Lesson Current => _lessons[_currentIndex];

    private readonly Dictionary<int, KeyBind> _bindsByVk = new();
    private readonly HashSet<int> _pressedVks = new();

    // --- Raccourcis Ctrl+Alt+* (verrouiller/suspendre), dupliqués depuis MainWindow.
    // OnGlobalKeyDown — jusqu'ici seuls les boutons d'en-tête (🔒/⏸) fonctionnaient quand cette
    // fenêtre est ouverte SEULE (sans MainWindow/l'overlay à côté, qui est la seule autre fenêtre
    // à intercepter ces touches) : les leçons 0.2/0.3 enseignent explicitement Ctrl+Alt+O/H comme
    // méthode, elle doit donc marcher ici aussi, pas seulement via le bouton. Même petit bug
    // évité que le §M18/régression Ctrl+Alt (CLAUDE.md, Version 26) : ne jamais resynchroniser le
    // modificateur que CET appui vient de presser lui-même contre GetAsyncKeyState.
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_MENU = 0x12;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private bool _ctrlDown;
    private bool _altDown;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsCtrl(int vkCode) => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL;
    private static bool IsAlt(int vkCode) => vkCode is VK_MENU or VK_LMENU or VK_RMENU;
    private static bool CtrlHeldNow() => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0;
    private static bool AltHeldNow() => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 || (GetAsyncKeyState(VK_LMENU) & 0x8000) != 0 || (GetAsyncKeyState(VK_RMENU) & 0x8000) != 0;

    private TextBlock _headerText = null!;
    private TextBlock _objectiveText = null!;
    private TextBlock _explanationText = null!;
    private TextBlock _badgeText = null!;
    private TextBlock _verifyText = null!;
    private TextBlock _sourceText = null!;
    private StackPanel _drillPanel = null!;
    private Button _prevBtn = null!;
    private Button _nextBtn = null!;

    private bool _currentLessonValidated;

    // État spécifique au Kind de la leçon affichée — nettoyé à chaque changement de leçon
    // (CleanupCurrentLessonState) pour qu'un timer/handler d'une leçon abandonnée ne continue
    // pas à valider la leçon suivante par erreur.
    private readonly HashSet<string> _pressedOnce = new();
    private readonly Dictionary<string, Border> _chipByAction = new();
    private ComboRunner? _sequenceRunner;
    private readonly List<Border> _stepPills = new();
    private DateTime _absenceResetTime;
    private DispatcherTimer? _absenceTimer;
    private TextBlock? _absenceCountdownText;
    private Action? _unsubscribeToggle;
    private DispatcherTimer? _autoAdvanceTimer;

    // Bandeau overlay en jeu (demande explicite de l'utilisateur, 2026-08-08) : possédé par
    // cette fenêtre — créé à l'ouverture, fermé avec elle — pour que jouer une leçon ne demande
    // plus d'alt-tabber vers cette fenêtre bordée. Voir Windows/ParcoursOverlayWindow.xaml.cs.
    private readonly ParcoursOverlayWindow _overlay = new();

    public ParcoursWindow()
    {
        InitializeComponent();

        // Mutuellement exclusif avec MainWindow (voir AppState.ParcoursRunning) — consulté par
        // DashboardWindow avant de lancer l'overlay pendant que le Parcours tourne.
        AppState.ParcoursRunning = true;
        AppState.CloseParcoursRequested = Close;

        RebuildBindMap();
        AppState.BindsChanged += OnBindsChanged;

        BuildStaticLayout();
        LoadLesson(FindStartIndex(AppState.ParcoursCurrentLessonId));

        _overlay.Show();

        Loaded += (_, _) =>
        {
            AppState.Hook.KeyDown += OnGlobalKeyDown;
            AppState.Hook.KeyUp += OnGlobalKeyUp;
            AppState.Hook.Start();
            AppState.Gamepad.ButtonDown += OnGlobalKeyDown;
            AppState.Gamepad.ButtonUp += OnGlobalKeyUp;
            AppState.Gamepad.Start();
        };
        Closed += (_, _) =>
        {
            AppState.Hook.KeyDown -= OnGlobalKeyDown;
            AppState.Hook.KeyUp -= OnGlobalKeyUp;
            AppState.Gamepad.ButtonDown -= OnGlobalKeyDown;
            AppState.Gamepad.ButtonUp -= OnGlobalKeyUp;
            AppState.BindsChanged -= OnBindsChanged;
            _autoAdvanceTimer?.Stop();
            CleanupCurrentLessonState();
            _overlay.Close();
            AppState.ParcoursRunning = false;
            AppState.CloseParcoursRequested = null;
        };
    }

    private void OnBindsChanged() => RebuildBindMap();

    private void RebuildBindMap()
    {
        _bindsByVk.Clear();
        foreach (var bind in AppState.Binds)
            foreach (var vk in bind.VirtualKeyCodes)
                _bindsByVk[vk] = bind;
    }

    private int FindStartIndex(string startId)
    {
        if (!string.IsNullOrEmpty(startId))
        {
            var idx = _lessons.FindIndex(l => l.Id == startId);
            if (idx >= 0) return idx;
        }
        var firstIncomplete = _lessons.FindIndex(l => !AppState.IsLessonCompleted(l.Id));
        return firstIncomplete >= 0 ? firstIncomplete : 0;
    }

    // ------------------------------------------------------------------
    // Layout statique (header/footer/corps) — le contenu de leçon se recharge dans les mêmes
    // TextBlock/StackPanel plutôt que de reconstruire toute la fenêtre à chaque changement.
    //
    // Tous les boutons ci-dessous ont Focusable=false : repéré en testant en vrai qu'un bouton
    // WPF qui garde le focus clavier après un clic réagit ensuite à Espace/Entrée comme une
    // ré-activation — or Espace est justement l'action "Saut" par défaut. Sans ce réglage, taper
    // Saut pendant un drill de la leçon 1.1 pouvait ré-déclencher "Suivant"/"Précédent" (dernier
    // bouton cliqué) en plus d'alimenter la séquence, faisant sauter des leçons de façon
    // imprévisible. Focusable=false n'empêche pas le clic souris, seulement la rétention du focus
    // clavier après coup.
    // ------------------------------------------------------------------
    private void BuildStaticLayout()
    {
        var root = new DockPanel();

        var header = new Border
        {
            Background = Theme.BgCard,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xE8, 0xC4, 0x4A)),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(20, 14, 20, 14),
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _headerText = new TextBlock { FontSize = 15, FontFamily = Theme.AccentFontFamily, FontWeight = FontWeights.Bold, Foreground = Theme.AccentGold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(_headerText, 0);

        var headerButtons = new StackPanel { Orientation = Orientation.Horizontal };
        // Cette fenêtre peut s'ouvrir seule (bouton "Leçons" de DashboardWindow), sans l'overlay
        // ni son tray/sa barre de contrôle — sans ce bouton ici, aucun moyen de suspendre la
        // capture globale si un test révèle qu'elle réagit à autre chose (voir le commentaire de
        // OnGlobalKeyDown sur AppState.CaptureSuspended, bug repéré en testant ce Parcours).
        var suspendBtn = new Button { Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0), Focusable = false };
        void UpdateSuspendLabel() => suspendBtn.Content = AppState.CaptureSuspended ? "▶ Reprendre la capture" : "⏸ Suspendre la capture";
        UpdateSuspendLabel();
        suspendBtn.Click += (_, _) => AppState.ToggleCaptureSuspended();
        Action<bool> onSuspendChanged = _ => Dispatcher.Invoke(UpdateSuspendLabel);
        AppState.CaptureSuspendedChanged += onSuspendChanged;
        Closed += (_, _) => AppState.CaptureSuspendedChanged -= onSuspendChanged;
        headerButtons.Children.Add(suspendBtn);

        // Même raison que le bouton de suspension ci-dessus : la leçon 0.2 demande de
        // déverrouiller l'overlay via Ctrl+Alt+O, un raccourci géré par MainWindow — sans overlay
        // ouverte à côté (cas du bouton "Leçons" de DashboardWindow, hors parcours Débutant),
        // Ctrl+Alt+O ne serait intercepté par personne. Ce bouton rend la leçon faisable dans
        // tous les cas, même si son effet visuel (l'overlay qui devient déplaçable) ne se voit
        // que si l'overlay est aussi ouverte.
        var lockBtn = new Button { Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0), Focusable = false };
        void UpdateLockLabel() => lockBtn.Content = AppState.Locked ? "🔓 Déverrouiller l'overlay" : "🔒 Verrouiller l'overlay";
        UpdateLockLabel();
        lockBtn.Click += (_, _) => AppState.ToggleLock();
        Action<bool> onLockChanged = _ => Dispatcher.Invoke(UpdateLockLabel);
        AppState.LockChanged += onLockChanged;
        Closed += (_, _) => AppState.LockChanged -= onLockChanged;
        headerButtons.Children.Add(lockBtn);

        var listBtn = new Button { Content = "Voir toutes les leçons", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0), Focusable = false };
        listBtn.Click += (_, _) => OpenLessonList();
        headerButtons.Children.Add(listBtn);

        // Demande explicite de l'utilisateur (2026-08-08) : pouvoir repartir de zéro pour
        // retester le Parcours en entier. Confirmation obligatoire (même style que la suppression
        // de profil dans ControlPanelWindow) : action destructive sur la progression, pas de undo.
        var resetBtn = new Button { Content = "↺ Recommencer le Parcours", Padding = new Thickness(8, 4, 8, 4), Focusable = false };
        resetBtn.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(
                "Remettre à zéro toute la progression du Parcours ? Toutes les leçons validées repasseront à \"pas encore validé\".",
                "Recommencer le Parcours",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            AppState.ResetParcoursProgress();
            _autoAdvanceTimer?.Stop();
            CleanupCurrentLessonState();
            LoadLesson(0);
        };
        headerButtons.Children.Add(resetBtn);

        Grid.SetColumn(headerButtons, 1);
        headerGrid.Children.Add(_headerText);
        headerGrid.Children.Add(headerButtons);
        header.Child = headerGrid;
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Border
        {
            Background = Theme.BgCard,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xE8, 0xC4, 0x4A)),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(20, 12, 20, 12),
        };
        var footerRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _prevBtn = new Button { Content = "◀ Précédent", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), Focusable = false };
        _prevBtn.Click += (_, _) => GoTo(_currentIndex - 1);
        _nextBtn = new Button { Content = "Suivant ▶", Padding = new Thickness(12, 6, 12, 6), Focusable = false };
        _nextBtn.Click += (_, _) => GoTo(_currentIndex + 1);
        footerRow.Children.Add(_prevBtn);
        footerRow.Children.Add(_nextBtn);
        footer.Child = footerRow;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };

        _objectiveText = new TextBlock { FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Theme.TextPrimary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        _explanationText = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        _badgeText = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16) };

        _drillPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        _verifyText = new TextBlock { FontSize = 12, FontStyle = FontStyles.Italic, Foreground = Theme.StateWarning, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        _sourceText = new TextBlock { FontSize = 10, Foreground = Theme.TextSubtle, TextWrapping = TextWrapping.Wrap };

        body.Children.Add(_objectiveText);
        body.Children.Add(_explanationText);
        body.Children.Add(_badgeText);
        body.Children.Add(_drillPanel);
        body.Children.Add(_verifyText);
        body.Children.Add(_sourceText);

        scroll.Content = body;
        root.Children.Add(scroll);

        RootGrid.Children.Add(root);
    }

    private void GoTo(int index)
    {
        if (index < 0 || index >= _lessons.Count) return;
        _autoAdvanceTimer?.Stop();
        CleanupCurrentLessonState();
        LoadLesson(index);
    }

    private void LoadLesson(int index)
    {
        _currentIndex = index;
        var lesson = Current;
        AppState.SetParcoursCurrentLesson(lesson.Id);
        _currentLessonValidated = AppState.IsLessonCompleted(lesson.Id);

        _headerText.Text = $"Chapitre {lesson.Chapter} — {lesson.ChapterTitle} · Leçon {index + 1}/{_lessons.Count} : {lesson.Title}";
        _objectiveText.Text = lesson.Objective;
        _explanationText.Text = lesson.Explanation;

        _verifyText.Text = lesson.VerifyYourselfNote;
        _verifyText.Visibility = string.IsNullOrEmpty(lesson.VerifyYourselfNote) ? Visibility.Collapsed : Visibility.Visible;

        _sourceText.Text = string.IsNullOrEmpty(lesson.SourceNote) ? "" : $"Source : {lesson.SourceNote}";
        _sourceText.Visibility = string.IsNullOrEmpty(lesson.SourceNote) ? Visibility.Collapsed : Visibility.Visible;

        BuildDrillPanel(lesson);
        UpdateBadge();
        // _pressedOnce est déjà rempli par BuildPressAllOncePanel ci-dessus (repris depuis
        // AppState.IsLessonCompleted si la leçon était déjà validée) — le bandeau overlay part
        // du même état plutôt que de le recalculer indépendamment.
        _overlay.ShowLesson(lesson, _currentLessonValidated, _pressedOnce);

        _prevBtn.IsEnabled = index > 0;
        _nextBtn.IsEnabled = index < _lessons.Count - 1;
    }

    private void UpdateBadge()
    {
        var lesson = Current;
        if (_currentLessonValidated)
        {
            _badgeText.Text = "✅ Validé";
            _badgeText.Foreground = Theme.StateSuccess;
        }
        else if (!lesson.FullyValidatedByApp)
        {
            _badgeText.Text = "🟡 Partiellement validé par l'app";
            _badgeText.Foreground = Theme.StateWarning;
        }
        else
        {
            _badgeText.Text = "○ Pas encore validé";
            _badgeText.Foreground = Theme.TextSubtle;
        }
    }

    private void MarkValidated()
    {
        if (_currentLessonValidated) return;
        _currentLessonValidated = true;
        AppState.MarkLessonCompleted(Current.Id);
        UpdateBadge();
        _overlay.ShowValidatedFlash();

        // Auto-avance vers la leçon suivante une fois validée (demande explicite de
        // l'utilisateur, "comme un vrai tuto") — délai court pour laisser le temps de voir la
        // confirmation ("✅ Leçon validée") avant que l'écran ne change. Sans effet sur la
        // dernière leçon (GoTo ignore un index hors bornes). Un GoTo manuel (Précédent/Suivant/
        // liste) pendant ce délai l'annule (voir GoTo).
        _autoAdvanceTimer?.Stop();
        _autoAdvanceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _autoAdvanceTimer.Tick += (_, _) =>
        {
            _autoAdvanceTimer!.Stop();
            GoTo(_currentIndex + 1);
        };
        _autoAdvanceTimer.Start();
    }

    /// <summary>Stoppe tout état vivant (timer, souscription) attaché à la leçon quittée — sinon un
    /// AbsenceTimer ou un handler ToggleOnce de la leçon précédente continuerait à tourner en
    /// arrière-plan et pourrait valider la mauvaise leçon.</summary>
    private void CleanupCurrentLessonState()
    {
        _absenceTimer?.Stop();
        _absenceTimer = null;
        _absenceCountdownText = null;
        _unsubscribeToggle?.Invoke();
        _unsubscribeToggle = null;
        _sequenceRunner = null;
        _pressedOnce.Clear();
        _chipByAction.Clear();
        _stepPills.Clear();
    }

    private void BuildDrillPanel(Lesson lesson)
    {
        CleanupCurrentLessonState();
        _drillPanel.Children.Clear();

        switch (lesson.Kind)
        {
            case LessonValidationKind.PressAllOnce:
                BuildPressAllOncePanel(lesson);
                break;
            case LessonValidationKind.Sequence:
                BuildSequencePanel(lesson);
                break;
            case LessonValidationKind.AbsenceTimer:
                BuildAbsenceTimerPanel(lesson);
                break;
            case LessonValidationKind.ToggleOnce:
                BuildToggleOncePanel(lesson);
                break;
        }
    }

    private void BuildPressAllOncePanel(Lesson lesson)
    {
        var wrap = new WrapPanel();
        foreach (var action in lesson.RequiredActionsOnce)
        {
            var alreadyDone = _currentLessonValidated;
            var chip = new Border
            {
                Background = alreadyDone ? Theme.StateSuccess : Theme.BgCard,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 8),
                Child = new TextBlock { Text = action, Foreground = Theme.TextPrimary },
            };
            _chipByAction[action] = chip;
            if (alreadyDone) _pressedOnce.Add(action);
            wrap.Children.Add(chip);
        }
        _drillPanel.Children.Add(wrap);
    }

    private void BuildSequencePanel(Lesson lesson)
    {
        var combo = new Combo
        {
            Steps = lesson.Sequence.Select(s => new ComboStep { RequiredActions = new List<string>(s.RequiredActions) }).ToList(),
            MatchMode = MatchMode.IgnoreExtraneous,
        };
        _sequenceRunner = new ComboRunner(combo);
        _sequenceRunner.StepSucceeded += idx =>
        {
            if (idx >= 0 && idx < _stepPills.Count) _stepPills[idx].Background = Theme.StateSuccess;
            _overlay.MarkSequenceStepDone(idx);
        };
        _sequenceRunner.ComboCompleted += MarkValidated;
        _sequenceRunner.StepFailed += (_, _) => { ResetStepPills(); _overlay.ResetSequenceSteps(); };
        _sequenceRunner.ComboReset += () => { ResetStepPills(); _overlay.ResetSequenceSteps(); };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < lesson.Sequence.Count; i++)
        {
            if (i > 0) row.Children.Add(new TextBlock { Text = "→", Foreground = Theme.TextSubtle, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
            var label = string.Join(" + ", lesson.Sequence[i].RequiredActions);
            var pill = new Border
            {
                Background = Theme.BgCard,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Child = new TextBlock { Text = label, Foreground = Theme.TextPrimary, TextAlignment = TextAlignment.Center },
            };
            _stepPills.Add(pill);
            row.Children.Add(pill);
        }
        _drillPanel.Children.Add(row);
    }

    private void ResetStepPills()
    {
        foreach (var pill in _stepPills) pill.Background = Theme.BgCard;
    }

    private void BuildAbsenceTimerPanel(Lesson lesson)
    {
        _absenceResetTime = DateTime.UtcNow;
        _absenceCountdownText = new TextBlock { FontSize = 14, Foreground = Theme.TextPrimary };
        _drillPanel.Children.Add(_absenceCountdownText);

        _absenceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _absenceTimer.Tick += (_, _) =>
        {
            var remaining = lesson.AbsenceSeconds - (DateTime.UtcNow - _absenceResetTime).TotalSeconds;
            if (remaining <= 0)
            {
                _absenceTimer!.Stop();
                _absenceCountdownText.Text = "✅ Tenu sans esquiver — bien joué.";
                _overlay.SetAbsenceCountdown("✅ Tenu sans esquiver — bien joué.");
                MarkValidated();
            }
            else
            {
                _absenceCountdownText.Text = $"Tiens encore {remaining:0.0}s sans esquiver…";
                _overlay.SetAbsenceCountdown($"Tiens encore {remaining:0.0}s sans esquiver…");
            }
        };
        _absenceTimer.Start();
    }

    private void BuildToggleOncePanel(Lesson lesson)
    {
        var hint = new TextBlock
        {
            FontSize = 12,
            Foreground = Theme.TextSubtle,
            TextWrapping = TextWrapping.Wrap,
            Text = "Rien à taper ici — fais l'action décrite ci-dessus quand tu veux, la leçon se valide toute seule.",
        };
        _drillPanel.Children.Add(hint);

        if (_currentLessonValidated) return;

        switch (lesson.ToggleEventName)
        {
            case "Lock":
                Action<bool> lockHandler = null!;
                lockHandler = _ => { AppState.LockChanged -= lockHandler; MarkValidated(); };
                AppState.LockChanged += lockHandler;
                _unsubscribeToggle = () => AppState.LockChanged -= lockHandler;
                break;
            case "CaptureSuspended":
                Action<bool> suspendHandler = null!;
                suspendHandler = _ => { AppState.CaptureSuspendedChanged -= suspendHandler; MarkValidated(); };
                AppState.CaptureSuspendedChanged += suspendHandler;
                _unsubscribeToggle = () => AppState.CaptureSuspendedChanged -= suspendHandler;
                break;
        }
    }

    private void OpenLessonList()
    {
        var dialog = new Window
        {
            Title = "Tout le parcours",
            Width = 420,
            Height = 480,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Theme.BgPanel,
        };

        var list = new ListBox { Background = Theme.BgCard, Foreground = Theme.TextPrimary, BorderThickness = new Thickness(0), Margin = new Thickness(12) };
        foreach (var lesson in _lessons)
        {
            var done = AppState.IsLessonCompleted(lesson.Id) ? "✓ " : "";
            list.Items.Add($"{done}Chapitre {lesson.Chapter} — {lesson.Title}");
        }
        list.SelectedIndex = _currentIndex;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var target = list.SelectedIndex;
            dialog.Close();
            GoTo(target);
        };

        dialog.Content = list;
        dialog.ShowDialog();
    }

    // ------------------------------------------------------------------
    // Capture globale (clavier + manette) — même style que MainWindow, mais logique bien plus
    // légère : pas d'historique, pas de couleurs de touches, juste alimenter la leçon en cours.
    // ------------------------------------------------------------------
    private void OnGlobalKeyDown(int vkCode)
    {
        if (IsCtrl(vkCode)) _ctrlDown = true;
        if (IsAlt(vkCode)) _altDown = true;
        // Ne jamais resynchroniser le modificateur que CET appui vient de presser lui-même —
        // voir la docstring du bloc de champs ci-dessus (même bug que CLAUDE.md Version 26).
        if (_ctrlDown && !IsCtrl(vkCode) && !CtrlHeldNow()) _ctrlDown = false;
        if (_altDown && !IsAlt(vkCode) && !AltHeldNow()) _altDown = false;

        // Vérifiés AVANT le garde CaptureSuspended ci-dessous (même ordre que MainWindow) : le
        // raccourci de suspension doit rester joignable au clavier pendant qu'on est suspendu,
        // sinon impossible de reprendre sans le bouton.
        if (_ctrlDown && _altDown && vkCode == AppState.Settings.LockVk)
        {
            Dispatcher.Invoke(AppState.ToggleLock);
            return;
        }
        if (_ctrlDown && _altDown && vkCode == AppState.Settings.SuspendVk)
        {
            Dispatcher.Invoke(AppState.ToggleCaptureSuspended);
            return;
        }

        // Même garde que MainWindow.OnGlobalKeyDown (voir AppState.CaptureSuspended) : le hook est
        // global, donc sans ça n'importe quelle frappe faite ailleurs sur le PC (hors de cette
        // fenêtre, hors du jeu) validerait silencieusement une leçon. Repéré en testant en vrai
        // (une leçon PressAllOnce s'est retrouvée validée sans qu'aucune touche n'ait été pressée
        // depuis l'automation de test — la capture globale réagissait à la frappe réelle sur la
        // machine pendant le test).
        if (AppState.CaptureSuspended) return;

        // Même garde que MainWindow (audit 2026-08-07 §F7) : un appui servant à assigner une
        // touche dans une autre fenêtre ne doit pas valider une leçon au passage.
        if (AppState.BindingCaptureActive) return;

        var isNewPress = _pressedVks.Add(vkCode);
        if (!_bindsByVk.TryGetValue(vkCode, out var bind)) return;

        Dispatcher.Invoke(() =>
        {
            var lesson = Current;
            switch (lesson.Kind)
            {
                case LessonValidationKind.PressAllOnce:
                    if (lesson.RequiredActionsOnce.Contains(bind.Action) && _pressedOnce.Add(bind.Action))
                    {
                        if (_chipByAction.TryGetValue(bind.Action, out var chip)) chip.Background = Theme.StateSuccess;
                        _overlay.MarkActionDone(bind.Action);
                        if (_pressedOnce.Count == lesson.RequiredActionsOnce.Count) MarkValidated();
                    }
                    break;

                case LessonValidationKind.Sequence:
                    if (isNewPress)
                    {
                        var held = new List<KeyBind>();
                        foreach (var vk in _pressedVks)
                        {
                            if (_bindsByVk.TryGetValue(vk, out var heldBind) && !held.Contains(heldBind)) held.Add(heldBind);
                        }
                        _sequenceRunner?.Feed(held, DateTime.UtcNow, bind);
                    }
                    break;

                case LessonValidationKind.AbsenceTimer:
                    if (lesson.AbsenceActions.Contains(bind.Action)) _absenceResetTime = DateTime.UtcNow;
                    break;
            }
        });
    }

    private void OnGlobalKeyUp(int vkCode)
    {
        if (IsCtrl(vkCode)) _ctrlDown = false;
        if (IsAlt(vkCode)) _altDown = false;
        _pressedVks.Remove(vkCode);
    }
}
