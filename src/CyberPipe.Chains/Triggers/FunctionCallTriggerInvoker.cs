using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Triggers;

/// <summary>
/// Внутрипроцессный инициатор вызова цепочек вида FunctionCall (TASK-041).
/// Синхронно возвращает <see cref="RunId"/>, не блокируя вызывающий поток до завершения цепочки.
/// </summary>
public sealed class FunctionCallTriggerInvoker : ITriggerInvoker
{
    private readonly IChainDefinitionRepository _repository;
    private readonly IChainExecutor _executor;

    /// <summary>
    /// Инициализирует инвокер функций.
    /// </summary>
    public FunctionCallTriggerInvoker(IChainDefinitionRepository repository, IChainExecutor executor)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <inheritdoc />
    public async Task<RunId> InvokeFunctionCallAsync(ChainId chainId, string payloadJson, CancellationToken cancellationToken = default)
    {
        ChainDefinitionDto? chain = await _repository.GetByIdAsync(chainId, cancellationToken).ConfigureAwait(false);
        if (chain is null)
        {
            throw new ChainNotFoundException(chainId);
        }

        if (!chain.IsEnabled)
        {
            throw new InvalidOperationException($"Chain '{chainId}' is disabled.");
        }

        if (chain.Trigger.Kind != ChainTriggerKind.FunctionCall)
        {
            throw new InvalidOperationException($"Chain '{chainId}' does not use a FunctionCall trigger.");
        }

        string safePayload = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
        try
        {
            using var doc = JsonDocument.Parse(safePayload);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON payload: {ex.Message}", nameof(payloadJson), ex);
        }

        string functionName = chain.Trigger.FunctionName ?? "function_call";

        var triggerEvent = new TriggerEvent
        {
            EventId = Guid.NewGuid(),
            ChainId = chainId,
            Kind = ChainTriggerKind.FunctionCall,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            PayloadJson = safePayload,
            IdempotencyKey = Guid.NewGuid().ToString(),
            Source = functionName,
        };

        return await _executor.StartAsync(triggerEvent, cancellationToken).ConfigureAwait(false);
    }
}
