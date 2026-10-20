using System.Collections.Concurrent;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Router;

/// <summary>
/// Губернатор токен-бюджета: централизованное ограничение расходов, лимитов токенов и ходов (TASK-033).
/// </summary>
public sealed class TokenBudgetGovernor : ITokenBudgetGovernor
{
    private readonly IBudgetStore _store;
    private readonly IModelPriceProvider? _priceProvider;
    private readonly CyberPipeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TokenBudgetGovernor>? _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, (int CloudTurns, int LocalTurns)> _requestTurns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, decimal> _pendingReservations = new(StringComparer.Ordinal);

    private DateOnly _currentDate;
    private decimal _settledCostTodayUsd;
    private decimal _reservedCostTodayUsd;
    private bool _initialized;

    public TokenBudgetGovernor(
        IBudgetStore store,
        CyberPipeOptions? options = null,
        IModelPriceProvider? priceProvider = null,
        TimeProvider? timeProvider = null,
        ILogger<TokenBudgetGovernor>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? new CyberPipeOptions();
        _priceProvider = priceProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<BudgetDecision> TryAuthorizeAsync(
        BudgetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        BudgetLimitsDto limits = GetEffectiveLimits();
        DateOnly today = GetCurrentDate();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureDateRolloverAsync(today, cancellationToken).ConfigureAwait(false);

            (int cloudTurns, int localTurns) = request.RequestId != null && _requestTurns.TryGetValue(request.RequestId, out var t)
                ? t
                : (0, 0);

            // 1. Check Turn limits
            if (request.HostingType == ModelHostingType.RemoteCloud)
            {
                if (cloudTurns >= limits.MaxCloudTurns)
                {
                    _logger?.LogWarning(
                        "Cloud turn limit exceeded ({Current}/{Max}) for request '{RequestId}'",
                        cloudTurns,
                        limits.MaxCloudTurns,
                        request.RequestId);

                    return CreateDenial(
                        BudgetDenialReason.TurnLimitExceeded,
                        $"Cloud turn limit ({limits.MaxCloudTurns}) exceeded for request '{request.RequestId}'.",
                        limits,
                        today,
                        cloudTurns,
                        localTurns);
                }
            }
            else
            {
                if (localTurns >= limits.MaxLocalTurns)
                {
                    _logger?.LogWarning(
                        "Local turn limit exceeded ({Current}/{Max}) for request '{RequestId}'",
                        localTurns,
                        limits.MaxLocalTurns,
                        request.RequestId);

                    return CreateDenial(
                        BudgetDenialReason.TurnLimitExceeded,
                        $"Local turn limit ({limits.MaxLocalTurns}) exceeded for request '{request.RequestId}'.",
                        limits,
                        today,
                        cloudTurns,
                        localTurns);
                }
            }

            // 2. Check Token limits per call
            int totalEstimatedTokens = request.EstimatedInputTokens + request.EstimatedOutputTokens;
            int tokenCeiling = request.HostingType == ModelHostingType.RemoteCloud
                ? limits.SoftTokenLimit
                : limits.HardTokenLimit;

            if (totalEstimatedTokens > tokenCeiling)
            {
                _logger?.LogWarning(
                    "Token limit exceeded for model '{ModelId}' ({Tokens} > {Ceiling})",
                    request.ModelId,
                    totalEstimatedTokens,
                    tokenCeiling);

                return CreateDenial(
                    BudgetDenialReason.TokenLimitExceeded,
                    $"Estimated tokens ({totalEstimatedTokens}) exceed limit ({tokenCeiling}) for {request.HostingType} call.",
                    limits,
                    today,
                    cloudTurns,
                    localTurns);
            }

            // 3. Check Financial cost (daily budget cap)
            decimal estimatedCostUsd = 0m;
            if (request.HostingType == ModelHostingType.RemoteCloud)
            {
                var pricing = _priceProvider?.GetModelPricing(request.ModelId);
                if (pricing == null)
                {
                    _logger?.LogWarning("Unknown pricing for cloud model '{ModelId}'. Authorization rejected.", request.ModelId);
                    return CreateDenial(
                        BudgetDenialReason.UnknownModelPrice,
                        $"Pricing for cloud model '{request.ModelId}' is not configured.",
                        limits,
                        today,
                        cloudTurns,
                        localTurns);
                }

                estimatedCostUsd =
                    (request.EstimatedInputTokens / 1_000_000m * pricing.Value.InputPricePer1mUsd) +
                    (request.EstimatedOutputTokens / 1_000_000m * pricing.Value.OutputPricePer1mUsd);

                decimal projectedTotal = _settledCostTodayUsd + _reservedCostTodayUsd + estimatedCostUsd;
                if (projectedTotal > limits.DailyCapUsd)
                {
                    _logger?.LogWarning(
                        "Daily budget cap exceeded. Projected: ${Projected:F4}, Cap: ${Cap:F2}",
                        projectedTotal,
                        limits.DailyCapUsd);

                    return CreateDenial(
                        BudgetDenialReason.DailyBudgetExceeded,
                        $"Daily budget cap (${limits.DailyCapUsd:F2}) exceeded: spent ${_settledCostTodayUsd:F4} + reserved ${_reservedCostTodayUsd:F4} + call estimate ${estimatedCostUsd:F4}.",
                        limits,
                        today,
                        cloudTurns,
                        localTurns);
                }

                // Reserve estimated cost
                _reservedCostTodayUsd += estimatedCostUsd;
                if (request.RequestId != null)
                {
                    _pendingReservations.AddOrUpdate(
                        request.RequestId,
                        estimatedCostUsd,
                        (_, existing) => existing + estimatedCostUsd);
                }
            }

            // Record turn increment
            int newCloudTurns = request.HostingType == ModelHostingType.RemoteCloud ? cloudTurns + 1 : cloudTurns;
            int newLocalTurns = request.HostingType == ModelHostingType.Local ? localTurns + 1 : localTurns;
            if (request.RequestId != null)
            {
                _requestTurns[request.RequestId] = (newCloudTurns, newLocalTurns);
            }

            BudgetStatusDto status = BuildStatusDto(limits, today, newCloudTurns, newLocalTurns);

            return new BudgetDecision
            {
                IsAllowed = true,
                DenialReason = null,
                Message = "Authorization granted.",
                Status = status
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    // Вызывающие (Tier23) передают CostUsd=0: фактическую стоимость считаем по каталогу цен,
    // иначе дневной кап никогда не расходуется и бюджетная защита не работает.
    private decimal EffectiveCostUsd(TokenUsageDto usage)
    {
        if (usage.CostUsd != 0m || usage.HostingType != ModelHostingType.RemoteCloud)
            return usage.CostUsd;
        var pricing = _priceProvider?.GetModelPricing(usage.ModelId);
        if (pricing is null)
            return usage.CostUsd;
        return usage.InputTokens / 1_000_000m * pricing.Value.InputPricePer1mUsd
             + usage.OutputTokens / 1_000_000m * pricing.Value.OutputPricePer1mUsd;
    }

    public async Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usage);

        DateOnly today = GetCurrentDate();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureDateRolloverAsync(today, cancellationToken).ConfigureAwait(false);

            if (usage.RequestId != null && _pendingReservations.TryRemove(usage.RequestId, out decimal reserved))
            {
                _reservedCostTodayUsd = Math.Max(0m, _reservedCostTodayUsd - reserved);
            }

            _settledCostTodayUsd += EffectiveCostUsd(usage);
        }
        finally
        {
            _lock.Release();
        }

        TokenUsageDto effective = usage.CostUsd == 0m && EffectiveCostUsd(usage) != 0m
            ? usage with { CostUsd = EffectiveCostUsd(usage) }
            : usage;
        await _store.RecordUsageAsync(effective, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BudgetStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        BudgetLimitsDto limits = GetEffectiveLimits();
        DateOnly today = GetCurrentDate();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureDateRolloverAsync(today, cancellationToken).ConfigureAwait(false);
            int tokensToday = await _store.GetTokensUsedTodayAsync(today, cancellationToken).ConfigureAwait(false);

            decimal totalSpentAndReserved = _settledCostTodayUsd + _reservedCostTodayUsd;
            decimal remaining = Math.Max(0m, limits.DailyCapUsd - totalSpentAndReserved);

            return new BudgetStatusDto
            {
                Limits = limits,
                SpentTodayUsd = _settledCostTodayUsd,
                RemainingTodayUsd = remaining,
                TokensUsedToday = tokensToday,
                CloudTurnsUsed = 0,
                LocalTurnsUsed = 0,
                BudgetDate = today
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    private DateOnly GetCurrentDate() =>
        DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    private async Task EnsureDateRolloverAsync(DateOnly today, CancellationToken ct)
    {
        if (!_initialized || today != _currentDate)
        {
            _currentDate = today;
            _settledCostTodayUsd = await _store.GetSpentTodayUsdAsync(today, ct).ConfigureAwait(false);
            _reservedCostTodayUsd = 0m;
            _pendingReservations.Clear();
            _requestTurns.Clear();
            _initialized = true;

            _logger?.LogInformation("Budget governor day initialized for {Date}. Settled spent: ${Spent:F4}", today, _settledCostTodayUsd);
        }
    }

    private BudgetLimitsDto GetEffectiveLimits()
    {
        BudgetOptions opts = _options.Budget;
        return new BudgetLimitsDto
        {
            DailyCapUsd = opts.DailyCapUsd,
            SoftTokenLimit = opts.SoftTokenLimit,
            HardTokenLimit = opts.HardTokenLimit,
            MaxCloudTurns = opts.MaxCloudTurns,
            MaxLocalTurns = opts.MaxLocalTurns
        };
    }

    private BudgetStatusDto BuildStatusDto(BudgetLimitsDto limits, DateOnly date, int cloudTurns, int localTurns)
    {
        decimal totalSpentAndReserved = _settledCostTodayUsd + _reservedCostTodayUsd;
        decimal remaining = Math.Max(0m, limits.DailyCapUsd - totalSpentAndReserved);

        return new BudgetStatusDto
        {
            Limits = limits,
            SpentTodayUsd = _settledCostTodayUsd,
            RemainingTodayUsd = remaining,
            TokensUsedToday = 0,
            CloudTurnsUsed = cloudTurns,
            LocalTurnsUsed = localTurns,
            BudgetDate = date
        };
    }

    private BudgetDecision CreateDenial(
        BudgetDenialReason reason,
        string message,
        BudgetLimitsDto limits,
        DateOnly date,
        int cloudTurns,
        int localTurns)
    {
        return new BudgetDecision
        {
            IsAllowed = false,
            DenialReason = reason,
            Message = message,
            Status = BuildStatusDto(limits, date, cloudTurns, localTurns)
        };
    }
}
