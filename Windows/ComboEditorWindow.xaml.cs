using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BrawlhallaOverlay;

/// <summary>
/// Éditeur manuel d'un combo : chaque étape est une ligne qu'on construit en
/// cliquant "Écouter" puis en appuyant sur UNE touche/bouton à la fois — exactement
/// la mécanique de réassignation de l'onglet Touches du panneau de contrôle, pas du
/// texte libre à taper. Une étape à plusieurs actions simultanées (ex. direction +
/// attaque) se construit en cliquant "Écouter" plusieurs fois de suite *sur la même
/// ligne* : chaque appui ajoute une puce ; c'est l'utilisateur qui décide explicitement
/// qu'un appui rejoint la même étape (en recliquant sur cette ligne) plutôt que d'en
/// démarrer une nouvelle (bouton "+ Ajouter une étape" séparé) — pas de détection
/// automatique par fenêtre de temps, plus fiable et plus prévisible.
/// </summary>
public partial class ComboEditorWindow : Window
{
    private static readonly Brush TextColor = Brushes.White;

    private readonly Combo? _existing;
    private readonly StackPanel _stepsPanel = new();
    private TextBox _nameBox = null!;
    private TextBox _descriptionBox = null!;
    private TextBox _toleranceBox = null!;
    private ComboBox _matchModeCombo = null!;
    private ComboBox _weaponCombo = null!;
    private ComboBox _legendCombo = null!;
    private TextBox _damageNoteBox = null!;

    // Une seule écoute active à la fois : un nouveau clic "Écouter" (ou la
    // fermeture de la fenêtre) coupe proprement celle en cours plutôt que de
    // laisser plusieurs handlers accrochés à AppState.Hook/Gamepad en parallèle.
    private Action? _cancelActiveListen;

    public Combo? Result { get; private set; }

    // Vrai quand cette fenêtre sert d'écran de relecture juste après un enregistrement
    // en direct (Ctrl+Alt+R) plutôt que d'édition d'un combo déjà sauvegardé — même
    // formulaire, juste un titre/aide différents pour que l'utilisateur comprenne qu'il
    // relit une capture pas encore validée (voir MainWindow.ReviewRecordedCombo et
    // docs/audit_features.md §1.5, qui sauvegardait auparavant direct sans relecture).
    private readonly bool _isRecordingReview;

    public ComboEditorWindow(Combo? existing, bool isRecordingReview = false)
    {
        InitializeComponent();
        _existing = existing;
        _isRecordingReview = isRecordingReview;
        Build();
        Loaded += (_, _) => _nameBox.Focus();
        Closed += (_, _) => _cancelActiveListen?.Invoke();
    }

    private void Build()
    {
        var root = new StackPanel();

        var titleText = _isRecordingReview ? "Vérifier le combo enregistré" : _existing is null ? "Nouveau combo" : "Modifier le combo";
        root.Children.Add(new TextBlock { Text = titleText, Foreground = TextColor, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
        if (_isRecordingReview)
        {
            root.Children.Add(new TextBlock
            {
                Text = "Vérifie les étapes capturées (supprime une étape parasite, ajuste les tolérances) avant de valider — rien n'est encore sauvegardé.",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }

        root.Children.Add(FieldLabel("Nom"));
        _nameBox = new TextBox { Text = _existing?.Name ?? "Nouveau combo", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(_nameBox);

        root.Children.Add(FieldLabel("Description (optionnel)"));
        _descriptionBox = new TextBox { Text = _existing?.Description ?? "", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(_descriptionBox);

        // Sans ça, un combo créé/enregistré à la main ne peut jamais être
        // rattachée à une arme (Combo.Weapon ne se remplissait auparavant que
        // via l'import des presets), donc elle devenait invisible dès qu'un
        // filtre d'arme était actif ailleurs dans l'app — voir
        // docs/audit_features.md §1.3.
        root.Children.Add(FieldLabel("Arme (optionnel — filtre la liste par arme)"));
        var weaponItems = new List<string> { "(aucune)" };
        weaponItems.AddRange(WeaponComboPresets.Weapons);
        _weaponCombo = new ComboBox
        {
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
            ItemsSource = weaponItems,
            SelectedItem = string.IsNullOrEmpty(_existing?.Weapon) ? "(aucune)" : _existing!.Weapon,
        };
        root.Children.Add(_weaponCombo);

        root.Children.Add(FieldLabel("Légend (optionnel — filtre la liste par légend)"));
        var legendItems = new List<string> { "(aucun)" };
        legendItems.AddRange(LegendComboPresets.Legends);
        _legendCombo = new ComboBox
        {
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
            ItemsSource = legendItems,
            SelectedItem = string.IsNullOrEmpty(_existing?.Legend) ? "(aucun)" : _existing!.Legend,
        };
        root.Children.Add(_legendCombo);

        var toleranceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        toleranceRow.Children.Add(FieldLabel("Tolérance par défaut (ms)", inline: true));
        _toleranceBox = new TextBox { Text = (_existing?.DefaultToleranceMs ?? 400).ToString(), Width = 70, Margin = new Thickness(6, 0, 20, 0) };
        toleranceRow.Children.Add(_toleranceBox);
        toleranceRow.Children.Add(FieldLabel("Mode", inline: true));
        _matchModeCombo = new ComboBox
        {
            Width = 160,
            Margin = new Thickness(6, 0, 0, 0),
            ItemsSource = new[] { "Refuser les directions en trop", "Les ignorer (mouvement libre)" },
            SelectedIndex = _existing?.MatchMode == MatchMode.IgnoreExtraneous ? 1 : 0,
            ToolTip = "Refuser les directions en trop : une direction tenue en plus de ce que demande l'étape (non demandée) fait échouer le combo. Les ignorer : ce mouvement pur hors combo n'est pas compté comme une faute.",
        };
        toleranceRow.Children.Add(_matchModeCombo);
        root.Children.Add(toleranceRow);

        root.Children.Add(FieldLabel("Limite de % de dégâts (optionnel, ex. \"true combo jusqu'à ~40%\")"));
        _damageNoteBox = new TextBox { Text = _existing?.DamageNote ?? "", Margin = new Thickness(0, 0, 0, 2) };
        root.Children.Add(_damageNoteBox);
        root.Children.Add(new TextBlock
        {
            Text = "Le knockback augmente avec les dégâts déjà subis par l'adversaire : certaines combos ne connectent que jusqu'à un certain %. Laisse vide si tu ne sais pas — mieux vaut vide que faux.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        root.Children.Add(new TextBlock { Text = "Étapes (dans l'ordre)", Foreground = TextColor, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 6) });

        root.Children.Add(new TextBlock
        {
            Text = "Clique « Écouter » sur une étape puis appuie sur une touche/bouton : ça ajoute une puce. Reclique sur la même étape pour ajouter une touche pressée en même temps (ex. Droite + Att. légère). « + Ajouter une étape » démarre la suivante.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var scroller = new ScrollViewer { Content = _stepsPanel, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroller);

        if (_existing is not null)
        {
            foreach (var step in _existing.Steps) AddStepRow(step);
        }
        else
        {
            AddStepRow(null);
        }

        var addStepBtn = new Button { Content = "+ Ajouter une étape", Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 16) };
        addStepBtn.Click += (_, _) => AddStepRow(null);
        root.Children.Add(addStepBtn);

        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = _isRecordingReview ? "Rejeter l'enregistrement" : "Annuler", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        var saveBtn = new Button { Content = _isRecordingReview ? "Valider et enregistrer" : "Enregistrer", Padding = new Thickness(14, 5, 14, 5) };
        saveBtn.Click += (_, _) => SaveAndClose();
        buttonsRow.Children.Add(cancelBtn);
        buttonsRow.Children.Add(saveBtn);
        root.Children.Add(buttonsRow);

        RootGrid.Children.Add(root);
    }

    private static TextBlock FieldLabel(string text, bool inline = false) => new()
    {
        Text = text,
        Foreground = TextColor,
        VerticalAlignment = inline ? VerticalAlignment.Center : VerticalAlignment.Top,
        Margin = inline ? new Thickness(0) : new Thickness(0, 0, 0, 2),
    };

    private void AddStepRow(ComboStep? step)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        // Liste mutable des actions de cette étape, alimentée uniquement par des
        // clics (une puce = une touche/bouton pressé, ajoutée en cliquant "Écouter"
        // puis en appuyant dessus) — stockée sur le Tag de la ligne pour que
        // SaveAndClose la relise directement, sans repasser par du texte.
        var stepActions = new List<string>(step?.RequiredActions ?? new List<string>());
        row.Tag = stepActions;

        var chipsPanel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chipsPanel, 0);
        row.Children.Add(chipsPanel);

        void RefreshChips()
        {
            chipsPanel.Children.Clear();
            foreach (var action in stepActions)
            {
                var chipText = new TextBlock { Text = action, Foreground = TextColor, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0) };
                var removeChipBtn = new Button { Content = "✕", FontSize = 9, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center };
                removeChipBtn.Click += (_, _) => { stepActions.Remove(action); RefreshChips(); };

                var chipContent = new StackPanel { Orientation = Orientation.Horizontal };
                chipContent.Children.Add(chipText);
                chipContent.Children.Add(removeChipBtn);

                chipsPanel.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(2),
                    Child = chipContent,
                });
            }

            var listenBtn = new Button
            {
                Content = "Écouter",
                Margin = new Thickness(2),
                ToolTip = "Appuie sur une touche/bouton pour l'ajouter à cette étape — reclique pour en ajouter une autre pressée en même temps",
            };
            listenBtn.Click += (_, _) =>
            {
                _cancelActiveListen?.Invoke();
                listenBtn.Content = "…";
                _cancelActiveListen = ListenForNextBindAction(
                    onCaptured: action =>
                    {
                        _cancelActiveListen = null;
                        if (!stepActions.Contains(action)) stepActions.Add(action);
                        RefreshChips();
                    },
                    onCancelled: () =>
                    {
                        _cancelActiveListen = null;
                        RefreshChips();
                    });
            };
            chipsPanel.Children.Add(listenBtn);
        }

        RefreshChips();

        var maxBox = new TextBox { Text = step?.MaxDelayMs?.ToString() ?? "", Margin = new Thickness(2), Tag = "max", ToolTip = "Délai max (ms)" };
        Grid.SetColumn(maxBox, 1);
        row.Children.Add(maxBox);

        var minBox = new TextBox { Text = step?.MinDelayMs?.ToString() ?? "", Margin = new Thickness(2), Tag = "min", ToolTip = "Délai min (ms)" };
        Grid.SetColumn(minBox, 2);
        row.Children.Add(minBox);

        var freeMovementBox = new CheckBox
        {
            IsChecked = step?.FreeMovement ?? false,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = "Tolère n'importe quelle direction tenue en plus sur cette étape, même avec « Refuser les directions en trop » (ex. un coup qui demande de se décaler pour toucher la hitbox)",
        };
        Grid.SetColumn(freeMovementBox, 3);
        row.Children.Add(freeMovementBox);

        var removeBtn = new Button { Content = "✕", Margin = new Thickness(2) };
        Grid.SetColumn(removeBtn, 4);
        removeBtn.Click += (_, _) => _stepsPanel.Children.Remove(row);
        row.Children.Add(removeBtn);

        _stepsPanel.Children.Add(row);
    }

    // Écoute une seule touche/bouton (même mécanique que ListenForNextKey de
    // l'onglet Touches) et la résout en nom d'action via KeyBind.VirtualKeyCodes.
    // Annulation automatique après 6s sans appui, pour ne pas rester bloqué en
    // écoute si l'utilisateur change d'avis. Retourne une action d'annulation
    // manuelle (utilisée si un autre "Écouter" est cliqué ou si la fenêtre se
    // ferme pendant l'écoute).
    private Action ListenForNextBindAction(Action<string> onCaptured, Action onCancelled)
    {
        var bindsByVk = new Dictionary<int, KeyBind>();
        foreach (var bind in AppState.Binds)
            foreach (var vk in bind.VirtualKeyCodes)
                bindsByVk[vk] = bind;

        var done = false;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };

        void OnKey(int vk)
        {
            if (!bindsByVk.TryGetValue(vk, out var bind)) return;
            Dispatcher.Invoke(() => Finish(bind.Action, cancelled: false));
        }

        void Unsubscribe()
        {
            timeout.Stop();
            AppState.Hook.KeyDown -= OnKey;
            AppState.Gamepad.ButtonDown -= OnKey;
        }

        void Finish(string? action, bool cancelled)
        {
            if (done) return;
            done = true;
            Unsubscribe();
            if (cancelled) onCancelled();
            else onCaptured(action!);
        }

        timeout.Tick += (_, _) => Finish(null, cancelled: true);
        AppState.Hook.KeyDown += OnKey;
        AppState.Gamepad.ButtonDown += OnKey;
        timeout.Start();

        return () => Finish(null, cancelled: true);
    }

    private void SaveAndClose()
    {
        var knownActions = new HashSet<string>(AppState.Binds.Select(b => b.Action));
        var unknownActions = new HashSet<string>();

        var steps = new List<ComboStep>();
        foreach (var child in _stepsPanel.Children)
        {
            if (child is not Grid row || row.Tag is not List<string> actions) continue;
            if (actions.Count == 0) continue;

            var maxBox = (TextBox)row.Children[1];
            var minBox = (TextBox)row.Children[2];
            var freeMovementBox = (CheckBox)row.Children[3];

            // Les puces ne peuvent contenir que des actions réellement existantes
            // (ListenForNextBindAction les résout via KeyBind.VirtualKeyCodes), donc
            // en pratique unknownActions reste toujours vide — gardé par sécurité
            // si un ComboStep chargé depuis un fichier importé contenait un nom
            // devenu invalide entre-temps (touche supprimée depuis).
            foreach (var action in actions)
            {
                if (!knownActions.Contains(action)) unknownActions.Add(action);
            }

            // MinDelayMs/MaxDelayMs n'ont aucun effet sur la première étape (pas
            // d'étape précédente pour mesurer un délai) : on les ignore plutôt que
            // de laisser croire qu'ils seront appliqués.
            var isFirstStep = steps.Count == 0;

            steps.Add(new ComboStep
            {
                RequiredActions = new List<string>(actions),
                MaxDelayMs = isFirstStep ? null : (int.TryParse(maxBox.Text, out var max) ? max : null),
                MinDelayMs = isFirstStep ? null : (int.TryParse(minBox.Text, out var min) ? min : null),
                FreeMovement = freeMovementBox.IsChecked ?? false,
            });
        }

        if (steps.Count == 0)
        {
            MessageBox.Show("Ajoute au moins une étape avec une touche écoutée.", "Combo vide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (unknownActions.Count > 0)
        {
            var validNames = string.Join(", ", knownActions.OrderBy(a => a));
            MessageBox.Show(
                $"Action(s) inconnue(s) : {string.Join(", ", unknownActions)}.\n" +
                $"Cette étape ne pourra jamais être validée en jeu tant que le nom ne correspond pas exactement à une action existante.\n\n" +
                $"Actions valides : {validNames}",
                "Action inconnue", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tolerance = int.TryParse(_toleranceBox.Text, out var t) ? t : 400;

        Result = new Combo
        {
            Id = _existing?.Id ?? Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "Combo" : _nameBox.Text.Trim(),
            Description = _descriptionBox.Text.Trim(),
            Weapon = _weaponCombo.SelectedItem as string == "(aucune)" ? "" : (_weaponCombo.SelectedItem as string ?? ""),
            Legend = _legendCombo.SelectedItem as string == "(aucun)" ? "" : (_legendCombo.SelectedItem as string ?? ""),
            Steps = steps,
            DefaultToleranceMs = tolerance,
            MatchMode = _matchModeCombo.SelectedIndex == 1 ? MatchMode.IgnoreExtraneous : MatchMode.Strict,
            DamageNote = _damageNoteBox.Text.Trim(),
            BestStreak = _existing?.BestStreak ?? 0,
            TotalCompletions = _existing?.TotalCompletions ?? 0,
            TotalAttempts = _existing?.TotalAttempts ?? 0,
            Mastered = _existing?.Mastered ?? false,
        };

        DialogResult = true;
        Close();
    }
}
