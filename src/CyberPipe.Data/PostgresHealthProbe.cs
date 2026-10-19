using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>
/// Реальная проверка подключения PostgreSQL для Service (TASK-020/TASK-063 через TASK-004
/// IDatabaseHealthProbe). Ready отражается как Healthy, любая недоступность — как Degraded
/// с кодом причины; секретов, строки подключения и текста ошибки драйвера в Detail нет.
/// </summary>
public sealed class PostgresHealthProbe : IDatabaseHealthProbe
{
    private readonly PostgresOptions _options;
    private readonly ISecretStore _secretStore;
    private readonly IPostgresBootstrapGateway _gateway;

    /// <summary>Создаёт пробу с реальной реализацией шлюза Npgsql.</summary>
    /// <param name="options">Опции подключения.</param>
    /// <param name="secretStore">Хранилище секретов TASK-003.</param>
    public PostgresHealthProbe(PostgresOptions options, ISecretStore secretStore)
        : this(
            options,
            secretStore,
            new PostgresBootstrapGateway(options, new PostgresConnectionFactory(options, secretStore)))
    {
    }

    /// <summary>Создаёт пробу с явным шлюзом (шов unit-тестов).</summary>
    /// <param name="options">Опции подключения.</param>
    /// <param name="secretStore">Хранилище секретов TASK-003.</param>
    /// <param name="gateway">Операции над сервером.</param>
    public PostgresHealthProbe(
        PostgresOptions options,
        ISecretStore secretStore,
        IPostgresBootstrapGateway gateway)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    /// <summary>Сопоставляет результат bootstrap состоянию здоровья без секретов.</summary>
    /// <param name="result">Результат bootstrap.</param>
    /// <param name="options">Опции подключения для адреса в отчёте.</param>
    /// <param name="checkedAtUtc">Момент проверки.</param>
    /// <returns>Состояние базы данных для HealthReportDto.</returns>
    public static DatabaseHealthDto ToHealthDto(
        BootstrapResult result,
        PostgresOptions options,
        DateTimeOffset checkedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        return result.Status switch
        {
            BootstrapStatus.Ready => new DatabaseHealthDto
            {
                Status = HealthStatus.Healthy,
                Host = options.Host,
                Port = options.Port,
                Database = options.Database,
                CheckedAtUtc = checkedAtUtc,
            },
            _ => new DatabaseHealthDto
            {
                Status = HealthStatus.Degraded,
                Host = options.Host,
                Port = options.Port,
                Database = options.Database,
                Detail = $"PostgreSQL {result.Status}: {result.ReasonCode}.",
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
            SecretValue? password = await _secretStore
                .GetAsync(_options.PasswordSecretKey, cancellationToken).ConfigureAwait(false);
            await _gateway.ProbeAsync(password, cancellationToken).ConfigureAwait(false);
            return new DatabaseHealthDto
            {
                Status = HealthStatus.Healthy,
                Host = _options.Host,
                Port = _options.Port,
                Database = _options.Database,
                CheckedAtUtc = checkedAtUtc,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgresUnavailableException ex)
        {
            return Degraded(checkedAtUtc, ex.ReasonCode);
        }
        catch (Exception ex)
        {
            return Degraded(checkedAtUtc, PostgresErrorClassifier.Classify(ex));
        }
    }

    private DatabaseHealthDto Degraded(DateTimeOffset checkedAtUtc, BootstrapReasonCode reasonCode) => new()
    {
        Status = HealthStatus.Degraded,
        Host = _options.Host,
        Port = _options.Port,
        Database = _options.Database,
        Detail = $"PostgreSQL unavailable: {reasonCode}.",
        CheckedAtUtc = checkedAtUtc,
    };
}
