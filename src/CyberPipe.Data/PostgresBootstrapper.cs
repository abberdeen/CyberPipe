using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CyberPipe.Data;

/// <summary>Точка входа bootstrap PostgreSQL.</summary>
public interface IPostgresBootstrapper
{
    /// <summary>
    /// Обеспечивает готовность PostgreSQL: подключение, создание базы, расширения vector/pgcrypto.
    /// При неверном пароле запрашивает его через ISecretPrompt ограниченное число раз.
    /// Не бросает при недоступности сервера: возвращает BootstrapResult без секретов.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат bootstrap без секретов.</returns>
    Task<BootstrapResult> EnsureReadyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Bootstrap PostgreSQL (TASK-010). Идемпотентен: повторный запуск не пересоздаёт базу,
/// не сбрасывает данные и не меняет пароль пользователя в PostgreSQL. Пароль, введённый через
/// промпт и подтверждённый успешным подключением, сохраняется в ISecretStore под ключом из опций,
/// чтобы следующий запуск промпт не требовал. В логи и результаты пароль и строка подключения
/// не попадают: логируются только коды причин и факт попыток.
/// </summary>
public sealed class PostgresBootstrapper : IPostgresBootstrapper
{
    private readonly PostgresOptions _options;
    private readonly ISecretStore _secretStore;
    private readonly ISecretPrompt _secretPrompt;
    private readonly IPostgresBootstrapGateway _gateway;
    private readonly ILogger<PostgresBootstrapper> _logger;

    /// <summary>Создаёт bootstrapper с реальной реализацией шлюза Npgsql.</summary>
    /// <param name="options">Опции подключения; пароль значением не содержат.</param>
    /// <param name="secretStore">Хранилище секретов TASK-003.</param>
    /// <param name="secretPrompt">Запрос пароля у пользователя; null — NullSecretPrompt (отказ).</param>
    /// <param name="logger">Логгер; в события попадают только коды причин.</param>
    public PostgresBootstrapper(
        PostgresOptions options,
        ISecretStore secretStore,
        ISecretPrompt? secretPrompt = null,
        ILogger<PostgresBootstrapper>? logger = null)
        : this(
            options,
            secretStore,
            secretPrompt,
            new PostgresBootstrapGateway(
                options,
                new PostgresConnectionFactory(options, secretStore)),
            logger)
    {
    }

    /// <summary>Создаёт bootstrapper с явным шлюзом (шов unit-тестов без реальной БД).</summary>
    /// <param name="options">Опции подключения.</param>
    /// <param name="secretStore">Хранилище секретов TASK-003.</param>
    /// <param name="secretPrompt">Запрос пароля; null — отказ без запроса.</param>
    /// <param name="gateway">Операции над сервером.</param>
    /// <param name="logger">Логгер; в события попадают только коды причин.</param>
    public PostgresBootstrapper(
        PostgresOptions options,
        ISecretStore secretStore,
        ISecretPrompt? secretPrompt,
        IPostgresBootstrapGateway gateway,
        ILogger<PostgresBootstrapper>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _secretPrompt = secretPrompt ?? new NullSecretPrompt();
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? NullLogger<PostgresBootstrapper>.Instance;
    }

    /// <inheritdoc />
    public async Task<BootstrapResult> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> validationErrors = _options.Validate();
        if (validationErrors.Count > 0)
        {
            _logger.LogWarning(
                "PostgreSQL bootstrap skipped: invalid options ({ErrorCount} errors).", validationErrors.Count);
            return BootstrapResult.Unavailable(BootstrapReasonCode.Unknown);
        }

        SecretValue? stored = await _secretStore
            .GetAsync(_options.PasswordSecretKey, cancellationToken).ConfigureAwait(false);
        SecretValue? password = stored;
        bool passwordFromStore = stored is not null;
        int promptAttempts = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                PostgresServerInfo info = await _gateway
                    .EnsureReadyAsync(password, cancellationToken).ConfigureAwait(false);

                if (!passwordFromStore && password is not null)
                {
                    // Пароль из промпта подтверждён сервером: сохранить, чтобы повторный запуск не спрашивал.
                    await _secretStore
                        .SetAsync(_options.PasswordSecretKey, password, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "PostgreSQL bootstrap ready. ServerVersion={ServerVersion} PgVectorVersion={PgVectorVersion}",
                    info.ServerVersion, info.PgVectorVersion);
                return BootstrapResult.Ready(info.ServerVersion, info.PgVectorVersion);
            }
            catch (PostgresUnavailableException ex)
            {
                _logger.LogWarning(
                    "PostgreSQL bootstrap attempt failed. Reason={ReasonCode} PromptAttempts={PromptAttempts}",
                    ex.ReasonCode, promptAttempts);

                bool authenticationProblem = ex.ReasonCode is
                    BootstrapReasonCode.AuthenticationFailed or BootstrapReasonCode.PasswordRequired;
                if (!authenticationProblem)
                    return BootstrapResult.Unavailable(ex.ReasonCode);

                if (promptAttempts >= _options.MaxPasswordPromptAttempts)
                {
                    return stored is null && password is null
                        ? BootstrapResult.NeedsPassword(BootstrapReasonCode.PasswordRequired)
                        : BootstrapResult.NeedsPassword(BootstrapReasonCode.AuthenticationFailed);
                }

                promptAttempts++;
                string purpose =
                    $"{_options.PasswordSecretKey} for {_options.Username}@{_options.Host}:{_options.Port}/{_options.Database}";
                SecretValue? prompted = await _secretPrompt
                    .RequestPasswordAsync(purpose, cancellationToken).ConfigureAwait(false);
                if (prompted is null)
                {
                    _logger.LogInformation("PostgreSQL password prompt was declined by the user.");
                    return BootstrapResult.NeedsPassword(
                        password is null
                            ? BootstrapReasonCode.PasswordRequired
                            : BootstrapReasonCode.AuthenticationFailed);
                }

                password = prompted;
                passwordFromStore = false;
            }
        }
    }
}
