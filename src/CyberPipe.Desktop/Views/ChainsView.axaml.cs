using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CyberPipe.Desktop.Views;

public partial class ChainsView : UserControl
{
    public ChainsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
