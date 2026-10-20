using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CyberPipe.Desktop.ViewModels;

namespace CyberPipe.Desktop.Views;

public partial class ModelTestWindow : Window
{
    public ModelTestWindow()
    {
        InitializeComponent();
    }

    public ModelTestWindow(ModelTestViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose = Close;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
