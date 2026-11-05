using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CyberPipe.Desktop.ViewModels;
using CyberPipe.Desktop.Views;

namespace CyberPipe.Desktop;

public partial class MainWindow : Window
{
    private TelemetryLogsWindow? _logsWindow;
    private OllamaManagerWindow? _ollamaWindow;
    private RemoteProvidersWindow? _providersWindow;

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
            WireModelWindows(vm);
        }
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        WireLogsWindow(viewModel);
        WireModelWindows(viewModel);
    }

    private void WireModelWindows(MainWindowViewModel vm)
    {
        vm.Models.OpenOllamaManagerRequested = manager =>
        {
            if (_ollamaWindow is { IsVisible: true })
            {
                _ollamaWindow.Activate();
                return Task.CompletedTask;
            }

            _ollamaWindow = new OllamaManagerWindow(manager);
            _ollamaWindow.Closed += (_, _) =>
            {
                _ollamaWindow = null;
                vm.Models.RefreshAvailableModels();
            };
            _ollamaWindow.Show(this);
            return Task.CompletedTask;
        };

        vm.Models.OpenRemoteProvidersRequested = providers =>
        {
            if (_providersWindow is { IsVisible: true })
            {
                _providersWindow.Activate();
                return Task.CompletedTask;
            }

            _providersWindow = new RemoteProvidersWindow(providers);
            _providersWindow.Closed += (_, _) =>
            {
                _providersWindow = null;
                vm.Models.RefreshAvailableModels();
            };
            _providersWindow.Show(this);
            return Task.CompletedTask;
        };
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
