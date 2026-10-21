using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Data;

namespace CyberPipe.Chains.Triggers;

/// <summary>
/// Обработчик входящих webhook-запросов (TASK-041).
/// Выполняет проверку секретов, лимита размера, дедупликацию и запуск цепочки.
/// </summary>
public sealed class WebhookTriggerHandler
{
    private readonly ChainCatalogService _repository;
    private readonly IChainExecutor _executor;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly ISecretStore? _secretStore;
    private readonly long _maxBodySizeBytes;
    private readonly TimeSpan _idempotencyWindow;

    /// <summary>
    /// Инициализирует обработчик вебхуков.
    /// </summary>
    /// <param name="repository">Репозиторий цепочек.</param>
    /// <param name="executor">Исполнитель цепочек.</param>
    /// <param name="idempotencyStore">Хранилище идемпотентности.</param>
    /// <param name="secretStore">Хранилище секретов (опционально).</param>
    /// <param name="maxBodySizeBytes">Максимальный размер тела в байтах (по умолчанию 1 МБ).</param>
    /// <param name="idempotencyWindow">Окно дедупликации (по умолчанию 24 часа).</param>
    public WebhookTriggerHandler(
        ChainCatalogService repository,
        IChainExecutor executor,
        IIdempotencyStore idempotencyStore,
        ISecretStore? secretStore = null,
        long maxBodySizeBytes = 1024 * 1024,
        TimeSpan? idempotencyWindow = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        _secretStore = secretStore;
        _maxBodySizeBytes = maxBodySizeBytes;
        _idempotencyWindow = idempotencyWindow ?? TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Обрабатывает входящий запрос вебхука.
    /// </summary>
    /// <param name="request">Запрос вебхука.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ответ вебхука со статус-кодом и RunId.</returns>
    public async Task<WebhookResponse> HandleWebhookAsync(WebhookRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Проверка размера тела (по умолчанию 1 МБ) -> 413
        string body = request.Body ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(body) > _maxBodySizeBytes)
        {
            return new WebhookResponse
            {
                StatusCode = 413,
                Message = $"Payload too large. Maximum allowed size is {_maxBodySizeBytes} bytes.",
            };
        }

        // 2. Проверка JSON-тела -> 400
        try
        {
            using var doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return new WebhookResponse
            {
                StatusCode = 400,
                Message = "Malformed JSON payload.",
            };
        }

        // 3. Загрузка цепочки
        ChainDefinitionDto? chain = await _repository.GetByIdAsync(request.ChainId, cancellationToken).ConfigureAwait(false);
        if (chain is null)
        {
            return new WebhookResponse
            {
                StatusCode = 404,
                Message = $"Chain '{request.ChainId}' not found.",
            };
        }

        // 4. Проверка активности цепочки -> 409
        if (!chain.IsEnabled)
        {
            return new WebhookResponse
            {
                StatusCode = 409,
                Message = $"Chain '{request.ChainId}' is disabled.",
            };
        }

        // 5. Проверка вида триггера -> 400
        if (chain.Trigger.Kind != ChainTriggerKind.Webhook)
        {
            return new WebhookResponse
            {
                StatusCode = 400,
                Message = $"Chain '{request.ChainId}' does not use a Webhook trigger.",
            };
        }

        // 6. Проверка секрета (при наличии ссылки на секрет в параметрах) -> 401
        if (_secretStore is not null)
        {
            string? secretKey = null;
            if (chain.Trigger.Parameters.TryGetValue("secretRef", out string? secretRef))
            {
                secretKey = secretRef;
            }
            else if (chain.Trigger.Parameters.TryGetValue("secretKey", out string? sKey))
            {
                secretKey = sKey;
            }

            if (!string.IsNullOrWhiteSpace(secretKey))
            {
                SecretValue? expectedSecret = await _secretStore.GetAsync(secretKey, cancellationToken).ConfigureAwait(false);
                if (expectedSecret is not null)
                {
                    string headerName = "X-Webhook-Secret";
                    if (chain.Trigger.Parameters.TryGetValue("secretHeader", out string? customHeader) && !string.IsNullOrWhiteSpace(customHeader))
                    {
                        headerName = customHeader;
                    }

                    string? actualSecret = null;
                    foreach (var (k, v) in request.Headers)
                    {
                        if (k.Equals(headerName, StringComparison.OrdinalIgnoreCase))
                        {
                            actualSecret = v;
                            break;
                        }
                    }

                    if (actualSecret is null || !string.Equals(actualSecret, expectedSecret.Reveal(), StringComparison.Ordinal))
                    {
                        return new WebhookResponse
                        {
                            StatusCode = 401,
                            Message = "Invalid or missing webhook secret.",
                        };
                    }
                }
            }
        }

        // 7. Дедупликация (IdempotencyKey)
        string? idempotencyKey = null;
        foreach (var (k, v) in request.Headers)
        {
            if (k.Equals("Idempotency-Key", StringComparison.OrdinalIgnoreCase)
                || k.Equals("X-Idempotency-Key", StringComparison.OrdinalIgnoreCase))
            {
                idempotencyKey = v;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Path + ":" + body));
            idempotencyKey = "sha256:" + Convert.ToHexString(hash);
        }

        bool isNew = await _idempotencyStore.TryRegisterAsync(idempotencyKey, _idempotencyWindow, cancellationToken).ConfigureAwait(false);
        if (!isNew)
        {
            return new WebhookResponse
            {
                StatusCode = 200,
                Message = "Duplicate webhook ignored.",
            };
        }

        // 8. Запуск исполнителя цепочки
        var triggerEvent = new TriggerEvent
        {
            EventId = Guid.NewGuid(),
            ChainId = request.ChainId,
            Kind = ChainTriggerKind.Webhook,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            PayloadJson = body,
            IdempotencyKey = idempotencyKey,
            Source = request.Path,
        };

        RunId runId = await _executor.StartAsync(triggerEvent, cancellationToken).ConfigureAwait(false);

        return new WebhookResponse
        {
            StatusCode = 200,
            RunId = runId,
            Message = "Triggered successfully.",
        };
    }
}
