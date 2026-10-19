namespace CyberPipe.Contracts;

/// <summary>
/// Действие по безопасности воспроизведения аудио и медиаконтента (TASK-071).
/// Вычисляется на основе эвристической оценки риска, без фиксированного ограничения громкости (Q17).
/// </summary>
public enum AudioSafetyAction
{
    /// <summary>Воспроизведение безопасно на исходном уровне громкости.</summary>
    Allow = 0,

    /// <summary>
    /// Снизить громкость пропорционально риску контента.
    /// Целевой уровень вычисляется динамически (не фиксированные 60%/25%).
    /// </summary>
    ReduceVolume = 1,

    /// <summary>Предупредить владельца и запросить подтверждение перед воспроизведением (HITL).</summary>
    Warn = 2,

    /// <summary>Отложить или отклонить воспроизведение (например, внезапный громкий звук ночью).</summary>
    DeferOrDeny = 3,
}

/// <summary>
/// Вклад конкретного сигнала в итоговую оценку риска (TASK-071).
/// </summary>
public sealed record SignalContribution
{
    /// <summary>Вид сигнала (Semantic, Acoustic, Context).</summary>
    public required string SignalKind { get; init; }

    /// <summary>Исходное ненормированное значение или описание.</summary>
    public required string RawValue { get; init; }

    /// <summary>Нормированная оценка риска от этого сигнала (0.0 .. 1.0).</summary>
    public double NormalizedRisk { get; init; }

    /// <summary>Вес сигнала в общей формуле (0.0 .. 1.0).</summary>
    public double Weight { get; init; }

    /// <summary>Вклад в общую оценку риска (NormalizedRisk * Weight).</summary>
    public double Contribution => NormalizedRisk * Weight;

    /// <summary>Подробности вычисления.</summary>
    public string? Details { get; init; }
}

/// <summary>
/// Запрос на оценку безопасности аудиоконтента перед воспроизведением (TASK-071).
/// </summary>
public sealed record AudioSafetyRequest
{
    /// <summary>Заголовок аудио или медиа (если известен).</summary>
    public string? Title { get; init; }

    /// <summary>Описание контента.</summary>
    public string? Description { get; init; }

    /// <summary>Транскрипт речи или текста (если имеется).</summary>
    public string? Transcript { get; init; }

    /// <summary>Пиковый уровень сигнала в dBFS (например, 0 dBFS — максимум, -20 dBFS — умеренно).</summary>
    public double? PeakDbfs { get; init; }

    /// <summary>Интегральная громкость контента в LUFS (например, -6 LUFS — очень громко, -24 LUFS — тихо).</summary>
    public double? LoudnessLufs { get; init; }

    /// <summary>Резкий перепад громкости на старте контента в dB (атака звука / скачок громкости).</summary>
    public double? StartTransientDb { get; init; }

    /// <summary>Длительность аудиоконтента.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Локальное время владельца (если не задано, берётся текущее).</summary>
    public TimeOnly? OwnerLocalTime { get; init; }

    /// <summary>Источник запроса воспроизведения (канал, цепочка, навык).</summary>
    public string Source { get; init; } = "unknown";

    /// <summary>
    /// Признак, что владелец явно запросил воспроизведение этого аудио
    /// (в отличие от внезапного воспроизведения по фоновому триггеру или оповещению).
    /// </summary>
    public bool IsExplicitOwnerRequest { get; init; }
}

/// <summary>
/// Решение по безопасности аудиоконтента (TASK-071).
/// </summary>
public sealed record AudioSafetyDecision
{
    /// <summary>Рекомендуемое действие.</summary>
    public required AudioSafetyAction Action { get; init; }

    /// <summary>Агрегированная оценка риска (0.0 — абсолютно безопасно, 1.0 — критический риск).</summary>
    public required double RiskScore { get; init; }

    /// <summary>
    /// Рекомендуемый целевой уровень громкости (0.0 .. 1.0) для действия <see cref="AudioSafetyAction.ReduceVolume"/>.
    /// Монотонно убывает с ростом риска. Null для остальных действий.
    /// </summary>
    public double? RecommendedVolumeLevel { get; init; }

    /// <summary>Детализация вкладов сигналов в оценку риска (объяснение решения).</summary>
    public IReadOnlyList<SignalContribution> Contributions { get; init; } = [];

    /// <summary>Сводное объяснение решения.</summary>
    public required string Explanation { get; init; }
}

/// <summary>
/// Параметры конфигурации сервиса эвристической безопасности аудио (TASK-071).
/// </summary>
public sealed record AudioContentSafetyOptions
{
    /// <summary>Начало ночного интервала (час в сутках 0..23).</summary>
    public int NightStartHour { get; init; } = 23;

    /// <summary>Конец ночного интервала (час в сутках 0..23).</summary>
    public int NightEndHour { get; init; } = 7;

    /// <summary>Вес семантического сигнала в оценке риска.</summary>
    public double SemanticWeight { get; init; } = 0.40;

    /// <summary>Вес акустического сигнала (LUFS, Peak, transient) в оценке риска.</summary>
    public double AcousticWeight { get; init; } = 0.35;

    /// <summary>Вес контекстного сигнала (ночь, внезапность) в оценке риска.</summary>
    public double ContextWeight { get; init; } = 0.25;

    /// <summary>Порог риска для разрешения без ограничений (ниже этого порога — Allow).</summary>
    public double LowRiskThreshold { get; init; } = 0.30;

    /// <summary>Порог риска для перехода от снижения громкости к предупреждению (Warn).</summary>
    public double MediumRiskThreshold { get; init; } = 0.60;

    /// <summary>Порог высокого риска (выше этого порога — DeferOrDeny).</summary>
    public double HighRiskThreshold { get; init; } = 0.85;

    /// <summary>
    /// Прототипы рисковых категорий контента (RU/EN) для семантической близости эмбеддингов.
    /// </summary>
    public IReadOnlyList<string> RiskPrototypes { get; init; } =
    [
        "loud jump scare sudden screaming horror noise",
        "громкий внезапный скример крик пугающий звук",
        "emergency siren blaring alarm explosion blast",
        "сирена тревоги громкий взрыв оглушительный сигнал",
        "ear piercing high pitch screech intense noise",
        "пронзительный визг оглушающий скрежет резкий шум",
    ];
}

/// <summary>
/// Сервис эвристической оценки риска воспроизведения аудио и медиаконтента (TASK-071).
/// </summary>
public interface IAudioContentSafetyEvaluator
{
    /// <summary>Оценивает риск воспроизведения контента и возвращает соразмерное действие.</summary>
    Task<AudioSafetyDecision> EvaluateAsync(AudioSafetyRequest request, CancellationToken cancellationToken = default);
}
