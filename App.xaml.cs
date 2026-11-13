using System.Windows;

namespace Smiley;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            MainWindow = new MainWindow(e.Args.Contains("--paused"), e.Args.Contains("--window-diagnostics"));
            // Opt-in inspection mode exposes a taskbar entry for UI automation tools.
            if (e.Args.Contains("--inspect")) MainWindow.ShowInTaskbar = true;
            MainWindow.Show();
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Smiley — falha ao iniciar", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
