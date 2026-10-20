using CyberPipe.Contracts;
using Xunit;

namespace CyberPipe.Data.Tests;

public sealed class PostgresBudgetStoreTests
{
    private sealed class FakeBudgetRepository : ITokenBudgetRepository
    {
        public TokenUsageDto? LastUsage { get; private set; }

        public Task<TokenBudgetDailySummary> GetDailySummaryAsync(DateOnly budgetDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TokenBudgetDailySummary
            {
                BudgetDate = budgetDate,
                TotalCostUsd = 1.25m,
                TotalInputTokens = 100,
                TotalOutputTokens = 50,
                EntryCount = 3
            });

        public Task<TokenBudgetEntryRecord> AddUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default)
        {
            LastUsage = usage;
            return Task.FromResult(new TokenBudgetEntryRecord
            {
                Id = Guid.NewGuid(),
                BudgetDate = DateOnly.FromDateTime(usage.OccurredAtUtc.UtcDateTime),
                ModelId = usage.ModelId,
                HostingType = usage.HostingType,
                EntryType = "Usage",
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                CostUsd = usage.CostUsd,
                CreatedAtUtc = usage.OccurredAtUtc,
                RowVersion = usage.OccurredAtUtc
            });
        }

        public Task<TokenBudgetEntryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<TokenBudgetEntryRecord>> ListAsync(PagedQuery query, DateOnly? budgetDate = null, string? modelId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TokenBudgetEntryRecord> AddAsync(TokenBudgetEntryRecord entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TokenBudgetEntryRecord> UpdateAsync(TokenBudgetEntryRecord entry, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task PostgresBudgetStore_ReadsSummary_AndRecordsUsage()
    {
        var repo = new FakeBudgetRepository();
        var store = new PostgresBudgetStore(repo);
        var date = new DateOnly(2026, 10, 20);

        Assert.Equal(1.25m, await store.GetSpentTodayUsdAsync(date));
        Assert.Equal(150, await store.GetTokensUsedTodayAsync(date));

        var usage = new TokenUsageDto
        {
            ModelId = "gpt-4o",
            HostingType = ModelHostingType.RemoteCloud,
            InputTokens = 10,
            OutputTokens = 5,
            CostUsd = 0.01m,
            OccurredAtUtc = DateTimeOffset.UtcNow
        };
        await store.RecordUsageAsync(usage);
        Assert.Same(usage, repo.LastUsage);
    }
}
