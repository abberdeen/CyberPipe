using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.Data.Sqlite;

namespace CyberPipe.Data;

/// <summary>SQLite реестр Netrunner-пакетов (TASK-042): регистрации переживают рестарт.</summary>
public sealed class SqlitePackageRegistryRepository : IPackageRegistryRepository
{
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public SqlitePackageRegistryRepository(IDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<PackageRegistrationRecord?> GetPackageAsync(string slug, string version, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqliteCommand(
            "SELECT slug, version, manifest_hash, package_dir, manifest_json, registered_at FROM netrunner_packages WHERE slug = @slug AND version = @version",
            connection);
        command.Parameters.AddWithValue("@slug", slug);
        command.Parameters.AddWithValue("@version", version);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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
            RegisteredAtUtc = ReadRequiredTime(reader, 5),
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

        await using SqliteConnection connection = await _connectionFactory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqliteCommand("""
            INSERT INTO netrunner_packages (slug, version, manifest_hash, package_dir, manifest_json, registered_at)
            VALUES (@slug, @version, @hash, @dir, @manifest, @at)
            """, connection);
        command.Parameters.AddWithValue("@slug", record.Slug);
        command.Parameters.AddWithValue("@version", record.Version);
        command.Parameters.AddWithValue("@hash", record.ManifestHash);
        command.Parameters.AddWithValue("@dir", record.PackageDir);
        command.Parameters.AddWithValue("@manifest", JsonSerializer.Serialize(manifest));
        command.Parameters.AddWithValue("@at", ToDbText(record.RegisteredAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    private static string ToDbText(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset ReadRequiredTime(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            throw new InvalidOperationException("Unexpected NULL timestamp.");
        object value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            string s when DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset parsed) => parsed,
            string s when DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AllowWhiteSpaces, out DateTime parsedDt) => new DateTimeOffset(DateTime.SpecifyKind(parsedDt, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException($"Unexpected timestamp value of type {value.GetType().Name}."),
        };
    }
}
