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
    }
}
