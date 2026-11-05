using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CyberPipe.Desktop.ViewModels;

namespace CyberPipe.Desktop.Views;

public partial class RemoteProvidersWindow : Window
{
    public RemoteProvidersWindow()
    {
        InitializeComponent();
    }

    public RemoteProvidersWindow(RemoteProvidersViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose = Close;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
