using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Chains;

/// <summary>
/// Потокобезопасная in-memory реализация репозитория цепочек (TASK-040).
/// Используется в тестах и при работе без PostgreSQL.
/// </summary>
public sealed class InMemoryChainDefinitionRepository : IChainDefinitionRepository
{
    private readonly ConcurrentDictionary<ChainId, ChainDefinitionDto> _chains = new();

    /// <inheritdoc />
    public Task<ChainDefinitionDto?> GetByIdAsync(ChainId id, CancellationToken cancellationToken = default)
    {
        _chains.TryGetValue(id, out ChainDefinitionDto? chain);
        return Task.FromResult(chain);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChainDefinitionDto>> ListEnabledAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChainDefinitionDto> enabled = _chains.Values
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(enabled);
    }

    /// <inheritdoc />
    public Task<ChainDefinitionDto> UpsertAsync(ChainDefinitionDto definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset created = definition.CreatedAtUtc == default ? now : definition.CreatedAtUtc;

        ChainDefinitionDto saved = _chains.AddOrUpdate(
            definition.Id,
            _ => definition with
            {
                Version = definition.Version <= 0 ? 1 : definition.Version,
                CreatedAtUtc = created,
                UpdatedAtUtc = now,
            },
            (_, existing) =>
            {
                int nextVersion = existing.IsEnabled ? existing.Version + 1 : existing.Version;
                return definition with
                {
                    Version = nextVersion,
                    CreatedAtUtc = existing.CreatedAtUtc,
                    UpdatedAtUtc = now,
                };
            });

        return Task.FromResult(saved);
    }

    /// <inheritdoc />
    public Task<bool> SetEnabledAsync(ChainId id, bool isEnabled, CancellationToken cancellationToken = default)
    {
        if (!_chains.TryGetValue(id, out ChainDefinitionDto? existing))
        {
            return Task.FromResult(false);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        _chains[id] = existing with
        {
            IsEnabled = isEnabled,
            UpdatedAtUtc = now,
        };

        return Task.FromResult(true);
    }
}
