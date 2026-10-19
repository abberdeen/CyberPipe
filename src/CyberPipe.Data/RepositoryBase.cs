using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace CyberPipe.Data;

/// <summary>Амбиентная сессия активного unit-of-work; репозитории используют её соединение и транзакцию.</summary>
internal sealed class UnitOfWorkSession
{
    public required NpgsqlConnection Connection { get; init; }
    public required NpgsqlTransaction Transaction { get; init; }
}

internal static class AmbientUnitOfWork
{
    private static readonly AsyncLocal<UnitOfWorkSession?> CurrentSession = new();

    public static UnitOfWorkSession? Current => CurrentSession.Value;

    public static void Set(UnitOfWorkSession? session) => CurrentSession.Value = session;
}

/// <summary>
/// База репозиториев: соединение только из активного unit-of-work или фабрики TASK-010,
/// только параметризованные запросы, тайминг и логирование медленных запросов без значений
/// параметров (в лог попадает только имя операции и длительность).
/// </summary>
public abstract class RepositoryBase
{
    private readonly IPostgresConnectionFactory _connectionFactory;
    private readonly UnitOfWorkMetrics _metrics;

    /// <summary>Создаёт базу репозитория.</summary>
    /// <param name="connectionFactory">Фабрика соединений TASK-010.</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    protected RepositoryBase(
        IPostgresConnectionFactory connectionFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
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

    /// <summary>Выполняет операцию: в активном unit-of-work — на его транзакции, иначе на собственном соединении.</summary>
    /// <typeparam name="T">Тип результата.</typeparam>
    /// <param name="operation">Имя операции для лога медленных запросов, без данных.</param>
    /// <param name="body">Тело операции на соединении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат операции.</returns>
    protected async Task<T> ExecuteAsync<T>(
        string operation,
        Func<NpgsqlConnection, NpgsqlTransaction?, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken)
    {
        UnitOfWorkSession? session = SessionProvider?.Invoke() ?? AmbientUnitOfWork.Current;
        if (session is not null)
            return await RunTimedAsync(operation, () => body(session.Connection, session.Transaction, cancellationToken)).ConfigureAwait(false);

        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await RunTimedAsync(operation, () => body(connection, null, cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Выполняет операцию без результата.</summary>
    /// <param name="operation">Имя операции для лога.</param>
    /// <param name="body">Тело операции.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача операции.</returns>
    protected Task ExecuteAsync(
        string operation,
        Func<NpgsqlConnection, NpgsqlTransaction?, CancellationToken, Task> body,
        CancellationToken cancellationToken) =>
        ExecuteAsync<object?>(
            operation,
            async (connection, transaction, token) =>
            {
                await body(connection, transaction, token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

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
                // Значения параметров и SQL с данными сюда не попадают: только операция и длительность.
                Logger.LogWarning(
                    "Slow query {Operation} took {ElapsedMs} ms (threshold {ThresholdMs} ms).",
                    operation,
                    stopwatch.ElapsedMilliseconds,
                    (long)Options.SlowQueryThreshold.TotalMilliseconds);
            }
        }
    }

    /// <summary>Создаёт параметризованную команду на соединении/транзакции сессии.</summary>
    /// <param name="connection">Соединение.</param>
    /// <param name="transaction">Транзакция unit-of-work или null.</param>
    /// <param name="sql">SQL только с именованными параметрами.</param>
    /// <returns>Команда без значений параметров.</returns>
    protected static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql) =>
        new(sql, connection, transaction);

    /// <summary>Добавляет типизированный параметр uuid; null — DBNull с типом uuid.</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="value">Значение или null.</param>
    protected static void UuidParam(NpgsqlCommand command, string name, Guid? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    /// <summary>Добавляет типизированный текстовый параметр; null — DBNull с типом text.</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="value">Значение или null.</param>
    protected static void TextParam(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    /// <summary>Добавляет типизированный параметр date; null — DBNull с типом date.</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="value">Значение или null.</param>
    protected static void DateParam(NpgsqlCommand command, string name, DateOnly? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Date);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    /// <summary>Добавляет параметр; значение никогда не подставляется в текст SQL.</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="value">Значение; null превращается в DBNull.</param>
    protected static void Param(NpgsqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    /// <summary>Добавляет параметр jsonb из JSON-строки.</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="json">JSON-строка или null.</param>
    protected static void JsonParam(NpgsqlCommand command, string name, string? json)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Jsonb);
        parameter.Value = json is null ? DBNull.Value : json;
    }

    /// <summary>Добавляет параметр timestamptz.</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="value">Момент времени или null.</param>
    protected static void TimeParam(NpgsqlCommand command, string name, DateTimeOffset? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    /// <summary>
    /// Строит ORDER BY только из белого списка полей репозитория; неизвестное поле — исключение
    /// до SQL. Всегда добавляет детерминированный tie-breaker, чтобы страницы не пересекались.
    /// </summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="allowedSorts">Белый список: публичное имя поля в SQL-выражение колонки.</param>
    /// <param name="tieBreaker">SQL-выражение детерминированного вторичного ключа.</param>
    /// <returns>Фрагмент ORDER BY без слова ORDER BY.</returns>
    /// <exception cref="ArgumentException">Сортировка пуста или не входит в белый список.</exception>
    protected string OrderBy(PagedQuery query, IReadOnlyDictionary<string, string> allowedSorts, string tieBreaker)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate(Options.MaxPageLimit);
        string key = query.SortBy.Trim();
        if (!allowedSorts.TryGetValue(key, out string? column))
        {
            throw new ArgumentException(
                $"Sort field '{key}' is not allowed. Allowed: {string.Join(", ", allowedSorts.Keys)}.",
                nameof(query));
        }

        string direction = query.Descending ? "DESC" : "ASC";
        return $"{column} {direction}, {tieBreaker} ASC";
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

    /// <summary>Читает timestamptz как DateTimeOffset; DBNull — null.</summary>
    /// <param name="reader">Читатель строки.</param>
    /// <param name="ordinal">Порядковый номер колонки.</param>
    /// <returns>Момент времени или null.</returns>
    protected static DateTimeOffset? ReadTime(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    /// <summary>Читает обязательный timestamptz.</summary>
    /// <param name="reader">Читатель строки.</param>
    /// <param name="ordinal">Порядковый номер колонки.</param>
    /// <returns>Момент времени.</returns>
    protected static DateTimeOffset ReadRequiredTime(NpgsqlDataReader reader, int ordinal) =>
        ReadTime(reader, ordinal) ?? throw new InvalidOperationException("Unexpected NULL timestamp.");
}
