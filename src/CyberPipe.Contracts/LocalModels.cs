namespace CyberPipe.Contracts;

/// <summary>
/// Состояние локального рантайма моделей (Ollama) на ПК владельца (TASK-081).
/// </summary>
public enum LocalRuntimeState
{
    /// <summary>Рантайм не обнаружен: предлагается установка или ручная инструкция.</summary>
    NotInstalled = 0,

    /// <summary>Рантайм установлен и его локальный API отвечает.</summary>
    Ready = 1,
}

/// <summary>
/// Состояние локального рантайма: обнаружение, версия, адрес API (TASK-081).
/// </summary>
public sealed record LocalRuntimeInfo
{
    /// <summary>Состояние рантайма.</summary>
    public required LocalRuntimeState State { get; init; }

    /// <summary>Установленная версия рантайма; отсутствует, если не установлен.</summary>
    public string? Version { get; init; }

    /// <summary>Базовый адрес локального API рантайма из конфигурации.</summary>
    public required string ApiBaseUrl { get; init; }
}

/// <summary>
/// Установленная в рантайме локальная модель (TASK-081).
/// </summary>
public sealed record InstalledLocalModel
{
    /// <summary>Тег модели в рантайме (соответствует <c>ModelDescriptorDto.ModelId</c>).</summary>
    public required string ModelId { get; init; }

    /// <summary>Размер весов в байтах; может отсутствовать.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Семейство модели из <c>details.family</c> ответа <c>/api/tags</c>.</summary>
    public string? Family { get; init; }

    /// <summary>Размер параметров из <c>details.parameter_size</c> (например, <c>7B</c>).</summary>
    public string? ParameterSize { get; init; }

    /// <summary>Уровень квантования из <c>details.quantization_level</c> (например, <c>Q4_K_M</c>).</summary>
    public string? QuantizationLevel { get; init; }

    /// <summary>Время последнего изменения модели в рантайме.</summary>
    public DateTimeOffset? ModifiedAtUtc { get; init; }
}

/// <summary>
/// Модель, загруженная в RAM/VRAM рантайма Ollama (<c>GET /api/ps</c>).
/// </summary>
public sealed record OllamaRunningModelDto
{
    /// <summary>Тег модели в рантайме.</summary>
    public required string ModelId { get; init; }

    /// <summary>Полный размер модели в памяти, байты.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Часть размера, размещённая в видеопамяти, байты.</summary>
    public long SizeVramBytes { get; init; }

    /// <summary>Момент, когда рантайм выгрузит модель, если она не используется.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

/// <summary>
/// Прогресс установки модели: проценты, объёмы, стадия (TASK-081).
/// Передаётся через <c>IProgress&lt;T&gt;</c>, типов HTTP-клиента в сигнатурах нет.
/// </summary>
public sealed record ModelInstallProgress
{
    /// <summary>Тег устанавливаемой модели.</summary>
    public required string ModelId { get; init; }

    /// <summary>Процент выполнения 0–100.</summary>
    public required int Percent { get; init; }

    /// <summary>Скачано байт.</summary>
    public long DownloadedBytes { get; init; }

    /// <summary>Всего байт; может отсутствовать, если рантайм не сообщил.</summary>
    public long? TotalBytes { get; init; }

    /// <summary>Текущая стадия строкой рантайма (pulling, verifying и т.п.).</summary>
    public string? Stage { get; init; }
}

/// <summary>
/// Итог пробного запроса к установленной модели (TASK-081).
/// </summary>
public sealed record ModelProbeResult
{
    /// <summary>Признак успешной пробы.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Образец ответа пробы; отсутствует при неуспехе.</summary>
    public string? SampleText { get; init; }

    /// <summary>Причина неуспеха; отсутствует при успехе.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Итог установки стандартного эмбеддинга Tier 1 (TASK-081).
/// </summary>
public sealed record EmbeddingInstallResult
{
    /// <summary>True, если эмбеддинг уже был установлен (no-op).</summary>
    public required bool AlreadyInstalled { get; init; }

    /// <summary>Каталог установленных ONNX-файлов эмбеддинга.</summary>
    public required string InstallPath { get; init; }

    /// <summary>Размерность вектора из дескриптора каталога TASK-080.</summary>
    public int? VectorDimensions { get; init; }
}

/// <summary>
/// Состояние установки модели для UI установщика (TASK-081).
/// </summary>
public enum LocalModelInstallState
{
    /// <summary>Модель не установлена в рантайме.</summary>
    NotInstalled = 0,

    /// <summary>Идёт скачивание весов.</summary>
    Downloading = 1,

    /// <summary>Модель установлена и прошла пробный запрос.</summary>
    Installed = 2,

    /// <summary>Последняя операция завершилась ошибкой.</summary>
    Error = 3,
}

/// <summary>
/// Установщик локального рантайма моделей (TASK-081): обнаружение Ollama,
/// установка по подтверждению владельца, ожидание готовности локального API.
/// </summary>
public interface ILocalRuntimeInstaller
{
    /// <summary>Проверяет наличие рантайма и готовность его локального API.</summary>
    Task<LocalRuntimeInfo> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Устанавливает рантайм из официального дистрибутива (адрес — конфигурация),
    /// если он отсутствует, и дожидается готовности API. Вызывается только после
    /// явного подтверждения владельца в UI; фоновых установок нет.
    /// </summary>
    Task<Result<LocalRuntimeInfo>> EnsureInstalledAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Установщик локальных моделей и стандартного эмбеддинга (TASK-081).
/// Кандидаты — только записи каталога TASK-080 с <c>hostingType = Local</c>.
/// </summary>
public interface ILocalModelInstaller
{
    /// <summary>Список моделей, установленных в рантайме.</summary>
    Task<IReadOnlyList<InstalledLocalModel>> ListInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>Модели, которые сейчас находятся в памяти рантайма (<c>GET /api/ps</c>).</summary>
    Task<IReadOnlyList<OllamaRunningModelDto>> ListRunningAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OllamaRunningModelDto>>([]);

    /// <summary>Выгружает модель из оперативной и видеопамяти (<c>keep_alive: 0</c>).</summary>
    Task<Result> UnloadAsync(string modelId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(
            "Model.UnloadFailed",
            "This installer does not support unloading models from memory."));

    /// <summary>
    /// Скачивает произвольную модель из библиотеки Ollama по тегу и проверяет её.
    /// Реализация по умолчанию делегирует в <see cref="InstallAsync"/> с синтетическим дескриптором.
    /// </summary>
    Task<Result<ModelProbeResult>> PullByTagAsync(
        string modelTag,
        IProgress<ModelInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelTag))
        {
            return Task.FromResult(Result<ModelProbeResult>.Failure("Model.NoRuntimeId", "Model tag is required."));
        }

        string tag = modelTag.Trim();
        return InstallAsync(
            new ModelDescriptorDto
            {
                Id = tag,
                Name = tag,
                HostingType = ModelHostingType.Local,
                RecommendedFor = ModelTaskCategory.HeavyReasoningTier3,
                Provider = "Ollama",
                ModelId = tag,
                ParamsSummary = tag,
                DefaultContextTokens = 8192,
                LicenseOrPricing = "Ollama library",
                BestFor = tag,
            },
            progress,
            cancellationToken);
    }

    /// <summary>
    /// Устанавливает модель из каталога: проверка места, скачивание с прогрессом,
    /// применение <c>defaultContextTokens</c> из каталога и пробный запрос.
    /// Модель считается установленной только после успешной пробы.
    /// </summary>
    Task<Result<ModelProbeResult>> InstallAsync(
        ModelDescriptorDto model,
        IProgress<ModelInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Удаляет установленную модель по подтверждению владельца.</summary>
    Task<Result> RemoveAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>Выполняет пробный запрос к установленной модели.</summary>
    Task<Result<ModelProbeResult>> VerifyAsync(
        ModelDescriptorDto model,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Устанавливает стандартный эмбеддинг <c>multilingual-e5-small</c> для Tier 1
    /// (Ollama не требуется). Повторная установка — no-op «уже установлен».
    /// </summary>
    Task<Result<EmbeddingInstallResult>> EnsureEmbeddingAsync(
        IProgress<ModelInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Параметры установщика локальных моделей и рантайма (TASK-081).
/// Адреса скачивания и порт API — конфигурация TASK-003, не константы в коде.
/// </summary>
public sealed record LocalModelInstallerOptions
{
    /// <summary>Базовый адрес локального API рантайма.</summary>
    public string OllamaApiBaseUrl { get; init; } = "http://localhost:11434";

    /// <summary>Адрес официального дистрибутива Ollama для Windows.</summary>
    public string OllamaWindowsDownloadUrl { get; init; } = "https://ollama.com/download/OllamaSetup.exe";

    /// <summary>Справочная инструкция при недоступности дистрибутива для текущей ОС.</summary>
    public string OllamaManualInstallHint { get; init; } = "Install Ollama manually: https://ollama.com/download";

    /// <summary>Таймаут ожидания готовности API рантайма в секундах.</summary>
    public int RuntimeReadyTimeoutSeconds { get; init; } = 120;

    /// <summary>Каталог данных приложения для ONNX-файлов эмбеддинга; null — каталог по умолчанию.</summary>
    public string? EmbeddingDataDirectory { get; init; }

    /// <summary>Базовый адрес источника ONNX-файлов эмбеддинга.</summary>
    public string EmbeddingSourceBaseUrl { get; init; } = "https://huggingface.co/intfloat/multilingual-e5-small/resolve/main/onnx";

    /// <summary>Файлы эмбеддинга для скачивания (имя и относительный путь от базового адреса).</summary>
    public IReadOnlyList<string> EmbeddingFiles { get; init; } = ["model.onnx", "tokenizer.json"];

    /// <summary>Запас свободного места сверх q4SizeGb при проверке диска (множитель).</summary>
    public double DiskHeadroomFactor { get; init; } = 1.5;
}
