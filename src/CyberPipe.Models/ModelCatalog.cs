using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Models;

/// <summary>
/// Ошибка загрузки или валидации каталога моделей (TASK-080).
/// Сообщение всегда содержит файл каталога и запись, а не молчаливый пропуск.
/// </summary>
public sealed class ModelCatalogException : Exception
{
    /// <summary>Файл каталога, вызвавший ошибку.</summary>
    public string CatalogFile { get; }

    /// <summary>Идентификатор записи, вызвавшей ошибку; null для ошибок уровня файла.</summary>
    public string? RecordId { get; }

    /// <summary>Создаёт ошибку каталога.</summary>
    public ModelCatalogException(string catalogFile, string? recordId, string message)
        : base(recordId is null
            ? $"Model catalog '{catalogFile}': {message}"
            : $"Model catalog '{catalogFile}' record '{recordId}': {message}")
    {
        CatalogFile = catalogFile;
        RecordId = recordId;
    }
}

/// <summary>
/// Дескриптор эмбеддинг-модели Tier 1: контрактный <see cref="ModelDescriptorDto" />
/// плюс параметры векторов, которые потребители (TASK-031, TASK-012) берут из данных,
/// а не из зашитых в код констант.
/// </summary>
public sealed record EmbeddingModelDescriptor
{
    /// <summary>Базовый дескриптор модели из каталога.</summary>
    public required ModelDescriptorDto Model { get; init; }

    /// <summary>Размерность вектора эмбеддинга.</summary>
    public required int VectorDimensions { get; init; }

    /// <summary>Нормализация векторов (например <c>l2</c>).</summary>
    public required string Normalization { get; init; }
}

/// <summary>
/// Каталог моделей CyberPipe (TASK-080): курируемые наборы из JSON-файлов,
/// проекция в контрактный <see cref="ModelDescriptorDto" />. Записи в каталоги
/// из приложения нет; чтение — только из поставляемых с приложением файлов.
/// </summary>
public interface IModelCatalog
{
    /// <summary>Версия локального каталога.</summary>
    string LocalCatalogVersion { get; }

    /// <summary>Версия облачного каталога.</summary>
    string CloudCatalogVersion { get; }

    /// <summary>Версия каталога эмбеддингов.</summary>
    string EmbeddingCatalogVersion { get; }

    /// <summary>Локальные модели (ровно 15 записей).</summary>
    IReadOnlyList<ModelDescriptorDto> LocalModels { get; }

    /// <summary>Облачные модели (ровно 8 записей).</summary>
    IReadOnlyList<ModelDescriptorDto> CloudModels { get; }

    /// <summary>Стандартный эмбеддинг Tier 1 (<c>multilingual-e5-small</c>).</summary>
    EmbeddingModelDescriptor EmbeddingModel { get; }

    /// <summary>Альтернативные эмбеддинги; может быть пустым.</summary>
    IReadOnlyList<EmbeddingModelDescriptor> AlternativeEmbeddingModels { get; }

    /// <summary>Поиск дескриптора по стабильному идентификатору во всех каталогах.</summary>
    ModelDescriptorDto? GetById(string id);

    /// <summary>Фильтрация дескрипторов по категории и размещению.</summary>
    IReadOnlyList<ModelDescriptorDto> Find(ModelTaskCategory? category = null, ModelHostingType? hostingType = null);
}

/// <summary>
/// Загрузчик каталогов моделей из трёх JSON-файлов (TASK-080).
/// В коде загрузчика нет зашитого списка моделей: состав определяется файлами.
/// </summary>
public sealed class FileModelCatalog : IModelCatalog
{
    private readonly List<ModelDescriptorDto> _local;
    private readonly List<ModelDescriptorDto> _cloud;
    private readonly List<EmbeddingModelDescriptor> _embeddings;

    private FileModelCatalog(
        string localVersion,
        List<ModelDescriptorDto> local,
        string cloudVersion,
        List<ModelDescriptorDto> cloud,
        string embeddingVersion,
        List<EmbeddingModelDescriptor> embeddings,
        EmbeddingModelDescriptor primary)
    {
        LocalCatalogVersion = localVersion;
        _local = local;
        CloudCatalogVersion = cloudVersion;
        _cloud = cloud;
        EmbeddingCatalogVersion = embeddingVersion;
        _embeddings = embeddings;
        EmbeddingModel = primary;
    }

    /// <inheritdoc />
    public string LocalCatalogVersion { get; }

    /// <inheritdoc />
    public string CloudCatalogVersion { get; }

    /// <inheritdoc />
    public string EmbeddingCatalogVersion { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptorDto> LocalModels => _local;

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptorDto> CloudModels => _cloud;

    /// <inheritdoc />
    public EmbeddingModelDescriptor EmbeddingModel { get; }

    /// <inheritdoc />
    public IReadOnlyList<EmbeddingModelDescriptor> AlternativeEmbeddingModels =>
        _embeddings.Where(e => !string.Equals(e.Model.Id, EmbeddingModel.Model.Id, StringComparison.Ordinal)).ToList();

    /// <inheritdoc />
    public ModelDescriptorDto? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        foreach (ModelDescriptorDto model in _local)
        {
            if (string.Equals(model.Id, id, StringComparison.Ordinal))
            {
                return model;
            }
        }

        foreach (ModelDescriptorDto model in _cloud)
        {
            if (string.Equals(model.Id, id, StringComparison.Ordinal))
            {
                return model;
            }
        }

        foreach (EmbeddingModelDescriptor embedding in _embeddings)
        {
            if (string.Equals(embedding.Model.Id, id, StringComparison.Ordinal))
            {
                return embedding.Model;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptorDto> Find(ModelTaskCategory? category = null, ModelHostingType? hostingType = null)
    {
        var result = new List<ModelDescriptorDto>();
        foreach (ModelDescriptorDto model in _local.Concat(_cloud).Concat(_embeddings.Select(e => e.Model)))
        {
            if (category.HasValue && model.RecommendedFor != category.Value)
            {
                continue;
            }

            if (hostingType.HasValue && model.HostingType != hostingType.Value)
            {
                continue;
            }

            result.Add(model);
        }

        return result;
    }

    /// <summary>
    /// Загружает и валидирует три каталога. Невалидный каталог отклоняется
    /// с <see cref="ModelCatalogException" />, содержащей файл и запись.
    /// </summary>
    public static FileModelCatalog Load(
        string localCatalogPath,
        string cloudCatalogPath,
        string embeddingCatalogPath,
        int expectedLocalCount = 15,
        int expectedCloudCount = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localCatalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudCatalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingCatalogPath);

        (string localVersion, List<ModelDescriptorDto> local) =
            ModelCatalogValidator.LoadChatCatalog(localCatalogPath, ModelHostingType.Local, expectedLocalCount);
        (string cloudVersion, List<ModelDescriptorDto> cloud) =
            ModelCatalogValidator.LoadChatCatalog(cloudCatalogPath, ModelHostingType.RemoteCloud, expectedCloudCount);
        (string embeddingVersion, List<EmbeddingModelDescriptor> embeddings) =
            ModelCatalogValidator.LoadEmbeddingCatalog(embeddingCatalogPath);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in local.Select(m => m.Id).Concat(cloud.Select(m => m.Id)).Concat(embeddings.Select(e => e.Model.Id)))
        {
            if (!seen.Add(id))
            {
                throw new ModelCatalogException(embeddingCatalogPath, id, "Duplicate model id across catalogs.");
            }
        }

        EmbeddingModelDescriptor? primary = embeddings.FirstOrDefault(e =>
            string.Equals(e.Model.Id, "multilingual-e5-small", StringComparison.Ordinal));
        if (primary is null)
        {
            throw new ModelCatalogException(embeddingCatalogPath, null, "Embedding catalog must contain 'multilingual-e5-small'.");
        }

        return new FileModelCatalog(localVersion, local, cloudVersion, cloud, embeddingVersion, embeddings, primary);
    }

    /// <summary>
    /// Загружает каталоги из подкаталога <c>models</c> базового каталога приложения
    /// (совпадает с путями по умолчанию <c>ModelOptions</c> из TASK-003).
    /// </summary>
    public static FileModelCatalog LoadDefault(string? baseDirectory = null)
    {
        string root = baseDirectory ?? AppContext.BaseDirectory;
        return Load(
            Path.Combine(root, "models", "local-models.json"),
            Path.Combine(root, "models", "cloud-models.json"),
            Path.Combine(root, "models", "embedding-models.json"));
    }
}

/// <summary>
/// Валидатор каталогов моделей (TASK-080): проверка при загрузке — счётчики,
/// уникальность <c>id</c>, обязательные поля по виду размещения, соотношение
/// окон контекста, положительность размеров и цен.
/// </summary>
public static class ModelCatalogValidator
{
    /// <summary>Загружает и валидирует каталог чат-моделей (локальный или облачный).</summary>
    public static (string Version, List<ModelDescriptorDto> Models) LoadChatCatalog(
        string path, ModelHostingType expectedHosting, int expectedCount)
    {
        using JsonDocument doc = ReadDocument(path);
        JsonElement root = doc.RootElement;

        string version = RequiredVersion(root, path);
        JsonElement models = RequiredArray(root, "models", path, null);
        var result = new List<ModelDescriptorDto>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonElement entry in models.EnumerateArray())
        {
            string? id = OptionalString(entry, "id");
            ModelDescriptorDto dto = ParseChatEntry(entry, path, id, expectedHosting);
            if (!ids.Add(dto.Id))
            {
                throw new ModelCatalogException(path, dto.Id, "Duplicate model id within catalog.");
            }

            result.Add(dto);
        }

        if (result.Count != expectedCount)
        {
            throw new ModelCatalogException(
                path, null, $"Expected {expectedCount} model records, found {result.Count}.");
        }

        return (version, result);
    }

    /// <summary>Загружает и валидирует каталог эмбеддингов.</summary>
    public static (string Version, List<EmbeddingModelDescriptor> Models) LoadEmbeddingCatalog(string path)
    {
        using JsonDocument doc = ReadDocument(path);
        JsonElement root = doc.RootElement;

        string version = RequiredVersion(root, path);
        var result = new List<EmbeddingModelDescriptor>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonElement entry in RequiredArray(root, "models", path, null).EnumerateArray())
        {
            string? id = OptionalString(entry, "id");
            EmbeddingModelDescriptor descriptor = ParseEmbeddingEntry(entry, path, id);
            if (!ids.Add(descriptor.Model.Id))
            {
                throw new ModelCatalogException(path, descriptor.Model.Id, "Duplicate model id within catalog.");
            }

            result.Add(descriptor);
        }

        if (result.Count == 0)
        {
            throw new ModelCatalogException(path, null, "Embedding catalog must contain at least one model.");
        }

        if (root.TryGetProperty("alternatives", out JsonElement alternatives) &&
            alternatives.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in alternatives.EnumerateArray())
            {
                string? id = OptionalString(entry, "id");
                EmbeddingModelDescriptor descriptor = ParseEmbeddingEntry(entry, path, id);
                if (!ids.Add(descriptor.Model.Id))
                {
                    throw new ModelCatalogException(path, descriptor.Model.Id, "Duplicate model id within catalog.");
                }

                result.Add(descriptor);
            }
        }

        return (version, result);
    }

    private static ModelDescriptorDto ParseChatEntry(
        JsonElement entry, string path, string? id, ModelHostingType expectedHosting)
    {
        string modelId = RequiredString(entry, "id", path, id);
        var dto = new ModelDescriptorDto
        {
            Id = modelId,
            Name = RequiredString(entry, "name", path, modelId),
            HostingType = ParseHosting(RequiredString(entry, "hostingType", path, modelId), path, modelId),
            RecommendedFor = ParseCategory(RequiredString(entry, "recommendedFor", path, modelId), path, modelId),
            Provider = RequiredString(entry, "provider", path, modelId),
            ModelId = RequiredString(entry, "modelId", path, modelId),
            ParamsSummary = RequiredString(entry, "paramsSummary", path, modelId),
            ContextWindowTokens = RequiredPositiveInt(entry, "contextWindowTokens", path, modelId),
            DefaultContextTokens = RequiredPositiveInt(entry, "defaultContextTokens", path, modelId),
            Q4SizeGb = OptionalDouble(entry, "q4SizeGb", path, modelId),
            MinRamGb = OptionalDouble(entry, "minRamGb", path, modelId),
            InputPricePer1mUsd = OptionalDecimal(entry, "inputPricePer1mUsd", path, modelId),
            OutputPricePer1mUsd = OptionalDecimal(entry, "outputPricePer1mUsd", path, modelId),
            LicenseOrPricing = RequiredString(entry, "licenseOrPricing", path, modelId),
            BestFor = RequiredString(entry, "bestFor", path, modelId),
        };

        if (dto.HostingType != expectedHosting)
        {
            throw new ModelCatalogException(path, modelId, $"Expected hosting '{expectedHosting}', found '{dto.HostingType}'.");
        }

        if (dto.RecommendedFor == ModelTaskCategory.EmbeddingTier1)
        {
            throw new ModelCatalogException(path, modelId, "Chat catalogs must use Tier 2 or Tier 3 categories.");
        }

        if (dto.DefaultContextTokens > dto.ContextWindowTokens)
        {
            throw new ModelCatalogException(
                path, modelId,
                $"defaultContextTokens ({dto.DefaultContextTokens}) exceeds contextWindowTokens ({dto.ContextWindowTokens}).");
        }

        if (dto.HostingType == ModelHostingType.Local)
        {
            if (!dto.Q4SizeGb.HasValue || dto.Q4SizeGb.Value <= 0)
            {
                throw new ModelCatalogException(path, modelId, "Local model requires positive 'q4SizeGb'.");
            }

            if (!dto.MinRamGb.HasValue || dto.MinRamGb.Value <= 0)
            {
                throw new ModelCatalogException(path, modelId, "Local model requires positive 'minRamGb'.");
            }

            if ((dto.InputPricePer1mUsd ?? 0) != 0 || (dto.OutputPricePer1mUsd ?? 0) != 0)
            {
                throw new ModelCatalogException(path, modelId, "Local model must not carry cloud prices.");
            }
        }
        else
        {
            if (!dto.InputPricePer1mUsd.HasValue || dto.InputPricePer1mUsd.Value <= 0 ||
                !dto.OutputPricePer1mUsd.HasValue || dto.OutputPricePer1mUsd.Value <= 0)
            {
                throw new ModelCatalogException(path, modelId, "Cloud model requires positive input/output prices per 1M tokens.");
            }

            if (dto.Q4SizeGb.HasValue || dto.MinRamGb.HasValue)
            {
                throw new ModelCatalogException(path, modelId, "Cloud model must not carry 'q4SizeGb'/'minRamGb'.");
            }
        }

        return dto;
    }

    private static EmbeddingModelDescriptor ParseEmbeddingEntry(JsonElement entry, string path, string? id)
    {
        string modelId = RequiredString(entry, "id", path, id);
        var dto = new ModelDescriptorDto
        {
            Id = modelId,
            Name = RequiredString(entry, "name", path, modelId),
            HostingType = ParseHosting(RequiredString(entry, "hostingType", path, modelId), path, modelId),
            RecommendedFor = ParseCategory(RequiredString(entry, "recommendedFor", path, modelId), path, modelId),
            Provider = RequiredString(entry, "provider", path, modelId),
            ModelId = RequiredString(entry, "modelId", path, modelId),
            ParamsSummary = RequiredString(entry, "paramsSummary", path, modelId),
            ContextWindowTokens = RequiredPositiveInt(entry, "contextWindowTokens", path, modelId),
            DefaultContextTokens = RequiredPositiveInt(entry, "defaultContextTokens", path, modelId),
            Q4SizeGb = OptionalDouble(entry, "q4SizeGb", path, modelId),
            MinRamGb = OptionalDouble(entry, "minRamGb", path, modelId),
            InputPricePer1mUsd = OptionalDecimal(entry, "inputPricePer1mUsd", path, modelId),
            OutputPricePer1mUsd = OptionalDecimal(entry, "outputPricePer1mUsd", path, modelId),
            LicenseOrPricing = RequiredString(entry, "licenseOrPricing", path, modelId),
            BestFor = RequiredString(entry, "bestFor", path, modelId),
        };

        if (dto.RecommendedFor != ModelTaskCategory.EmbeddingTier1)
        {
            throw new ModelCatalogException(path, modelId, "Embedding catalog entries must use the Tier 1 embedding category.");
        }

        if (dto.DefaultContextTokens > dto.ContextWindowTokens)
        {
            throw new ModelCatalogException(
                path, modelId,
                $"defaultContextTokens ({dto.DefaultContextTokens}) exceeds contextWindowTokens ({dto.ContextWindowTokens}).");
        }

        int dimensions = RequiredPositiveInt(entry, "vectorDimensions", path, modelId);
        string normalization = RequiredString(entry, "normalization", path, modelId);

        return new EmbeddingModelDescriptor
        {
            Model = dto,
            VectorDimensions = dimensions,
            Normalization = normalization,
        };
    }

    private static JsonDocument ReadDocument(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ModelCatalogException(path, null, $"Cannot read catalog file: {ex.Message}");
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new ModelCatalogException(path, null, $"Invalid JSON: {ex.Message}");
        }
    }

    private static string RequiredVersion(JsonElement root, string path)
    {
        if (!root.TryGetProperty("catalogVersion", out JsonElement version) ||
            version.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(version.GetString()))
        {
            throw new ModelCatalogException(path, null, "Missing required 'catalogVersion'.");
        }

        return version.GetString()!;
    }

    private static JsonElement RequiredArray(JsonElement root, string name, string path, string? id)
    {
        if (!root.TryGetProperty(name, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            throw new ModelCatalogException(path, id, $"Missing required array '{name}'.");
        }

        return array;
    }

    private static string RequiredString(JsonElement entry, string name, string path, string? id)
    {
        string? value = OptionalString(entry, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ModelCatalogException(path, id, $"Missing required field '{name}'.");
        }

        return value!;
    }

    private static string? OptionalString(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int RequiredPositiveInt(JsonElement entry, string name, string path, string? id)
    {
        if (!entry.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out int number) ||
            number <= 0)
        {
            throw new ModelCatalogException(path, id, $"Field '{name}' must be a positive integer.");
        }

        return number;
    }

    private static double? OptionalDouble(JsonElement entry, string name, string path, string? id)
    {
        if (!entry.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number))
        {
            throw new ModelCatalogException(path, id, $"Field '{name}' must be a number or null.");
        }

        return number;
    }

    private static decimal? OptionalDecimal(JsonElement entry, string name, string path, string? id)
    {
        if (!entry.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out decimal number))
        {
            throw new ModelCatalogException(path, id, $"Field '{name}' must be a number or null.");
        }

        return number;
    }

    private static ModelHostingType ParseHosting(string text, string path, string? id) =>
        text switch
        {
            "Local" => ModelHostingType.Local,
            "RemoteCloud" => ModelHostingType.RemoteCloud,
            _ => throw new ModelCatalogException(path, id, $"Unknown hostingType '{text}'."),
        };

    private static ModelTaskCategory ParseCategory(string text, string path, string? id) =>
        text switch
        {
            "LightFastTier2" => ModelTaskCategory.LightFastTier2,
            "HeavyReasoningTier3" => ModelTaskCategory.HeavyReasoningTier3,
            "EmbeddingTier1" => ModelTaskCategory.EmbeddingTier1,
            _ => throw new ModelCatalogException(path, id, $"Unknown recommendedFor '{text}'."),
        };
}
