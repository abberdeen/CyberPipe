using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Router;

/// <summary>
/// Production-каскад маршрутизации (TASK-030..032): Tier0 (regex) → Tier1 (semantic) → Tier2/3 (LLM).
/// Раньше каскад существовал только в E2E-харнессе; в боевой композиции IIntentRouter никто не реализовывал.
/// Matched и BudgetDenied завершают каскад; Escalate/Failed передают запрос следующему ярусу.
/// </summary>
public sealed class CompositeIntentRouter : IIntentRouter
{
    private readonly Tier0RegexRouter _tier0;
    private readonly Tier1SemanticRouter _tier1;
    private readonly Tier23LlmRouter _tier23;
    private readonly ISkillInvoker? _skillInvoker;
    private readonly ILogger<CompositeIntentRouter>? _logger;

    public CompositeIntentRouter(
        Tier0RegexRouter tier0,
        Tier1SemanticRouter tier1,
        Tier23LlmRouter tier23,
        ISkillInvoker? skillInvoker = null,
        ILogger<CompositeIntentRouter>? logger = null)
    {
        _tier0 = tier0 ?? throw new ArgumentNullException(nameof(tier0));
        _tier1 = tier1 ?? throw new ArgumentNullException(nameof(tier1));
        _tier23 = tier23 ?? throw new ArgumentNullException(nameof(tier23));
        _skillInvoker = skillInvoker;
        _logger = logger;
    }

    public async Task<RouteResult> RouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RouteResult tier0 = await _tier0.TryRouteAsync(request, cancellationToken).ConfigureAwait(false);
        if (tier0.Status is RouteStatus.Matched)
            return await SkillIntentDispatcher.DispatchAsync(_skillInvoker, tier0, cancellationToken).ConfigureAwait(false);
        if (tier0.Status is RouteStatus.BudgetDenied)
            return tier0;

        RouteResult tier1 = await _tier1.TryRouteAsync(request, cancellationToken).ConfigureAwait(false);
        if (tier1.Status is RouteStatus.Matched)
            return await SkillIntentDispatcher.DispatchAsync(_skillInvoker, tier1, cancellationToken).ConfigureAwait(false);
        if (tier1.Status is RouteStatus.BudgetDenied)
            return tier1;

        RouteResult tier23 = await _tier23.TryRouteAsync(request, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Route cascade finished at Tier2/3 with {Status} for channel {Channel}", tier23.Status, request.Channel);
        if (tier23.Status is RouteStatus.Matched)
            return await SkillIntentDispatcher.DispatchAsync(_skillInvoker, tier23, cancellationToken).ConfigureAwait(false);

        return tier23;
    }
}
