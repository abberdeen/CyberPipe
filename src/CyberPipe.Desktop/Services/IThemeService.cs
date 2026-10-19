using Avalonia;
using Avalonia.Styling;
using CyberPipe.Desktop.Common;

namespace CyberPipe.Desktop.Services;

public interface IThemeService
{
    bool IsDarkMode { get; }
    void ToggleTheme();
    void SetTheme(bool isDark);
}

public sealed class ThemeService : ViewModelBase, IThemeService
{
    private bool _isDarkMode = true;

    public bool IsDarkMode
    {
        get => _isDarkMode;
        private set => SetProperty(ref _isDarkMode, value);
    }

    public void ToggleTheme()
    {
        SetTheme(!IsDarkMode);
    }

    public void SetTheme(bool isDark)
    {
        IsDarkMode = isDark;
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
