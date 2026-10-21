using CyberPipe.Contracts;
using CyberPipe.Data;
using Xunit;

namespace CyberPipe.Data.Tests;

public sealed class SqliteBudgetStoreTests
{
    [SqliteIntegrationFact]
    public async Task SqliteBudgetStore_ReadsSummary_AndRecordsUsage()
    {
        await using SqliteTestDatabase db = await SqliteTestDatabase.CreateAsync();
        var ledger = new TokenBudgetLedgerService(db.DbFactory);
        var store = new BudgetLedgerService(ledger);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(0m, await store.GetSpentTodayUsdAsync(date));
        Assert.Equal(0, await store.GetTokensUsedTodayAsync(date));

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

        Assert.Equal(0.01m, await store.GetSpentTodayUsdAsync(date));
        Assert.Equal(15, await store.GetTokensUsedTodayAsync(date));

        // Книга под сервисом видит ту же запись: итоги книги и дневного бюджета совпадают.
        TokenBudgetDailySummary summary = await ledger.GetDailySummaryAsync(date);
        Assert.Equal(1, summary.EntryCount);
        Assert.Equal(0.01m, summary.TotalCostUsd);
        Assert.Equal(10, summary.TotalInputTokens);
        Assert.Equal(5, summary.TotalOutputTokens);
    }
}
