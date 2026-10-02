using System.Windows;

namespace Syuas.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // WPF already splits the command line, including quoted paths containing spaces.
        var window = new MainWindow(e.Args);
        MainWindow = window;
        window.Show();
    }
}
