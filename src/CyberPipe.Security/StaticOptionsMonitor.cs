using Microsoft.Extensions.Options;

namespace CyberPipe.Security;

/// <summary>
/// Простой монитор опций поверх фиксированного снимка (TASK-090).
/// Используется композицией до появления файлового перезагрузчика TASK-003:
/// <see cref="Refresh" /> применяет новый снимок и рассылает событие подписчикам
/// (например, белый список Owner применяется без перезапуска Service).
/// </summary>
/// <typeparam name="T">Тип опций.</typeparam>
public sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    private readonly object _sync = new();
    private T _currentValue;
    private event Action<T, string?>? Changed;

    /// <summary>Создаёт монитор с начальным снимком.</summary>
    public StaticOptionsMonitor(T currentValue)
    {
        ArgumentNullException.ThrowIfNull(currentValue);
        _currentValue = currentValue;
    }

    /// <inheritdoc />
    public T CurrentValue
    {
        get
        {
            lock (_sync)
            {
                return _currentValue;
            }
        }
    }

    /// <inheritdoc />
    public T Get(string? name) => CurrentValue;

    /// <inheritdoc />
    public IDisposable OnChange(Action<T, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        Changed += listener;
        return new Subscription(() => Changed -= listener);
    }

    /// <summary>Заменяет снимок и уведомляет подписчиков.</summary>
    public void Refresh(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Action<T, string?>? handlers;
        lock (_sync)
        {
            _currentValue = value;
            handlers = Changed;
        }

        handlers?.Invoke(value, string.Empty);
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
