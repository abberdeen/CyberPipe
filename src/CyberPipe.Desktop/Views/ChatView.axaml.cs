using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CyberPipe.Desktop.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
