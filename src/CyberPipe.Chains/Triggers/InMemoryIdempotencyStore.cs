using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Triggers;

/// <summary>
/// Потокобезопасная in-memory реализация хранилища ключей идемпотентности (TASK-041).
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new();

    /// <inheritdoc />
    public Task<bool> TryRegisterAsync(string key, TimeSpan window, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.Add(window);

        // Clean expired entry if present
        if (_entries.TryGetValue(key, out DateTimeOffset existingExpiresAt))
        {
            if (existingExpiresAt > now)
            {
                // Key is still valid/active: this is a duplicate
                return Task.FromResult(false);
            }

            // Key expired: remove and allow new registration
            _entries.TryRemove(key, out _);
        }

        bool added = _entries.TryAdd(key, expiresAt);
        return Task.FromResult(added);
    }
}
