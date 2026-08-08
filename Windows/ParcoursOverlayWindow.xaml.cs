using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrawlhallaOverlay;

/// <summary>
/// Bandeau overlay en jeu pour le Parcours (demande explicite de l'utilisateur, 2026-08-08) :
/// jusqu'ici, jouer une leçon ("testez les touches", "testez l'esquive"...) se faisait en
/// regardant des pastilles texte dans la fenêtre bordée <see cref="ParcoursWindow"/>, séparée du
/// jeu — il fallait alt-tabber pour la consulter. Ce bandeau reprend le même langage visuel que le
/// panneau du mode Tutoriel (icônes vectorielles <see cref="ActionIcons"/>, pastilles reliées par
/// des flèches) mais pour la leçon en cours, affiché PAR-DESSUS le jeu comme MainWindow.
///
/// Toujours click-through (contrairement à MainWindow, pas de bascule verrouillé/déverrouillé :
/// ce bandeau n'a jamais besoin d'être déplacé à la souris, juste consulté). Possédée par
/// <see cref="ParcoursWindow"/> : créée à son ouverture, fermée avec elle. La logique de
/// validation (quelle touche fait quoi) reste entièrement dans ParcoursWindow — cette fenêtre ne
/// fait qu'afficher l'état qu'on lui pousse (ShowLesson/MarkActionDone/MarkSequenceStepDone/...),
/// exactement comme MainWindow ne juge rien et se contente de refléter ComboRunner.
/// </summary>
public partial class ParcoursOverlayWindow : Window
{
    // --- Win32 interop pour le click-through, dupliqué depuis MainWindow.xaml.cs (même
    // rationale que Core/ActionIcons.cs : fenêtres volontairement indépendantes) — ici toujours
    // appliqué, pas de bascule verrouillé/déverrouillé nécessaire pour ce bandeau.
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const double IconSize = 44;

    private TextBlock _headerText = null!;
    private TextBlock _objectiveText = null!;
    private TextBlock _explanationText = null!;
    private TextBlock _validatedText = null!;
    private TextBlock _suspendedBanner = null!;
    private StackPanel _drillHost = null!;
    private Border _panel = null!;

    private readonly Dictionary<string, FrameworkElement> _pressAllOnceIcons = new();
    private readonly List<List<FrameworkElement>> _sequenceStepIcons = new();
    private TextBlock? _absenceCountdownText;

    public ParcoursOverlayWindow()
    {
        InitializeComponent();
        Build();

        Loaded += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        };

        _panel.SizeChanged += (_, _) => Reposition();
        Reposition();

        // Bug réel trouvé le 2026-08-08 : la leçon 0.3 ("Suspendre la capture") se valide dès le
        // PREMIER basculement (suspendre), sans jamais exiger ni rappeler de la réactiver. Avec
        // l'auto-avancement (ParcoursWindow.MarkValidated), rien n'empêchait d'atterrir sur la
        // leçon suivante capture toujours suspendue — plus AUCUNE touche n'est alors lue nulle
        // part (AppState.CaptureSuspended coupe OnGlobalKeyDown en tout premier), en silence :
        // aucune leçon ultérieure ne peut plus se valider et rien ne l'indique dans ce bandeau.
        // Bandeau rouge permanent tant que c'est le cas, pour rendre ce piège impossible à
        // manquer — même mécanisme que MainWindow._suspendedBadge, dupliqué ici (fenêtres
        // volontairement indépendantes, voir la docstring de classe).
        UpdateSuspendedBanner(AppState.CaptureSuspended);
        AppState.CaptureSuspendedChanged += OnCaptureSuspendedChanged;
        Closed += (_, _) => AppState.CaptureSuspendedChanged -= OnCaptureSuspendedChanged;
    }

    private void OnCaptureSuspendedChanged(bool suspended) => Dispatcher.Invoke(() => UpdateSuspendedBanner(suspended));

    private void UpdateSuspendedBanner(bool suspended)
    {
        _suspendedBanner.Visibility = suspended ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Centré en haut de la zone de travail ciblée (même logique que
    /// MainWindow.GetTargetWorkArea/RepositionTopCenter, dupliquée pour ne pas dépendre d'une
    /// instance de MainWindow — le Parcours doit pouvoir s'ouvrir seul, voir ParcoursWindow).</summary>
    private void Reposition()
    {
        var workArea = GetTargetWorkArea();
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Top + 30;
    }

    private static Rect GetTargetWorkArea()
    {
        var index = AppState.Settings.MonitorIndex;
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (index < 0 || index >= screens.Length) return SystemParameters.WorkArea;

        var primary = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var target = screens[index].WorkingArea;
        var scaleX = SystemParameters.WorkArea.Width / primary.Width;
        var scaleY = SystemParameters.WorkArea.Height / primary.Height;
        return new Rect(target.Left * scaleX, target.Top * scaleY, target.Width * scaleX, target.Height * scaleY);
    }

    private void Build()
    {
        _suspendedBanner = new TextBlock
        {
            Text = "⏸ Capture suspendue — Ctrl+Alt+H pour reprendre (aucune touche n'est lue tant que c'est le cas)",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x8A, 0x2B, 0x2B)),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };

        _headerText = new TextBlock
        {
            FontSize = 13,
            FontFamily = Theme.AccentFontFamily,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.AccentGold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 520,
        };

        // Objectif/explication complets affichés en jeu (demande explicite de l'utilisateur,
        // 2026-08-08, "pour des raisons de tests") — jusqu'ici seule ParcoursWindow (fenêtre
        // bordée, hors du jeu) les montrait. MaxWidth généreuse : certaines explications font
        // plusieurs phrases (ex. leçon 1.1 sur les ressources aériennes), le bandeau s'agrandit
        // en conséquence plutôt que de tronquer.
        _objectiveText = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Theme.TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 640,
            Margin = new Thickness(0, 6, 0, 0),
        };

        _explanationText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 640,
            Margin = new Thickness(0, 4, 0, 0),
        };

        _validatedText = new TextBlock
        {
            Text = "✅ Leçon validée",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Theme.StateSuccess,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        _drillHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var content = new StackPanel();
        content.Children.Add(_suspendedBanner);
        content.Children.Add(_headerText);
        content.Children.Add(_objectiveText);
        content.Children.Add(_explanationText);
        content.Children.Add(_drillHost);
        content.Children.Add(_validatedText);

        _panel = new Border
        {
            Background = Theme.OverlayPanelBg,
            CornerRadius = Theme.CrestMain,
            Padding = new Thickness(16, 10, 16, 10),
            BorderThickness = new Thickness(1, 1, 1, 2),
            BorderBrush = Theme.OverlayPanelBorder,
            Child = content,
        };

        RootGrid.Children.Add(_panel);
    }

    /// <summary>(Re)construit le bandeau pour la leçon donnée — appelé depuis
    /// ParcoursWindow.LoadLesson à chaque changement de leçon. `alreadyPressedOnce` restaure
    /// visuellement une leçon PressAllOnce déjà validée (mêmes icônes en vert dès l'ouverture).</summary>
    public void ShowLesson(Lesson lesson, bool alreadyValidated, IReadOnlyCollection<string> alreadyPressedOnce)
    {
        _headerText.Text = $"Parcours — Chapitre {lesson.Chapter} · Leçon {lesson.Title}";
        _objectiveText.Text = lesson.Objective;
        _explanationText.Text = lesson.Explanation;
        _drillHost.Children.Clear();
        _pressAllOnceIcons.Clear();
        _sequenceStepIcons.Clear();
        _absenceCountdownText = null;
        _validatedText.Visibility = Visibility.Collapsed;

        switch (lesson.Kind)
        {
            case LessonValidationKind.PressAllOnce:
                BuildPressAllOnce(lesson, alreadyPressedOnce);
                break;
            case LessonValidationKind.Sequence:
                BuildSequence(lesson);
                break;
            case LessonValidationKind.AbsenceTimer:
                BuildAbsence();
                break;
            case LessonValidationKind.ToggleOnce:
                BuildToggleHint();
                break;
        }

        if (alreadyValidated) _validatedText.Visibility = Visibility.Visible;
    }

    private void BuildPressAllOnce(Lesson lesson, IReadOnlyCollection<string> alreadyPressedOnce)
    {
        foreach (var action in lesson.RequiredActionsOnce)
        {
            var done = alreadyPressedOnce.Contains(action);
            var icon = ActionIcons.BuildIcon(action, IconSize, done ? "done" : "todo");
            _pressAllOnceIcons[action] = icon;

            var column = new StackPanel { Margin = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Center };
            column.Children.Add(icon);
            column.Children.Add(new TextBlock
            {
                Text = action,
                FontSize = 10,
                Foreground = Theme.TextSubtle,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
            });
            _drillHost.Children.Add(column);
        }
    }

    private void BuildSequence(Lesson lesson)
    {
        for (var i = 0; i < lesson.Sequence.Count; i++)
        {
            if (i > 0)
            {
                _drillHost.Children.Add(new TextBlock
                {
                    Text = "→",
                    Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                    FontSize = 22,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0),
                });
            }

            var stepIcons = new List<FrameworkElement>();
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var actions = lesson.Sequence[i].RequiredActions;
            for (var a = 0; a < actions.Count; a++)
            {
                var icon = ActionIcons.BuildIcon(actions[a], IconSize, "todo");
                icon.Margin = new Thickness(0, 0, a < actions.Count - 1 ? 6.0 : 0.0, 0);
                stepIcons.Add(icon);
                row.Children.Add(icon);
            }
            _sequenceStepIcons.Add(stepIcons);
            _drillHost.Children.Add(row);
        }
    }

    private void BuildAbsence()
    {
        _absenceCountdownText = new TextBlock
        {
            FontSize = 14,
            Foreground = Theme.TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _drillHost.Children.Add(_absenceCountdownText);
    }

    private void BuildToggleHint()
    {
        _drillHost.Children.Add(new TextBlock
        {
            Text = "Fais l'action décrite dans le Parcours — ça se valide tout seul.",
            FontSize = 11,
            Foreground = Theme.TextSubtle,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        });
    }

    /// <summary>PressAllOnce : recolore l'icône de l'action qui vient d'être pressée.</summary>
    public void MarkActionDone(string action)
    {
        if (_pressAllOnceIcons.TryGetValue(action, out var icon)) ActionIcons.Recolor(icon, "done");
    }

    /// <summary>Sequence : recolore toutes les icônes de l'étape qui vient de réussir.</summary>
    public void MarkSequenceStepDone(int index)
    {
        if (index < 0 || index >= _sequenceStepIcons.Count) return;
        foreach (var icon in _sequenceStepIcons[index]) ActionIcons.Recolor(icon, "done");
    }

    /// <summary>Sequence : remet toutes les icônes à l'état "pas encore joué" (mauvaise touche ou
    /// reset de la tentative).</summary>
    public void ResetSequenceSteps()
    {
        foreach (var step in _sequenceStepIcons)
            foreach (var icon in step) ActionIcons.Recolor(icon, "todo");
    }

    /// <summary>AbsenceTimer : texte du compte à rebours, identique à celui de ParcoursWindow.</summary>
    public void SetAbsenceCountdown(string text)
    {
        if (_absenceCountdownText is not null) _absenceCountdownText.Text = text;
    }

    public void ShowValidatedFlash() => _validatedText.Visibility = Visibility.Visible;
}
