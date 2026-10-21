using System.Reflection;
using CyberPipe.Contracts;

namespace CyberPipe.Logging;

/// <summary>Заглушка проверки БД до TASK-010: подключение ещё не реализовано, состояние Degraded без секретов.</summary>
public sealed class UnavailableDatabaseHealthProbe : IDatabaseHealthProbe
{
    /// <inheritdoc />
    public Task<DatabaseHealthDto> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DatabaseHealthDto
        {
            Status = HealthStatus.Degraded,
            Detail = "Database connection is not configured yet (TASK-010).",
            CheckedAtUtc = DateTimeOffset.UtcNow,
        });
}

/// <summary>
/// Сборка HealthReportDto из fake/реальных зависимостей через интерфейсы контрактов.
/// Недоступность БД не бросает исключений наружу: даёт Degraded с типом ошибки без её текста,
/// который мог бы содержать строку подключения. Реальное подключение подключается в TASK-010
/// заменой IDatabaseHealthProbe без изменения контракта.
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly string _processName;
    private readonly CyberPipeOptions _options;
    private readonly IDatabaseHealthProbe _databaseProbe;
    private readonly IMessageTransport? _transport;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly string _version;
    private readonly string _processState;

    /// <summary>Создаёт сервис диагностики.</summary>
    /// <param name="processName">Имя процесса для отчёта.</param>
    /// <param name="options">Конфигурация; из неё берутся SLO-цели и адрес БД без секретов.</param>
    /// <param name="databaseProbe">Проверка подключения к SQLite.</param>
    /// <param name="transport">Транспорт сообщений; может быть null, если не настроен.</param>
    /// <param name="startedAtUtc">Момент старта процесса; по умолчанию момент создания сервиса.</param>
    /// <param name="version">Версия приложения; по умолчанию версия входной сборки.</param>
    /// <param name="processState">Состояние процесса строкой; по умолчанию Running.</param>
    public DiagnosticsService(
        string processName,
        CyberPipeOptions options,
        IDatabaseHealthProbe databaseProbe,
        IMessageTransport? transport = null,
        DateTimeOffset? startedAtUtc = null,
        string? version = null,
        string processState = "Running")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        _processName = processName;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _databaseProbe = databaseProbe ?? throw new ArgumentNullException(nameof(databaseProbe));
        _transport = transport;
        _startedAtUtc = startedAtUtc ?? DateTimeOffset.UtcNow;
        _version = version ?? ResolveVersion();
        _processState = processState;
    }

    /// <inheritdoc />
    public async Task<HealthReportDto> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset checkedAtUtc = DateTimeOffset.UtcNow;
        DatabaseHealthDto database = await CheckDatabaseAsync(checkedAtUtc, cancellationToken).ConfigureAwait(false);
        TransportHealthDto transport = CheckTransport(checkedAtUtc);

        var components = new List<ComponentHealthDto>
        {
            new()
            {
                Name = "Database",
                Status = database.Status,
                Detail = database.Detail,
                CheckedAtUtc = database.CheckedAtUtc,
            },
            new()
            {
                Name = "MessageTransport",
                Status = transport.Status,
                Detail = transport.Detail,
                CheckedAtUtc = transport.CheckedAtUtc,
            },
        };

        HealthStatus overall = components.Aggregate(
            HealthStatus.Healthy,
            (current, component) => component.Status > current ? component.Status : current);

        return new HealthReportDto
        {
            ProcessName = _processName,
            ProcessState = _processState,
            Status = overall,
            Version = _version,
            StartedAtUtc = _startedAtUtc,
            CheckedAtUtc = checkedAtUtc,
            Database = database,
            Transport = transport,
            PerformanceTargets = new SloTargetsDto
            {
                Tier0TargetMilliseconds = _options.PerformanceTargets.Tier0TargetMilliseconds,
                StopTargetMilliseconds = _options.PerformanceTargets.StopTargetMilliseconds,
                Tier0ActualMilliseconds = null,
                StopActualMilliseconds = null,
            },
            Components = components,
        };
    }

    private async Task<DatabaseHealthDto> CheckDatabaseAsync(
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            DatabaseHealthDto result = await _databaseProbe.CheckAsync(cancellationToken).ConfigureAwait(false);
            return result with
            {
                Path = result.Path ?? _options.Database.Path,
                Database = result.Database ?? _options.Database.Path,
                Detail = SecretRedaction.RedactText(result.Detail),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DatabaseHealthDto
            {
                Status = HealthStatus.Degraded,
                Path = _options.Database.Path,
                Database = _options.Database.Path,
                Detail = $"Database check failed: {ex.GetType().Name}.",
                CheckedAtUtc = checkedAtUtc,
            };
        }
    }

    private TransportHealthDto CheckTransport(DateTimeOffset checkedAtUtc)
    {
        if (_transport is null)
        {
            return new TransportHealthDto
            {
                Status = HealthStatus.Degraded,
                ImplementationType = "none",
                Detail = "Message transport is not configured.",
                CheckedAtUtc = checkedAtUtc,
            };
        }

        return new TransportHealthDto
        {
            Status = HealthStatus.Healthy,
            ImplementationType = _transport.GetType().FullName ?? _transport.GetType().Name,
            CheckedAtUtc = checkedAtUtc,
        };
    }

    private static string ResolveVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(DiagnosticsService).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
