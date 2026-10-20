using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CyberPipe.Desktop.ViewModels;

namespace CyberPipe.Desktop.Views;

public partial class ModelsView : UserControl
{
    public ModelsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => OnDataContextChanged(this, EventArgs.Empty);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ModelsViewModel vm)
        {
            vm.OpenModelTestRequested = OpenModelTestDialogAsync;
        }
    }

    private async Task OpenModelTestDialogAsync(ModelTestViewModel testVm)
    {
        var window = new ModelTestWindow(testVm);
        var owner = VisualRoot as Window
            ?? TopLevel.GetTopLevel(this) as Window
            ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (owner != null)
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
        }
        else
        {
            window.Show();
        }
    }
}
