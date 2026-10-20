using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CyberPipe.Desktop.ViewModels;
using CyberPipe.Desktop.Views;

namespace CyberPipe.Desktop;

public partial class MainWindow : Window
{
    private TelemetryLogsWindow? _logsWindow;

    public MainWindow()
    {
        InitializeComponent();
        if (DataContext == null)
        {
            try
            {
                DataContext = MainWindowViewModelFactory.CreateDefault();
            }
            catch (Exception ex)
            {
                Program.LogFailure(ex, "Failed to create default MainWindowViewModel, using fallback");
                DataContext = MainWindowViewModelFactory.CreateFallback($"Сбой инициализации: {ex.Message}");
            }
        }

        if (DataContext is MainWindowViewModel vm)
        {
            WireLogsWindow(vm);
        }
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        WireLogsWindow(viewModel);
    }

    private void WireLogsWindow(MainWindowViewModel? vm)
    {
        if (vm == null) return;

        vm.OpenLogsWindowRequested = () =>
        {
            if (_logsWindow is { IsVisible: true })
            {
                _logsWindow.Activate();
                return;
            }

            _logsWindow = new TelemetryLogsWindow(vm.Logs);
            _logsWindow.Closed += (_, _) => _logsWindow = null;
            _logsWindow.Show();
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
