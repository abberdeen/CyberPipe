using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Logging;

/// <summary>Структурированная запись расхода LLM-вызовов полями из TokenUsageDto TASK-002.</summary>
public static class TokenUsageLoggingExtensions
{
    /// <summary>
    /// Пишет событие вызова LLM: модель, входные/выходные/суммарные токены, стоимость и ярус.
    /// RunId, ChainId и ChannelKind добавляет OperationContextEnricher из текущей области.
    /// </summary>
    /// <param name="logger">Логер потребителя.</param>
    /// <param name="usage">Фактический расход вызова из TASK-002.</param>
    /// <param name="intentTier">Ярус маршрутизации вызова; может отсутствовать.</param>
    public static void LogTokenUsage(this ILogger logger, TokenUsageDto usage, IntentTier? intentTier = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(usage);

        logger.LogInformation(
            "LLM call completed ModelId={ModelId} HostingType={HostingType} InputTokens={InputTokens} OutputTokens={OutputTokens} TotalTokens={TotalTokens} CostUsd={CostUsd} IntentTier={IntentTier}",
            usage.ModelId,
            usage.HostingType,
            usage.InputTokens,
            usage.OutputTokens,
            usage.InputTokens + usage.OutputTokens,
            usage.CostUsd,
            intentTier?.ToString());
    }
}
