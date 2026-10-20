using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CyberPipe.Desktop.ViewModels;

namespace CyberPipe.Desktop.Views;

public partial class TelemetryLogsWindow : Window
{
    public TelemetryLogsWindow()
    {
        InitializeComponent();
    }

    public TelemetryLogsWindow(TelemetryLogsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
