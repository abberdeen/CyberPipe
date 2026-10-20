using System.Diagnostics;
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

    public ModelTestService(HttpClient? httpClient = null, string? ollamaBaseUrl = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _ollamaBaseUrl = string.IsNullOrWhiteSpace(ollamaBaseUrl)
            ? "http://localhost:11434"
            : ollamaBaseUrl.TrimEnd('/');
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
            string runtimeModelId = hostingType == ModelHostingType.RemoteCloud
                ? trimmedModelId
                : await ResolveInstalledTagAsync(trimmedModelId, cancellationToken).ConfigureAwait(false);

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
                if (hostingType == ModelHostingType.RemoteCloud)
                {
                    return new ModelTestResponse
                    {
                        Success = false,
                        ErrorMessage = $"Модель '{trimmedModelId}' является удалённой облачной моделью ({provider ?? "Cloud"}). " +
                                       "Для прямого тестирования требуется настроенный облачный ключ доступа или шлюз. " +
                                       "Для локального тестирования выберите модель со значком Ollama.",
                        LatencyMs = sw.ElapsedMilliseconds,
                    };
                }

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
        catch (HttpRequestException ex)
        {
            sw.Stop();
            if (hostingType == ModelHostingType.RemoteCloud)
            {
                return new ModelTestResponse
                {
                    Success = false,
                    ErrorMessage = $"Модель '{trimmedModelId}' является удалённой облачной моделью ({provider ?? "Cloud"}). " +
                                   $"Локальный шлюз недоступен ({ex.Message}). Для прямого вызова требуется настроенный API-ключ.",
                    LatencyMs = sw.ElapsedMilliseconds,
                };
            }

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
