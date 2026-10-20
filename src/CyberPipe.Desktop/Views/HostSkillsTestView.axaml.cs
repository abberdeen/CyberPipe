using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CyberPipe.Desktop.Views;

public partial class HostSkillsTestView : UserControl
{
    public HostSkillsTestView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
