using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CyberPipe.Contracts;
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

    /// <summary>Создаёт провайдер поверх каталога и настроек рантайма.</summary>
    public OllamaChatModelProvider(
        HttpClient httpClient,
        IModelCatalog catalog,
        IOptions<LocalModelInstallerOptions>? options = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _installerOptions = options?.Value ?? new LocalModelInstallerOptions();
    }

    /// <inheritdoc />
    public async Task<ChatModelResponse> CompleteAsync(ChatModelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        (string tag, int numCtx) = ResolveRuntimeModel(request);

        using HttpResponseMessage response = await _http.PostAsync(
            $"{_installerOptions.OllamaApiBaseUrl.TrimEnd('/')}/api/chat",
            JsonContent.Create(new
            {
                model = tag,
                stream = false,
                messages = new object[]
                {
                    new { role = "system", content = request.SystemPrompt ?? string.Empty },
                    new { role = "user", content = request.Prompt },
                },
                options = new { num_ctx = numCtx },
            }),
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Ollama model '{tag}' is not installed.");
        }

        response.EnsureSuccessStatusCode();
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

    private (string Tag, int NumCtx) ResolveRuntimeModel(ChatModelRequest request)
    {
        ModelTaskCategory category = request.ModelClass == ChatModelClass.Heavy
            ? ModelTaskCategory.HeavyReasoningTier3
            : ModelTaskCategory.LightFastTier2;
        ModelDescriptorDto? first = _catalog.Find(category, ModelHostingType.Local).FirstOrDefault();
        if (first is null)
        {
            throw new InvalidOperationException($"No local model in catalog for category '{category}'.");
        }

        return (first.ModelId, first.DefaultContextTokens);
    }
}
