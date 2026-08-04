using System.Windows;

namespace BrawlhallaOverlay;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Surface unhandled exceptions instead of silently dying, since this
        // runs with no console attached.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Erreur inattendue :\n{args.Exception.Message}",
                "Brawlhalla Overlay",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        // Pas de StartupUri XAML fixe : le tout premier lancement (OnboardingCompleted
        // encore null/false) passe par la fourche OnboardingWindow (voir docs/plan_ux_onboarding.md
        // §5.1) ; tous les suivants vont directement sur l'écran de reprise/config existant
        // (StartupWindow). Il faut lire AppState.Settings *avant* de choisir la fenêtre, donc
        // c'est fait ici plutôt que de laisser XAML décider.
        Window entry = AppState.Settings.OnboardingCompleted == true
            ? new StartupWindow()
            : new OnboardingWindow();
        MainWindow = entry;
        entry.Show();
    }
}
