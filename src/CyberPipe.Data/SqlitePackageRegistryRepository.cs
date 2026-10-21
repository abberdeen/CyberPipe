using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>SQLite реестр Netrunner-пакетов (TASK-042) на EF Core: регистрации переживают рестарт.</summary>
public sealed class SqlitePackageRegistryRepository : RepositoryBase, IPackageRegistryRepository
{
    /// <summary>Создаёт реестр пакетов поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public SqlitePackageRegistryRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
        : base(dbFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<PackageRegistrationRecord?> GetPackageAsync(string slug, string version, CancellationToken cancellationToken = default) =>
        ExecuteAsync<PackageRegistrationRecord?>("packages.get", async (db, token) =>
        {
            PackageEntity? entity = await db.Packages
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Slug == slug && e.Version == version, token)
                .ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
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
            await SaveAsync(db, token).ConfigureAwait(false);
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
}
