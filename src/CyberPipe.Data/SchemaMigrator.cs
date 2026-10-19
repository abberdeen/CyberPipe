using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CyberPipe.Data;

/// <summary>Отчёт применения миграций (TASK-011).</summary>
public sealed record MigrationReport
{
    /// <summary>Версии, применённые этим вызовом, по возрастанию.</summary>
    public required IReadOnlyList<int> AppliedVersions { get; init; }

    /// <summary>Версии, уже применённые ранее и пропущенные этим вызовом.</summary>
    public required IReadOnlyList<int> SkippedVersions { get; init; }

    /// <summary>Текущая версия схемы после вызова; 0, если миграций не применено.</summary>
    public required int CurrentVersion { get; init; }
}

/// <summary>Контрольная сумма применённой миграции не совпадает с файлом миграции.</summary>
public sealed class SchemaChecksumMismatchException : Exception
{
    /// <summary>Создаёт исключение для версии миграции.</summary>
    /// <param name="version">Версия миграции с несовпадающей контрольной суммой.</param>
    public SchemaChecksumMismatchException(int version)
        : base($"Schema migration {version.ToString(CultureInfo.InvariantCulture)} checksum mismatch: " +
               "the applied migration file was changed. Restore the original file or add a new migration.")
        => Version = version;

    /// <summary>Версия миграции с несовпадающей контрольной суммой.</summary>
    public int Version { get; }
}

/// <summary>Каталог миграций невалиден: пропуск нумерации, дубликат или отсутствующий откат.</summary>
public sealed class MigrationCatalogException : Exception
{
    /// <summary>Создаёт исключение каталога миграций.</summary>
    /// <param name="message">Описание проблемы без секретов.</param>
    public MigrationCatalogException(string message) : base(message)
    {
    }
}

/// <summary>Одна версионируемая миграция: вверх, необязательный откат и признак обратимости.</summary>
public sealed record MigrationDefinition
{
    /// <summary>Номер версии (NNNN из имени файла).</summary>
    public required int Version { get; init; }

    /// <summary>Слаг из имени файла без номера и расширения.</summary>
    public required string Slug { get; init; }

    /// <summary>SQL миграции вверх.</summary>
    public required string UpSql { get; init; }

    /// <summary>SQL отката; null, если отката нет.</summary>
    public string? DownSql { get; init; }

    /// <summary>Объявлена ли миграция обратимой (заголовок reversible в SQL или явное значение).</summary>
    public bool IsReversible { get; init; } = true;

    /// <summary>SHA-256 контрольная сумма нормализованного SQL вверх, hex в нижнем регистре.</summary>
    public string Checksum => MigrationChecksum.Compute(UpSql);
}

/// <summary>Контрольная сумма миграции: SHA-256 от SQL с нормализованными переводами строк.</summary>
public static class MigrationChecksum
{
    /// <summary>Вычисляет контрольную сумму SQL миграции.</summary>
    /// <param name="sql">SQL миграции вверх.</param>
    /// <returns>SHA-256 hex в нижнем регистре.</returns>
    public static string Compute(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        string normalized = sql.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>Источник миграций: встроенные ресурсы сборки или явный набор для тестов.</summary>
public interface IMigrationSource
{
    /// <summary>Возвращает все определения миграций в любом порядке; порядок проверяет каталог.</summary>
    /// <returns>Определения миграций.</returns>
    IReadOnlyList<MigrationDefinition> GetMigrations();
}

/// <summary>Источник миграций из явного набора определений (шов unit- и integration-тестов).</summary>
public sealed class StaticMigrationSource : IMigrationSource
{
    private readonly IReadOnlyList<MigrationDefinition> _migrations;

    /// <summary>Создаёт источник из явного набора.</summary>
    /// <param name="migrations">Определения миграций.</param>
    public StaticMigrationSource(IEnumerable<MigrationDefinition> migrations) =>
        _migrations = (migrations ?? throw new ArgumentNullException(nameof(migrations))).ToArray();

    /// <inheritdoc />
    public IReadOnlyList<MigrationDefinition> GetMigrations() => _migrations;
}

/// <summary>
/// Каталог миграций: разбор имён файлов, порядок версий, обнаружение пропусков нумерации,
/// дубликатов, потерянных откатов и обратимых миграций без пары отката.
/// </summary>
public static class MigrationCatalog
{
    private static readonly Regex FileNamePattern = new(
        @"^(?<version>\d{4})_(?<slug>[a-z0-9][a-z0-9_\-]*?)(?<down>\.down)?\.sql$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ReversibleHeaderPattern = new(
        @"--\s*reversible\s*:\s*(?<value>true|false)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Разбирает имя файла миграции NNNN_slug.sql или NNNN_slug.down.sql.</summary>
    /// <param name="fileName">Имя файла без каталога.</param>
    /// <param name="version">Номер версии при успешном разборе.</param>
    /// <param name="slug">Слаг при успешном разборе.</param>
    /// <param name="isDown">true для файла отката.</param>
    /// <returns>true, если имя соответствует соглашению.</returns>
    public static bool TryParseFileName(string fileName, out int version, out string slug, out bool isDown)
    {
        version = 0;
        slug = string.Empty;
        isDown = false;
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        Match match = FileNamePattern.Match(Path.GetFileName(fileName));
        if (!match.Success)
            return false;
        version = int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
        slug = match.Groups["slug"].Value;
        isDown = match.Groups["down"].Success;
        return true;
    }

    /// <summary>Читает заголовок обратимости из SQL; отсутствие заголовка означает обратимую миграцию.</summary>
    /// <param name="upSql">SQL миграции вверх.</param>
    /// <returns>true, если миграция обратима.</returns>
    public static bool ParseIsReversible(string upSql)
    {
        ArgumentNullException.ThrowIfNull(upSql);
        Match match = ReversibleHeaderPattern.Match(upSql);
        return !match.Success || bool.Parse(match.Groups["value"].Value);
    }

    /// <summary>Собирает определения из текстов файлов вверх/вниз, сгруппированных по имени файла.</summary>
    /// <param name="files">Отображение имени файла в его текст.</param>
    /// <returns>Проверенный упорядоченный каталог.</returns>
    public static IReadOnlyList<MigrationDefinition> FromFiles(IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var ups = new Dictionary<int, (string Slug, string Sql)>();
        var downs = new Dictionary<int, string>();
        foreach ((string name, string sql) in files)
        {
            if (!TryParseFileName(name, out int version, out string slug, out bool isDown))
                throw new MigrationCatalogException($"Invalid migration file name '{name}': expected NNNN_<slug>.sql or NNNN_<slug>.down.sql.");
            if (isDown)
            {
                if (!downs.TryAdd(version, sql))
                    throw new MigrationCatalogException($"Duplicate down migration for version {version}.");
            }
            else if (!ups.TryAdd(version, (slug, sql)))
            {
                throw new MigrationCatalogException($"Duplicate migration version {version}.");
            }
        }

        foreach (int version in downs.Keys)
        {
            if (!ups.ContainsKey(version))
                throw new MigrationCatalogException($"Down migration {version} has no matching up migration.");
        }

        var definitions = ups
            .Select(pair => new MigrationDefinition
            {
                Version = pair.Key,
                Slug = pair.Value.Slug,
                UpSql = pair.Value.Sql,
                DownSql = downs.TryGetValue(pair.Key, out string? down) ? down : null,
                IsReversible = ParseIsReversible(pair.Value.Sql),
            })
            .ToArray();
        return Validate(definitions);
    }

    /// <summary>Проверяет и сортирует определения: строгая последовательность с 1 без пропусков и дублей.</summary>
    /// <param name="definitions">Определения в любом порядке.</param>
    /// <returns>Определения по возрастанию версии.</returns>
    /// <exception cref="MigrationCatalogException">Пропуск, дубликат или обратимая миграция без отката.</exception>
    public static IReadOnlyList<MigrationDefinition> Validate(IEnumerable<MigrationDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        MigrationDefinition[] ordered = definitions.OrderBy(d => d.Version).ToArray();
        if (ordered.Length == 0)
            throw new MigrationCatalogException("Migration catalog is empty.");
        int duplicate = ordered.GroupBy(d => d.Version).FirstOrDefault(g => g.Count() > 1)?.Key ?? 0;
        if (duplicate != 0)
            throw new MigrationCatalogException($"Duplicate migration version {duplicate}.");
        for (int i = 0; i < ordered.Length; i++)
        {
            int expected = i + 1;
            MigrationDefinition current = ordered[i];
            if (current.Version != expected)
            {
                throw new MigrationCatalogException(
                    $"Migration numbering gap: expected version {expected}, found {current.Version}. Versions must be sequential from 1 without gaps.");
            }

            if (string.IsNullOrWhiteSpace(current.UpSql))
                throw new MigrationCatalogException($"Migration {current.Version} has empty up SQL.");
            if (current.IsReversible && string.IsNullOrWhiteSpace(current.DownSql))
                throw new MigrationCatalogException($"Migration {current.Version} is marked reversible but has no down migration.");
        }

        return ordered;
    }
}

/// <summary>Источник миграций из встроенных ресурсов CyberPipe.Data (Migrations/*.sql).</summary>
public sealed class EmbeddedMigrationSource : IMigrationSource
{
    /// <inheritdoc />
    public IReadOnlyList<MigrationDefinition> GetMigrations()
    {
        Assembly assembly = typeof(EmbeddedMigrationSource).Assembly;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            int marker = resourceName.IndexOf(".Migrations.", StringComparison.Ordinal);
            if (marker < 0 || !resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                continue;
            string fileName = resourceName[(marker + ".Migrations.".Length)..];
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new MigrationCatalogException($"Embedded migration resource '{resourceName}' is unavailable.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            files[fileName] = reader.ReadToEnd();
        }

        return MigrationCatalog.FromFiles(files);
    }
}

/// <summary>Механизм миграций схемы PostgreSQL (TASK-011).</summary>
public interface ISchemaMigrator
{
    /// <summary>
    /// Применяет все неприменённые миграции по порядку, каждую в отдельной транзакции, под
    /// advisory lock PostgreSQL. Повторный вызов ничего не меняет. Применим при старте Service
    /// через IPostgresConnectionFactory после bootstrap TASK-010.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отчёт: применённые, пропущенные и текущая версия.</returns>
    /// <exception cref="SchemaChecksumMismatchException">Файл уже применённой миграции изменён.</exception>
    Task<MigrationReport> MigrateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Режим ValidateOnly: только читает состояние и возвращает расхождения (контрольные суммы,
    /// отсутствующие файлы, ожидающие миграции), ничего в базе не меняя.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Расхождения; пуст при соответствии базы каталогу миграций.</returns>
    Task<IReadOnlyList<string>> ValidateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Откатывает последнюю применённую обратимую миграцию в транзакции и удаляет её запись истории.
    /// Строки таблиц более ранних миграций не затрагиваются.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отчёт после отката.</returns>
    Task<MigrationReport> RollbackLastAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Мигратор схемы: транзакция на миграцию, проверка контрольных сумм уже применённых миграций,
/// блокировка конкурентного запуска advisory lock-ом PostgreSQL, ValidateOnly через ValidateAsync.
/// </summary>
public sealed class SchemaMigrator : ISchemaMigrator
{
    /// <summary>Ключ advisory lock мигратора; один на всю базу приложения.</summary>
    public const long AdvisoryLockKey = 2026021101L;

    private const string HistoryBootstrapSql = """
        CREATE SCHEMA IF NOT EXISTS app;
        CREATE TABLE IF NOT EXISTS app.schema_migrations (
            version integer PRIMARY KEY,
            applied_at timestamptz NOT NULL DEFAULT now(),
            checksum text NOT NULL
        );
        """;

    private readonly IPostgresConnectionFactory _connectionFactory;
    private readonly IMigrationSource _source;
    private readonly ILogger<SchemaMigrator> _logger;

    /// <summary>Создаёт мигратор со встроенными миграциями сборки.</summary>
    /// <param name="connectionFactory">Фабрика открытых соединений TASK-010.</param>
    /// <param name="logger">Логгер; контрольные суммы и SQL в логи не пишутся целиком.</param>
    public SchemaMigrator(IPostgresConnectionFactory connectionFactory, ILogger<SchemaMigrator>? logger = null)
        : this(connectionFactory, new EmbeddedMigrationSource(), logger)
    {
    }

    /// <summary>Создаёт мигратор с явным источником миграций (шов тестов).</summary>
    /// <param name="connectionFactory">Фабрика открытых соединений TASK-010.</param>
    /// <param name="source">Источник миграций.</param>
    /// <param name="logger">Логгер.</param>
    public SchemaMigrator(
        IPostgresConnectionFactory connectionFactory,
        IMigrationSource source,
        ILogger<SchemaMigrator>? logger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _logger = logger ?? NullLogger<SchemaMigrator>.Instance;
    }

    /// <inheritdoc />
    public async Task<MigrationReport> MigrateAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MigrationDefinition> catalog = MigrationCatalog.Validate(_source.GetMigrations());
        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await AcquireLockAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(connection, null, HistoryBootstrapSql, cancellationToken).ConfigureAwait(false);
            Dictionary<int, string> applied = await ReadAppliedAsync(connection, null, cancellationToken).ConfigureAwait(false);
            ThrowOnChecksumMismatch(catalog, applied);

            var appliedNow = new List<int>();
            var skipped = new List<int>();
            foreach (MigrationDefinition migration in catalog)
            {
                if (applied.ContainsKey(migration.Version))
                {
                    skipped.Add(migration.Version);
                    continue;
                }

                await using NpgsqlTransaction transaction = await connection
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, migration.UpSql, cancellationToken).ConfigureAwait(false);
                await using (var insert = new NpgsqlCommand(
                    "INSERT INTO app.schema_migrations (version, checksum) VALUES (@version, @checksum)",
                    connection, transaction))
                {
                    insert.Parameters.AddWithValue("version", migration.Version);
                    insert.Parameters.AddWithValue("checksum", migration.Checksum);
                    await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                appliedNow.Add(migration.Version);
                _logger.LogInformation("Applied schema migration {Version}.", migration.Version);
            }

            int current = catalog.Where(m => applied.ContainsKey(m.Version) || appliedNow.Contains(m.Version))
                .Select(m => m.Version).DefaultIfEmpty(0).Max();
            return new MigrationReport { AppliedVersions = appliedNow, SkippedVersions = skipped, CurrentVersion = current };
        }
        finally
        {
            await ReleaseLockAsync(connection).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ValidateAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MigrationDefinition> catalog = MigrationCatalog.Validate(_source.GetMigrations());
        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        var discrepancies = new List<string>();
        bool historyExists = await HistoryExistsAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!historyExists)
        {
            discrepancies.Add("Table app.schema_migrations is missing: no migrations have been applied.");
            discrepancies.AddRange(catalog.Select(m => $"Migration {m.Version} is pending."));
            return discrepancies;
        }

        Dictionary<int, string> applied = await ReadAppliedAsync(connection, null, cancellationToken).ConfigureAwait(false);
        var byVersion = catalog.ToDictionary(m => m.Version);
        foreach ((int version, string checksum) in applied.OrderBy(p => p.Key))
        {
            if (!byVersion.TryGetValue(version, out MigrationDefinition? migration))
                discrepancies.Add($"Applied migration {version} has no migration file.");
            else if (!string.Equals(checksum, migration.Checksum, StringComparison.Ordinal))
                discrepancies.Add($"Migration {version} checksum mismatch: applied migration file was changed.");
        }

        discrepancies.AddRange(catalog.Where(m => !applied.ContainsKey(m.Version)).Select(m => $"Migration {m.Version} is pending."));
        return discrepancies;
    }

    /// <inheritdoc />
    public async Task<MigrationReport> RollbackLastAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MigrationDefinition> catalog = MigrationCatalog.Validate(_source.GetMigrations());
        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await AcquireLockAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await HistoryExistsAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                return new MigrationReport { AppliedVersions = [], SkippedVersions = [], CurrentVersion = 0 };
            }

            Dictionary<int, string> applied = await ReadAppliedAsync(connection, null, cancellationToken).ConfigureAwait(false);
            if (applied.Count == 0)
            {
                return new MigrationReport { AppliedVersions = [], SkippedVersions = [], CurrentVersion = 0 };
            }

            ThrowOnChecksumMismatch(catalog, applied);
            int lastVersion = applied.Keys.Max();
            MigrationDefinition migration = catalog.FirstOrDefault(m => m.Version == lastVersion)
                ?? throw new MigrationCatalogException($"Applied migration {lastVersion} has no migration file to roll back.");
            if (!migration.IsReversible || string.IsNullOrWhiteSpace(migration.DownSql))
                throw new InvalidOperationException($"Migration {lastVersion} is not reversible and cannot be rolled back.");

            await using NpgsqlTransaction transaction = await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, migration.DownSql, cancellationToken).ConfigureAwait(false);
            await using (var delete = new NpgsqlCommand(
                "DELETE FROM app.schema_migrations WHERE version = @version", connection, transaction))
            {
                delete.Parameters.AddWithValue("version", lastVersion);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            int[] remaining = applied.Keys.Where(v => v != lastVersion).OrderBy(v => v).ToArray();
            _logger.LogInformation("Rolled back schema migration {Version}.", lastVersion);
            return new MigrationReport
            {
                AppliedVersions = [],
                SkippedVersions = remaining,
                CurrentVersion = remaining.DefaultIfEmpty(0).Max(),
            };
        }
        finally
        {
            await ReleaseLockAsync(connection).ConfigureAwait(false);
        }
    }

    private static void ThrowOnChecksumMismatch(
        IReadOnlyList<MigrationDefinition> catalog,
        IReadOnlyDictionary<int, string> applied)
    {
        var byVersion = catalog.ToDictionary(m => m.Version);
        foreach ((int version, string checksum) in applied.OrderBy(p => p.Key))
        {
            if (byVersion.TryGetValue(version, out MigrationDefinition? migration)
                && !string.Equals(checksum, migration.Checksum, StringComparison.Ordinal))
            {
                throw new SchemaChecksumMismatchException(version);
            }
        }
    }

    private static async Task AcquireLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReleaseLockAsync(NpgsqlConnection connection)
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            command.Parameters.AddWithValue("key", AdvisoryLockKey);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception) when (connection.State != System.Data.ConnectionState.Open)
        {
            // Соединение уже закрыто: lock освобождён сервером вместе с сессией.
        }
    }

    private static async Task<bool> HistoryExistsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass('app.schema_migrations') IS NOT NULL", connection);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is true;
    }

    private static async Task<Dictionary<int, string>> ReadAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var applied = new Dictionary<int, string>();
        await using var command = new NpgsqlCommand(
            "SELECT version, checksum FROM app.schema_migrations ORDER BY version", connection, transaction);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            applied[reader.GetInt32(0)] = reader.GetString(1);
        return applied;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
