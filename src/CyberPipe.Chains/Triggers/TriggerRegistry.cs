using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Triggers;

/// <summary>
/// Единый реестр триггеров всех 4 видов (TASK-041).
/// Управляет жизненным циклом регистраций Webhook, Cron, FunctionCall и Chat.
/// </summary>
public sealed class TriggerRegistry : ITriggerRegistry
{
    private readonly IChainDefinitionRepository _repository;
    private readonly CronTriggerManager? _cronManager;
    private readonly ConcurrentDictionary<ChainId, ChainDefinitionDto> _registeredChains = new();

    /// <summary>
    /// Инициализирует реестр триггеров.
    /// </summary>
    public TriggerRegistry(
        IChainDefinitionRepository repository,
        CronTriggerManager? cronManager = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cronManager = cronManager;
    }

    /// <inheritdoc />
    public async Task RegisterAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);

        // Сохраняем цепочку в репозиторий
        await _repository.UpsertAsync(chain, cancellationToken).ConfigureAwait(false);

        if (!chain.IsEnabled)
        {
            _registeredChains.TryRemove(chain.Id, out _);
            return;
        }

        _registeredChains[chain.Id] = chain;

        // Если это Cron-триггер, регистрируем расписание в планировщике
        if (chain.Trigger.Kind == ChainTriggerKind.Cron && _cronManager is not null)
        {
            await _cronManager.RegisterCronChainAsync(chain, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        if (_registeredChains.TryRemove(chainId, out ChainDefinitionDto? removed))
        {
            // Отключаем цепочку в репозитории
            await _repository.SetEnabledAsync(chainId, false, cancellationToken).ConfigureAwait(false);

            // Если это был Cron-триггер, снимаем из планировщика
            if (removed.Trigger.Kind == ChainTriggerKind.Cron && _cronManager is not null)
            {
                await _cronManager.UnregisterCronChainAsync(chainId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public bool IsRegistered(ChainId chainId) =>
        _registeredChains.ContainsKey(chainId);
}
