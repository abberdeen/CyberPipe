using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Desktop.Services;

/// <summary>
/// Результат прямого тестового вызова ИИ модели.
/// </summary>
public sealed record ModelTestResponse
{
    public bool Success { get; init; }
    public string? ResponseText { get; init; }
    public string? ThinkingText { get; init; }
    public string? ErrorMessage { get; init; }
    public long LatencyMs { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public string? ModelUsed { get; init; }
}

/// <summary>
/// Сервис прямого интерактивного тестирования модели (Ollama / Cloud).
/// </summary>
public interface IModelTestService
{
    Task<ModelTestResponse> TestModelAsync(
        string modelId,
        ModelHostingType hostingType,
        string prompt,
        string? provider = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Реализация прямого тестирования моделей через Ollama API и каталоги рантайма.
/// </summary>
public sealed class ModelTestService : IModelTestService
{
    private readonly HttpClient _http;
    private readonly string _ollamaBaseUrl;
    private readonly IRemoteProviderService? _remoteProviders;

    public ModelTestService(
        HttpClient? httpClient = null,
        string? ollamaBaseUrl = null,
        IRemoteProviderService? remoteProviders = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _ollamaBaseUrl = string.IsNullOrWhiteSpace(ollamaBaseUrl)
            ? "http://localhost:11434"
            : ollamaBaseUrl.TrimEnd('/');
        _remoteProviders = remoteProviders;
    }

    public async Task<ModelTestResponse> TestModelAsync(
        string modelId,
        ModelHostingType hostingType,
        string prompt,
        string? provider = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new ModelTestResponse
            {
                Success = false,
                ErrorMessage = "Текст запроса не может быть пустым. Введите сообщение для модели.",
            };
        }

        string trimmedModelId = modelId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedModelId))
        {
            return new ModelTestResponse
            {
                Success = false,
                ErrorMessage = "Идентификатор модели не указан.",
            };
        }

        // Специальная обработка модели эмбеддингов
        if (string.Equals(provider, "ONNX", StringComparison.OrdinalIgnoreCase) ||
            trimmedModelId.Contains("multilingual-e5", StringComparison.OrdinalIgnoreCase) ||
            trimmedModelId.Contains("embedding", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelTestResponse
            {
                Success = true,
                ResponseText = $"Модель '{trimmedModelId}' является векторной моделью эмбеддингов (ONNX, 384 измерений). " +
                               "Она преобразует текст в числовые семантические векторы для классификации и семантического поиска, " +
                               "а не генерирует текстовые ответы чата. Модель полностью готова к работе в конвейере маршрутизации.",
                ModelUsed = trimmedModelId,
                LatencyMs = 0,
            };
        }

        // Специальная обработка детерминированных правил Tier 0
        if (trimmedModelId.Contains("regex", StringComparison.OrdinalIgnoreCase) ||
            trimmedModelId.Contains("rules", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelTestResponse
            {
                Success = true,
                ResponseText = $"Модель '{trimmedModelId}' представляет собой локальный детерминированный движок регулярных выражений (Tier 0). " +
                               "Она работает со временем отклика < 1 мс и не требует вызова нейросетевых рантаймов.",
                ModelUsed = trimmedModelId,
                LatencyMs = 0,
            };
        }

        var sw = Stopwatch.StartNew();

        try
        {
            if (hostingType == ModelHostingType.RemoteCloud)
            {
                return await TestRemoteCloudAsync(trimmedModelId, prompt, provider, sw, cancellationToken).ConfigureAwait(false);
            }

            string runtimeModelId = await ResolveInstalledTagAsync(trimmedModelId, cancellationToken).ConfigureAwait(false);

            using var response = await _http.PostAsync(
                $"{_ollamaBaseUrl}/api/chat",
                JsonContent.Create(new
                {
                    model = runtimeModelId,
                    stream = false,
                    messages = new object[]
                    {
                        new { role = "user", content = prompt }
                    }
                }),
                cancellationToken).ConfigureAwait(false);

            sw.Stop();

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new ModelTestResponse
                {
                    Success = false,
                    ErrorMessage = $"Модель '{trimmedModelId}' не найдена в рантайме Ollama. " +
                                   "Установите модель кнопкой «Установить» в списке локальных моделей перед тестированием.",
                    LatencyMs = sw.ElapsedMilliseconds,
                };
            }

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string errorDetail = TryExtractErrorMessage(responseJson) ?? $"HTTP {(int)response.StatusCode}";
                return new ModelTestResponse
                {
                    Success = false,
                    ErrorMessage = $"Ошибка вызова модели: {errorDetail}",
                    LatencyMs = sw.ElapsedMilliseconds,
                };
            }

            using var doc = JsonDocument.Parse(responseJson);
            JsonElement root = doc.RootElement;

            string content = string.Empty;
            string? thinking = null;

            if (root.TryGetProperty("message", out JsonElement messageElement) && messageElement.ValueKind == JsonValueKind.Object)
            {
                if (messageElement.TryGetProperty("content", out JsonElement contentElement) && contentElement.ValueKind == JsonValueKind.String)
                {
                    content = contentElement.GetString() ?? string.Empty;
                }

                if (messageElement.TryGetProperty("thinking", out JsonElement thinkingElement) && thinkingElement.ValueKind == JsonValueKind.String)
                {
                    thinking = thinkingElement.GetString();
                }
            }

            int? inputTokens = null;
            if (root.TryGetProperty("prompt_eval_count", out JsonElement promptEval) && promptEval.ValueKind == JsonValueKind.Number && promptEval.TryGetInt32(out int inTok))
            {
                inputTokens = inTok;
            }

            int? outputTokens = null;
            if (root.TryGetProperty("eval_count", out JsonElement evalCount) && evalCount.ValueKind == JsonValueKind.Number && evalCount.TryGetInt32(out int outTok))
            {
                outputTokens = outTok;
            }

            string? modelUsed = null;
            if (root.TryGetProperty("model", out JsonElement modelElem) && modelElem.ValueKind == JsonValueKind.String)
            {
                modelUsed = modelElem.GetString();
            }

            long latencyMs = sw.ElapsedMilliseconds;
            if (root.TryGetProperty("total_duration", out JsonElement durElem) && durElem.ValueKind == JsonValueKind.Number && durElem.TryGetInt64(out long totalDurNs) && totalDurNs > 0)
            {
                latencyMs = totalDurNs / 1_000_000L;
            }

            // Если content пуст, но есть reasoning/thinking
            if (string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(thinking))
            {
                content = thinking;
            }

            return new ModelTestResponse
            {
                Success = true,
                ResponseText = content,
                ThinkingText = thinking,
                LatencyMs = latencyMs,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ModelUsed = modelUsed ?? runtimeModelId,
            };
        }
        catch (HttpRequestException)
        {
            sw.Stop();
            return new ModelTestResponse
            {
                Success = false,
                ErrorMessage = $"Не удалось подключиться к Ollama по адресу {_ollamaBaseUrl}. " +
                               "Убедитесь, что служба Ollama запущена на компьютере.",
                LatencyMs = sw.ElapsedMilliseconds,
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ModelTestResponse
            {
                Success = false,
                ErrorMessage = "Время ожидания ответа от модели истекло (таймаут) или запрос был отменён пользователем.",
                LatencyMs = sw.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ModelTestResponse
            {
                Success = false,
                ErrorMessage = $"Ошибка при тестировании модели: {ex.Message}",
                LatencyMs = sw.ElapsedMilliseconds,
            };
        }
    }

    private async Task<ModelTestResponse> TestRemoteCloudAsync(
        string modelId,
        string prompt,
        string? provider,
        Stopwatch watch,
        CancellationToken cancellationToken)
    {
        if (_remoteProviders is null)
        {
            watch.Stop();
            return new ModelTestResponse
            {
                Success = false,
                ErrorMessage = $"Модель '{modelId}' является удалённой облачной моделью ({provider ?? "Cloud"}). " +
                               "Откройте «Управление облачными провайдерами» и укажите ключ доступа.",
                LatencyMs = watch.ElapsedMilliseconds,
            };
        }

        IReadOnlyList<RemoteProviderProfileDto> providers = _remoteProviders.LoadProviders();
        RemoteProviderProfileDto? profile = null;
        if (!string.IsNullOrWhiteSpace(provider))
        {
            profile = providers.FirstOrDefault(item =>
                string.Equals(item.Id, provider, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, provider, StringComparison.OrdinalIgnoreCase));
        }

        profile ??= providers.FirstOrDefault(item =>
            item.IsEnabled && item.Models.Any(model => string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase)));
        profile ??= providers.FirstOrDefault(item =>
            item.Models.Any(model => string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase)));

        if (profile is null)
        {
            watch.Stop();
            return new ModelTestResponse
            {
                Success = false,
                ErrorMessage = $"Не найден облачный провайдер для модели '{modelId}'. Откройте «Управление облачными провайдерами» и добавьте провайдера.",
                LatencyMs = watch.ElapsedMilliseconds,
            };
        }

        SecretValue? secret = await _remoteProviders.GetApiKeyAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        string? apiKey = secret?.Reveal();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            watch.Stop();
            return new ModelTestResponse
            {
                Success = false,
                ErrorMessage = $"Для модели '{modelId}' ({profile.Name}) не настроен API-ключ. Откройте «Управление облачными провайдерами» и укажите ключ доступа.",
                LatencyMs = watch.ElapsedMilliseconds,
            };
        }

        try
        {
            using HttpRequestMessage request = CreateRemoteRequest(profile, modelId, prompt, apiKey);
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            watch.Stop();
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                return new ModelTestResponse
                {
                    Success = false,
                    ErrorMessage = "Ошибка авторизации (HTTP 401/403): проверьте правильность API-ключа.",
                    LatencyMs = watch.ElapsedMilliseconds,
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ModelTestResponse
                {
                    Success = false,
                    ErrorMessage = $"Ошибка вызова модели: {TryExtractErrorMessage(body) ?? $"HTTP {(int)response.StatusCode}"}",
                    LatencyMs = watch.ElapsedMilliseconds,
                };
            }

            return ParseRemoteResponse(profile.Protocol, body, modelId, watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            watch.Stop();
            return new ModelTestResponse
            {
                Success = false,
                ErrorMessage = $"Не удалось вызвать {profile.Name}: {ex.Message}",
                LatencyMs = watch.ElapsedMilliseconds,
            };
        }
    }

    private static HttpRequestMessage CreateRemoteRequest(
        RemoteProviderProfileDto profile,
        string modelId,
        string prompt,
        string apiKey)
    {
        string baseUrl = profile.BaseUrl.TrimEnd('/');
        HttpRequestMessage request;
        if (profile.Protocol == RemoteProviderProtocol.Anthropic)
        {
            request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/messages")
            {
                Content = JsonContent.Create(new
                {
                    model = modelId,
                    max_tokens = 512,
                    messages = new[] { new { role = "user", content = prompt } },
                }),
            };
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            return request;
        }

        if (profile.Protocol == RemoteProviderProtocol.GoogleGemini)
        {
            string url = $"{baseUrl}/models/{Uri.EscapeDataString(modelId)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            return new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new
                {
                    contents = new[] { new { parts = new[] { new { text = prompt } } } },
                }),
            };
        }

        request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = modelId,
                max_tokens = 512,
                messages = new[] { new { role = "user", content = prompt } },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private static ModelTestResponse ParseRemoteResponse(
        RemoteProviderProtocol protocol,
        string json,
        string modelId,
        long latencyMs)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        string text = protocol switch
        {
            RemoteProviderProtocol.Anthropic => ReadAnthropicText(root),
            RemoteProviderProtocol.GoogleGemini => ReadGeminiText(root),
            _ => ReadOpenAiText(root),
        };
        (int? input, int? output) = protocol switch
        {
            RemoteProviderProtocol.GoogleGemini => ReadGeminiUsage(root),
            _ => ReadChatUsage(root),
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return new ModelTestResponse
            {
                Success = false,
                ErrorMessage = TryExtractErrorMessage(json) ?? "Провайдер вернул ответ без текста.",
                LatencyMs = latencyMs,
                ModelUsed = modelId,
            };
        }

        return new ModelTestResponse
        {
            Success = true,
            ResponseText = text,
            LatencyMs = latencyMs,
            InputTokens = input,
            OutputTokens = output,
            ModelUsed = modelId,
        };
    }

    private static string ReadOpenAiText(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out JsonElement choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (JsonElement choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("message", out JsonElement message) &&
                message.TryGetProperty("content", out JsonElement content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string ReadAnthropicText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out JsonElement content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (JsonElement block in content.EnumerateArray())
        {
            if (block.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
            {
                string? value = text.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value);
                }
            }
        }

        return string.Join(string.Empty, parts);
    }

    private static string ReadGeminiText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out JsonElement candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (JsonElement candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out JsonElement content) ||
                !content.TryGetProperty("parts", out JsonElement contentParts) ||
                contentParts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement part in contentParts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                {
                    string? value = text.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        parts.Add(value);
                    }
                }
            }
        }

        return string.Join(string.Empty, parts);
    }

    private static (int? Input, int? Output) ReadChatUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        return (ReadInt(usage, "prompt_tokens") ?? ReadInt(usage, "input_tokens"),
            ReadInt(usage, "completion_tokens") ?? ReadInt(usage, "output_tokens"));
    }

    private static (int? Input, int? Output) ReadGeminiUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        return (ReadInt(usage, "promptTokenCount"), ReadInt(usage, "candidatesTokenCount"));
    }

    private static int? ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int number)
            ? number
            : null;

    /// <summary>
    /// Каталог хранит id вроде <c>qwen3-0.6b</c>, а <c>ollama list</c> показывает тег <c>qwen3:0.6b</c>.
    /// Если тег уже установлен, вызываем его, а не каталожный id.
    /// </summary>
    private async Task<string> ResolveInstalledTagAsync(string requested, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync($"{_ollamaBaseUrl}/api/tags", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return requested;
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("models", out JsonElement models) || models.ValueKind != JsonValueKind.Array)
            {
                return requested;
            }

            foreach (JsonElement model in models.EnumerateArray())
            {
                if (model.TryGetProperty("name", out JsonElement nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String &&
                    nameElement.GetString() is string installed &&
                    SameOllamaModel(installed, requested))
                {
                    return installed;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
        }

        return requested;
    }

    private static bool SameOllamaModel(string installed, string requested) =>
        string.Equals(CanonicalOllamaName(installed), CanonicalOllamaName(requested), StringComparison.OrdinalIgnoreCase);

    private static string CanonicalOllamaName(string tag)
    {
        string value = tag.Trim();
        const string latest = ":latest";
        if (value.EndsWith(latest, StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^latest.Length];
        }

        return value.Replace(':', '-');
    }

    private static string? TryExtractErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out JsonElement err) && err.ValueKind == JsonValueKind.String)
            {
                return err.GetString();
            }
        }
        catch
        {
            // Ignore parse errors
        }
        return null;
    }
}
