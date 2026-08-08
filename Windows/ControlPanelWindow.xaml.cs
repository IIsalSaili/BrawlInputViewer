using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BrawlhallaOverlay;

/// <summary>
/// Fenêtre de réglages classique (bordée, dans la taskbar) séparée de l'overlay :
/// navigation latérale + panneau de contenu par onglet. Tout ce qu'elle édite
/// passe par AppState, donc les changements se répercutent immédiatement sur
/// l'overlay sans redémarrage.
/// </summary>
public partial class ControlPanelWindow : Window
{
    private static readonly string[] TabNames = { "Général", "Touches", "Périphériques", "Combos", "Apparence", "À propos" };
    // Fond bleu-nuit/violet sombre (au lieu du gris neutre d'origine) + accent doré
    // repris de la tray icon (#E8C44A) : signature de marque cohérente avec l'overlay
    // et le motif "écusson" de l'UI Brawlhalla — voir Brawhl.md section 4/7.
    private static readonly Brush PanelBg = new SolidColorBrush(Color.FromRgb(0x18, 0x17, 0x22));
    private static readonly Brush CardBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x27, 0x35));
    private static readonly Brush TextColor = Brushes.White;
    private static readonly Brush SubtleText = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
    private static readonly Brush AccentGold = new SolidColorBrush(Color.FromRgb(0xE8, 0xC4, 0x4A));

    private ContentControl _content = null!;
    private ListBox _nav = null!;
    private Action? _unsubscribeCurrentTab;

    public ControlPanelWindow()
    {
        InitializeComponent();
        Build();
    }

    private void Build()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _nav = new ListBox
        {
            Background = PanelBg,
            BorderThickness = new Thickness(0),
            Foreground = TextColor,
            FontSize = 14,
            Padding = new Thickness(0, 12, 0, 0),
        };
        foreach (var name in TabNames)
        {
            _nav.Items.Add(new ListBoxItem { Content = name, Padding = new Thickness(16, 10, 16, 10) });
        }

        // Onglet actif marqué d'un liseré doré à gauche (accent de marque) plutôt que
        // la surbrillance système par défaut — BorderThickness posé même à l'état
        // non sélectionné (brush transparent) pour que la largeur ne saute pas au clic.
        var navItemStyle = new Style(typeof(ListBoxItem));
        navItemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(3, 0, 0, 0)));
        navItemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, AccentGold));
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, CardBg));
        navItemStyle.Triggers.Add(selectedTrigger);
        _nav.ItemContainerStyle = navItemStyle;

        _nav.SelectionChanged += (_, _) => ShowTab(_nav.SelectedIndex);
        // La nav garde le focus clavier par défaut, et ses flèches (ou même une
        // lettre comme "G", qui saute au premier onglet commençant par G via la
        // recherche incrémentale native du ListBox) sont aussi des touches de jeu
        // (Att. légère, Taunt...) : sans ça, jouer/enregistrer un combo pendant
        // que le panneau a le focus changeait d'onglet à chaque appui. Onglet =
        // souris uniquement.
        // Ne bloque QUE ce qui pilote réellement la sélection du ListBox (flèches, Home/End,
        // Espace/Entrée, et la recherche incrémentale native déclenchée par une lettre — "G"
        // sautait au premier onglet commençant par G, or G est la touche Taunt). Tab et
        // Shift+Tab restent disponibles : l'ancienne version marquait TOUT comme géré, ce qui
        // rendait la fenêtre entière inutilisable au clavier (audit 2026-08-07 §F3).
        _nav.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Tab or Key.LeftShift or Key.RightShift) return;
            e.Handled = true;
        };
        Grid.SetColumn(_nav, 0);
        root.Children.Add(_nav);

        _content = new ContentControl { Margin = new Thickness(20) };
        Grid.SetColumn(_content, 1);
        root.Children.Add(_content);

        RootGrid.Children.Add(root);

        _nav.SelectedIndex = 0;

        Closed += (_, _) =>
        {
            _cancelActiveListen?.Invoke();
            _unsubscribeCurrentTab?.Invoke();
        };
    }

    private void ShowTab(int index)
    {
        // Coupe toute écoute de touche encore armée avant de quitter l'onglet (§E4) : sinon elle
        // survivait au changement d'onglet et capturait la prochaine touche tapée n'importe où.
        _cancelActiveListen?.Invoke();
        _cancelActiveListen = null;

        _unsubscribeCurrentTab?.Invoke();
        _unsubscribeCurrentTab = null;

        _content.Content = index switch
        {
            0 => BuildGeneralTab(),
            1 => BuildKeybindsTab(),
            2 => BuildDevicesTab(),
            3 => BuildCombosTab(),
            4 => BuildAppearanceTab(),
            5 => BuildAboutTab(),
            _ => null,
        };
    }

    // --- Helpers de mise en page communs ---

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeights.Bold,
        Foreground = AccentGold,
        Margin = new Thickness(0, 0, 0, 14),
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = TextColor,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 10, 0),
    };

    private static TextBlock HelpText(string text) => new()
    {
        Text = text,
        Foreground = SubtleText,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 14),
    };

    // ================= Général =================

    private UIElement BuildGeneralTab()
    {
        // Découpage en deux niveaux (§5.5 du plan UX onboarding, docs/plan_ux_onboarding.md) :
        // l'onglet Général accumulait 12 réglages sans rapport entre eux, à plat, dans l'ordre où
        // ils ont été codés — un débutant devait trier 9 options qui ne le concernent pas encore.
        // "panel" ne garde que ce qui sert au premier usage ; "advancedPanel" (repliée par défaut
        // via un Expander) contient les réglages puissants — ils ne disparaissent pas, ils cessent
        // d'être un péage devant les réglages du quotidien.
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("Général"));

        var advancedPanel = new StackPanel();

        advancedPanel.Children.Add(new TextBlock { Text = "Profil de touches", Foreground = TextColor, Margin = new Thickness(0, 0, 0, 4) });
        var profileRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var profileCombo = new ComboBox { Width = 160, ItemsSource = KeyBindConfig.ListProfiles(), SelectedItem = AppState.Settings.ActiveProfile, Margin = new Thickness(0, 0, 6, 0) };
        profileCombo.SelectionChanged += (_, _) =>
        {
            if (profileCombo.SelectedItem is string name && name != AppState.Settings.ActiveProfile) AppState.SwitchProfile(name);
        };
        profileRow.Children.Add(profileCombo);

        var newProfileBtn = new Button { Content = "Nouveau (copie)", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0) };
        newProfileBtn.Click += (_, _) =>
        {
            var name = PromptForText("Nom du nouveau profil (copie du profil actif) :", "Nouveau profil");
            if (string.IsNullOrWhiteSpace(name)) return;
            AppState.DuplicateProfileAs(name.Trim());
            profileCombo.ItemsSource = KeyBindConfig.ListProfiles();
            profileCombo.SelectedItem = AppState.Settings.ActiveProfile;
        };
        profileRow.Children.Add(newProfileBtn);

        var deleteProfileBtn = new Button { Content = "Supprimer le profil actif", Padding = new Thickness(8, 4, 8, 4) };
        deleteProfileBtn.Click += (_, _) =>
        {
            if (AppState.Settings.ActiveProfile == KeyBindConfig.DefaultProfileName)
            {
                MessageBox.Show("Le profil « Défaut » ne peut pas être supprimé.", "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"Supprimer le profil « {AppState.Settings.ActiveProfile} » ?", "Confirmer", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            AppState.DeleteProfile(AppState.Settings.ActiveProfile);
            profileCombo.ItemsSource = KeyBindConfig.ListProfiles();
            profileCombo.SelectedItem = AppState.Settings.ActiveProfile;
        };
        profileRow.Children.Add(deleteProfileBtn);
        advancedPanel.Children.Add(profileRow);
        advancedPanel.Children.Add(HelpText("Utile pour un mapping AZERTY/QWERTY différent selon le PC, ou un jeu de touches distinct solo/équipe. Change les touches de l'onglet « Touches » ; les combos et l'apparence restent partagés entre profils."));

        var lockBtn = new Button { Content = LockLabel(), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        lockBtn.Click += (_, _) =>
        {
            AppState.ToggleLock();
            lockBtn.Content = LockLabel();
        };
        panel.Children.Add(lockBtn);
        panel.Children.Add(HelpText("Raccourci rapide en jeu : Ctrl+Alt+O."));

        var suspendBtn = new Button { Content = SuspendLabel(), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        suspendBtn.Click += (_, _) => AppState.ToggleCaptureSuspended();
        void SuspendHandler(bool _) => Dispatcher.Invoke(() => suspendBtn.Content = SuspendLabel());
        AppState.CaptureSuspendedChanged += SuspendHandler;
        panel.Children.Add(suspendBtn);
        panel.Children.Add(HelpText("Le suivi clavier/manette capte les touches partout sur le PC, même hors jeu (nécessaire pour fonctionner par-dessus Brawlhalla). Suspends la capture quand tu utilises juste ton PC normalement, sans quoi l'historique et le combo en cours du mode Tutoriel réagissent à ce que tu tapes ailleurs. Raccourci rapide : Ctrl+Alt+H."));

        var hideBtn = new Button { Content = HideOverlayLabel(), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        hideBtn.Click += (_, _) => AppState.ToggleOverlayHidden();
        void HideHandler(bool _) => Dispatcher.Invoke(() => hideBtn.Content = HideOverlayLabel());
        AppState.OverlayHiddenChanged += HideHandler;
        panel.Children.Add(hideBtn);
        panel.Children.Add(HelpText("Contrairement au verrouillage, ceci cache complètement l'overlay à l'écran (sans le fermer ni couper les raccourcis). Raccourci rapide : Ctrl+Alt+M."));

        var startupCheck = new CheckBox
        {
            Content = "Lancer automatiquement au démarrage de Windows",
            Foreground = TextColor,
            IsChecked = StartupConfig.IsEnabled(),
            Margin = new Thickness(0, 4, 0, 4),
        };
        startupCheck.Checked += (_, _) => { StartupConfig.SetEnabled(true); AppState.Settings.LaunchAtStartup = true; AppState.SaveSettings(); };
        startupCheck.Unchecked += (_, _) => { StartupConfig.SetEnabled(false); AppState.Settings.LaunchAtStartup = false; AppState.SaveSettings(); };
        advancedPanel.Children.Add(startupCheck);

        // Import/export de profil complet (docs/amelioration.md piste #11) : touches du profil
        // actif + combos + apparence en un seul fichier, pour changer de PC d'un coup plutôt que
        // de recréer profils et combos séparément (seul l'export/import combo par combo existait
        // jusque-là — ExportSelectedCombo/ImportCombo ci-dessous, gardés inchangés).
        advancedPanel.Children.Add(new TextBlock { Text = "Profil complet (touches + combos + apparence)", Foreground = TextColor, Margin = new Thickness(0, 16, 0, 4) });
        var bundleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var bundleExportBtn = new Button { Content = "Exporter tout", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        bundleExportBtn.Click += (_, _) => ExportProfileBundle();
        bundleRow.Children.Add(bundleExportBtn);
        var bundleImportBtn = new Button { Content = "Importer tout", Padding = new Thickness(10, 4, 10, 4) };
        bundleImportBtn.Click += (_, _) => ImportProfileBundle();
        bundleRow.Children.Add(bundleImportBtn);
        advancedPanel.Children.Add(bundleRow);
        advancedPanel.Children.Add(HelpText("Exporte les touches du profil actif, tous les combos et l'apparence en un seul fichier JSON. Importer REMPLACE entièrement les touches du profil actif et la liste de combos actuelle — demande confirmation avant d'écraser quoi que ce soit."));

        // Raccourcis combo suivante/précédente reconfigurables (demande explicite : le bouton
        // précédent était figé sur Ctrl+Alt+K sans équivalent clavier pour "précédente", qui
        // n'existait qu'à la manette). Seule la touche finale change ; le préfixe Ctrl+Alt reste
        // fixe comme tous les autres raccourcis pour éviter toute collision avec le jeu.
        advancedPanel.Children.Add(new TextBlock { Text = "Raccourcis clavier globaux (Ctrl+Alt+*)", Foreground = TextColor, Margin = new Thickness(0, 16, 0, 4) });
        advancedPanel.Children.Add(HelpText("Reconfigurables un par un pour éviter une collision avec un autre logiciel (OBS, Discord, un launcher...) sans devoir recompiler. Le préfixe Ctrl+Alt reste fixe. Clique directement sur la touche affichée pour la réassigner."));

        // Toutes les touches finales actuellement utilisées par un raccourci global, pour détecter
        // un conflit dès l'écoute plutôt qu'après coup (voir docs/amelioration.md piste #13).
        int[] AllShortcutVks() => new[]
        {
            AppState.Settings.LockVk,
            AppState.Settings.ComboNextVk,
            AppState.Settings.ComboPrevVk,
            AppState.Settings.RecordVk,
            AppState.Settings.DashboardVk,
            AppState.Settings.RevealVk,
            AppState.Settings.SuspendVk,
            AppState.Settings.HideVk,
        };

        UIElement ShortcutRow(string label, Func<int> getVk, Action<int> setVk)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(Label(label));
            var keyBtn = new Button
            {
                Content = $"Ctrl+Alt+{System.Windows.Input.KeyInterop.KeyFromVirtualKey(getVk())}",
                Foreground = TextColor,
                Background = CardBg,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x40, 0x55)),
                BorderThickness = new Thickness(1),
                Width = 130,
                Padding = new Thickness(0, 5, 0, 5),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Clique puis appuie sur la touche voulue.",
            };
            keyBtn.Click += (_, _) =>
            {
                _cancelActiveListen?.Invoke();
                var original = keyBtn.Content;
                keyBtn.Content = "… (6s)";
                ListenForNextKey(
                    onCaptured: vk =>
                    {
                        var current = getVk();
                        if (AllShortcutVks().Any(v => v != current && v == vk))
                        {
                            MessageBox.Show($"Ctrl+Alt+{System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk)} est déjà utilisé par un autre raccourci.", "Touche déjà utilisée", MessageBoxButton.OK, MessageBoxImage.Warning);
                            keyBtn.Content = original;
                            return;
                        }
                        setVk(vk);
                        AppState.SaveSettings();
                        keyBtn.Content = $"Ctrl+Alt+{System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk)}";
                    },
                    onCancelled: () => keyBtn.Content = original);
            };
            row.Children.Add(keyBtn);
            return row;
        }

        advancedPanel.Children.Add(ShortcutRow("Verrouiller/déverrouiller", () => AppState.Settings.LockVk, vk => AppState.Settings.LockVk = vk));
        advancedPanel.Children.Add(ShortcutRow("Combo suivante", () => AppState.Settings.ComboNextVk, vk => AppState.Settings.ComboNextVk = vk));
        advancedPanel.Children.Add(ShortcutRow("Combo précédente", () => AppState.Settings.ComboPrevVk, vk => AppState.Settings.ComboPrevVk = vk));
        advancedPanel.Children.Add(ShortcutRow("Enregistrer un combo", () => AppState.Settings.RecordVk, vk => AppState.Settings.RecordVk = vk));
        advancedPanel.Children.Add(ShortcutRow("Ouvrir l'accueil", () => AppState.Settings.DashboardVk, vk => AppState.Settings.DashboardVk = vk));
        advancedPanel.Children.Add(ShortcutRow("Révéler le combo", () => AppState.Settings.RevealVk, vk => AppState.Settings.RevealVk = vk));
        advancedPanel.Children.Add(ShortcutRow("Suspendre la capture", () => AppState.Settings.SuspendVk, vk => AppState.Settings.SuspendVk = vk));
        advancedPanel.Children.Add(ShortcutRow("Masquer l'overlay", () => AppState.Settings.HideVk, vk => AppState.Settings.HideVk = vk));
        advancedPanel.Children.Add(HelpText("Clique « Écouter » puis appuie sur la touche voulue (utilisée avec Ctrl+Alt, comme les autres raccourcis). Les boutons ◀/▶ du panneau de combo (mode Tutoriel) et le menu du tray affichent toujours la touche réellement configurée."));

        var keepStreakCheck = new CheckBox
        {
            Content = "Garder la série de réussites même après un combo raté",
            Foreground = TextColor,
            IsChecked = AppState.Settings.KeepStreakOnFail,
            Margin = new Thickness(0, 16, 0, 4),
        };
        keepStreakCheck.Checked += (_, _) => { AppState.Settings.KeepStreakOnFail = true; AppState.SaveSettings(); };
        keepStreakCheck.Unchecked += (_, _) => { AppState.Settings.KeepStreakOnFail = false; AppState.SaveSettings(); };
        advancedPanel.Children.Add(keepStreakCheck);

        var soundCheck = new CheckBox
        {
            Content = "Bips de succès/échec en mode Tutoriel",
            Foreground = TextColor,
            IsChecked = AppState.Settings.SoundEnabled,
            Margin = new Thickness(0, 4, 0, 4),
        };
        soundCheck.Checked += (_, _) => { AppState.Settings.SoundEnabled = true; AppState.SaveSettings(); };
        soundCheck.Unchecked += (_, _) => { AppState.Settings.SoundEnabled = false; AppState.SaveSettings(); };
        panel.Children.Add(soundCheck);

        var chainCheck = new CheckBox
        {
            Content = "Enchaîner automatiquement vers le combo suivant après réussite",
            Foreground = TextColor,
            IsChecked = AppState.Settings.ChainCombos,
            Margin = new Thickness(0, 4, 0, 4),
        };
        chainCheck.Checked += (_, _) => { AppState.Settings.ChainCombos = true; AppState.SaveSettings(); };
        chainCheck.Unchecked += (_, _) => { AppState.Settings.ChainCombos = false; AppState.SaveSettings(); };
        advancedPanel.Children.Add(chainCheck);

        var thresholdRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 4) };
        thresholdRow.Children.Add(Label("Réussites consécutives requises avant de passer à la suivante"));
        var thresholdBox = new TextBox { Text = AppState.Settings.ChainStreakThreshold.ToString(), Width = 40 };
        thresholdBox.LostFocus += (_, _) =>
        {
            if (int.TryParse(thresholdBox.Text, out var value) && value >= 1)
            {
                AppState.Settings.ChainStreakThreshold = value;
                AppState.SaveSettings();
            }
            else
            {
                thresholdBox.Text = AppState.Settings.ChainStreakThreshold.ToString();
            }
        };
        thresholdRow.Children.Add(thresholdBox);
        advancedPanel.Children.Add(thresholdRow);
        advancedPanel.Children.Add(HelpText("Session guidée : un combo n'est marqué « maîtrisé » et n'enchaîne vers le suivant qu'après ce nombre de réussites d'affilée (par défaut 1 = enchaîne dès la 1ère réussite)."));

        var autoHideCheck = new CheckBox
        {
            Content = "Estomper le panneau après une période d'inactivité",
            Foreground = TextColor,
            IsChecked = AppState.Settings.AutoHideEnabled,
            Margin = new Thickness(0, 16, 0, 4),
        };
        autoHideCheck.Checked += (_, _) => { AppState.Settings.AutoHideEnabled = true; AppState.SaveSettings(); };
        autoHideCheck.Unchecked += (_, _) => { AppState.Settings.AutoHideEnabled = false; AppState.SaveSettings(); };
        advancedPanel.Children.Add(autoHideCheck);

        var autoHideRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 4) };
        autoHideRow.Children.Add(Label("Secondes d'inactivité avant estompage"));
        var autoHideBox = new TextBox { Text = AppState.Settings.AutoHideIdleSeconds.ToString(), Width = 40 };
        autoHideBox.LostFocus += (_, _) =>
        {
            if (int.TryParse(autoHideBox.Text, out var value) && value >= 1)
            {
                AppState.Settings.AutoHideIdleSeconds = value;
                AppState.SaveSettings();
            }
            else
            {
                autoHideBox.Text = AppState.Settings.AutoHideIdleSeconds.ToString();
            }
        };
        autoHideRow.Children.Add(autoHideBox);
        advancedPanel.Children.Add(autoHideRow);
        advancedPanel.Children.Add(HelpText("Le panneau redevient pleinement visible dès le prochain appui — utile pour ne pas polluer l'écran pendant les phases sans combat."));

        var quizCheck = new CheckBox
        {
            Content = "Cacher les étapes (mémorisation) — masque les étapes pas encore jouées du combo",
            Foreground = TextColor,
            IsChecked = AppState.Settings.QuizMode,
            Margin = new Thickness(0, 4, 0, 4),
        };
        quizCheck.Checked += (_, _) => { AppState.Settings.QuizMode = true; AppState.SaveSettings(); };
        quizCheck.Unchecked += (_, _) => { AppState.Settings.QuizMode = false; AppState.SaveSettings(); };
        panel.Children.Add(quizCheck);

        var revealRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 4) };
        var revealBtn = new Button { Content = "Révéler le combo (3s)", Padding = new Thickness(10, 4, 10, 4) };
        revealBtn.Click += (_, _) => AppState.RequestQuizReveal();
        revealRow.Children.Add(revealBtn);
        panel.Children.Add(revealRow);
        panel.Children.Add(HelpText("Force à se souvenir du combo plutôt que de le lire. Ctrl+Alt+I (en jeu) ou ce bouton révèlent temporairement (3s) les étapes masquées."));

        var gamepadStatus = new TextBlock { Foreground = SubtleText, Margin = new Thickness(0, 8, 0, 4) };
        gamepadStatus.Text = AppState.Gamepad.Connected ? "Manette détectée." : "Aucune manette détectée (facultatif — voir l'onglet Touches pour assigner des boutons).";
        panel.Children.Add(gamepadStatus);

        advancedPanel.Children.Add(SectionTitle("Session"));
        var sessionInfo = new TextBlock { Foreground = SubtleText, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        UpdateSessionInfo(sessionInfo);
        advancedPanel.Children.Add(sessionInfo);

        var sessionRow = new StackPanel { Orientation = Orientation.Horizontal };
        var refreshBtn = new Button { Content = "Actualiser", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        refreshBtn.Click += (_, _) => UpdateSessionInfo(sessionInfo);
        sessionRow.Children.Add(refreshBtn);

        var exportSessionBtn = new Button { Content = "Exporter la session (CSV)", Padding = new Thickness(10, 4, 10, 4) };
        exportSessionBtn.Click += (_, _) => ExportSessionCsv();
        sessionRow.Children.Add(exportSessionBtn);
        advancedPanel.Children.Add(sessionRow);

        advancedPanel.Children.Add(SectionTitle("Détection de hit (expérimental)"));
        // Texte réécrit lors de l'audit 2026-08-07 (§E3) : il affirmait « Purement informatif :
        // ne fait jamais échouer ni réussir un combo », ce qui est faux depuis la Version 24 —
        // c'est même devenu le mécanisme le plus impactant de l'app. Un utilisateur dont les
        // combos étaient invalidés n'avait aucune raison de soupçonner ce réglage : l'aide en
        // place le disculpait explicitement.
        advancedPanel.Children.Add(HelpText("⚠ Ce réglage CONDITIONNE la validation des combos : quand la zone surveillée est active, un combo n'est compté comme réussi que si le HUD confirme un hit pour chaque coup porté. Il surveille une petite zone du HUD (dégâts de l'ADVERSAIRE) et détecte qu'elle a changé — aucun montant n'est lu. Sécurité intégrée : tant que la zone n'a rien montré de vivant (mauvaise résolution, jeu fermé, HUD ailleurs), la validation N'EST PAS conditionnée et l'app se comporte comme si ce réglage était éteint. Nécessite \"Nombre de dégâts\" activé dans les réglages Brawlhalla, et un calibrage par résolution d'écran."));

        var hudRoiStatus = new TextBlock { Foreground = SubtleText, Margin = new Thickness(0, 0, 0, 4) };
        void UpdateHudRoiStatus()
        {
            if (!AppState.Settings.HudRoiCalibrated)
            {
                hudRoiStatus.Text = "Aucune zone calibrée pour l'instant.";
                return;
            }

            // Dit si la zone est réellement VIVANTE, pas seulement "renseignée" (audit
            // 2026-08-07 §C1/§F8) : les coordonnées par défaut viennent d'un seul setup et sont
            // persistées dès le tout premier lancement, donc "calibrée" ne voulait rien dire
            // quant à savoir si elle regarde le bon endroit. C'est cette distinction qui décide
            // si la validation des combos est conditionnée ou non.
            var roi = $"{AppState.Settings.HudRoiWidth}×{AppState.Settings.HudRoiHeight}px à ({AppState.Settings.HudRoiX},{AppState.Settings.HudRoiY})";
            hudRoiStatus.Text = AppState.HudDamageSource.HasLiveSignal
                ? $"Zone {roi} — active : un changement y a été détecté récemment, la validation des combos EST conditionnée par les hits."
                : $"Zone {roi} — inerte : rien n'y a bougé récemment, la validation des combos n'est PAS conditionnée (lance Brawlhalla, ou recalibre si ta résolution diffère).";
        }
        UpdateHudRoiStatus();
        advancedPanel.Children.Add(hudRoiStatus);

        var hudEnabledCheck = new CheckBox
        {
            Content = "Activer la détection de hit",
            Foreground = TextColor,
            IsChecked = AppState.Settings.HudDetectionEnabled,
            IsEnabled = AppState.Settings.HudRoiCalibrated,
            Margin = new Thickness(0, 4, 0, 4),
        };
        hudEnabledCheck.Checked += (_, _) => { AppState.Settings.HudDetectionEnabled = true; AppState.SaveSettings(); };
        hudEnabledCheck.Unchecked += (_, _) => { AppState.Settings.HudDetectionEnabled = false; AppState.SaveSettings(); };

        var hudRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var calibrateBtn = new Button { Content = "Calibrer la zone…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        calibrateBtn.Click += (_, _) =>
        {
            Hide();
            var calib = new HudCalibrationWindow();
            calib.ShowDialog();
            Show();
            if (calib.Result is { } r)
            {
                AppState.Settings.HudRoiX = r.X;
                AppState.Settings.HudRoiY = r.Y;
                AppState.Settings.HudRoiWidth = r.Width;
                AppState.Settings.HudRoiHeight = r.Height;
                AppState.Settings.HudRoiCalibrated = true;
                AppState.SaveSettings();
                UpdateHudRoiStatus();
                hudEnabledCheck.IsEnabled = true;
                hudEnabledCheck.IsChecked = true; // active tout de suite pour un retour en direct sans clic supplémentaire
            }
        };
        hudRow.Children.Add(calibrateBtn);
        advancedPanel.Children.Add(hudRow);
        advancedPanel.Children.Add(hudEnabledCheck);

        // Sorti en réglage lors de l'audit 2026-08-07 (§F9) : c'est le paramètre du chemin
        // critique qui a demandé le plus de réajustements en test réel (900 → 400 → 250 → 600 ms),
        // et il dépend du setup — le figer en constante obligeait à recompiler pour l'ajuster.
        var hitWindowRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 4, 0, 4) };
        hitWindowRow.Children.Add(Label("Délai accordé au HUD pour confirmer un coup (ms)"));
        var hitWindowBox = new TextBox { Text = AppState.Settings.HitConfirmationWindowMs.ToString(), Width = 60 };
        hitWindowBox.LostFocus += (_, _) =>
        {
            if (int.TryParse(hitWindowBox.Text, out var value) && value >= 100 && value <= 3000)
            {
                AppState.Settings.HitConfirmationWindowMs = value;
                AppState.SaveSettings();
            }
            else
            {
                hitWindowBox.Text = AppState.Settings.HitConfirmationWindowMs.ToString();
            }
        };
        hitWindowRow.Children.Add(hitWindowBox);
        advancedPanel.Children.Add(hitWindowRow);
        advancedPanel.Children.Add(HelpText("Entre 100 et 3000 ms (600 par défaut). Trop bas : de vrais coups qui touchent sont invalidés avant que le HUD ait eu le temps de l'afficher. Trop haut : un coup porté dans le vide met plus longtemps à être détecté comme raté."));

        // Retour en direct (pas juste au moment d'un vrai hit) : sans ça, la seule façon de
        // vérifier que le calibrage vise la bonne zone était d'attendre un hit en jeu — demande
        // explicite de l'utilisateur. S'abonne à Sampled (levé à chaque capture, changement ou
        // non), désabonné avec le reste de l'onglet.
        var hudLiveText = new TextBlock { Foreground = SubtleText, Margin = new Thickness(0, 0, 0, 12), FontFamily = new FontFamily("Consolas") };
        hudLiveText.Text = AppState.Settings.HudDetectionEnabled ? "En attente d'une lecture…" : "Inactif (coche \"Activer la détection de hit\" pour voir un retour en direct).";
        advancedPanel.Children.Add(hudLiveText);
        void HudSampledHandler(double ratio) => Dispatcher.Invoke(() =>
        {
            hudLiveText.Text = $"Dernière lecture : {ratio * 100:0.0} % de la zone a changé.";
            UpdateHudRoiStatus(); // l'état actif/inerte évolue tout seul au fil du jeu
        });
        AppState.HudDamageSource.Sampled += HudSampledHandler;

        advancedPanel.Children.Add(HelpText("Palier de dégâts par couleur : lit la couleur de la barre sous l'icône ADVERSAIRE (Blanc/Jaune/Orange/Rouge/Noir, les paliers officiels 0/50/100/150/200%) — aucun réglage de jeu requis, contrairement à la détection de hit ci-dessus."));

        var hudTierRoiStatus = new TextBlock { Foreground = SubtleText, Margin = new Thickness(0, 0, 0, 4) };
        void UpdateHudTierRoiStatus()
        {
            hudTierRoiStatus.Text = AppState.Settings.HudTierRoiCalibrated
                ? $"Zone calibrée : {AppState.Settings.HudTierRoiWidth}×{AppState.Settings.HudTierRoiHeight}px à ({AppState.Settings.HudTierRoiX},{AppState.Settings.HudTierRoiY})."
                : "Aucune zone calibrée pour l'instant.";
        }
        UpdateHudTierRoiStatus();
        advancedPanel.Children.Add(hudTierRoiStatus);

        var hudTierEnabledCheck = new CheckBox
        {
            Content = "Activer le palier de dégâts par couleur",
            Foreground = TextColor,
            IsChecked = AppState.Settings.HudTierDetectionEnabled,
            IsEnabled = AppState.Settings.HudTierRoiCalibrated,
            Margin = new Thickness(0, 4, 0, 4),
        };
        hudTierEnabledCheck.Checked += (_, _) => { AppState.Settings.HudTierDetectionEnabled = true; AppState.SaveSettings(); };
        hudTierEnabledCheck.Unchecked += (_, _) => { AppState.Settings.HudTierDetectionEnabled = false; AppState.SaveSettings(); };

        var hudTierRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var calibrateTierBtn = new Button { Content = "Calibrer la couleur…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        calibrateTierBtn.Click += (_, _) =>
        {
            Hide();
            var calib = new HudCalibrationWindow("Dessine un PETIT rectangle collé à la barre de couleur sous l'icône de l'ADVERSAIRE (blanc/jaune/orange/rouge/noir selon ses dégâts). Entrée pour valider, Échap pour annuler.");
            calib.ShowDialog();
            Show();
            if (calib.Result is { } r)
            {
                AppState.Settings.HudTierRoiX = r.X;
                AppState.Settings.HudTierRoiY = r.Y;
                AppState.Settings.HudTierRoiWidth = r.Width;
                AppState.Settings.HudTierRoiHeight = r.Height;
                AppState.Settings.HudTierRoiCalibrated = true;
                AppState.SaveSettings();
                UpdateHudTierRoiStatus();
                hudTierEnabledCheck.IsEnabled = true;
                hudTierEnabledCheck.IsChecked = true; // active tout de suite pour un retour en direct sans clic supplémentaire
            }
        };
        hudTierRow.Children.Add(calibrateTierBtn);
        advancedPanel.Children.Add(hudTierRow);
        advancedPanel.Children.Add(hudTierEnabledCheck);

        // Même principe que le retour en direct de la détection de hit ci-dessus : un carré de
        // la couleur réellement lue + le palier déduit, mis à jour à chaque capture.
        var hudTierLiveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var hudTierSwatch = new Border { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), BorderBrush = SubtleText, BorderThickness = new Thickness(1), Background = Brushes.Transparent };
        var hudTierLiveText = new TextBlock { Foreground = SubtleText, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
        hudTierLiveText.Text = AppState.Settings.HudTierDetectionEnabled ? "En attente d'une lecture…" : "Inactif (coche \"Activer le palier de dégâts par couleur\" pour voir un retour en direct).";
        hudTierLiveRow.Children.Add(hudTierSwatch);
        hudTierLiveRow.Children.Add(hudTierLiveText);
        advancedPanel.Children.Add(hudTierLiveRow);
        void HudTierSampledHandler((int R, int G, int B, DamageTier? HueHint) sample) => Dispatcher.Invoke(() =>
        {
            // Même principe que MainWindow.OnHudTierSampled. Ici on garde les deux valeurs (la
            // lecture brute de CETTE capture et le palier stabilisé) : c'est l'écran de calibrage,
            // voir la teinte instantanée sauter est précisément ce qui permet de repérer une zone
            // mal cadrée. En jeu, l'overlay n'en montre qu'une (voir MainWindow).
            hudTierSwatch.Background = new SolidColorBrush(Color.FromRgb((byte)sample.R, (byte)sample.G, (byte)sample.B));
            var hueNote = sample.HueHint is { } t ? HudTierLabel(t) : "non concluant";
            var stable = AppState.HudTierSource.CurrentTier is { } s ? HudTierLabel(s) : "pas encore stabilisé";
            hudTierLiveText.Text = $"rgb({sample.R},{sample.G},{sample.B}) — lecture : {hueNote}  ·  palier retenu : {stable}";
        });
        AppState.HudTierSource.Sampled += HudTierSampledHandler;

        var advancedExpander = new Expander
        {
            Header = "Réglages avancés",
            Foreground = TextColor,
            IsExpanded = false,
            Margin = new Thickness(0, 20, 0, 0),
            Content = advancedPanel,
        };
        panel.Children.Add(advancedExpander);

        _unsubscribeCurrentTab = () =>
        {
            AppState.CaptureSuspendedChanged -= SuspendHandler;
            AppState.OverlayHiddenChanged -= HideHandler;
            AppState.HudDamageSource.Sampled -= HudSampledHandler;
            AppState.HudTierSource.Sampled -= HudTierSampledHandler;
        };

        return Wrap(panel);
    }

    private static void UpdateSessionInfo(TextBlock target)
    {
        var elapsed = DateTime.UtcNow - AppState.SessionStart;
        var minutes = Math.Max(elapsed.TotalMinutes, 1.0 / 60);
        var count = AppState.SessionLog.Count;
        var perMinute = count / minutes;
        target.Text = $"{count} coup(s) joué(s) depuis {FormatElapsed(elapsed)} · {perMinute:0.0} actions/min.";
    }

    private static string FormatElapsed(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}h{span.Minutes:00}" : $"{span.Minutes}m{span.Seconds:00}";

    private static void ExportSessionCsv()
    {
        if (AppState.SessionLog.Count == 0)
        {
            MessageBox.Show("Aucun coup joué dans cette session pour l'instant.", "Session vide", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"session_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };
        if (dialog.ShowDialog() != true) return;

        var lines = new List<string> { "Horodatage,Coups" };
        foreach (var (time, actions) in AppState.SessionLog)
        {
            lines.Add($"{time:O},\"{actions.Replace("\"", "'")}\"");
        }
        File.WriteAllLines(dialog.FileName, lines);
        MessageBox.Show($"Session exportée : {AppState.SessionLog.Count} coup(s).", "Export terminé", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string HudTierLabel(DamageTier tier) => tier switch
    {
        DamageTier.White => "Blanc (0-49%)",
        DamageTier.Yellow => "Jaune (50-99%)",
        DamageTier.Orange => "Orange (100-149%)",
        DamageTier.Red => "Rouge (150-199%)",
        DamageTier.Black => "Noir (200%+)",
        _ => tier.ToString(),
    };

    private static string LockLabel() => AppState.Locked ? "Déverrouiller l'overlay" : "Verrouiller l'overlay";
    private static string SuspendLabel() => AppState.CaptureSuspended ? "Reprendre la capture" : "Suspendre la capture (hors du jeu)";
    private static string HideOverlayLabel() => AppState.OverlayHidden ? "Réafficher l'overlay" : "Masquer complètement l'overlay";

    // ================= Touches =================

    private UIElement BuildKeybindsTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("Touches"));
        panel.Children.Add(HelpText("Modifie le nom de chaque action. Le bouton Touches affiche la/les touche(s) actuelles — clique dessus puis appuie sur ce que tu veux assigner (tiens plusieurs touches ensemble pour un combo, ex. Shift+Bas), exactement comme dans n'importe quel jeu. Il se borde en rouge dès qu'une touche qu'il contient est aussi utilisée par une autre action — pas besoin d'attendre l'enregistrement pour le voir. Bouton 🎮 : bouton manette facultatif (en plus des touches, pas à la place), le ✕ à côté le retire."));

        var rows = new List<(KeyBind Bind, TextBox Label, Button Keys, Button Gamepad)>();

        const string KeysTooltipBase = "Clique puis appuie sur la/les touche(s) voulues (tiens-les ensemble pour un combo, ex. Shift+Bas).";
        const string GamepadTooltipBase = "Bouton manette (facultatif) — clique puis appuie sur un bouton de la manette pour l'assigner.";

        // Détection de conflit de touche en temps réel (à chaque capture, pas seulement à la
        // sauvegarde) : recalculée après chaque réassignation, plutôt que devoir enregistrer
        // pour découvrir un conflit. Purement visuel (bordure rouge) — la validation bloquante
        // reste dans saveBtn.
        void RecomputeKeyConflicts()
        {
            var buttonsByKey = new Dictionary<string, List<Button>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, _, keysBtn, _) in rows)
            {
                foreach (var keyName in (List<string>)keysBtn.Tag)
                {
                    if (!buttonsByKey.TryGetValue(keyName, out var list))
                    {
                        list = new List<Button>();
                        buttonsByKey[keyName] = list;
                    }
                    list.Add(keysBtn);
                }
            }

            var conflicted = new HashSet<Button>();
            foreach (var buttons in buttonsByKey.Values)
            {
                if (buttons.Count > 1) foreach (var b in buttons) conflicted.Add(b);
            }

            foreach (var (_, _, keysBtn, _) in rows)
            {
                if (conflicted.Contains(keysBtn))
                {
                    keysBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                    keysBtn.BorderThickness = new Thickness(2);
                    keysBtn.ToolTip = "Cette touche est aussi utilisée par une autre action — un seul bouton gagnera à l'enregistrement.";
                }
                else
                {
                    keysBtn.ClearValue(BorderBrushProperty);
                    keysBtn.ClearValue(BorderThicknessProperty);
                    keysBtn.ToolTip = KeysTooltipBase;
                }
            }
        }

        // Bouton "keycap" standard (fond distinct, coins arrondis, curseur main) réutilisé pour
        // Touches et Manette — retour utilisateur explicite : un champ en lecture seule qui
        // ressemble à un TextBox ne se lit pas comme cliquable, un vrai bouton si.
        Button KeycapButton(string content, object? tag, string tooltip)
        {
            var btn = new Button
            {
                Content = content,
                Tag = tag,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(2),
                MinWidth = 90,
                Background = CardBg,
                Foreground = TextColor,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x40, 0x55)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = tooltip,
            };
            return btn;
        }

        foreach (var bind in AppState.Binds)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            var actionText = new TextBlock { Text = bind.Action, Foreground = TextColor, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(actionText, 0);
            row.Children.Add(actionText);

            var labelBox = new TextBox { Text = bind.Label, Margin = new Thickness(2) };
            Grid.SetColumn(labelBox, 1);
            row.Children.Add(labelBox);

            // Bouton = touche(s) actuelle(s), cliquer dessus écoute immédiatement le prochain
            // appui (ou plusieurs touches tenues ensemble, capturées jusqu'au relâchement).
            var keysBtn = KeycapButton(string.Join(" + ", bind.Keys), new List<string>(bind.Keys), KeysTooltipBase);
            Grid.SetColumn(keysBtn, 2);
            keysBtn.Click += (_, _) =>
            {
                _cancelActiveListen?.Invoke();
                var original = keysBtn.Content;
                keysBtn.Content = "… (6s)";
                ListenForNextKeyChord(
                    onCaptured: names =>
                    {
                        keysBtn.Tag = names;
                        keysBtn.Content = string.Join(" + ", names);
                        RecomputeKeyConflicts();
                    },
                    onCancelled: () => keysBtn.Content = original);
            };
            row.Children.Add(keysBtn);

            var gamepadName = bind.GamepadButtons.Count > 0 ? bind.GamepadButtons[0] : null;
            var gamepadBtn = KeycapButton(gamepadName ?? "🎮 —", gamepadName, GamepadTooltipBase);
            Grid.SetColumn(gamepadBtn, 3);
            gamepadBtn.Click += (_, _) =>
            {
                _cancelActiveListen?.Invoke();
                var original = gamepadBtn.Content;
                gamepadBtn.Content = "… (6s)";
                ListenForNextGamepadButton(
                    onCaptured: name =>
                    {
                        gamepadBtn.Tag = name;
                        gamepadBtn.Content = name;
                    },
                    onCancelled: () => gamepadBtn.Content = original);
            };
            row.Children.Add(gamepadBtn);

            var clearGamepadBtn = new Button
            {
                Content = "✕",
                Padding = new Thickness(0),
                Margin = new Thickness(2),
                Background = Brushes.Transparent,
                Foreground = SubtleText,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Retirer le bouton manette assigné.",
            };
            Grid.SetColumn(clearGamepadBtn, 4);
            clearGamepadBtn.Click += (_, _) =>
            {
                gamepadBtn.Tag = null;
                gamepadBtn.Content = "🎮 —";
            };
            row.Children.Add(clearGamepadBtn);

            rows.Add((bind, labelBox, keysBtn, gamepadBtn));

            var wrapper = new StackPanel();
            wrapper.Children.Add(row);
            panel.Children.Add(wrapper);
        }

        var saveBtn = new Button { Content = "Enregistrer les touches", Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
        saveBtn.Click += (_, _) =>
        {
            // Valide tout avant de rien muter : sinon une seule touche invalide ou
            // en double laisse AppState.Binds à moitié modifié en mémoire alors que
            // rien n'est sauvegardé sur disque (état overlay/panneau désynchronisé).
            var parsedKeys = new List<List<string>>();
            var parsedGamepad = new List<List<string>>();
            var seenKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seenGamepadButtons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (bind, _, keysBtn, gamepadBtn) in rows)
            {
                // Les touches viennent toujours d'une capture réelle (KeyInterop), jamais d'une
                // saisie texte libre depuis le passage aux boutons keycap — plus besoin de
                // valider le nom, seul le doublon entre actions reste à vérifier.
                var keys = (List<string>)keysBtn.Tag;

                if (keys.Count == 0)
                {
                    MessageBox.Show($"« {bind.Action} » n'a aucune touche assignée.", "Touches invalides", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                foreach (var keyName in keys)
                {
                    if (seenKeys.TryGetValue(keyName, out var otherAction) && otherAction != bind.Action)
                    {
                        MessageBox.Show($"La touche « {keyName} » est déjà utilisée par « {otherAction} ». Une même touche ne peut déclencher qu'une seule action.", "Touche en double", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    seenKeys[keyName] = bind.Action;
                }
                parsedKeys.Add(keys);

                var gamepadName = gamepadBtn.Tag as string;
                var gamepadButtons = gamepadName is null ? new List<string>() : new List<string> { gamepadName };
                if (gamepadName is not null)
                {
                    if (seenGamepadButtons.TryGetValue(gamepadName, out var otherAction) && otherAction != bind.Action)
                    {
                        MessageBox.Show($"Le bouton « {gamepadName} » est déjà utilisé par « {otherAction} ».", "Bouton manette en double", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    seenGamepadButtons[gamepadName] = bind.Action;
                }
                parsedGamepad.Add(gamepadButtons);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var (bind, labelBox, _, _) = rows[i];
                bind.Label = labelBox.Text.Trim();
                bind.Keys = parsedKeys[i];
                bind.GamepadButtons = parsedGamepad[i];
            }
            AppState.NotifyBindsMutated();
            saveBtn.Content = "Enregistré ✓";
        };
        panel.Children.Add(saveBtn);

        return Wrap(panel);
    }

    /// <summary>Touches qui ne peuvent pas servir de touche finale à un raccourci Ctrl+Alt+* :
    /// les modificateurs eux-mêmes. Sans ce filtre, appuyer naturellement sur Ctrl+Alt+X pour
    /// « taper le raccourci » capturait d'abord Ctrl — le raccourci devenait Ctrl+Alt+Ctrl,
    /// c'est-à-dire déclenché en permanence dès qu'on tient Ctrl+Alt (audit 2026-08-07 §E4).</summary>
    private static readonly HashSet<int> ModifierVks = new()
    {
        0x10, 0xA0, 0xA1, // Shift, LShift, RShift
        0x11, 0xA2, 0xA3, // Ctrl, LCtrl, RCtrl
        0x12, 0xA4, 0xA5, // Alt, LAlt, RAlt
        0x5B, 0x5C,       // LWin, RWin
    };

    /// <summary>Écoute d'une touche unique, annulable. Retourne l'action d'annulation.
    ///
    /// Audit 2026-08-07 §E4 : l'ancienne version s'abonnait au hook global et ne se désabonnait
    /// qu'au premier appui — sans timeout, sans annulation, sans nettoyage à la fermeture de la
    /// fenêtre ou au changement d'onglet. Cliquer le bouton puis changer d'avis laissait l'écoute
    /// armée indéfiniment : la prochaine touche tapée n'importe où (dans le jeu, un navigateur…)
    /// devenait le raccourci. Le bon patron existait déjà dans le repo
    /// (ComboEditorWindow.ListenForNextBindAction), il n'était juste pas utilisé ici.</summary>
    private Action ListenForNextKey(Action<int> onCaptured, Action? onCancelled = null)
    {
        var done = false;
        var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };

        void Finish(int vk, bool cancelled)
        {
            if (done) return;
            done = true;
            timeout.Stop();
            AppState.Hook.KeyDown -= Handler;
            AppState.BindingCaptureActive = false;
            if (cancelled) onCancelled?.Invoke();
            else onCaptured(vk);
        }

        void Handler(int vk)
        {
            // Un modificateur seul n'est jamais une touche finale valide : on continue d'écouter
            // au lieu de le capturer, pour que taper le chord entier fonctionne naturellement.
            if (ModifierVks.Contains(vk)) return;
            Dispatcher.Invoke(() => Finish(vk, cancelled: false));
        }

        timeout.Tick += (_, _) => Finish(0, cancelled: true);
        AppState.BindingCaptureActive = true;
        AppState.Hook.KeyDown += Handler;
        timeout.Start();

        _cancelActiveListen = () => Finish(0, cancelled: true);
        return _cancelActiveListen;
    }

    /// <summary>Écoute en cours (touche ou chord), coupée si une autre démarre, si l'onglet
    /// change ou si la fenêtre se ferme.</summary>
    private Action? _cancelActiveListen;

    // Capture toutes les touches tenues ensemble (chord) jusqu'au premier relâchement, pour
    // pouvoir réassigner en un seul geste un combo comme Shift+Bas (Esquive) plutôt que de
    // ne capturer qu'une touche à la fois.
    private void ListenForNextKeyChord(Action<List<string>> onCaptured, Action? onCancelled = null)
    {
        var pressed = new List<int>();
        var done = false;
        var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };

        void Finish(List<string>? names, bool cancelled)
        {
            if (done) return;
            done = true;
            timeout.Stop();
            AppState.Hook.KeyDown -= KeyDownHandler;
            AppState.Hook.KeyUp -= KeyUpHandler;
            AppState.BindingCaptureActive = false;
            if (cancelled) onCancelled?.Invoke();
            else onCaptured(names!);
        }

        void KeyDownHandler(int vk)
        {
            if (!pressed.Contains(vk)) pressed.Add(vk);
        }

        void KeyUpHandler(int vk)
        {
            if (pressed.Count == 0) pressed.Add(vk);
            var names = pressed.Select(v => System.Windows.Input.KeyInterop.KeyFromVirtualKey(v).ToString()).ToList();
            Dispatcher.Invoke(() => Finish(names, cancelled: false));
        }

        // Même annulabilité que ListenForNextKey (§E4) : une écoute de chord laissée en plan
        // capturait sinon, elle aussi, la prochaine touche tapée n'importe où sur le PC.
        timeout.Tick += (_, _) => Finish(null, cancelled: true);
        AppState.BindingCaptureActive = true;
        AppState.Hook.KeyDown += KeyDownHandler;
        AppState.Hook.KeyUp += KeyUpHandler;
        timeout.Start();

        _cancelActiveListen = () => Finish(null, cancelled: true);
    }

    private void ListenForNextGamepadButton(Action<string> onCaptured, Action? onCancelled = null)
    {
        var done = false;
        var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };

        void Finish(string? name, bool cancelled)
        {
            if (done) return;
            done = true;
            timeout.Stop();
            AppState.Gamepad.ButtonDown -= Handler;
            AppState.BindingCaptureActive = false;
            if (cancelled) onCancelled?.Invoke();
            else onCaptured(name!);
        }

        void Handler(int syntheticCode)
        {
            var name = GamepadHook.Buttons.FirstOrDefault(b => GamepadHook.SyntheticCodeBase + b.Flag == syntheticCode).Name;
            if (name is not null) Dispatcher.Invoke(() => Finish(name, cancelled: false));
        }

        timeout.Tick += (_, _) => Finish(null, cancelled: true);
        AppState.BindingCaptureActive = true;
        AppState.Gamepad.ButtonDown += Handler;
        timeout.Start();

        _cancelActiveListen = () => Finish(null, cancelled: true);
    }

    // ================= Périphériques =================

    // Panneau de diagnostic façon "test des périphériques" de Discord (demande explicite de
    // l'utilisateur après un signalement "la manette n'est pas détectée, aucun input ne se
    // reflète en haut") : montre l'état brut lu par GamepadHook (connecté/pas, quel index
    // XInput, quels boutons sont tenus en ce moment, sticks/gâchettes) indépendamment de tout
    // binding — sert à distinguer "la manette n'est pas vue par XInput du tout" (ex. une
    // manette PS4/PS5 branchée sans pilote XInput, qui ne parlera jamais XInput) de "elle est
    // vue mais aucune action ne lui est assignée dans l'onglet Touches". Live, se met à jour
    // tant que cet onglet est affiché ; désabonné via _unsubscribeCurrentTab en le quittant.
    private UIElement BuildDevicesTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("Périphériques"));
        panel.Children.Add(HelpText("Diagnostic en direct, indépendant des touches assignées : confirme que le clavier/la manette sont bien lus par l'app avant de chercher un problème de binding. Si une manette reste \"Non détectée\" ici alors que Windows la voit, elle ne parle probablement pas XInput (cas fréquent des manettes PlayStation branchées sans pilote XInput type DS4Windows) — l'app ne peut lire que du XInput (comme l'immense majorité des overlays d'input)."));

        // --- Clavier ---
        panel.Children.Add(new TextBlock { Text = "Clavier", Foreground = TextColor, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        var keyboardStatus = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x58, 0xD6, 0x8D)), Text = "Hook actif — appuie sur une touche pour tester." };
        panel.Children.Add(keyboardStatus);
        var lastKeyText = new TextBlock { Foreground = SubtleText, Margin = new Thickness(0, 4, 0, 0), Text = "Dernière touche détectée : —" };
        panel.Children.Add(lastKeyText);

        void OnKeyDown(int vk)
        {
            var name = System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk).ToString();
            Dispatcher.Invoke(() => lastKeyText.Text = $"Dernière touche détectée : {name} ({DateTime.Now:HH:mm:ss})");
        }
        AppState.Hook.KeyDown += OnKeyDown;

        // --- Manette ---
        panel.Children.Add(new TextBlock { Text = "Manette (XInput)", Foreground = TextColor, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 4) });

        var gamepadStatus = new TextBlock { FontWeight = FontWeights.SemiBold };
        panel.Children.Add(gamepadStatus);

        var buttonsWrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var tilesByFlag = new Dictionary<ushort, Border>();
        foreach (var (name, flag) in GamepadHook.Buttons)
        {
            var tile = new Border
            {
                Background = CardBg,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x40, 0x55)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock { Text = name, Foreground = SubtleText },
            };
            tilesByFlag[flag] = tile;
            buttonsWrap.Children.Add(tile);
        }
        panel.Children.Add(buttonsWrap);

        var triggersRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var ltBar = new ProgressBar { Minimum = 0, Maximum = 255, Width = 140, Height = 12, Margin = new Thickness(0, 0, 16, 0) };
        var rtBar = new ProgressBar { Minimum = 0, Maximum = 255, Width = 140, Height = 12 };
        triggersRow.Children.Add(new TextBlock { Text = "LT ", Foreground = SubtleText, VerticalAlignment = VerticalAlignment.Center });
        triggersRow.Children.Add(ltBar);
        triggersRow.Children.Add(new TextBlock { Text = "  RT ", Foreground = SubtleText, VerticalAlignment = VerticalAlignment.Center });
        triggersRow.Children.Add(rtBar);
        panel.Children.Add(triggersRow);

        var sticksText = new TextBlock { Foreground = SubtleText, Margin = new Thickness(0, 8, 0, 0), Text = "Stick gauche : (0, 0)   Stick droit : (0, 0)" };
        panel.Children.Add(sticksText);

        void RefreshConnectionUi(bool connected)
        {
            if (connected)
            {
                gamepadStatus.Text = $"Connectée (index XInput {AppState.Gamepad.ActiveIndex})";
                gamepadStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x58, 0xD6, 0x8D));
            }
            else
            {
                gamepadStatus.Text = "Non détectée";
                gamepadStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                foreach (var tile in tilesByFlag.Values) tile.Background = CardBg;
                ltBar.Value = 0;
                rtBar.Value = 0;
                sticksText.Text = "Stick gauche : (0, 0)   Stick droit : (0, 0)";
            }
        }
        RefreshConnectionUi(AppState.Gamepad.Connected);

        void OnConnectionChanged(bool connected) => Dispatcher.Invoke(() => RefreshConnectionUi(connected));
        AppState.Gamepad.ConnectionChanged += OnConnectionChanged;

        void OnRawState(GamepadSnapshot snapshot)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var (flag, tile) in tilesByFlag)
                {
                    var held = (snapshot.Buttons & flag) != 0;
                    tile.Background = held ? new SolidColorBrush(Color.FromRgb(0xE8, 0xC4, 0x4A)) : CardBg;
                    if (tile.Child is TextBlock tb) tb.Foreground = held ? Brushes.Black : SubtleText;
                }
                ltBar.Value = snapshot.LeftTrigger;
                rtBar.Value = snapshot.RightTrigger;
                sticksText.Text = $"Stick gauche : ({snapshot.LeftStickX}, {snapshot.LeftStickY})   Stick droit : ({snapshot.RightStickX}, {snapshot.RightStickY})";
            });
        }
        AppState.Gamepad.RawStateChanged += OnRawState;

        _unsubscribeCurrentTab = () =>
        {
            AppState.Hook.KeyDown -= OnKeyDown;
            AppState.Gamepad.ConnectionChanged -= OnConnectionChanged;
            AppState.Gamepad.RawStateChanged -= OnRawState;
        };

        return Wrap(panel);
    }

    // ================= Combos =================

    private ListBox _combosList = null!;
    private ComboBox _weaponFilterCombo = null!;
    private ComboBox _legendFilterCombo = null!;
    private List<int> _visibleComboIndices = new();

    private UIElement BuildCombosTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("Combos"));

        // Personnage d'abord : choisir un personnage montre directement ses combos Signature +
        // les combos génériques des armes qu'il utilise réellement (voir AppState.
        // FilteredComboIndices), au lieu de croiser arme et légend comme deux filtres indépendants
        // sans lien entre eux (un légend n'a que 2 armes précises, pas 15 au hasard).
        var characterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        characterRow.Children.Add(new TextBlock { Text = "Personnage : ", Foreground = TextColor, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

        var characterItems = new List<string> { "Tous les personnages" };
        characterItems.AddRange(LegendComboPresets.Legends);
        _legendFilterCombo = new ComboBox { Width = 220, ItemsSource = characterItems, Margin = new Thickness(0, 0, 6, 0) };
        _legendFilterCombo.SelectedItem = string.IsNullOrEmpty(AppState.Settings.TrainingLegendFilter)
            ? "Tous les personnages"
            : AppState.Settings.TrainingLegendFilter;
        characterRow.Children.Add(_legendFilterCombo);
        panel.Children.Add(characterRow);
        panel.Children.Add(HelpText("Restreint la liste ci-dessous, le sous-filtre d'arme et le cycle Ctrl+Alt+K à ce personnage. « Tous les personnages » repasse en filtre par arme seule, sans notion de perso."));

        var weaponRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        weaponRow.Children.Add(new TextBlock { Text = "Arme : ", Foreground = TextColor, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _weaponFilterCombo = new ComboBox { Width = 220, Margin = new Thickness(0, 0, 6, 0) };
        weaponRow.Children.Add(_weaponFilterCombo);

        var importPresetsBtn = new Button { Padding = new Thickness(10, 4, 10, 4) };
        weaponRow.Children.Add(importPresetsBtn);
        panel.Children.Add(weaponRow);
        var weaponHelpText = HelpText("");
        panel.Children.Add(weaponHelpText);

        // Le contenu du sous-filtre d'arme et du bouton d'import dépend du personnage choisi : sans
        // personnage, c'est un filtre pur sur les 15 armes du jeu (comportement d'origine). Avec un
        // personnage, la liste ne propose que ses armes réelles (LegendComboPresets.WeaponsFor) —
        // impossible de se retrouver sur une combinaison perso/arme qui n'existe pas dans le jeu.
        void RefreshWeaponFilterOptions()
        {
            var character = _legendFilterCombo.SelectedItem as string;
            var noCharacter = string.IsNullOrEmpty(character) || character == "Tous les personnages";

            if (noCharacter)
            {
                var weaponItems = new List<string> { "Toutes les armes" };
                weaponItems.AddRange(WeaponComboPresets.Weapons);
                _weaponFilterCombo.ItemsSource = weaponItems;
                _weaponFilterCombo.SelectedItem = string.IsNullOrEmpty(AppState.Settings.TrainingWeaponFilter)
                    ? "Toutes les armes"
                    : AppState.Settings.TrainingWeaponFilter;
                importPresetsBtn.Content = "Importer les 5 combos de cette arme";
                importPresetsBtn.IsEnabled = _weaponFilterCombo.SelectedItem as string != "Toutes les armes";
                importPresetsBtn.Visibility = Visibility.Visible;
                weaponHelpText.Text = "Filtre la liste ci-dessous et les combos cyclées par Ctrl+Alt+K sur l'arme choisie. « Importer » ajoute les true combos vérifiés pour cette arme si elles n'y sont pas déjà.";
            }
            else
            {
                var weapons = LegendComboPresets.WeaponsFor(character!);
                var allLabel = $"Toutes les armes de {character}";
                var weaponItems = new List<string> { allLabel };
                weaponItems.AddRange(weapons);
                _weaponFilterCombo.ItemsSource = weaponItems;
                _weaponFilterCombo.SelectedItem = weapons.Contains(AppState.Settings.TrainingWeaponFilter)
                    ? AppState.Settings.TrainingWeaponFilter
                    : allLabel;
                // Les combos du personnage sont déjà importés automatiquement à sa sélection
                // (voir _legendFilterCombo.SelectionChanged) — pas besoin d'un bouton ici.
                importPresetsBtn.Visibility = Visibility.Collapsed;
                weaponHelpText.Text = $"Combos Signature de {character} + combos génériques de ses {weapons.Count} arme(s), importés automatiquement.";
            }
        }

        _legendFilterCombo.SelectionChanged += (_, _) =>
        {
            var selected = _legendFilterCombo.SelectedItem as string ?? "Tous les personnages";
            AppState.SetTrainingLegendFilter(selected == "Tous les personnages" ? "" : selected);
            // Le filtre d'arme précédent peut ne plus avoir de sens pour ce personnage (ex. "Marteau"
            // choisi puis bascule sur "Ada", qui ne joue pas Marteau) — repart sur "toutes ses armes".
            AppState.SetTrainingWeaponFilter("");
            if (selected != "Tous les personnages") AppState.ImportCharacterPresets(selected);
            RefreshWeaponFilterOptions();
            RefreshCombosList();
        };

        _weaponFilterCombo.SelectionChanged += (_, _) =>
        {
            var selected = _weaponFilterCombo.SelectedItem as string ?? "";
            var isAllOption = selected == "Toutes les armes" || selected.StartsWith("Toutes les armes de ");
            AppState.SetTrainingWeaponFilter(isAllOption ? "" : selected);
            RefreshCombosList();
        };

        importPresetsBtn.Click += (_, _) =>
        {
            var character = _legendFilterCombo.SelectedItem as string;
            if (!string.IsNullOrEmpty(character) && character != "Tous les personnages")
            {
                AppState.ImportCharacterPresets(character);
                RefreshCombosList();
                return;
            }

            var weapon = _weaponFilterCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(weapon) || weapon == "Toutes les armes") return;
            AppState.ImportWeaponPresets(weapon);
            RefreshCombosList();
        };

        // Créée avant le premier RefreshWeaponFilterOptions() : celui-ci fixe SelectedItem sur
        // _weaponFilterCombo, ce qui déclenche son SelectionChanged synchroniquement et donc
        // RefreshCombosList() — qui a besoin que _combosList existe déjà.
        _combosList = new ListBox { Height = 200, Background = CardBg, Foreground = TextColor, BorderThickness = new Thickness(0) };

        RefreshWeaponFilterOptions();
        RefreshCombosList();
        panel.Children.Add(_combosList);

        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };

        var recordBtn = new Button { Content = RecordLabel(), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        recordBtn.Click += (_, _) => AppState.SetRecording(!AppState.Recording);
        buttonsRow.Children.Add(recordBtn);

        var manualBtn = new Button { Content = "Créer manuellement", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        manualBtn.Click += (_, _) => OpenComboEditor(null);
        buttonsRow.Children.Add(manualBtn);

        var editBtn = new Button { Content = "Modifier", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        editBtn.Click += (_, _) =>
        {
            var idx = SelectedAbsoluteIndex();
            if (idx >= 0) OpenComboEditor(AppState.Combos[idx]);
        };
        buttonsRow.Children.Add(editBtn);

        var dupBtn = new Button { Content = "Dupliquer", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        dupBtn.Click += (_, _) =>
        {
            var idx = SelectedAbsoluteIndex();
            if (idx < 0) return;
            var original = AppState.Combos[idx];
            var json = JsonSerializer.Serialize(original);
            var copy = JsonSerializer.Deserialize<Combo>(json)!;
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = $"{original.Name} (copie)";
            AppState.Combos.Add(copy);
            AppState.NotifyCombosMutated();
            RefreshCombosList();
        };
        buttonsRow.Children.Add(dupBtn);

        var upBtn = new Button { Content = "▲", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 2, 0) };
        upBtn.Click += (_, _) => MoveSelectedCombo(-1);
        buttonsRow.Children.Add(upBtn);

        var downBtn = new Button { Content = "▼", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0) };
        downBtn.Click += (_, _) => MoveSelectedCombo(1);
        buttonsRow.Children.Add(downBtn);

        var deleteBtn = new Button { Content = "Supprimer", Padding = new Thickness(10, 4, 10, 4) };
        deleteBtn.Click += (_, _) =>
        {
            var idx = SelectedAbsoluteIndex();
            if (idx < 0) return;
            AppState.Combos.RemoveAt(idx);
            AppState.NotifyCombosMutated();
            RefreshCombosList();
        };
        buttonsRow.Children.Add(deleteBtn);

        panel.Children.Add(buttonsRow);
        panel.Children.Add(HelpText("« Enregistrer » capture tes prochains appuis en direct (même raccourci que Ctrl+Alt+R) ; reclique pour arrêter et sauvegarder. « Créer manuellement » ouvre un éditeur d'étapes."));

        var ioRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };

        var exportBtn = new Button { Content = "Exporter le combo sélectionné", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        exportBtn.Click += (_, _) => ExportSelectedCombo();
        ioRow.Children.Add(exportBtn);

        var importBtn = new Button { Content = "Importer un combo", Padding = new Thickness(10, 4, 10, 4) };
        importBtn.Click += (_, _) => ImportCombo();
        ioRow.Children.Add(importBtn);

        panel.Children.Add(ioRow);
        panel.Children.Add(HelpText("Permet de partager un combo (fichier .json) entre deux installations ou avec quelqu'un d'autre."));

        var statsBtn = new Button { Content = "Voir les stats de précision par action", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        statsBtn.Click += (_, _) => ShowActionStats();
        panel.Children.Add(statsBtn);
        panel.Children.Add(HelpText("Cumulé sur toutes les combos jouées en mode Tutoriel, persiste entre les sessions."));

        void RecordingHandler(bool _) => Dispatcher.Invoke(() => recordBtn.Content = RecordLabel());
        AppState.RecordingChanged += RecordingHandler;
        AppState.CombosChanged += RefreshCombosList;

        _unsubscribeCurrentTab = () =>
        {
            AppState.RecordingChanged -= RecordingHandler;
            AppState.CombosChanged -= RefreshCombosList;
        };

        return Wrap(panel);
    }

    /// <summary>Touches du profil actif + combos + apparence, format d'échange entre deux
    /// installations (docs/amelioration.md piste #11). Classe privée dédiée plutôt que réutiliser
    /// OverlaySettings tel quel : certains champs de OverlaySettings (MonitorIndex, ActiveProfile,
    /// OnboardingCompleted...) sont spécifiques à la machine/session courante et ne devraient pas
    /// écraser ceux de la machine de destination.</summary>
    private sealed class ProfileBundle
    {
        public List<KeyBind> Binds { get; set; } = new();
        public List<Combo> Combos { get; set; } = new();
        public double Scale { get; set; } = 1.0;
        public double Opacity { get; set; } = 1.0;
        public bool SoundEnabled { get; set; }
        public bool QuizMode { get; set; }
        public bool ChainCombos { get; set; }
        public int ChainStreakThreshold { get; set; } = 1;
        public bool KeepStreakOnFail { get; set; }
    }

    private void ExportProfileBundle()
    {
        var bundle = new ProfileBundle
        {
            Binds = AppState.Binds,
            Combos = AppState.Combos,
            Scale = AppState.Settings.Scale,
            Opacity = AppState.Settings.Opacity,
            SoundEnabled = AppState.Settings.SoundEnabled,
            QuizMode = AppState.Settings.QuizMode,
            ChainCombos = AppState.Settings.ChainCombos,
            ChainStreakThreshold = AppState.Settings.ChainStreakThreshold,
            KeepStreakOnFail = AppState.Settings.KeepStreakOnFail,
        };

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Profil BrawlhallaOverlay (*.json)|*.json",
            FileName = $"profil_{AppState.Settings.ActiveProfile}.json",
        };
        if (dialog.ShowDialog() != true) return;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(bundle, options));
        MessageBox.Show("Profil exporté.", "Export terminé", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportProfileBundle()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Profil BrawlhallaOverlay (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;

        ProfileBundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<ProfileBundle>(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fichier invalide : {ex.Message}", "Import impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (bundle is null || bundle.Binds.Count == 0)
        {
            MessageBox.Show("Ce fichier ne contient pas de profil valide.", "Import impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Ça va remplacer les touches du profil actif (« {AppState.Settings.ActiveProfile} ») et TOUTE la liste de combos actuelle ({AppState.Combos.Count} combo(s)) par le contenu du fichier ({bundle.Binds.Count} touche(s), {bundle.Combos.Count} combo(s)).\nContinuer ?",
            "Confirmer l'import", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        AppState.ReplaceBinds(bundle.Binds);
        AppState.ReplaceCombos(bundle.Combos);
        AppState.Settings.Scale = bundle.Scale;
        AppState.Settings.Opacity = bundle.Opacity;
        AppState.Settings.SoundEnabled = bundle.SoundEnabled;
        AppState.Settings.QuizMode = bundle.QuizMode;
        AppState.Settings.ChainCombos = bundle.ChainCombos;
        AppState.Settings.ChainStreakThreshold = bundle.ChainStreakThreshold;
        AppState.Settings.KeepStreakOnFail = bundle.KeepStreakOnFail;
        AppState.SaveSettings();

        MessageBox.Show("Profil importé.", "Import terminé", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportSelectedCombo()
    {
        var idx = SelectedAbsoluteIndex();
        if (idx < 0)
        {
            MessageBox.Show("Sélectionne d'abord un combo dans la liste.", "Aucun combo sélectionné", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var combo = AppState.Combos[idx];
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Combo JSON (*.json)|*.json",
            FileName = $"{combo.Name}.json",
        };
        if (dialog.ShowDialog() != true) return;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(combo, options));
    }

    private void ImportCombo()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Combo JSON (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;

        Combo? imported;
        try
        {
            imported = JsonSerializer.Deserialize<Combo>(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fichier invalide : {ex.Message}", "Import impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (imported is null || imported.Steps.Count == 0)
        {
            MessageBox.Show("Ce fichier ne contient pas de combo valide.", "Import impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var knownActions = new HashSet<string>(AppState.Binds.Select(b => b.Action));
        var unknownActions = imported.Steps.SelectMany(s => s.RequiredActions).Where(a => !knownActions.Contains(a)).Distinct().ToList();
        if (unknownActions.Count > 0)
        {
            MessageBox.Show(
                $"Ce combo référence des actions qui n'existent pas dans ta configuration de touches : {string.Join(", ", unknownActions)}.\n" +
                "Importée quand même, mais elle ne pourra pas être validée en jeu tant que ces actions n'existent pas (onglet Touches).",
                "Actions inconnues", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        imported.Id = Guid.NewGuid().ToString("N");
        AppState.Combos.Add(imported);
        AppState.NotifyCombosMutated();
        RefreshCombosList();
    }

    private static void ShowActionStats()
    {
        var stats = AppState.Stats.OrderByDescending(s => s.Failures == 0 ? 0 : (double)s.Failures / (s.Successes + s.Failures)).ToList();
        if (stats.Count == 0)
        {
            MessageBox.Show("Pas encore de données — joue un combo en mode Tutoriel pour commencer à accumuler des stats.", "Stats de précision", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lines = stats.Select(s =>
        {
            var total = s.Successes + s.Failures;
            var rate = total == 0 ? 0 : 100.0 * s.Successes / total;
            return $"{s.Action,-16} {s.Successes,4} réussite(s) / {s.Failures,4} échec(s)  ({rate:0}% de réussite)";
        });
        MessageBox.Show(string.Join("\n", lines), "Stats de précision par action", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string RecordLabel() => AppState.Recording ? "■ Arrêter l'enregistrement" : "● Enregistrer un combo";

    /// <summary>Résout la sélection de la ListBox (indexée sur la liste filtrée par arme) en
    /// index absolu dans AppState.Combos.</summary>
    private int SelectedAbsoluteIndex() =>
        _combosList.SelectedIndex >= 0 && _combosList.SelectedIndex < _visibleComboIndices.Count
            ? _visibleComboIndices[_combosList.SelectedIndex]
            : -1;

    private void RefreshCombosList()
    {
        var previouslySelectedAbsolute = SelectedAbsoluteIndex();

        _combosList.Items.Clear();
        _visibleComboIndices.Clear();

        var filter = AppState.Settings.TrainingWeaponFilter;
        var legendFilter = AppState.Settings.TrainingLegendFilter;
        // Réutilise le même calcul que AppState.CycleCombo/ActiveComboFilteredPosition au lieu de
        // ré-implémenter la logique de filtre ici — sinon les deux finissent par diverger (c'est
        // exactement ce qui s'était passé avec l'ancien ET arme/légend indépendant).
        var filteredAbsIndices = AppState.FilteredComboIndices();
        foreach (var absIndex in ComboFamilies.OrderWithFamilies(filteredAbsIndices, i => AppState.Combos[i]))
        {
            var combo = AppState.Combos[absIndex.Index];
            _visibleComboIndices.Add(absIndex.Index);
            var legendTag = string.IsNullOrEmpty(combo.Legend) ? "" : $"{combo.Legend} ";
            var weaponTag = string.IsNullOrEmpty(combo.Weapon) ? "" : $"[{legendTag}{combo.Weapon}] ";
            var masteredMark = combo.Mastered ? " ✓" : "";
            var perf = combo.TotalAttempts > 0
                ? $" — meilleure série {combo.BestStreak}, {combo.TotalCompletions}/{combo.TotalAttempts} réussie(s)"
                : "";
            var damageNote = string.IsNullOrEmpty(combo.DamageNote) ? "" : $"  ⚠ {combo.DamageNote}";
            var indent = absIndex.Level > 1 ? new string(' ', (absIndex.Level - 1) * 3) + "↳ " : "";
            var levelTag = absIndex.Level > 0 && absIndex.FamilySize > 1 ? $" [Niveau {absIndex.Level}]" : "";
            var followUpHint = combo.Mastered && absIndex.HasFollowUp ? "  → niveau supérieur disponible ci-dessous" : "";
            _combosList.Items.Add($"{indent}{weaponTag}{combo.Name}{masteredMark}{levelTag}  ({combo.Steps.Count} étapes){perf}{damageNote}{followUpHint}");
        }

        if (_combosList.Items.Count == 0)
        {
            // Sans ce message, une liste vide (cas du tout premier lancement, ou
            // d'un filtre d'arme/légend sans combo importé) ressemble à un bug
            // plutôt qu'à un état normal — rien n'indiquait quoi faire ensuite.
            string hint;
            if (string.IsNullOrEmpty(filter) && string.IsNullOrEmpty(legendFilter))
                hint = "Aucun combo pour l'instant. Choisis une arme ci-dessus puis « Importer les 5 combos de cette arme », ou clique « Créer manuellement » / « Enregistrer un combo » plus bas.";
            else if (!string.IsNullOrEmpty(legendFilter))
                hint = $"Aucun combo pour « {legendFilter} »{(string.IsNullOrEmpty(filter) ? "" : $" sur {filter}")}. Clique « Importer les combos de {legendFilter} » ci-dessus (certains personnages n'ont pas de combo listé), ou choisis « Tous les personnages ».";
            else
                hint = $"Aucun combo pour « {filter} ». Clique « Importer les 5 combos de cette arme » ci-dessus, ou choisis « Toutes les armes » pour voir les autres combos.";
            _combosList.Items.Add(new ListBoxItem
            {
                Content = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Foreground = SubtleText },
                IsEnabled = false,
                Padding = new Thickness(6),
            });
        }

        var restored = _visibleComboIndices.IndexOf(previouslySelectedAbsolute);
        if (restored >= 0) _combosList.SelectedIndex = restored;
        else if (_combosList.Items.Count > 0) _combosList.SelectedIndex = 0;
    }

    /// <summary>Déplace le combo sélectionné d'un cran dans la liste.
    ///
    /// Audit 2026-08-07 §M8 : l'ancienne version se resélectionnait sur `visibleTarget`, une
    /// position d'AFFICHAGE — or RefreshCombosList réordonne ensuite la liste via
    /// ComboFamilies.OrderWithFamilies, donc cet index pouvait très bien pointer sur un autre
    /// combo après coup (le bouton semblait alors ne rien faire, ou sélectionner un combo sans
    /// rapport). On retrouve désormais le combo déplacé par son Id, et on prévient
    /// explicitement quand le regroupement par familles annule visuellement le déplacement,
    /// plutôt que de laisser croire à un bouton cassé.</summary>
    private void MoveSelectedCombo(int direction)
    {
        var visibleIndex = _combosList.SelectedIndex;
        var visibleTarget = visibleIndex + direction;
        if (visibleIndex < 0 || visibleIndex >= _visibleComboIndices.Count) return;
        if (visibleTarget < 0 || visibleTarget >= _visibleComboIndices.Count) return;

        var absIndex = _visibleComboIndices[visibleIndex];
        var absTarget = _visibleComboIndices[visibleTarget];
        var movedId = AppState.Combos[absIndex].Id;

        (AppState.Combos[absIndex], AppState.Combos[absTarget]) = (AppState.Combos[absTarget], AppState.Combos[absIndex]);
        AppState.NotifyCombosMutated();
        RefreshCombosList();

        var newVisible = _visibleComboIndices.FindIndex(i => AppState.Combos[i].Id == movedId);
        if (newVisible >= 0) _combosList.SelectedIndex = newVisible;

        if (newVisible == visibleIndex)
        {
            MessageBox.Show(
                "Ce combo fait partie d'une famille (il étend un combo plus court, ou est étendu par un plus long) : "
                + "les membres d'une famille sont toujours affichés groupés et triés par nombre d'étapes, "
                + "donc le tri automatique a repris le dessus sur ce déplacement.\n\n"
                + "L'ordre a bien changé dans le fichier, il n'est simplement pas visible ici.",
                "Déplacement annulé par le regroupement", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OpenComboEditor(Combo? existing)
    {
        var editor = new ComboEditorWindow(existing) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is not null)
        {
            if (existing is null)
            {
                AppState.Combos.Add(editor.Result);
            }
            else
            {
                var idx = AppState.Combos.IndexOf(existing);
                if (idx >= 0) AppState.Combos[idx] = editor.Result;
            }
            AppState.NotifyCombosMutated();
            RefreshCombosList();
        }
    }

    // ================= Apparence =================

    private UIElement BuildAppearanceTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("Apparence"));

        panel.Children.Add(Label("Échelle"));
        var scaleSlider = new Slider { Minimum = 0.5, Maximum = 2.0, Value = AppState.Settings.Scale, Width = 300, HorizontalAlignment = HorizontalAlignment.Left, TickFrequency = 0.1 };
        var scaleValue = new TextBlock { Foreground = TextColor, Margin = new Thickness(10, 0, 0, 0), Text = $"{AppState.Settings.Scale:0.00}x" };
        var scaleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 14) };
        scaleRow.Children.Add(scaleSlider);
        scaleRow.Children.Add(scaleValue);
        scaleSlider.ValueChanged += (_, e) =>
        {
            AppState.Settings.Scale = e.NewValue;
            scaleValue.Text = $"{e.NewValue:0.00}x";
            AppState.SaveSettings();
        };
        panel.Children.Add(scaleRow);

        panel.Children.Add(Label("Opacité"));
        var opacitySlider = new Slider { Minimum = 0.2, Maximum = 1.0, Value = AppState.Settings.Opacity, Width = 300, HorizontalAlignment = HorizontalAlignment.Left, TickFrequency = 0.05 };
        var opacityValue = new TextBlock { Foreground = TextColor, Margin = new Thickness(10, 0, 0, 0), Text = $"{AppState.Settings.Opacity * 100:0}%" };
        var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 14) };
        opacityRow.Children.Add(opacitySlider);
        opacityRow.Children.Add(opacityValue);
        opacitySlider.ValueChanged += (_, e) =>
        {
            AppState.Settings.Opacity = e.NewValue;
            opacityValue.Text = $"{e.NewValue * 100:0}%";
            AppState.SaveSettings();
        };
        panel.Children.Add(opacityRow);

        panel.Children.Add(Label("Écran"));
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screenItems = new List<string> { "Écran principal" };
        for (var i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            screenItems.Add($"Écran {i + 1} ({s.Bounds.Width}x{s.Bounds.Height}){(s.Primary ? " — principal" : "")}");
        }
        var screenCombo = new ComboBox
        {
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = screenItems,
            SelectedIndex = AppState.Settings.MonitorIndex < 0 || AppState.Settings.MonitorIndex >= screens.Length ? 0 : AppState.Settings.MonitorIndex + 1,
            Margin = new Thickness(0, 4, 0, 14),
        };
        screenCombo.SelectionChanged += (_, _) =>
        {
            AppState.Settings.MonitorIndex = screenCombo.SelectedIndex - 1;
            AppState.SaveSettings();
        };
        panel.Children.Add(screenCombo);
        panel.Children.Add(HelpText("Utile si Brawlhalla tourne sur un moniteur secondaire : sans ce réglage l'overlay reste toujours calé sur l'écran principal Windows."));

        return Wrap(panel);
    }

    // ================= À propos =================

    private UIElement BuildAboutTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("À propos"));
        panel.Children.Add(new TextBlock { Text = "Brawlhalla Input Overlay", Foreground = TextColor, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(new TextBlock { Text = "Mode Tutoriel + Panneau de contrôle", Foreground = SubtleText, Margin = new Thickness(0, 0, 0, 20) });

        panel.Children.Add(new TextBlock { Text = "Raccourcis clavier (reconfigurables : onglet Général, réglages avancés)", Foreground = TextColor, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var (keys, desc) in new[]
        {
            ($"Ctrl+Alt+{System.Windows.Input.KeyInterop.KeyFromVirtualKey(AppState.Settings.LockVk)}", "Verrouiller / déverrouiller l'overlay"),
            ($"Ctrl+Alt+{System.Windows.Input.KeyInterop.KeyFromVirtualKey(AppState.Settings.ComboNextVk)}", "Combo suivante (mode Tutoriel)"),
            ($"Ctrl+Alt+{System.Windows.Input.KeyInterop.KeyFromVirtualKey(AppState.Settings.ComboPrevVk)}", "Combo précédente (mode Tutoriel)"),
            ($"Ctrl+Alt+{System.Windows.Input.KeyInterop.KeyFromVirtualKey(AppState.Settings.RecordVk)}", "Démarrer / arrêter l'enregistrement d'un combo"),
            ($"Ctrl+Alt+{System.Windows.Input.KeyInterop.KeyFromVirtualKey(AppState.Settings.DashboardVk)}", "Ouvrir / donner le focus à l'accueil"),
            ($"Ctrl+Alt+{System.Windows.Input.KeyInterop.KeyFromVirtualKey(AppState.Settings.RevealVk)}", "Révéler temporairement le combo (« Cacher les étapes »)"),
            ($"Ctrl+Alt+{System.Windows.Input.KeyInterop.KeyFromVirtualKey(AppState.Settings.SuspendVk)}", "Suspendre / reprendre la capture (utile hors du jeu)"),
            ($"Ctrl+Alt+{System.Windows.Input.KeyInterop.KeyFromVirtualKey(AppState.Settings.HideVk)}", "Masquer / afficher complètement l'overlay (sans fermer l'app)"),
        })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = keys, Foreground = TextColor, FontFamily = new FontFamily("Consolas"), Width = 110 });
            row.Children.Add(new TextBlock { Text = desc, Foreground = SubtleText });
            panel.Children.Add(row);
        }

        panel.Children.Add(new TextBlock { Text = "Raccourcis manette (Start + bouton)", Foreground = TextColor, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 20, 0, 6) });
        foreach (var (chord, desc) in new[]
        {
            ("Start + RB", "Combo suivante"),
            ("Start + LB", "Combo précédente"),
            ("Start + Back", "Suspendre / reprendre la capture"),
            ("Start + X", "Ouvrir l'accueil"),
        })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = chord, Foreground = TextColor, FontFamily = new FontFamily("Consolas"), Width = 110 });
            row.Children.Add(new TextBlock { Text = desc, Foreground = SubtleText });
            panel.Children.Add(row);
        }
        panel.Children.Add(HelpText("Fonctionne uniquement si les boutons manette utilisés (LB/RB/X/Y/Back) ne sont pas déjà réassignés à une action de jeu dans l'onglet Touches — sinon les deux se déclenchent en même temps quand Start est maintenu."));

        panel.Children.Add(new TextBlock { Text = "Glossaire (FR ↔ notation communautaire)", Foreground = TextColor, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 20, 0, 6) });
        panel.Children.Add(HelpText("Les guides Brawlhalla et la communauté utilisent leur propre notation (dLight, nSig, GC...) — voici la correspondance avec les noms d'action de cette app."));
        foreach (var (fr, notation) in new[]
        {
            ("Att. légère", "Light (nLight neutre, dLight bas, sLight latéral)"),
            ("Att. forte", "Signature / Sig (nSig, dSig, sSig — même bouton que Light, pas un bouton séparé)"),
            ("Esquive", "Dodge — tenue en l'air juste après un saut : Gravity Cancel (GC)"),
            ("Saut", "Jump (au sol ou en l'air : Air Jump)"),
            ("Lancer", "Item Throw / Unarm"),
            ("Direction + Att. légère/forte en l'air", "nAir / sAir / dAir selon la direction"),
        })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = fr, Foreground = TextColor, Width = 220, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new TextBlock { Text = notation, Foreground = SubtleText, Width = 300, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(row);
        }

        panel.Children.Add(new TextBlock { Text = "Icônes", Foreground = TextColor, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 20, 0, 6) });
        panel.Children.Add(new TextBlock
        {
            Text = "Icônes du mode Tutoriel par Lorc et Delapouite, sous licence CC BY 3.0. Disponibles sur game-icons.net.",
            Foreground = SubtleText,
            TextWrapping = TextWrapping.Wrap,
        });

        return Wrap(panel);
    }

    /// <summary>Petite boîte de dialogue modale à un champ texte (pas d'équivalent WPF natif
    /// à InputBox VB) — utilisée pour nommer un nouveau profil de touches.</summary>
    private string? PromptForText(string message, string title)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = PanelBg,
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = message, Foreground = TextColor, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var input = new TextBox { Margin = new Thickness(0, 0, 0, 14) };
        root.Children.Add(input);

        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "Annuler", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
        var okBtn = new Button { Content = "OK", Padding = new Thickness(14, 5, 14, 5), IsDefault = true };
        okBtn.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        buttonsRow.Children.Add(cancelBtn);
        buttonsRow.Children.Add(okBtn);
        root.Children.Add(buttonsRow);

        dialog.Content = root;
        input.Focus();

        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private static UIElement Wrap(UIElement content) =>
        new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
}
