using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using CyberPipe.Desktop.ViewModels;

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

    private void OnInputTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Shift+Enter -> allow TextBox to insert newline
                return;
            }

            // Enter or Ctrl+Enter -> Send message
            e.Handled = true;
            if (sender is TextBox textBox && DataContext is ChatViewModel vm)
            {
                if (!string.IsNullOrEmpty(textBox.Text) && string.IsNullOrEmpty(vm.InputText))
                {
                    vm.InputText = textBox.Text;
                }

                if (vm.SendCommand.CanExecute(null))
                {
                    vm.SendCommand.Execute(null);
                }
            }
        }
    }
}
