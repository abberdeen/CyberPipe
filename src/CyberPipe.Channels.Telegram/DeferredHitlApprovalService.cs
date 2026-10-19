using CyberPipe.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace CyberPipe.Channels.Telegram;

/// <summary>
/// Отложенный <see cref="IHitlApprovalService" /> для разрыва цикла композиции (TASK-090):
/// <c>TelegramChannelService → IHitlApprovalService → IHitlCardSender → TelegramChannelService</c>.
/// Циклы через фабричные лямбды MS.DI не детектирует, поэтому реальный сервис
/// резолвится только при первом вызове, когда граф уже полностью построен.
/// Первый вызов вне построения графа потокобезопасен (<see cref="Lazy{T}" />).
/// </summary>
internal sealed class DeferredHitlApprovalService(IServiceProvider services) : IHitlApprovalService
{
    private readonly Lazy<IHitlApprovalService> _real = new(services.GetRequiredService<IHitlApprovalService>);

    /// <inheritdoc />
    public Task<HitlApprovalRecord> CreateAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default) =>
        _real.Value.CreateAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<HitlApprovalResult> ApproveAsync(Guid approvalId, string ownerId, ChannelKind channel, CancellationToken cancellationToken = default) =>
        _real.Value.ApproveAsync(approvalId, ownerId, channel, cancellationToken);

    /// <inheritdoc />
    public Task<HitlApprovalResult> EditAsync(Guid approvalId, string ownerId, string editedPayloadJson, ChannelKind channel, CancellationToken cancellationToken = default) =>
        _real.Value.EditAsync(approvalId, ownerId, editedPayloadJson, channel, cancellationToken);

    /// <inheritdoc />
    public Task<HitlApprovalResult> RejectAsync(Guid approvalId, string ownerId, ChannelKind channel, string? reason = null, CancellationToken cancellationToken = default) =>
        _real.Value.RejectAsync(approvalId, ownerId, channel, reason, cancellationToken);

    /// <inheritdoc />
    public Task<int> ExpireOverdueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        _real.Value.ExpireOverdueAsync(nowUtc, cancellationToken);
}
