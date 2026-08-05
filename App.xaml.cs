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

        // Pas de StartupUri XAML fixe : DashboardWindow fusionnée gère les deux flux (fourche
        // onboarding si OnboardingCompleted est faux, flux complet sinon). Voir Phase 1 du plan de
        // refonte UX.
        var entry = new DashboardWindow();
        MainWindow = entry;
        entry.Show();
    }
}
