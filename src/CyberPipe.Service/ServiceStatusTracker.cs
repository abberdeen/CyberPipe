using System.Reflection;
using CyberPipe.Contracts;

namespace CyberPipe.Service;

/// <summary>
/// Отслеживает жизненный цикл и текущее состояние службы CyberPipe (TASK-020).
/// Допустимые состояния: Running, Stopping, Stopped, Faulted.
/// </summary>
public sealed class ServiceStatusTracker
{
    private readonly object _sync = new();
    private readonly string _version;
    private string _state;
    private DateTimeOffset _startedAtUtc;
    private string? _lastError;

    public ServiceStatusTracker(string? version = null)
    {
        _version = version
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? typeof(ServiceStatusTracker).Assembly.GetName().Version?.ToString()
            ?? "1.0.0";
        _state = "Stopped";
        _startedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Событие смены состояния службы.</summary>
    public event Action<ServiceStateChanged>? StateChanged;

    /// <summary>Текущее состояние строкой.</summary>
    public string State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    /// <summary>Момент старта службы (UTC).</summary>
    public DateTimeOffset StartedAtUtc
    {
        get
        {
            lock (_sync)
            {
                return _startedAtUtc;
            }
        }
    }

    /// <summary>Версия сборки.</summary>
    public string Version => _version;

    /// <summary>Последняя зафиксированная ошибка.</summary>
    public string? LastError
    {
        get
        {
            lock (_sync)
            {
                return _lastError;
            }
        }
    }

    /// <summary>
    /// Переводит службу в состояние Running и фиксирует момент запуска.
    /// </summary>
    public void MarkRunning()
    {
        TransitionTo("Running", null, resetStartTime: true);
    }

    /// <summary>
    /// Переводит службу в состояние Stopping.
    /// </summary>
    public void MarkStopping(string? reason = null)
    {
        TransitionTo("Stopping", reason);
    }

    /// <summary>
    /// Переводит службу в состояние Stopped.
    /// </summary>
    public void MarkStopped()
    {
        TransitionTo("Stopped", null);
    }

    /// <summary>
    /// Переводит службу в состояние Faulted с описанием ошибки.
    /// </summary>
    public void MarkFaulted(string error)
    {
        TransitionTo("Faulted", error);
    }

    /// <summary>
    /// Выполняет переход в новое состояние и уведомляет подписчиков.
    /// </summary>
    public void TransitionTo(string newState, string? error = null, bool resetStartTime = false)
    {
        ServiceStateChanged notification;
        lock (_sync)
        {
            _state = newState;
            if (resetStartTime)
            {
                _startedAtUtc = DateTimeOffset.UtcNow;
            }
            if (error != null)
            {
                _lastError = error;
            }
            notification = new ServiceStateChanged(_state, DateTimeOffset.UtcNow);
        }

        StateChanged?.Invoke(notification);
    }

    /// <summary>
    /// Формирует снимок состояния службы для health-check и Desktop (TASK-002).
    /// </summary>
    public ServiceStatusDto ToDto()
    {
        lock (_sync)
        {
            return new ServiceStatusDto(_state, _version, _startedAtUtc, _lastError);
        }
    }
}
