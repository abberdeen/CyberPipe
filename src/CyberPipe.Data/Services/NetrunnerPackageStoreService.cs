using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>
/// Реестр Netrunner-пакетов (таблица netrunner_packages) на EF Core:
/// регистрации переживают рестарт. Все чтения — LINQ с AsNoTracking,
/// сырых SQL-строк нет.
/// </summary>
public sealed class NetrunnerPackageStoreService : INetrunnerPackageStoreService
{
    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositoryOptions _options;
    private readonly ILogger _logger;
    private readonly UnitOfWorkMetrics _metrics;

    /// <summary>Создаёт реестр пакетов поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public NetrunnerPackageStoreService(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger<NetrunnerPackageStoreService>? logger = null,
        UnitOfWorkMetrics? metrics = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _options = options ?? new RepositoryOptions();
        IReadOnlyList<string> errors = _options.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _metrics = metrics ?? new UnitOfWorkMetrics();
    }

    /// <summary>Возвращает регистрацию пакета; отсутствие — null.</summary>
    /// <param name="slug">Слаг пакета.</param>
    /// <param name="version">Версия пакета.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Регистрация или null.</returns>
    public Task<PackageRegistrationRecord?> GetPackageAsync(string slug, string version, CancellationToken cancellationToken = default) =>
        ExecuteAsync<PackageRegistrationRecord?>("packages.get", async (db, token) =>
        {
            PackageEntity? entity = await db.Packages
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Slug == slug && e.Version == version, token)
                .ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <summary>
    /// Регистрирует пакет. Идемпотентность: повтор с тем же хешем манифеста возвращает
    /// существующую регистрацию; тот же slug+version с другим хешем — исключение.
    /// </summary>
    /// <param name="manifest">Манифест пакета.</param>
    /// <param name="packageDir">Каталог пакета.</param>
    /// <param name="manifestHash">Хеш манифеста.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Регистрация пакета.</returns>
    /// <exception cref="PackageRegistrationException">Версия уже зарегистрирована с другим хешем.</exception>
    public Task<PackageRegistrationRecord> RegisterPackageAsync(NetrunnerManifestDto manifest, string packageDir, string manifestHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestHash);
        return ExecuteAsync("packages.register", async (db, token) =>
        {
            PackageEntity? existing = await db.Packages
                .FirstOrDefaultAsync(e => e.Slug == manifest.Slug && e.Version == manifest.Version, token)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                if (string.Equals(existing.ManifestHash, manifestHash, StringComparison.OrdinalIgnoreCase))
                    return Map(existing);
                throw new PackageRegistrationException(PackageErrorCodes.VersionAlreadyRegistered,
                    $"Package '{manifest.Slug}' version '{manifest.Version}' is already registered with a different manifest hash.");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var entity = new PackageEntity
            {
                Slug = manifest.Slug,
                Version = manifest.Version,
                ManifestHash = manifestHash,
                PackageDir = packageDir,
                ManifestJson = JsonSerializer.Serialize(manifest),
                RegisteredAt = now,
            };
            db.Packages.Add(entity);
            await db.SaveChangesAsync(token).ConfigureAwait(false);
            PackageRegistrationRecord record = Map(entity);
            return record;
        }, cancellationToken);
    }

    internal static PackageRegistrationRecord Map(PackageEntity entity) => new()
    {
        Slug = entity.Slug,
        Version = entity.Version,
        ManifestHash = entity.ManifestHash,
        PackageDir = entity.PackageDir,
        Manifest = JsonSerializer.Deserialize<NetrunnerManifestDto>(entity.ManifestJson)
            ?? throw new InvalidOperationException("Stored package manifest is corrupt."),
        RegisteredAtUtc = entity.RegisteredAt,
    };

    private Task<T> ExecuteAsync<T>(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken) =>
        EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, operation, body, cancellationToken);
}
