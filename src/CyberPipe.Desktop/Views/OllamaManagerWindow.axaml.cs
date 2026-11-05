using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CyberPipe.Desktop.ViewModels;

namespace CyberPipe.Desktop.Views;

public partial class OllamaManagerWindow : Window
{
    public OllamaManagerWindow()
    {
        InitializeComponent();
    }

    public OllamaManagerWindow(OllamaManagerViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose = Close;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
