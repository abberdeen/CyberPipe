using System.Diagnostics;
using System.Text.RegularExpressions;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Router;

/// <summary>
/// Реализация Tier 0: сопоставление входящих запросов по регулярным выражениям (TASK-030).
/// Мгновенный, детерминированный ярус без внешних зависимостей (БД, сети, моделей).
/// </summary>
public sealed class Tier0RegexRouter : IRouterTier
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly IReadOnlyList<CompiledRule> _compiledRules;
    private readonly ILogger<Tier0RegexRouter>? _logger;

    private long _hitCount;
    private long _missCount;
    private long _timeoutCount;
    private double _lastMatchDurationMs;

    public Tier0RegexRouter(
        IRegexRuleProvider ruleProvider,
        CyberPipeOptions? options = null,
        ILogger<Tier0RegexRouter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ruleProvider);
        _logger = logger;

        int defaultTimeoutMs = options?.Router.RegexMatchTimeoutMs ?? 200;
        IReadOnlyList<RegexRuleDefinition> rules = ruleProvider.GetRules();

        var compiled = new List<CompiledRule>();
        foreach (RegexRuleDefinition def in rules)
        {
            try
            {
                var regexOptions = RegexOptions.CultureInvariant;
                if (def.IgnoreCase)
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                int timeoutMs = def.TimeoutMs > 0 ? def.TimeoutMs : defaultTimeoutMs;
                var regex = new Regex(def.Pattern, regexOptions, TimeSpan.FromMilliseconds(timeoutMs));

                compiled.Add(new CompiledRule(def, regex));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to compile regex rule '{RuleName}' with pattern '{Pattern}'.", def.Name, def.Pattern);
            }
        }

        // Sort rules: highest priority first, then deterministic by name
        _compiledRules = compiled
            .OrderByDescending(r => r.Definition.Priority)
            .ThenBy(r => r.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IntentTier Tier => IntentTier.Tier0;

    public long HitCount => Interlocked.Read(ref _hitCount);
    public long MissCount => Interlocked.Read(ref _missCount);
    public long TimeoutCount => Interlocked.Read(ref _timeoutCount);
    public double LastMatchDurationMs => Volatile.Read(ref _lastMatchDurationMs);

    public Task<RouteResult> TryRouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            Interlocked.Increment(ref _missCount);
            return Task.FromResult(new RouteResult
            {
                Status = RouteStatus.Escalate,
                Tier = IntentTier.Tier0,
                Intent = null,
                Confidence = 0.0,
                MatchedRuleName = null,
                Reason = "Empty or whitespace input."
            });
        }

        var sw = Stopwatch.StartNew();
        string normalized = WhitespaceRegex.Replace(request.Text.Trim(), " ");

        foreach (CompiledRule rule in _compiledRules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Match match = rule.Regex.Match(normalized);
                if (match.Success)
                {
                    sw.Stop();
                    Volatile.Write(ref _lastMatchDurationMs, sw.Elapsed.TotalMilliseconds);
                    Interlocked.Increment(ref _hitCount);

                    var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string groupName in rule.Regex.GetGroupNames())
                    {
                        if (groupName == "0" || int.TryParse(groupName, out _))
                        {
                            continue;
                        }

                        Group group = match.Groups[groupName];
                        if (group.Success)
                        {
                            parameters[groupName] = group.Value;
                        }
                    }

                    _logger?.LogDebug(
                        "Tier 0 matched rule '{RuleName}' with intent '{Intent}' in {Duration:F2}ms",
                        rule.Definition.Name,
                        rule.Definition.Intent,
                        sw.Elapsed.TotalMilliseconds);

                    return Task.FromResult(new RouteResult
                    {
                        Status = RouteStatus.Matched,
                        Tier = IntentTier.Tier0,
                        Intent = rule.Definition.Intent,
                        Parameters = parameters,
                        Confidence = 1.0,
                        MatchedRuleName = rule.Definition.Name,
                        Reason = null
                    });
                }
            }
            catch (RegexMatchTimeoutException)
            {
                Interlocked.Increment(ref _timeoutCount);
                _logger?.LogWarning(
                    "Regex rule '{RuleName}' timed out evaluating pattern '{Pattern}'. Skipping rule.",
                    rule.Definition.Name,
                    rule.Definition.Pattern);
            }
        }

        sw.Stop();
        Volatile.Write(ref _lastMatchDurationMs, sw.Elapsed.TotalMilliseconds);
        Interlocked.Increment(ref _missCount);

        _logger?.LogDebug("Tier 0 did not match any regex rules in {Duration:F2}ms. Escalating.", sw.Elapsed.TotalMilliseconds);

        return Task.FromResult(new RouteResult
        {
            Status = RouteStatus.Escalate,
            Tier = IntentTier.Tier0,
            Intent = null,
            Confidence = 0.0,
            MatchedRuleName = null,
            Reason = "No matching regex rule."
        });
    }

    private sealed record CompiledRule(RegexRuleDefinition Definition, Regex Regex);
}
