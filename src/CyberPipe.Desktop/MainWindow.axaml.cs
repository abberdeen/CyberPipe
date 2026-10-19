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
            DataContext = MainWindowViewModelFactory.CreateDefault();
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
