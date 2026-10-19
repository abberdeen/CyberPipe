using System.Collections.ObjectModel;
using CyberPipe.Desktop.Common;

namespace CyberPipe.Desktop.Services;

public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed class DesktopNotification
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Info;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public Action? OnClick { get; init; }
}

public interface IDesktopNotificationService
{
    ObservableCollection<DesktopNotification> ActiveNotifications { get; }
    void ShowNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Info, Action? onClick = null);
    void DismissNotification(string id);
}

public sealed class DesktopNotificationService : ViewModelBase, IDesktopNotificationService
{
    public ObservableCollection<DesktopNotification> ActiveNotifications { get; } = [];

    public void ShowNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Info, Action? onClick = null)
    {
        var notification = new DesktopNotification
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Message = message,
            Severity = severity,
            OnClick = onClick,
        };

        ActiveNotifications.Insert(0, notification);

        // Keep last 10 in-app notifications
        while (ActiveNotifications.Count > 10)
        {
            ActiveNotifications.RemoveAt(ActiveNotifications.Count - 1);
        }
    }

    public void DismissNotification(string id)
    {
        DesktopNotification? existing = ActiveNotifications.FirstOrDefault(n => n.Id == id);
        if (existing != null)
        {
            ActiveNotifications.Remove(existing);
        }
    }
}
