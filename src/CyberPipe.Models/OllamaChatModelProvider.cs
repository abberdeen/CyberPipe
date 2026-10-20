using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberPipe.Models;

/// <summary>
/// Провайдер чат-моделей Tier 2/3 поверх локального рантайма Ollama (TASK-032, TASK-081, TASK-090).
/// Модель выбирается по классу запроса из назначений <c>Router.Tier2ModelId/Tier3ModelId</c>
/// с проекцией через каталог TASK-080; расход токенов — из ответа рантайма.
/// </summary>
public sealed class OllamaChatModelProvider : IChatModelProvider
{
    private readonly HttpClient _http;
    private readonly IModelCatalog _catalog;
    private readonly LocalModelInstallerOptions _installerOptions;
    private readonly CyberPipeOptions? _cyberPipeOptions;
    private readonly ILogger<OllamaChatModelProvider>? _logger;

    /// <summary>Создаёт провайдер поверх каталога и настроек рантайма.</summary>
    public OllamaChatModelProvider(
        HttpClient httpClient,
        IModelCatalog catalog,
        IOptions<LocalModelInstallerOptions>? options = null,
        CyberPipeOptions? cyberPipeOptions = null,
        ILogger<OllamaChatModelProvider>? logger = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _installerOptions = options?.Value ?? new LocalModelInstallerOptions();
        _cyberPipeOptions = cyberPipeOptions;
        _logger = logger;
    }

    /// <summary>Создаёт провайдер для DI с IOptions&lt;CyberPipeOptions&gt;.</summary>
    public OllamaChatModelProvider(
        HttpClient httpClient,
        IModelCatalog catalog,
        IOptions<LocalModelInstallerOptions>? options,
        IOptions<CyberPipeOptions>? cyberPipeOptions,
        ILogger<OllamaChatModelProvider>? logger = null)
        : this(httpClient, catalog, options, cyberPipeOptions?.Value, logger)
    {
    }

    /// <inheritdoc />
    public async Task<ChatModelResponse> CompleteAsync(ChatModelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        (string tag, int numCtx) = await ResolveRuntimeModelAsync(request, cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response = await SendChatRequestAsync(tag, numCtx, request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            // Запрошенная модель не найдена в Ollama (не установлена).
            // Проверяем список установленных в Ollama моделей и выполняем fallback.
            IReadOnlyList<string> installed = await GetInstalledOllamaModelsAsync(cancellationToken).ConfigureAwait(false);
            string? fallbackTag = PickFallbackModel(installed, request.ModelClass, tag);

            var candidatesToTry = new List<string>();
            if (fallbackTag is not null && !MatchesModelTag(fallbackTag, tag))
            {
                candidatesToTry.Add(fallbackTag);
            }
            foreach (string inst in installed)
            {
                if (!MatchesModelTag(inst, tag) && !candidatesToTry.Any(c => MatchesModelTag(c, inst)))
                {
                    candidatesToTry.Add(inst);
                }
            }

            bool fallbackSuccess = false;
            foreach (string candidateTag in candidatesToTry)
            {
                _logger?.LogWarning(
                    "Ollama model '{RequestedTag}' is not installed. Trying installed candidate '{CandidateTag}'.",
                    tag, candidateTag);

                int fallbackCtx = ResolveNumCtxForTag(candidateTag);
                response = await SendChatRequestAsync(candidateTag, fallbackCtx, request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    tag = candidateTag;
                    fallbackSuccess = true;
                    break;
                }
                response.Dispose();
            }

            if (!fallbackSuccess)
            {
                string installedList = installed.Count > 0
                    ? $" Установленные модели в Ollama: {string.Join(", ", installed)}."
                    : " В Ollama не установлено ни одной модели.";
                throw new InvalidOperationException($"Ollama model '{tag}' is not installed.{installedList}");
            }
        }

        response.EnsureSuccessStatusCode();
        using (response)
        {
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            JsonElement root = doc.RootElement;

            string text = root.TryGetProperty("message", out JsonElement message) &&
                message.ValueKind == JsonValueKind.Object &&
                message.TryGetProperty("content", out JsonElement content) &&
                content.ValueKind == JsonValueKind.String
                ? content.GetString() ?? string.Empty
                : string.Empty;
            int inputTokens = root.TryGetProperty("prompt_eval_count", out JsonElement input) &&
                input.ValueKind == JsonValueKind.Number && input.TryGetInt32(out int inTokens)
                ? inTokens
                : 0;
            int outputTokens = root.TryGetProperty("eval_count", out JsonElement output) &&
                output.ValueKind == JsonValueKind.Number && output.TryGetInt32(out int outTokens)
                ? outTokens
                : 0;

            return new ChatModelResponse
            {
                Text = text,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ModelId = tag,
            };
        }
    }

    private Task<HttpResponseMessage> SendChatRequestAsync(
        string tag,
        int numCtx,
        ChatModelRequest request,
        CancellationToken cancellationToken)
    {
        return _http.PostAsync(
            $"{_installerOptions.OllamaApiBaseUrl.TrimEnd('/')}/api/chat",
            JsonContent.Create(new
            {
                model = tag,
                stream = false,
                think = false,
                messages = new object[]
                {
                    new { role = "system", content = request.SystemPrompt ?? string.Empty },
                    new { role = "user", content = request.Prompt },
                },
                options = new { num_ctx = numCtx },
            }),
            cancellationToken);
    }

    private async Task<(string Tag, int NumCtx)> ResolveRuntimeModelAsync(ChatModelRequest request, CancellationToken cancellationToken)
    {
        // 1. Приоритет: ModelId из запроса (если передан и не является плейсхолдером)
        string? candidate = !string.IsNullOrWhiteSpace(request.ModelId) && !IsPlaceholder(request.ModelId)
            ? request.ModelId.Trim()
            : null;

        // 2. Вторичный приоритет: Tier2ModelId / Tier3ModelId из опций CyberPipe
        if (candidate is null && _cyberPipeOptions?.Router is not null)
        {
            string? configured = request.ModelClass == ChatModelClass.Heavy
                ? _cyberPipeOptions.Router.Tier3ModelId
                : _cyberPipeOptions.Router.Tier2ModelId;

            if (!string.IsNullOrWhiteSpace(configured) && !IsPlaceholder(configured))
            {
                candidate = configured.Trim();
            }
        }

        // 3. Если кандидат задан — ищем в каталоге (по Id, ModelId или Name)
        if (candidate is not null)
        {
            ModelDescriptorDto? descriptor = FindInCatalog(candidate);
            if (descriptor is not null)
            {
                return (descriptor.ModelId, descriptor.DefaultContextTokens > 0 ? descriptor.DefaultContextTokens : 8192);
            }

            // Если в каталоге нет, но пользователь явно указал модель (например, тег Ollama напрямую)
            return (candidate, 8192);
        }

        // 4. Кандидат не задан: выбираем модель из каталога под соответствующую категорию
        ModelTaskCategory category = request.ModelClass == ChatModelClass.Heavy
            ? ModelTaskCategory.HeavyReasoningTier3
            : ModelTaskCategory.LightFastTier2;

        IReadOnlyList<ModelDescriptorDto> candidates = _catalog.Find(category, ModelHostingType.Local);

        // Проверяем, установлена ли какая-то из моделей каталога в Ollama
        IReadOnlyList<string> installed = await GetInstalledOllamaModelsAsync(cancellationToken).ConfigureAwait(false);
        foreach (ModelDescriptorDto model in candidates)
        {
            if (installed.Any(inst => MatchesModelTag(inst, model.ModelId)))
            {
                return (model.ModelId, model.DefaultContextTokens > 0 ? model.DefaultContextTokens : 8192);
            }
        }

        // Если ни одна из моделей категории не установлена, но в Ollama есть другие модели
        if (installed.Count > 0)
        {
            string? fallback = PickFallbackModel(installed, request.ModelClass, excludedTag: string.Empty);
            if (fallback is not null)
            {
                return (fallback, ResolveNumCtxForTag(fallback));
            }
        }

        // Если ничего не установлено или рантайм недоступен — берём первую модель из каталога
        ModelDescriptorDto? first = candidates.FirstOrDefault();
        if (first is null)
        {
            throw new InvalidOperationException($"No local model in catalog for category '{category}'.");
        }

        return (first.ModelId, first.DefaultContextTokens > 0 ? first.DefaultContextTokens : 8192);
    }

    private ModelDescriptorDto? FindInCatalog(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        ModelDescriptorDto? byId = _catalog.GetById(candidate);
        if (byId is not null)
            return byId;

        foreach (ModelDescriptorDto model in _catalog.LocalModels)
        {
            if (string.Equals(model.Id, candidate, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(model.ModelId, candidate, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(model.Name, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return model;
            }
        }

        foreach (ModelDescriptorDto model in _catalog.CloudModels)
        {
            if (string.Equals(model.Id, candidate, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(model.ModelId, candidate, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(model.Name, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return model;
            }
        }

        return null;
    }

    private int ResolveNumCtxForTag(string tag)
    {
        ModelDescriptorDto? desc = FindInCatalog(tag);
        return desc?.DefaultContextTokens > 0 ? desc.DefaultContextTokens : 8192;
    }

    private string? PickFallbackModel(IReadOnlyList<string> installed, ChatModelClass modelClass, string excludedTag)
    {
        if (installed.Count == 0)
            return null;

        ModelTaskCategory category = modelClass == ChatModelClass.Heavy
            ? ModelTaskCategory.HeavyReasoningTier3
            : ModelTaskCategory.LightFastTier2;

        // 1. Модель из каталога для той же категории
        foreach (ModelDescriptorDto m in _catalog.Find(category, ModelHostingType.Local))
        {
            string? match = installed.FirstOrDefault(i => MatchesModelTag(i, m.ModelId) && !MatchesModelTag(i, excludedTag));
            if (match is not null)
                return match;
        }

        // 2. Любая модель из локального каталога
        foreach (ModelDescriptorDto m in _catalog.LocalModels)
        {
            string? match = installed.FirstOrDefault(i => MatchesModelTag(i, m.ModelId) && !MatchesModelTag(i, excludedTag));
            if (match is not null)
                return match;
        }

        // 3. Любая установленная модель, отличная от исключённой
        return installed.FirstOrDefault(i => !MatchesModelTag(i, excludedTag));
    }

    private static bool MatchesModelTag(string installedName, string targetTag)
    {
        if (string.Equals(installedName, targetTag, StringComparison.OrdinalIgnoreCase))
            return true;

        string normInstalled = NormalizeTag(installedName);
        string normTarget = NormalizeTag(targetTag);
        return string.Equals(normInstalled, normTarget, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTag(string tag)
    {
        int colonIdx = tag.IndexOf(':');
        if (colonIdx >= 0)
        {
            string suffix = tag[(colonIdx + 1)..];
            if (string.Equals(suffix, "latest", StringComparison.OrdinalIgnoreCase))
            {
                return tag[..colonIdx];
            }
        }
        return tag;
    }

    private static bool IsPlaceholder(string? id) =>
        string.IsNullOrWhiteSpace(id) ||
        string.Equals(id, "tier2-light", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id, "tier3-heavy", StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<string>> GetInstalledOllamaModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(
                $"{_installerOptions.OllamaApiBaseUrl.TrimEnd('/')}/api/tags",
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return [];

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var list = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out JsonElement models) && models.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement m in models.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out JsonElement n) &&
                        n.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(n.GetString()))
                    {
                        list.Add(n.GetString()!);
                    }
                }
            }
            return list;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to query installed Ollama models from /api/tags.");
            return [];
        }
    }
}
