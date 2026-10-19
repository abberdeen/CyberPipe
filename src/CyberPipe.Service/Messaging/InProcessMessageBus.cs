using System.Collections.Concurrent;
using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Service.Messaging;

/// <summary>
/// Внутрипроцессная шина типизированных сообщений (TASK-090): публикация и подписка
/// без внешнего брокера. Транспорт заменяем через <see cref="IMessageTransport" />.
/// </summary>
public sealed class InProcessMessageBus : IMessageBus
{
    private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();
    private readonly object _sync = new();

    /// <inheritdoc />
    public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        List<object> snapshot;
        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TMessage), out List<object>? handlers))
            {
                return Task.CompletedTask;
            }

            snapshot = [.. handlers];
        }

        return InvokeAllAsync(snapshot, message, cancellationToken);
    }

    /// <inheritdoc />
    public IDisposable Subscribe<TMessage>(IMessageHandler<TMessage> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TMessage), out List<object>? handlers))
            {
                handlers = [];
                _handlers[typeof(TMessage)] = handlers;
            }

            handlers.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_sync)
            {
                if (_handlers.TryGetValue(typeof(TMessage), out List<object>? handlers))
                {
                    handlers.Remove(handler);
                }
            }
        });
    }

    private static async Task InvokeAllAsync<TMessage>(
        List<object> handlers, TMessage message, CancellationToken ct)
    {
        foreach (object handler in handlers)
        {
            await ((IMessageHandler<TMessage>)handler).HandleAsync(message, ct).ConfigureAwait(false);
        }
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}

/// <summary>
/// Внутрипроцессный транспорт сообщений по умолчанию (TASK-090): доставляет конверты
/// подписчикам в том же процессе. Замена реализации — только конфигурацией,
/// код логики при подмене не меняется.
/// </summary>
public sealed class InProcessMessageTransport : IMessageTransport
{
    private readonly List<IMessageHandler<MessageEnvelope>> _handlers = [];
    private readonly object _sync = new();

    /// <inheritdoc />
    public Task SendAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        List<IMessageHandler<MessageEnvelope>> snapshot;
        lock (_sync)
        {
            snapshot = [.. _handlers];
        }

        return InvokeAllAsync(snapshot, envelope, cancellationToken);
    }

    /// <inheritdoc />
    public IDisposable Subscribe(IMessageHandler<MessageEnvelope> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_sync)
        {
            _handlers.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_sync)
            {
                _handlers.Remove(handler);
            }
        });
    }

    /// <summary>Сериализует типизированное сообщение в конверт транспорта.</summary>
    public static MessageEnvelope ToEnvelope<TMessage>(TMessage message, string? correlationId = null) =>
        new()
        {
            Id = MessageId.New(),
            MessageType = typeof(TMessage).FullName ?? typeof(TMessage).Name,
            PayloadJson = JsonSerializer.Serialize(message),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
        };

    private static async Task InvokeAllAsync(
        List<IMessageHandler<MessageEnvelope>> handlers, MessageEnvelope envelope, CancellationToken ct)
    {
        foreach (IMessageHandler<MessageEnvelope> handler in handlers)
        {
            await handler.HandleAsync(envelope, ct).ConfigureAwait(false);
        }
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
