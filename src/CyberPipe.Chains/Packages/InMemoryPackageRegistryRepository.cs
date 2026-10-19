using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Packages;

/// <summary>
/// Потокобезопасная in-memory реализация реестра пакетов скриптов (TASK-042).
/// </summary>
public sealed class InMemoryPackageRegistryRepository : IPackageRegistryRepository
{
    private readonly ConcurrentDictionary<string, PackageRegistrationRecord> _packages = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<PackageRegistrationRecord?> GetPackageAsync(string slug, string version, CancellationToken cancellationToken = default)
    {
        string key = $"{slug}@{version}";
        _packages.TryGetValue(key, out PackageRegistrationRecord? record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<PackageRegistrationRecord> RegisterPackageAsync(
        NetrunnerManifestDto manifest,
        string packageDir,
        string manifestHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestHash);

        string key = $"{manifest.Slug}@{manifest.Version}";

        if (_packages.TryGetValue(key, out PackageRegistrationRecord? existing))
        {
            if (string.Equals(existing.ManifestHash, manifestHash, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(existing);
            }

            throw new PackageRegistrationException(
                PackageErrorCodes.VersionAlreadyRegistered,
                $"Package '{key}' is already registered with a different manifest hash (existing: {existing.ManifestHash}, new: {manifestHash}).");
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

        _packages[key] = record;
        return Task.FromResult(record);
    }
}
