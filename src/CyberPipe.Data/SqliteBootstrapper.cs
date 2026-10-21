using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CyberPipe.Data;

/// <summary>Точка входа bootstrap SQLite.</summary>
public interface IDatabaseBootstrapper
{
    /// <summary>
    /// Обеспечивает готовность файла базы: каталог, открытие, режим WAL.
    /// Не бросает при недоступности файла: возвращает BootstrapResult без секретов.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат bootstrap без секретов.</returns>
    Task<BootstrapResult> EnsureReadyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Bootstrap SQLite (WAL). Идемпотентен: повторный запуск не пересоздаёт файл
/// и не сбрасывает данные. Паролей и серверов нет: промпт не вызывается никогда.
/// </summary>
public sealed class SqliteBootstrapper : IDatabaseBootstrapper
{
    private readonly SqliteOptions _options;
    private readonly IDatabaseBootstrapGateway _gateway;
    private readonly ILogger<SqliteBootstrapper> _logger;

    /// <summary>Создаёт bootstrapper с реальной реализацией шлюза.</summary>
    /// <param name="options">Опции SQLite.</param>
    /// <param name="logger">Логгер; в события попадают только коды причин.</param>
    public SqliteBootstrapper(
        SqliteOptions options,
        ILogger<SqliteBootstrapper>? logger = null)
        : this(
            options,
            new SqliteBootstrapGateway(new SqliteConnectionFactory(options)),
            logger)
    {
    }

    /// <summary>Создаёт bootstrapper с явным шлюзом (шов unit-тестов без реальной БД).</summary>
    /// <param name="options">Опции SQLite.</param>
    /// <param name="gateway">Операции над файлом базы.</param>
    /// <param name="logger">Логгер; в события попадают только коды причин.</param>
    public SqliteBootstrapper(
        SqliteOptions options,
        IDatabaseBootstrapGateway gateway,
        ILogger<SqliteBootstrapper>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? NullLogger<SqliteBootstrapper>.Instance;
    }

    /// <inheritdoc />
    public async Task<BootstrapResult> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> validationErrors = _options.Validate();
        if (validationErrors.Count > 0)
        {
            _logger.LogWarning(
                "SQLite bootstrap skipped: invalid options ({ErrorCount} errors).", validationErrors.Count);
            return BootstrapResult.Unavailable(BootstrapReasonCode.Unknown);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            SqliteServerInfo info = await _gateway
                .EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "SQLite bootstrap ready. Path={Path} ServerVersion={ServerVersion}",
                _options.Path, info.ServerVersion);
            return BootstrapResult.Ready(info.ServerVersion);
        }
        catch (SqliteUnavailableException ex)
        {
            _logger.LogWarning(
                "SQLite bootstrap attempt failed. Reason={ReasonCode}",
                ex.ReasonCode);
            return BootstrapResult.Unavailable(ex.ReasonCode);
        }
    }
}
