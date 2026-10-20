using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CyberPipe.Desktop.ViewModels;

namespace CyberPipe.Desktop;

public partial class MainWindow : Window
{
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
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
