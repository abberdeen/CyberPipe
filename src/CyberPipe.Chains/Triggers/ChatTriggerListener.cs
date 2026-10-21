using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Data;

namespace CyberPipe.Chains.Triggers;

/// <summary>
/// Слушатель входящих сообщений каналов связи для запуска chat-триггеров (TASK-041).
/// </summary>
public sealed class ChatTriggerListener : IMessageHandler<IncomingMessage>, IDisposable
{
    private readonly IChainCatalogService _repository;
    private readonly IChainExecutor _executor;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IDisposable _subscription;
    private readonly TimeSpan _idempotencyWindow;

    /// <summary>
    /// Инициализирует слушатель сообщений чата и оформляет подписку.
    /// </summary>
    public ChatTriggerListener(
        IChainCatalogService repository,
        IChainExecutor executor,
        IInboundMessageSource messageSource,
        IIdempotencyStore idempotencyStore,
        TimeSpan? idempotencyWindow = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        _idempotencyWindow = idempotencyWindow ?? TimeSpan.FromHours(24);

        ArgumentNullException.ThrowIfNull(messageSource);
        _subscription = messageSource.Subscribe(this);
    }

    /// <inheritdoc />
    public Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken = default) =>
        OnMessageReceivedAsync(message, cancellationToken);

    /// <summary>
    /// Обрабатывает входящее сообщение из канала.
    /// </summary>
    public async Task OnMessageReceivedAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.Text))
        {
            return;
        }

        IReadOnlyList<ChainDefinitionDto> enabledChains = await _repository.ListEnabledAsync(cancellationToken).ConfigureAwait(false);

        foreach (ChainDefinitionDto chain in enabledChains)
        {
            if (chain.Trigger.Kind != ChainTriggerKind.Chat)
            {
                continue;
            }

            if (!MatchesFilter(chain.Trigger.ChatFilter, message))
            {
                continue;
            }

            string idempotencyKey = $"chat:{message.Id.Value}:{chain.Id}";
            bool isNew = await _idempotencyStore.TryRegisterAsync(idempotencyKey, _idempotencyWindow, cancellationToken).ConfigureAwait(false);
            if (!isNew)
            {
                continue;
            }

            var payload = new
            {
                text = message.Text,
                senderId = message.SenderId,
                chatId = message.ChatId,
                channel = message.Channel.ToString(),
            };

            string payloadJson = JsonSerializer.Serialize(payload);
            IReadOnlyList<string> fileRefs = message.Attachments
                .Select(a => a.FileRef)
                .Where(r => !string.IsNullOrEmpty(r))
                .Select(r => r!)
                .ToList();

            var triggerEvent = new TriggerEvent
            {
                EventId = Guid.NewGuid(),
                ChainId = chain.Id,
                Kind = ChainTriggerKind.Chat,
                OccurredAtUtc = message.SentAtUtc,
                PayloadJson = payloadJson,
                FileRefs = fileRefs,
                IdempotencyKey = idempotencyKey,
                Source = $"{message.Channel}:{message.ChatId ?? message.SenderId}",
            };

            await _executor.StartAsync(triggerEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Проверяет совпадение входящего сообщения с фильтром триггера.
    /// </summary>
    public static bool MatchesFilter(string? filter, IncomingMessage message)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.IsNullOrWhiteSpace(message.Text))
        {
            return false;
        }

        string text = message.Text.Trim();

        // Exact match
        if (string.Equals(text, filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Format "cmd:<command_name>"
        if (filter.StartsWith("cmd:", StringComparison.OrdinalIgnoreCase))
        {
            string cmdName = filter["cmd:".Length..].Trim();
            if (text.Equals($"/{cmdName}", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith($"/{cmdName} ", StringComparison.OrdinalIgnoreCase) ||
                text.Equals(cmdName, StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith($"{cmdName} ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Prefix match
        if (text.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _subscription.Dispose();
    }
}
