using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace CyberPipe.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, eventArgs) =>
        {
            Program.LogFailure(eventArgs.Exception, "Unhandled Desktop UI exception");
            eventArgs.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                desktop.MainWindow = new MainWindow();
            }
            catch (Exception ex)
            {
                Program.LogFailure(ex, "Failed to create MainWindow, initializing with fallback");
                desktop.MainWindow = new MainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
