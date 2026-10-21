using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>
/// Дневной бюджет губернатора поверх книги токен-бюджета: расход переживает
/// рестарт службы. Сырых SQL-строк нет (вся SQL-логика — в книге).
/// </summary>
public sealed class BudgetLedgerService
{
    private readonly TokenBudgetLedgerService _ledger;

    /// <summary>Создаёт сервис дневного бюджета поверх книги токен-бюджета.</summary>
    /// <param name="ledger">Книга токен-бюджета.</param>
    public BudgetLedgerService(TokenBudgetLedgerService ledger)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    /// <summary>Возвращает суммарный расход дня в долларах США.</summary>
    /// <param name="date">Дата бюджета.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Расход дня; 0, если записей нет.</returns>
    public async Task<decimal> GetSpentTodayUsdAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        TokenBudgetDailySummary summary = await _ledger.GetDailySummaryAsync(date, cancellationToken).ConfigureAwait(false);
        return summary.TotalCostUsd;
    }

    /// <summary>Возвращает суммарные токены дня (входные плюс выходные).</summary>
    /// <param name="date">Дата бюджета.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Токены дня, но не больше int.MaxValue.</returns>
    public async Task<int> GetTokensUsedTodayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        TokenBudgetDailySummary summary = await _ledger.GetDailySummaryAsync(date, cancellationToken).ConfigureAwait(false);
        long total = summary.TotalInputTokens + summary.TotalOutputTokens;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    /// <summary>Записывает фактический расход вызова модели в книгу.</summary>
    /// <param name="usage">Фактический расход вызова модели.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача записи.</returns>
    public async Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usage);
        await _ledger.AddUsageAsync(usage, cancellationToken).ConfigureAwait(false);
    }
}
