using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CyberPipe.Security;

/// <summary>
/// Сервис эвристической безопасности аудиоконтента (TASK-071).
/// Оценивает риск по семантике, акустическим характеристикам и контексту воспроизведения.
/// Динамически вычисляет уровень снижения громкости (без фиксированного Volume Clamp 60%/25%).
/// </summary>
public sealed class AudioContentSafetyEvaluator : IAudioContentSafetyEvaluator
{
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly AudioContentSafetyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AudioContentSafetyEvaluator> _logger;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private Dictionary<string, float[]>? _prototypeVectors;

    public AudioContentSafetyEvaluator(
        IEmbeddingProvider? embeddingProvider = null,
        AudioContentSafetyOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<AudioContentSafetyEvaluator>? logger = null)
    {
        _embeddingProvider = embeddingProvider;
        _options = options ?? new AudioContentSafetyOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<AudioContentSafetyEvaluator>.Instance;
    }

    /// <inheritdoc />
    public async Task<AudioSafetyDecision> EvaluateAsync(AudioSafetyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contributions = new List<SignalContribution>();

        // 1. Семантический сигнал
        SignalContribution semanticSignal = await EvaluateSemanticSignalAsync(request, cancellationToken).ConfigureAwait(false);
        contributions.Add(semanticSignal);

        // 2. Акустический сигнал (сигнальные метаданные)
        SignalContribution acousticSignal = EvaluateAcousticSignal(request);
        contributions.Add(acousticSignal);

        // 3. Контекстный сигнал (время суток, явный запрос владельца, длительность)
        SignalContribution contextSignal = EvaluateContextSignal(request);
        contributions.Add(contextSignal);

        // Взвешенная агрегация активных сигналов
        double totalWeightedScore = 0.0;
        double totalWeight = 0.0;

        foreach (SignalContribution sig in contributions)
        {
            if (sig.Weight > 0 && sig.NormalizedRisk >= 0)
            {
                totalWeightedScore += sig.NormalizedRisk * sig.Weight;
                totalWeight += sig.Weight;
            }
        }

        double riskScore;
        if (totalWeight > 0)
        {
            riskScore = Math.Clamp(totalWeightedScore / totalWeight, 0.0, 1.0);
        }
        else
        {
            // Отсутствие всех сигналов: для внезапного воспроизведения — Warn (fail-cautious)
            riskScore = request.IsExplicitOwnerRequest ? 0.0 : 0.65;
        }

        AudioSafetyAction action;
        double? recommendedVolume = null;

        bool hasContentOrSignal = (semanticSignal.Weight > 0) || (acousticSignal.Weight > 0);
        bool isNight = IsNightTime(request.OwnerLocalTime);

        // При отсутствии содержательных сигналов для внезапного воспроизведения — Warn (fail-cautious)
        if (!hasContentOrSignal && !request.IsExplicitOwnerRequest)
        {
            action = AudioSafetyAction.Warn;
            riskScore = Math.Max(riskScore, _options.MediumRiskThreshold);
        }
        else if (isNight && !request.IsExplicitOwnerRequest && riskScore >= _options.MediumRiskThreshold)
        {
            action = AudioSafetyAction.DeferOrDeny;
        }
        else if (riskScore < _options.LowRiskThreshold)
        {
            action = AudioSafetyAction.Allow;
        }
        else if (riskScore < _options.MediumRiskThreshold)
        {
            action = AudioSafetyAction.ReduceVolume;

            // Динамический расчёт целевой громкости:
            // Монотонно убывает с ростом риска в диапазоне [LowRiskThreshold .. MediumRiskThreshold]
            // От ~0.85 при низком риске до ~0.35 при приближении к порогу предупреждения
            double t = (riskScore - _options.LowRiskThreshold) / (_options.MediumRiskThreshold - _options.LowRiskThreshold);
            double volume = 0.85 - (t * 0.50);
            recommendedVolume = Math.Round(Math.Clamp(volume, 0.15, 0.95), 2);
        }
        else if (riskScore < _options.HighRiskThreshold)
        {
            action = AudioSafetyAction.Warn;
        }
        else
        {
            action = AudioSafetyAction.DeferOrDeny;
        }

        string explanation = BuildExplanation(action, riskScore, recommendedVolume, contributions);

        _logger.LogInformation(
            "AudioSafety evaluated: Action={Action}, Risk={Risk:F2}, TargetVolume={Volume}, Source={Source}",
            action,
            riskScore,
            recommendedVolume,
            request.Source);

        return new AudioSafetyDecision
        {
            Action = action,
            RiskScore = Math.Round(riskScore, 4),
            RecommendedVolumeLevel = recommendedVolume,
            Contributions = contributions,
            Explanation = explanation,
        };
    }

    private async Task<SignalContribution> EvaluateSemanticSignalAsync(AudioSafetyRequest request, CancellationToken cancellationToken)
    {
        string text = string.Join(" ", new[] { request.Title, request.Description, request.Transcript }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

        if (string.IsNullOrWhiteSpace(text) || _embeddingProvider is null)
        {
            return new SignalContribution
            {
                SignalKind = "Semantic",
                RawValue = string.IsNullOrWhiteSpace(text) ? "no-text" : "provider-unavailable",
                NormalizedRisk = 0.0,
                Weight = 0.0, // не учитывается при расчёте
                Details = "Semantic embedding evaluation skipped: text empty or embedding provider unavailable.",
            };
        }

        try
        {
            await EnsurePrototypesInitializedAsync(cancellationToken).ConfigureAwait(false);

            if (_prototypeVectors is null || _prototypeVectors.Count == 0)
            {
                return new SignalContribution
                {
                    SignalKind = "Semantic",
                    RawValue = "no-prototypes",
                    NormalizedRisk = 0.0,
                    Weight = 0.0,
                    Details = "Prototypes could not be vectorized.",
                };
            }

            float[] textVector = await _embeddingProvider.EmbedAsync(text, cancellationToken).ConfigureAwait(false);

            float maxSimilarity = 0f;
            string bestPrototype = "";

            foreach ((string proto, float[] protoVector) in _prototypeVectors)
            {
                float sim = CosineSimilarity(textVector, protoVector);
                if (sim > maxSimilarity)
                {
                    maxSimilarity = sim;
                    bestPrototype = proto;
                }
            }

            // Косинусная близость в L2-нормализованных векторах: обычно от 0 до 1
            double risk = Math.Clamp(maxSimilarity, 0.0, 1.0);

            return new SignalContribution
            {
                SignalKind = "Semantic",
                RawValue = $"sim={maxSimilarity:F3}",
                NormalizedRisk = risk,
                Weight = _options.SemanticWeight,
                Details = $"Max similarity to prototype: {maxSimilarity:F3}",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic embedding evaluation failed. Proceeding with degraded acoustic/context signals.");
            return new SignalContribution
            {
                SignalKind = "Semantic",
                RawValue = "error",
                NormalizedRisk = 0.0,
                Weight = 0.0,
                Details = "Semantic evaluation failed due to provider error.",
            };
        }
    }

    private SignalContribution EvaluateAcousticSignal(AudioSafetyRequest request)
    {
        if (!request.PeakDbfs.HasValue && !request.LoudnessLufs.HasValue && !request.StartTransientDb.HasValue)
        {
            return new SignalContribution
            {
                SignalKind = "Acoustic",
                RawValue = "no-metadata",
                NormalizedRisk = 0.0,
                Weight = 0.0,
                Details = "No acoustic metadata provided.",
            };
        }

        double acousticScore = 0.0;
        int subSignalCount = 0;

        // Peak dBFS: от -24 dBFS (0.0) до 0 dBFS (1.0)
        if (request.PeakDbfs.HasValue)
        {
            double peak = request.PeakDbfs.Value;
            double peakRisk = Math.Clamp((peak - (-24.0)) / (0.0 - (-24.0)), 0.0, 1.0);
            acousticScore += peakRisk;
            subSignalCount++;
        }

        // LUFS: от -24 LUFS (0.0) до -6 LUFS (1.0)
        if (request.LoudnessLufs.HasValue)
        {
            double lufs = request.LoudnessLufs.Value;
            double lufsRisk = Math.Clamp((lufs - (-24.0)) / (-6.0 - (-24.0)), 0.0, 1.0);
            acousticScore += lufsRisk;
            subSignalCount++;
        }

        // Start transient dB: резкий перепад громкости на старте от 0 dB (0.0) до 20 dB (1.0)
        if (request.StartTransientDb.HasValue)
        {
            double transient = request.StartTransientDb.Value;
            double transientRisk = Math.Clamp(transient / 20.0, 0.0, 1.0);
            acousticScore += transientRisk;
            subSignalCount++;
        }

        double normalizedAcousticRisk = subSignalCount > 0 ? (acousticScore / subSignalCount) : 0.0;

        return new SignalContribution
        {
            SignalKind = "Acoustic",
            RawValue = $"Peak={request.PeakDbfs:F1}dBFS, LUFS={request.LoudnessLufs:F1}, Transient={request.StartTransientDb:F1}dB",
            NormalizedRisk = normalizedAcousticRisk,
            Weight = _options.AcousticWeight,
            Details = $"Acoustic risk calculated across {subSignalCount} measured metrics.",
        };
    }

    private SignalContribution EvaluateContextSignal(AudioSafetyRequest request)
    {
        double contextRisk = 0.0;
        var factors = new List<string>();

        bool isNight = IsNightTime(request.OwnerLocalTime);
        if (isNight)
        {
            contextRisk += 0.50;
            factors.Add("NightHours");
        }

        if (!request.IsExplicitOwnerRequest)
        {
            contextRisk += 0.40;
            factors.Add("UnpromptedPlayback");
        }
        else
        {
            // Явный запрос владельца уменьшает контекстный риск
            contextRisk = Math.Max(0.0, contextRisk - 0.20);
            factors.Add("ExplicitOwnerRequest");
        }

        if (request.Duration.HasValue && request.Duration.Value.TotalSeconds is > 0 and < 4 && !request.IsExplicitOwnerRequest)
        {
            // Очень короткий внезапный звук несёт повышенный риск испуга (startle)
            contextRisk += 0.20;
            factors.Add("ShortSuddenBurst");
        }

        double normalizedContextRisk = Math.Clamp(contextRisk, 0.0, 1.0);

        return new SignalContribution
        {
            SignalKind = "Context",
            RawValue = string.Join("+", factors),
            NormalizedRisk = normalizedContextRisk,
            Weight = _options.ContextWeight,
            Details = $"Context risk evaluated from: {string.Join(", ", factors)}.",
        };
    }

    private bool IsNightTime(TimeOnly? ownerLocalTime)
    {
        TimeOnly time = ownerLocalTime ?? TimeOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        int hour = time.Hour;

        if (_options.NightStartHour > _options.NightEndHour)
        {
            // Например 23:00 - 07:00
            return hour >= _options.NightStartHour || hour < _options.NightEndHour;
        }

        // В редком случае дневного диапазона
        return hour >= _options.NightStartHour && hour < _options.NightEndHour;
    }

    private async Task EnsurePrototypesInitializedAsync(CancellationToken cancellationToken)
    {
        if (_prototypeVectors is not null)
        {
            return;
        }

        if (_embeddingProvider is null)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_prototypeVectors is not null)
            {
                return;
            }

            var vectors = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string proto in _options.RiskPrototypes)
            {
                try
                {
                    float[] vec = await _embeddingProvider.EmbedAsync(proto, cancellationToken).ConfigureAwait(false);
                    vectors[proto] = vec;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to vectorize prototype description.");
                }
            }

            if (vectors.Count == 0 && _options.RiskPrototypes.Count > 0)
            {
                throw new InvalidOperationException("Failed to vectorize risk prototypes: embedding provider failed.");
            }

            _prototypeVectors = vectors;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            return 0f;
        }

        float dot = 0f;
        float normA = 0f;
        float normB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        float denom = (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
        return denom > 0f ? (dot / denom) : 0f;
    }

    private static string BuildExplanation(AudioSafetyAction action, double riskScore, double? volume, IReadOnlyList<SignalContribution> contributions)
    {
        string actionText = action switch
        {
            AudioSafetyAction.Allow => "Playback allowed at full volume.",
            AudioSafetyAction.ReduceVolume => $"Playback allowed with dynamically reduced volume to {Math.Round((volume ?? 1.0) * 100)}% based on risk score.",
            AudioSafetyAction.Warn => "High risk detected. Confirmation required before playback.",
            AudioSafetyAction.DeferOrDeny => "Critical risk detected (e.g. unprompted loud sound at night). Playback deferred or denied.",
            _ => "Evaluation complete.",
        };

        string contribDetails = string.Join("; ", contributions
            .Where(c => c.Weight > 0)
            .Select(c => $"{c.SignalKind}: risk={c.NormalizedRisk:F2} (weight={c.Weight:F2})"));

        return $"{actionText} Overall risk: {riskScore:F2}. [{contribDetails}]";
    }
}
