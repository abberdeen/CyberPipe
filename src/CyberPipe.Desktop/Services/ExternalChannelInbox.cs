using CyberPipe.Contracts;

namespace CyberPipe.Desktop.Services;

/// <summary>
/// Входящий ящик внешнего канала. Форма проверки навыков кладёт сюда сообщение,
/// подписчик забирает его так же, как сообщение, пришедшее снаружи процесса.
/// </summary>
public sealed class ExternalChannelInbox : IInboundMessageSource
{
    private readonly Lock _gate = new();
    private readonly List<IMessageHandler<IncomingMessage>> _handlers = [];

    /// <summary>Канал вне этого процесса. В окне он называется внешним каналом.</summary>
    public static ChannelKind OutsideChannel => ChannelKind.Telegram;

    public IDisposable Subscribe(IMessageHandler<IncomingMessage> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _handlers.Add(handler);
        }

        return new Subscription(this, handler);
    }

    public async Task PublishAsync(IncomingMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        IMessageHandler<IncomingMessage>[] snapshot;
        lock (_gate)
        {
            snapshot = _handlers.ToArray();
        }

        foreach (IMessageHandler<IncomingMessage> handler in snapshot)
        {
            await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class Subscription(ExternalChannelInbox inbox, IMessageHandler<IncomingMessage> handler) : IDisposable
    {
        public void Dispose()
        {
            lock (inbox._gate)
            {
                inbox._handlers.Remove(handler);
            }
        }
    }
}
