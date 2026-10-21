using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>
/// Реальная проверка файла SQLite для Service через <see cref="IDatabaseHealthProbe" />:
/// Ready отражается как Healthy, любая недоступность — как Degraded с кодом причины.
/// </summary>
public sealed class SqliteHealthProbe : IDatabaseHealthProbe
{
    private readonly SqliteOptions _options;
    private readonly IDatabaseBootstrapGateway _gateway;

    /// <summary>Создаёт пробу с реальной реализацией шлюза.</summary>
    /// <param name="options">Опции SQLite.</param>
    public SqliteHealthProbe(SqliteOptions options)
        : this(
            options,
            new SqliteBootstrapGateway(new SqliteConnectionFactory(options)))
    {
    }

    /// <summary>Создаёт пробу с явным шлюзом (шов unit-тестов).</summary>
    /// <param name="options">Опции SQLite.</param>
    /// <param name="gateway">Операции над файлом базы.</param>
    public SqliteHealthProbe(
        SqliteOptions options,
        IDatabaseBootstrapGateway gateway)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    /// <summary>Сопоставляет результат bootstrap состоянию здоровья без секретов.</summary>
    /// <param name="result">Результат bootstrap.</param>
    /// <param name="options">Опции SQLite для отчёта.</param>
    /// <param name="checkedAtUtc">Момент проверки.</param>
    /// <returns>Состояние базы данных для HealthReportDto.</returns>
    public static DatabaseHealthDto ToHealthDto(
        BootstrapResult result,
        SqliteOptions options,
        DateTimeOffset checkedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        return result.Status switch
        {
            BootstrapStatus.Ready => new DatabaseHealthDto
            {
                Status = HealthStatus.Healthy,
                Path = options.Path,
                Database = options.Path,
                CheckedAtUtc = checkedAtUtc,
            },
            _ => new DatabaseHealthDto
            {
                Status = HealthStatus.Degraded,
                Path = options.Path,
                Database = options.Path,
                Detail = $"SQLite {result.Status}: {result.ReasonCode}.",
                CheckedAtUtc = checkedAtUtc,
            },
        };
    }

    /// <inheritdoc />
    public async Task<DatabaseHealthDto> CheckAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset checkedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            await _gateway.ProbeAsync(cancellationToken).ConfigureAwait(false);
            return new DatabaseHealthDto
            {
                Status = HealthStatus.Healthy,
                Path = _options.Path,
                Database = _options.Path,
                CheckedAtUtc = checkedAtUtc,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteUnavailableException ex)
        {
            return Degraded(checkedAtUtc, ex.ReasonCode);
        }
        catch (Exception ex)
        {
            return Degraded(checkedAtUtc, SqliteErrorClassifier.Classify(ex));
        }
    }

    private DatabaseHealthDto Degraded(DateTimeOffset checkedAtUtc, BootstrapReasonCode reasonCode) => new()
    {
        Status = HealthStatus.Degraded,
        Path = _options.Path,
        Database = _options.Path,
        Detail = $"SQLite unavailable: {reasonCode}.",
        CheckedAtUtc = checkedAtUtc,
    };
}
