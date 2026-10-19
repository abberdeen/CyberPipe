using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Hitl;

/// <summary>
/// Реализация порта IHitlApprovalGate (TASK-044, TASK-046).
/// Создаёт запрос подтверждения через IHitlApprovalService и поддерживает ожидание решения.
/// </summary>
public sealed class HitlApprovalGate : IHitlApprovalGate
{
    private readonly IHitlApprovalService _approvalService;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<HitlApprovalResult>> _waiters = new();

    /// <summary>
    /// Инициализирует HITL-гейт.
    /// </summary>
    public HitlApprovalGate(IHitlApprovalService approvalService)
    {
        _approvalService = approvalService ?? throw new ArgumentNullException(nameof(approvalService));
        if (approvalService is HitlApprovalService concrete)
        {
            concrete.DecisionResolved += NotifyDecision;
        }
    }

    /// <inheritdoc />
    public async Task<HitlApprovalResult> RequestApprovalAsync(
        HitlApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tcs = new TaskCompletionSource<HitlApprovalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[request.ApprovalId] = tcs;

        // Создаём запись в репозитории и рассылаем карточки в каналы
        await _approvalService.CreateAsync(request, cancellationToken).ConfigureAwait(false);

        using CancellationTokenRegistration reg = cancellationToken.Register(() =>
        {
            tcs.TrySetCanceled(cancellationToken);
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Уведомляет ожидающий гейт о принятом решении (для синхронных сценариев выполнения цепочки).
    /// </summary>
    public void NotifyDecision(HitlApprovalResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (_waiters.TryRemove(result.ApprovalId, out TaskCompletionSource<HitlApprovalResult>? tcs))
        {
            tcs.TrySetResult(result);
        }
    }
}
