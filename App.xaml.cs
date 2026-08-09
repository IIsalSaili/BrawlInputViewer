using System;
using System.Windows;

namespace BrawlhallaOverlay;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Surface unhandled exceptions instead of silently dying, since this
        // runs with no console attached. Aussi journalisées dans diagnostic.log (voir
        // Config/DiagnosticLog.cs) : la MessageBox seule ne laisse aucune trace une fois
        // fermée, impossible à diagnostiquer après coup pour un bug non reproduit en direct.
        DispatcherUnhandledException += (_, args) =>
        {
            DiagnosticLog.LogException("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(
                $"Erreur inattendue :\n{args.Exception.Message}",
                "Brawlhalla Overlay",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        // Exceptions hors thread UI (hooks clavier/manette tournent sur leurs propres threads) —
        // celles-ci ne passent pas par DispatcherUnhandledException ci-dessus.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) DiagnosticLog.LogException("AppDomain.UnhandledException", ex);
        };

        // §PATCH-7 (2026-08-09) : le hook clavier/manette n'était démarré que par
        // MainWindow_Loaded / ParcoursWindow.Loaded — mais ControlPanelWindow/ComboEditorWindow
        // sont accessibles bien avant l'un ou l'autre (bouton "Réglages avancés…" dès le tout
        // premier écran de la Dashboard) et écoutent AppState.Hook.KeyDown /
        // AppState.Gamepad.ButtonDown pour la réassignation de touche ("Écouter"). Sans hook
        // démarré, cette écoute expirait silencieusement au bout de 6s sans avoir jamais rien
        // reçu. Start() est idempotent des deux côtés (garde déjà en place dans KeyboardHook et
        // GamepadHook), donc le redémarrer plus tard depuis MainWindow/ParcoursWindow ne fait rien
        // de plus.
        AppState.Hook.Start();
        AppState.Gamepad.Start();

        // Pas de StartupUri XAML fixe : DashboardWindow fusionnée gère les deux flux (fourche
        // onboarding si OnboardingCompleted est faux, flux complet sinon). Voir Phase 1 du plan de
        // refonte UX.
        var entry = new DashboardWindow();
        MainWindow = entry;
        entry.Show();
    }

    /// <summary>Libère les ressources partagées (hooks clavier/manette, sources de vision) et
    /// vide les sauvegardes en attente.
    ///
    /// Audit 2026-08-07 §M16 : c'était MainWindow.Closed qui appelait Dispose sur
    /// AppState.Hook/Gamepad, alors que ce sont des singletons partagés — une ParcoursWindow
    /// encore ouverte après la fermeture de l'overlay cessait silencieusement de recevoir des
    /// touches. La propriété du cycle de vie appartient à l'application, pas à une fenêtre parmi
    /// d'autres. §M3 : le flush final garantit qu'aucun compteur différé n'est perdu.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            AppState.FlushPendingSaves();
            AppState.Hook.Dispose();
            AppState.Gamepad.Dispose();
            AppState.HudDamageSource.Dispose();
            AppState.HudTierSource.Dispose();
        }
        catch (Exception ex)
        {
            DiagnosticLog.LogException("OnExit", ex);
        }

        base.OnExit(e);
    }
}
