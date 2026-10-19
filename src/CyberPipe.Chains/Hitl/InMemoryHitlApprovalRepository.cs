using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Hitl;

/// <summary>
/// Потокобезопасная in-memory реализация IHitlApprovalRepository (TASK-046).
/// Обеспечивает атомарный переход из Pending в Resolved/Expired для предотвращения гонок.
/// </summary>
public sealed class InMemoryHitlApprovalRepository : IHitlApprovalRepository
{
    private readonly ConcurrentDictionary<Guid, HitlApprovalRecord> _store = new();
    private readonly object _syncRoot = new();

    /// <inheritdoc />
    public Task<HitlApprovalRecord> CreateAsync(HitlApprovalRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        _store[record.ApprovalId] = record;
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<HitlApprovalRecord?> GetAsync(Guid approvalId, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(approvalId, out HitlApprovalRecord? record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<bool> TryResolveAsync(Guid approvalId, HitlApprovalResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (_syncRoot)
        {
            if (!_store.TryGetValue(approvalId, out HitlApprovalRecord? existing))
            {
                return Task.FromResult(false);
            }

            if (existing.Status != HitlStatus.Pending)
            {
                return Task.FromResult(false);
            }

            HitlStatus newStatus = result.Decision == HitlDecision.Expired
                ? HitlStatus.Expired
                : HitlStatus.Resolved;

            string payload = result.Decision == HitlDecision.Edited && !string.IsNullOrWhiteSpace(result.EditedPayloadJson)
                ? result.EditedPayloadJson
                : existing.PayloadJson;

            var updated = existing with
            {
                Status = newStatus,
                Resolution = result,
                PayloadJson = payload,
            };

            _store[approvalId] = updated;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HitlApprovalRecord>> ListPendingExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var expired = _store.Values
            .Where(r => r.Status == HitlStatus.Pending && r.ExpiresAtUtc <= nowUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<HitlApprovalRecord>>(expired);
    }
}
