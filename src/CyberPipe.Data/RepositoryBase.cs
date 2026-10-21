using System.Diagnostics;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>Амбиентная сессия активного unit-of-work: общий контекст и транзакция репозиториев.</summary>
internal sealed class UnitOfWorkSession
{
    public required CyberPipeDbContext Context { get; init; }
}

internal static class AmbientUnitOfWork
{
    private static readonly AsyncLocal<UnitOfWorkSession?> CurrentSession = new();

    public static UnitOfWorkSession? Current => CurrentSession.Value;

    public static void Set(UnitOfWorkSession? session) => CurrentSession.Value = session;
}

/// <summary>
/// База репозиториев на EF Core: контекст берётся из активного unit-of-work или создаётся
/// из пула фабрики на операцию; все запросы — LINQ, сырых SQL-строк в репозиториях нет.
/// Тайминг и логирование медленных запросов без значений параметров (только операция и длительность).
/// </summary>
public abstract class RepositoryBase
{
    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly UnitOfWorkMetrics _metrics;

    /// <summary>Создаёт базу репозитория.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    protected RepositoryBase(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        Options = options ?? new RepositoryOptions();
        IReadOnlyList<string> errors = Options.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        Logger = logger ?? NullLogger.Instance;
        _metrics = metrics ?? new UnitOfWorkMetrics();
    }

    /// <summary>Поставщик сессии unit-of-work, к которому привязан экземпляр репозитория внутри IUnitOfWork.</summary>
    internal Func<UnitOfWorkSession?>? SessionProvider { get; set; }

    /// <summary>Опции репозиториев.</summary>
    protected RepositoryOptions Options { get; }

    /// <summary>Логгер репозитория.</summary>
    protected ILogger Logger { get; }

    /// <summary>Выполняет операцию: в активном unit-of-work — на его контексте, иначе на контексте из пула.</summary>
    /// <typeparam name="T">Тип результата.</typeparam>
    /// <param name="operation">Имя операции для лога медленных запросов, без данных.</param>
    /// <param name="body">Тело операции на контексте.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат операции.</returns>
    protected async Task<T> ExecuteAsync<T>(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken)
    {
        UnitOfWorkSession? session = SessionProvider?.Invoke() ?? AmbientUnitOfWork.Current;
        if (session is not null)
            return await RunTimedAsync(operation, () => body(session.Context, cancellationToken)).ConfigureAwait(false);

        await using CyberPipeDbContext context = await _dbFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await RunTimedAsync(operation, () => body(context, cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Выполняет операцию без результата.</summary>
    /// <param name="operation">Имя операции для лога.</param>
    /// <param name="body">Тело операции.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача операции.</returns>
    protected Task ExecuteAsync(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task> body,
        CancellationToken cancellationToken) =>
        ExecuteAsync<object?>(
            operation,
            async (context, token) =>
            {
                await body(context, token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <summary>Сохраняет отслеживаемые изменения контекста.</summary>
    /// <param name="context">Контекст операции.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    protected static Task SaveAsync(CyberPipeDbContext context, CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    private async Task<T> RunTimedAsync<T>(string operation, Func<Task<T>> body)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            if (stopwatch.Elapsed >= Options.SlowQueryThreshold)
            {
                _metrics.IncrementSlowQuery();
                // Значения параметров и данные сюда не попадают: только операция и длительность.
                Logger.LogWarning(
                    "Slow query {Operation} took {ElapsedMs} ms (threshold {ThresholdMs} ms).",
                    operation,
                    stopwatch.ElapsedMilliseconds,
                    (long)Options.SlowQueryThreshold.TotalMilliseconds);
            }
        }
    }

    /// <summary>
    /// Проверяет поле сортировки по белому списку репозитория и возвращает имя свойства сущности
    /// для <c>EF.Property</c>; неизвестное поле — исключение до SQL. Сортировка всегда дополняется
    /// детерминированным tie-breaker на стороне вызова, чтобы страницы не пересекались.
    /// </summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="allowedSorts">Белый список: публичное имя поля в имя свойства сущности.</param>
    /// <returns>Имя свойства сущности для сортировки.</returns>
    /// <exception cref="ArgumentException">Сортировка пуста или не входит в белый список.</exception>
    protected string ResolveSortKey(PagedQuery query, IReadOnlyDictionary<string, string> allowedSorts)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate(Options.MaxPageLimit);
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
    protected static IOrderedQueryable<T> ApplyOrdering<T>(
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
    protected static void RequireText(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be empty.", paramName);
    }

    /// <summary>Нормализует опциональный строковый фильтр: пустое — отсутствие фильтра.</summary>
    /// <param name="value">Значение фильтра.</param>
    /// <returns>Значение или null.</returns>
    protected static string? OptionalFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Guid в строковый ключ ("D").</summary>
    protected static string GuidKey(Guid value) =>
        value.ToString("D", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Перехватывает нарушение уникальности и возвращает существующую запись вместо дубля.</summary>
    /// <typeparam name="T">Тип записи.</typeparam>
    /// <param name="add">Вставка новой записи.</param>
    /// <param name="refetch">Повторное чтение существующей записи после конфликта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Новая или существующая запись.</returns>
    protected static async Task<T> AddIdempotentAsync<T>(
        Func<CancellationToken, Task<T>> add,
        Func<CancellationToken, Task<T?>> refetch,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await add(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            T? existing = await refetch(cancellationToken).ConfigureAwait(false);
            if (existing is null)
                throw;
            return existing;
        }
    }
}
