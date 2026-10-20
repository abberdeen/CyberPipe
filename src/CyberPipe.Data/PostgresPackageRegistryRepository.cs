using System.Text.Json;
using CyberPipe.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace CyberPipe.Data;

/// <summary>PostgreSQL реестр Netrunner-пакетов (TASK-042): регистрации переживают рестарт.</summary>
public sealed class PostgresPackageRegistryRepository : IPackageRegistryRepository
{
    private readonly IPostgresConnectionFactory _connectionFactory;

    public PostgresPackageRegistryRepository(IPostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<PackageRegistrationRecord?> GetPackageAsync(string slug, string version, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connectionFactory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT slug, version, manifest_hash, package_dir, manifest_json, registered_at FROM app.netrunner_packages WHERE slug = @slug AND version = @version",
            connection);
        command.Parameters.AddWithValue("slug", slug);
        command.Parameters.AddWithValue("version", version);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new PackageRegistrationRecord
        {
            Slug = reader.GetString(0),
            Version = reader.GetString(1),
            ManifestHash = reader.GetString(2),
            PackageDir = reader.GetString(3),
            Manifest = JsonSerializer.Deserialize<NetrunnerManifestDto>(reader.GetString(4))
                ?? throw new InvalidOperationException("Stored package manifest is corrupt."),
            RegisteredAtUtc = reader.GetFieldValue<DateTimeOffset>(5),
        };
    }

    public async Task<PackageRegistrationRecord> RegisterPackageAsync(NetrunnerManifestDto manifest, string packageDir, string manifestHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestHash);

        PackageRegistrationRecord? existing = await GetPackageAsync(manifest.Slug, manifest.Version, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (string.Equals(existing.ManifestHash, manifestHash, StringComparison.OrdinalIgnoreCase))
                return existing;
            throw new PackageRegistrationException(PackageErrorCodes.VersionAlreadyRegistered,
                $"Package '{manifest.Slug}' version '{manifest.Version}' is already registered with a different manifest hash.");
        }

        var record = new PackageRegistrationRecord
        {
            Slug = manifest.Slug,
            Version = manifest.Version,
            ManifestHash = manifestHash,
            PackageDir = packageDir,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            Manifest = manifest,
        };

        await using NpgsqlConnection connection = await _connectionFactory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            INSERT INTO app.netrunner_packages (slug, version, manifest_hash, package_dir, manifest_json, registered_at)
            VALUES (@slug, @version, @hash, @dir, @manifest, @at)
            """, connection);
        command.Parameters.AddWithValue("slug", record.Slug);
        command.Parameters.AddWithValue("version", record.Version);
        command.Parameters.AddWithValue("hash", record.ManifestHash);
        command.Parameters.AddWithValue("dir", record.PackageDir);
        command.Parameters.Add("manifest", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(manifest);
        command.Parameters.AddWithValue("at", record.RegisteredAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }
}
