using System.Diagnostics;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>
/// Общая инфраструктура сервисов данных на EF Core: контекст создаётся фабрикой
/// на каждый публичный метод, все чтения — LINQ с <c>AsNoTracking</c>, сырых SQL-строк
/// в сервисах нет. Замер медленных запросов — без значений параметров, только операция
/// и длительность (секреты в логи не попадают).
/// </summary>
internal static class EfServiceHelper
{
    /// <summary>Выполняет операцию с результатом на свежем контексте из фабрики с slow-log.</summary>
    /// <typeparam name="T">Тип результата.</typeparam>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции (порог медленных запросов).</param>
    /// <param name="logger">Логгер медленных запросов.</param>
    /// <param name="metrics">Общие счётчики.</param>
    /// <param name="operation">Имя операции для лога, без данных.</param>
    /// <param name="body">Тело операции на контексте.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат операции.</returns>
    internal static async Task<T> ExecuteAsync<T>(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions options,
        ILogger logger,
        DataServiceMetrics metrics,
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken)
    {
        await using CyberPipeDbContext context = await dbFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await TimedAsync(logger, metrics, options, operation, () => body(context, cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>Выполняет операцию без результата на свежем контексте из фабрики с slow-log.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции (порог медленных запросов).</param>
    /// <param name="logger">Логгер медленных запросов.</param>
    /// <param name="metrics">Общие счётчики.</param>
    /// <param name="operation">Имя операции для лога, без данных.</param>
    /// <param name="body">Тело операции на контексте.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача операции.</returns>
    internal static Task ExecuteAsync(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions options,
        ILogger logger,
        DataServiceMetrics metrics,
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task> body,
        CancellationToken cancellationToken) =>
        ExecuteAsync<object?>(
            dbFactory, options, logger, metrics, operation,
            async (context, token) =>
            {
                await body(context, token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <summary>Замеряет операцию и пишет Warning при превышении порога (без значений параметров).</summary>
    /// <typeparam name="T">Тип результата.</typeparam>
    /// <param name="logger">Логгер.</param>
    /// <param name="metrics">Счётчики (инкремент медленных запросов).</param>
    /// <param name="options">Опции с порогом.</param>
    /// <param name="operation">Имя операции для лога, без данных.</param>
    /// <param name="body">Тело операции.</param>
    /// <returns>Результат операции.</returns>
    internal static async Task<T> TimedAsync<T>(
        ILogger logger,
        DataServiceMetrics metrics,
        RepositoryOptions options,
        string operation,
        Func<Task<T>> body)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            if (stopwatch.Elapsed >= options.SlowQueryThreshold)
            {
                metrics.IncrementSlowQuery();
                // Значения параметров и данные сюда не попадают: только операция и длительность.
                logger.LogWarning(
                    "Slow query {Operation} took {ElapsedMs} ms (threshold {ThresholdMs} ms).",
                    operation,
                    stopwatch.ElapsedMilliseconds,
                    (long)options.SlowQueryThreshold.TotalMilliseconds);
            }
        }
    }

    /// <summary>
    /// Проверяет поле сортировки по белому списку сервиса и возвращает имя свойства сущности
    /// для сортировки; неизвестное поле — исключение до SQL. Сортировка всегда дополняется
    /// детерминированным tie-breaker на стороне вызова, чтобы страницы не пересекались.
    /// </summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="allowedSorts">Белый список: публичное имя поля в имя свойства сущности.</param>
    /// <param name="maxLimit">Максимально допустимый размер страницы из опций.</param>
    /// <returns>Имя свойства сущности для сортировки.</returns>
    /// <exception cref="ArgumentException">Сортировка пуста или не входит в белый список.</exception>
    internal static string ResolveSortKey(PagedQuery query, IReadOnlyDictionary<string, string> allowedSorts, int maxLimit)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate(maxLimit);
        string key = query.SortBy.Trim();
        if (!allowedSorts.TryGetValue(key, out string? property))
        {
            throw new ArgumentException(
                $"Sort field '{key}' is not allowed. Allowed: {string.Join(", ", allowedSorts.Keys)}.",
                nameof(query));
        }

        return property;
    }

    /// <summary>Применяет сортировку страницы по свойству сущности и детерминированный tie-breaker.</summary>
    /// <typeparam name="T">Тип сущности.</typeparam>
    /// <param name="source">Запрос.</param>
    /// <param name="sortProperty">Имя свойства для сортировки (из белого списка).</param>
    /// <param name="descending">Порядок убывания.</param>
    /// <param name="tieBreakerProperty">Имя свойства детерминированного вторичного ключа.</param>
    /// <returns>Упорядоченный запрос.</returns>
    internal static IOrderedQueryable<T> ApplyOrdering<T>(
        IQueryable<T> source, string sortProperty, bool descending, string tieBreakerProperty)
    {
        IOrderedQueryable<T> ordered = OrderByProperty(source, sortProperty, descending, thenBy: false);
        return OrderByProperty(ordered, tieBreakerProperty, descending: false, thenBy: true);
    }

    private static IOrderedQueryable<T> OrderByProperty<T>(
        IQueryable<T> source, string propertyName, bool descending, bool thenBy)
    {
        System.Reflection.PropertyInfo? property = typeof(T).GetProperty(propertyName);
        if (property is null)
            throw new ArgumentException($"Unknown sort property '{propertyName}'.", nameof(propertyName));
        var parameter = Expression.Parameter(typeof(T), "e");
        var key = Expression.Property(parameter, property);
        var lambda = Expression.Lambda(key, parameter);
        string method = (thenBy ? "ThenBy" : "OrderBy") + (descending ? "Descending" : string.Empty);
        var call = Expression.Call(
            typeof(Queryable), method, [typeof(T), property.PropertyType],
            source.Expression, Expression.Quote(lambda));
        return (IOrderedQueryable<T>)source.Provider.CreateQuery<T>(call);
    }

    /// <summary>Проверяет обязательную непустую строку аргумента.</summary>
    /// <param name="value">Значение.</param>
    /// <param name="paramName">Имя параметра.</param>
    /// <exception cref="ArgumentException">Значение пусто.</exception>
    internal static void RequireText(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be empty.", paramName);
    }

    /// <summary>Нормализует опциональный строковый фильтр: пустое — отсутствие фильтра.</summary>
    /// <param name="value">Значение фильтра.</param>
    /// <returns>Значение или null.</returns>
    internal static string? OptionalFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Преобразует Guid в строковый ключ ("D").</summary>
    /// <param name="value">Идентификатор.</param>
    /// <returns>Строковый ключ базы.</returns>
    internal static string GuidKey(Guid value) =>
        value.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
}
