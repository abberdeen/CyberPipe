using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Triggers;

/// <summary>
/// Менеджер cron-триггеров (TASK-041).
/// Обеспечивает интеграцию с планировщиком задач (TASK-045) и обработку срабатываний по расписанию.
/// </summary>
public sealed class CronTriggerManager
{
    private readonly IChainDefinitionRepository _repository;
    private readonly IChainExecutor _executor;
    private readonly ICronTriggerRegistrar _cronRegistrar;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly TimeSpan _idempotencyWindow;

    /// <summary>
    /// Инициализирует менеджер cron-триггеров.
    /// </summary>
    public CronTriggerManager(
        IChainDefinitionRepository repository,
        IChainExecutor executor,
        ICronTriggerRegistrar cronRegistrar,
        IIdempotencyStore idempotencyStore,
        TimeSpan? idempotencyWindow = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _cronRegistrar = cronRegistrar ?? throw new ArgumentNullException(nameof(cronRegistrar));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        _idempotencyWindow = idempotencyWindow ?? TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Регистрирует cron-цепочку в планировщике.
    /// </summary>
    public async Task RegisterCronChainAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Trigger.Kind != ChainTriggerKind.Cron || string.IsNullOrWhiteSpace(chain.Trigger.CronExpression))
        {
            throw new ArgumentException("Chain must have a Cron trigger with a non-empty CronExpression.", nameof(chain));
        }

        if (!chain.IsEnabled)
        {
            return;
        }

        await _cronRegistrar.RegisterCronAsync(chain.Id, chain.Trigger.CronExpression, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отменяет регистрацию cron-цепочки в планировщике.
    /// </summary>
    public async Task UnregisterCronChainAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        await _cronRegistrar.UnregisterCronAsync(chainId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Обрабатывает событие наступления времени расписания от планировщика.
    /// </summary>
    public async Task<RunId?> FireCronAsync(ChainId chainId, DateTimeOffset scheduledAtUtc, CancellationToken cancellationToken = default)
    {
        ChainDefinitionDto? chain = await _repository.GetByIdAsync(chainId, cancellationToken).ConfigureAwait(false);
        if (chain is null || !chain.IsEnabled || chain.Trigger.Kind != ChainTriggerKind.Cron)
        {
            return null;
        }

        string idempotencyKey = $"cron:{chainId}:{scheduledAtUtc.ToUnixTimeSeconds()}";
        bool isNew = await _idempotencyStore.TryRegisterAsync(idempotencyKey, _idempotencyWindow, cancellationToken).ConfigureAwait(false);
        if (!isNew)
        {
            return null;
        }

        string payloadJson = JsonSerializer.Serialize(new
        {
            scheduledAtUtc = scheduledAtUtc.ToString("o"),
            cronExpression = chain.Trigger.CronExpression,
        });

        var triggerEvent = new TriggerEvent
        {
            EventId = Guid.NewGuid(),
            ChainId = chainId,
            Kind = ChainTriggerKind.Cron,
            OccurredAtUtc = scheduledAtUtc,
            PayloadJson = payloadJson,
            IdempotencyKey = idempotencyKey,
            Source = $"cron:{chainId}",
        };

        return await _executor.StartAsync(triggerEvent, cancellationToken).ConfigureAwait(false);
    }
}
