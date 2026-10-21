using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>
/// SQLite backing для <see cref="IBudgetStore"/> (TASK-033): дневной расход переживает
/// рестарт службы, в отличие от InMemoryBudgetStore роутера (тот остаётся fallback-ом
/// для standalone-композиций без Data).
/// </summary>
public sealed class SqliteBudgetStore : IBudgetStore
{
    private readonly ITokenBudgetRepository _repository;

    public SqliteBudgetStore(ITokenBudgetRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<decimal> GetSpentTodayUsdAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        TokenBudgetDailySummary summary = await _repository.GetDailySummaryAsync(date, cancellationToken).ConfigureAwait(false);
        return summary.TotalCostUsd;
    }

    public async Task<int> GetTokensUsedTodayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        TokenBudgetDailySummary summary = await _repository.GetDailySummaryAsync(date, cancellationToken).ConfigureAwait(false);
        long total = summary.TotalInputTokens + summary.TotalOutputTokens;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    public async Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usage);
        await _repository.AddUsageAsync(usage, cancellationToken).ConfigureAwait(false);
    }
}
