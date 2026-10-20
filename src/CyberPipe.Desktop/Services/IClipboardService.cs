using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace CyberPipe.Desktop.Services;

public interface IClipboardService
{
    Task SetTextAsync(string? text);
}

public sealed class AvaloniaClipboardService : IClipboardService
{
    public static AvaloniaClipboardService Default { get; } = new();

    public async Task SetTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            await CopyToClipboardInternalAsync(text).ConfigureAwait(false);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await CopyToClipboardInternalAsync(text).ConfigureAwait(false);
            });
        }
    }

    private static async Task CopyToClipboardInternalAsync(string text)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text).ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignore clipboard errors (e.g. headless or locked by another app)
        }
    }
}
