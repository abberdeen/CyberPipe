using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Router;

/// <summary>
/// Потокобезопасная реализация хранилища токен-бюджета в памяти (TASK-033).
/// Используется по умолчанию и в тестах.
/// </summary>
public sealed class InMemoryBudgetStore : IBudgetStore
{
    private readonly ConcurrentDictionary<DateOnly, decimal> _dailySpentUsd = new();
    private readonly ConcurrentDictionary<DateOnly, int> _dailyTokens = new();
    private readonly ConcurrentBag<TokenUsageDto> _records = new();

    public IReadOnlyCollection<TokenUsageDto> Records => _records;

    public Task<decimal> GetSpentTodayUsdAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        _dailySpentUsd.TryGetValue(date, out decimal spent);
        return Task.FromResult(spent);
    }

    public Task<int> GetTokensUsedTodayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        _dailyTokens.TryGetValue(date, out int tokens);
        return Task.FromResult(tokens);
    }

    public Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usage);

        _records.Add(usage);
        DateOnly date = DateOnly.FromDateTime(usage.OccurredAtUtc.DateTime);

        _dailySpentUsd.AddOrUpdate(date, usage.CostUsd, (_, existing) => existing + usage.CostUsd);
        _dailyTokens.AddOrUpdate(
            date,
            usage.InputTokens + usage.OutputTokens,
            (_, existing) => existing + usage.InputTokens + usage.OutputTokens);

        return Task.CompletedTask;
    }
}
