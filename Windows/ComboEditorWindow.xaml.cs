using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrawlhallaOverlay;

/// <summary>
/// Éditeur manuel d'une combo (option B de la partie 1.3 du plan) : chaque
/// étape est éditée comme du texte simple (actions séparées par "+", délais
/// en ms) plutôt qu'avec des dropdowns par action, pour rester rapide à
/// écrire même avec beaucoup d'étapes.
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
    private TextBox _damageNoteBox = null!;

    public Combo? Result { get; private set; }

    public ComboEditorWindow(Combo? existing)
    {
        InitializeComponent();
        _existing = existing;
        Build();
        Loaded += (_, _) => _nameBox.Focus();
    }

    private void Build()
    {
        var root = new StackPanel();

        root.Children.Add(new TextBlock { Text = _existing is null ? "Nouvelle combo" : "Modifier la combo", Foreground = TextColor, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });

        root.Children.Add(FieldLabel("Nom"));
        _nameBox = new TextBox { Text = _existing?.Name ?? "Nouvelle combo", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(_nameBox);

        root.Children.Add(FieldLabel("Description (optionnel)"));
        _descriptionBox = new TextBox { Text = _existing?.Description ?? "", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(_descriptionBox);

        var toleranceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        toleranceRow.Children.Add(FieldLabel("Tolérance par défaut (ms)", inline: true));
        _toleranceBox = new TextBox { Text = (_existing?.DefaultToleranceMs ?? 400).ToString(), Width = 70, Margin = new Thickness(6, 0, 20, 0) };
        toleranceRow.Children.Add(_toleranceBox);
        toleranceRow.Children.Add(FieldLabel("Mode", inline: true));
        _matchModeCombo = new ComboBox
        {
            Width = 160,
            Margin = new Thickness(6, 0, 0, 0),
            ItemsSource = new[] { "Strict", "Tolérant (ignore le mouvement pur)" },
            SelectedIndex = _existing?.MatchMode == MatchMode.IgnoreExtraneous ? 1 : 0,
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

        root.Children.Add(new TextBlock { Text = "Étapes (actions séparées par « + », dans l'ordre)", Foreground = TextColor, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 6) });

        // Le nom d'action doit être tapé exactement (accents compris) pour être
        // reconnu — sans ça, l'erreur "action inconnue" n'apparaît qu'après coup,
        // au clic sur Enregistrer. On rappelle donc ici le format ET la liste des
        // noms valides, tirée en direct des touches actuellement configurées.
        var validNames = string.Join(", ", AppState.Binds.Select(b => b.Action).OrderBy(a => a));
        root.Children.Add(new TextBlock
        {
            Text = $"Exemple : Droite + Att. légère    ·    Actions valides : {validNames}",
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
        var cancelBtn = new Button { Content = "Annuler", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        var saveBtn = new Button { Content = "Enregistrer", Padding = new Thickness(14, 5, 14, 5) };
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var actionsBox = new TextBox { Text = step is null ? "" : string.Join(" + ", step.RequiredActions), Margin = new Thickness(2), Tag = "actions" };
        Grid.SetColumn(actionsBox, 0);
        row.Children.Add(actionsBox);

        var maxBox = new TextBox { Text = step?.MaxDelayMs?.ToString() ?? "", Margin = new Thickness(2), Tag = "max", ToolTip = "Délai max (ms)" };
        Grid.SetColumn(maxBox, 1);
        row.Children.Add(maxBox);

        var minBox = new TextBox { Text = step?.MinDelayMs?.ToString() ?? "", Margin = new Thickness(2), Tag = "min", ToolTip = "Délai min (ms)" };
        Grid.SetColumn(minBox, 2);
        row.Children.Add(minBox);

        var removeBtn = new Button { Content = "✕", Margin = new Thickness(2) };
        Grid.SetColumn(removeBtn, 3);
        removeBtn.Click += (_, _) => _stepsPanel.Children.Remove(row);
        row.Children.Add(removeBtn);

        _stepsPanel.Children.Add(row);
    }

    private void SaveAndClose()
    {
        var knownActions = new HashSet<string>(AppState.Binds.Select(b => b.Action));
        var unknownActions = new HashSet<string>();

        var steps = new List<ComboStep>();
        foreach (var child in _stepsPanel.Children)
        {
            if (child is not Grid row) continue;

            var actionsBox = (TextBox)row.Children[0];
            var maxBox = (TextBox)row.Children[1];
            var minBox = (TextBox)row.Children[2];

            var actions = actionsBox.Text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            if (actions.Count == 0) continue;

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
                RequiredActions = actions,
                MaxDelayMs = isFirstStep ? null : (int.TryParse(maxBox.Text, out var max) ? max : null),
                MinDelayMs = isFirstStep ? null : (int.TryParse(minBox.Text, out var min) ? min : null),
            });
        }

        if (steps.Count == 0)
        {
            MessageBox.Show("Ajoute au moins une étape avec une action valide.", "Combo vide", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            Weapon = _existing?.Weapon ?? "",
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
