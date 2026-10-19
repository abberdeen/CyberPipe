using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CyberPipe.Desktop.Views;

public partial class ServiceView : UserControl
{
    public ServiceView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
