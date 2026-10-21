using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CyberPipe.Data;

/// <summary>Амбиентная сессия активного unit-of-work; репозитории используют её соединение и транзакцию.</summary>
internal sealed class UnitOfWorkSession
{
    public required SqliteConnection Connection { get; init; }
    public required SqliteTransaction Transaction { get; init; }
}

internal static class AmbientUnitOfWork
{
    private static readonly AsyncLocal<UnitOfWorkSession?> CurrentSession = new();

    public static UnitOfWorkSession? Current => CurrentSession.Value;

    public static void Set(UnitOfWorkSession? session) => CurrentSession.Value = session;
}

/// <summary>
/// База репозиториев: соединение только из активного unit-of-work или фабрики,
/// только параметризованные запросы, тайминг и логирование медленных запросов без значений
/// параметров (в лог попадает только имя операции и длительность).
/// </summary>
public abstract class RepositoryBase
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly UnitOfWorkMetrics _metrics;

    /// <summary>Создаёт базу репозитория.</summary>
    /// <param name="connectionFactory">Фабрика соединений SQLite.</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    protected RepositoryBase(
        IDatabaseConnectionFactory connectionFactory,
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
        Func<SqliteConnection, SqliteTransaction?, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken)
    {
        UnitOfWorkSession? session = SessionProvider?.Invoke() ?? AmbientUnitOfWork.Current;
        if (session is not null)
            return await RunTimedAsync(operation, () => body(session.Connection, session.Transaction, cancellationToken)).ConfigureAwait(false);

        await using SqliteConnection connection = await _connectionFactory
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
        Func<SqliteConnection, SqliteTransaction?, CancellationToken, Task> body,
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
    protected static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    /// <summary>Добавляет типизированный параметр uuid (TEXT); null — DBNull.</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="value">Значение или null.</param>
    protected static void UuidParam(SqliteCommand command, string name, Guid? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = value.HasValue ? value.Value.ToString("D", CultureInfo.InvariantCulture) : DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Добавляет типизированный текстовый параметр; пустое — DBNull.</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="value">Значение или null.</param>
    protected static void TextParam(SqliteCommand command, string name, string? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Добавляет параметр даты ISO-8601; null — DBNull.</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="value">Значение или null.</param>
    protected static void DateParam(SqliteCommand command, string name, DateOnly? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Добавляет параметр; значение никогда не подставляется в текст SQL.</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="value">Значение; null превращается в DBNull. Guid хранится текстом.</param>
    protected static void Param(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = value switch
        {
            null => DBNull.Value,
            Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
            _ => value,
        };
        command.Parameters.Add(parameter);
    }

    /// <summary>Добавляет параметр JSON-строки (TEXT).</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="json">JSON-строка или null.</param>
    protected static void JsonParam(SqliteCommand command, string name, string? json)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = json is null ? DBNull.Value : json;
        command.Parameters.Add(parameter);
    }

    /// <summary>Добавляет параметр метки времени (ISO-8601 UTC).</summary>
    /// <param name="command">Команда.</param>
    /// <param name="name">Имя параметра без @.</param>
    /// <param name="value">Момент времени или null.</param>
    protected static void TimeParam(SqliteCommand command, string name, DateTimeOffset? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = value.HasValue ? value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) : DBNull.Value;
        command.Parameters.Add(parameter);
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

    /// <summary>Читает метку времени (TEXT ISO-8601, DateTime или DateTimeOffset); DBNull — null.</summary>
    /// <param name="reader">Читатель строки.</param>
    /// <param name="ordinal">Порядковый номер колонки.</param>
    /// <returns>Момент времени или null.</returns>
    protected static DateTimeOffset? ReadTime(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;
        object value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset parsed) => parsed,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTime parsedDt) => new DateTimeOffset(DateTime.SpecifyKind(parsedDt, DateTimeKind.Utc)),
            long unix => DateTimeOffset.FromUnixTimeSeconds(unix),
            _ => throw new InvalidOperationException($"Unexpected timestamp value of type {value.GetType().Name}."),
        };
    }

    /// <summary>Читает обязательную метку времени.</summary>
    /// <param name="reader">Читатель строки.</param>
    /// <param name="ordinal">Порядковый номер колонки.</param>
    /// <returns>Момент времени.</returns>
    protected static DateTimeOffset ReadRequiredTime(SqliteDataReader reader, int ordinal) =>
        ReadTime(reader, ordinal) ?? throw new InvalidOperationException("Unexpected NULL timestamp.");

    /// <summary>Читает дату (TEXT yyyy-MM-dd или DateTime); DBNull — null.</summary>
    /// <param name="reader">Читатель строки.</param>
    /// <param name="ordinal">Порядковый номер колонки.</param>
    /// <returns>Дата или null.</returns>
    protected static DateOnly? ReadDate(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;
        object value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly date => date,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s when DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed) => parsed,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDt) => DateOnly.FromDateTime(parsedDt),
            _ => throw new InvalidOperationException($"Unexpected date value of type {value.GetType().Name}."),
        };
    }

    /// <summary>Читает обязательную дату.</summary>
    /// <param name="reader">Читатель строки.</param>
    /// <param name="ordinal">Порядковый номер колонки.</param>
    /// <returns>Дата.</returns>
    protected static DateOnly ReadRequiredDate(SqliteDataReader reader, int ordinal) =>
        ReadDate(reader, ordinal) ?? throw new InvalidOperationException("Unexpected NULL date.");

    /// <summary>Преобразует скалярное значение метки времени (TEXT ISO-8601, DateTime, DateTimeOffset).</summary>
    /// <param name="value">Значение из RETURNING/ExecuteScalar.</param>
    /// <returns>Момент времени.</returns>
    protected static DateTimeOffset ToDateTimeOffset(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset parsed) => parsed,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTime parsedDt) => new DateTimeOffset(DateTime.SpecifyKind(parsedDt, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException($"Unexpected timestamp value of type {value.GetType().Name}."),
        };
    }

    /// <summary>Читает GUID (TEXT или BLOB).</summary>
    /// <param name="reader">Читатель строки.</param>
    /// <param name="ordinal">Порядковый номер колонки.</param>
    /// <returns>GUID.</returns>
    protected static Guid ReadGuid(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            throw new InvalidOperationException("Unexpected NULL guid.");
        object value = reader.GetValue(ordinal);
        return value switch
        {
            Guid guid => guid,
            string s => Guid.Parse(s),
            byte[] bytes when bytes.Length == 16 => new Guid(bytes),
            _ => reader.GetGuid(ordinal),
        };
    }
}
