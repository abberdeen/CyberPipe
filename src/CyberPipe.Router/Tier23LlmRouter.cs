using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Router;

/// <summary>
/// Маршрутизатор намерений ярусов Tier 2 и Tier 3 на базе LLM (TASK-032).
/// Запросы сначала классифицируются лёгкой моделью (Tier 2); при низкой уверенности или невалидном ответе
/// происходит эскалация на тяжёлую модель (Tier 3).
/// Каждый вызов обязательно авторизуется в TokenBudgetGovernor (TASK-033).
/// </summary>
public sealed class Tier23LlmRouter : IRouterTier
{
    private const string DefaultSystemPrompt = """
        You are CyberPipe's Intent Classifier. Reply with JSON only:
        {"intent":"...","confidence":0.0,"parameters":{},"reason":"..."}

        Use a host command only when the user is clearly asking to perform it:
        system.help, system.status, system.cancel, diagnostics.run, knowledge.search,
        volume.get, volume.up, volume.down, volume.mute, volume.unmute, media.volume,
        media.play, media.pause, timer.set.

        Questions, facts, translations, math, and conversation use intent "chat.reply".
        Never use system.*, volume.*, or media.* for those.
        """;

    private const string AnswerSystemPrompt = """
        You are CyberPipe, a local assistant. Answer the user directly in the same language.
        Do not output JSON, intent names, confidence scores, or routing notes.
        """;

    private readonly IChatModelProvider _chatModelProvider;
    private readonly ITokenBudgetGovernor _budgetGovernor;
    private readonly CyberPipeOptions _options;
    private readonly ISemanticCache? _semanticCache;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly IEgressSanitizer? _egressSanitizer;
    private readonly ILogger<Tier23LlmRouter>? _logger;

    public Tier23LlmRouter(
        IChatModelProvider chatModelProvider,
        ITokenBudgetGovernor budgetGovernor,
        CyberPipeOptions? options = null,
        ISemanticCache? semanticCache = null,
        IEmbeddingProvider? embeddingProvider = null,
        ILogger<Tier23LlmRouter>? logger = null,
        IEgressSanitizer? egressSanitizer = null)
    {
        _chatModelProvider = chatModelProvider ?? throw new ArgumentNullException(nameof(chatModelProvider));
        _budgetGovernor = budgetGovernor ?? throw new ArgumentNullException(nameof(budgetGovernor));
        _options = options ?? new CyberPipeOptions();
        _semanticCache = semanticCache;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
        _egressSanitizer = egressSanitizer;
    }

    public IntentTier Tier => IntentTier.Tier2;

    public async Task<RouteResult> TryRouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.Router.Tier2Enabled && !_options.Router.Tier3Enabled)
        {
            _logger?.LogDebug("Both Tier 2 and Tier 3 LLM routing are disabled.");
            return new RouteResult
            {
                Status = RouteStatus.Escalate,
                Tier = IntentTier.Tier2,
                Reason = "Tier 2 and Tier 3 routers are disabled in configuration."
            };
        }

        string requestId = request.Metadata.TryGetValue("requestId", out string? rid) && !string.IsNullOrWhiteSpace(rid)
            ? rid
            : Guid.NewGuid().ToString("N");

        // --- Phase 1: Tier 2 (Light LLM) ---
        if (_options.Router.Tier2Enabled)
        {
            RouteResult tier2Result = await ExecuteTier2Async(request, requestId, cancellationToken);

            if (tier2Result.Status == RouteStatus.Matched || tier2Result.Status == RouteStatus.BudgetDenied)
            {
                // Decisive result: matched or budget denied (budget denials are terminal)
                return tier2Result;
            }

            _logger?.LogInformation(
                "Tier 2 light model did not produce a decisive match (Status: {Status}, Reason: {Reason}). Escalating to Tier 3...",
                tier2Result.Status,
                tier2Result.Reason);
        }

        // --- Phase 2: Tier 3 (Heavy LLM) ---
        if (_options.Router.Tier3Enabled)
        {
            return await ExecuteTier3Async(request, requestId, cancellationToken);
        }

        return new RouteResult
        {
            Status = RouteStatus.Escalate,
            Tier = IntentTier.Tier2,
            Reason = "Tier 2 confidence insufficient and Tier 3 router is disabled."
        };
    }

    private async Task<RouteResult> ExecuteTier2Async(
        RouteRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        string modelId = _options.Router.Tier2ModelId ?? "tier2-light";
        ModelHostingType hosting = _options.Router.Tier2HostingType ?? DetermineHostingType(modelId, ModelHostingType.Local);

        int estimatedInput = 200 + (request.Text.Length / 3);
        int estimatedOutput = 100;

        // 1. Authorize in Budget Governor
        var budgetRequest = new BudgetRequest
        {
            ModelId = modelId,
            HostingType = hosting,
            EstimatedInputTokens = estimatedInput,
            EstimatedOutputTokens = estimatedOutput,
            RequestId = requestId
        };

        BudgetDecision auth = await _budgetGovernor.TryAuthorizeAsync(budgetRequest, cancellationToken);
        if (!auth.IsAllowed)
        {
            _logger?.LogWarning("Token budget denied for Tier 2 model '{ModelId}': {Reason} - {Message}",
                modelId, auth.DenialReason, auth.Message);

            return new RouteResult
            {
                Status = RouteStatus.BudgetDenied,
                Tier = IntentTier.Tier2,
                Reason = $"Token budget denied: {auth.DenialReason} - {auth.Message}"
            };
        }

        // 2. Call model with transient retry
        ChatModelResponse response;
        try
        {
            response = await CallWithRetriesAsync(
                new ChatModelRequest
                {
                    ModelClass = ChatModelClass.Light,
                    ModelId = modelId,
                    SystemPrompt = DefaultSystemPrompt,
                    Prompt = request.Text
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Tier 2 model call failed.");
            return new RouteResult
            {
                Status = RouteStatus.Failed,
                Tier = IntentTier.Tier2,
                Reason = $"Tier 2 model failure: {ex.Message}"
            };
        }

        // 3. Record actual usage
        await RecordUsageAsync(modelId, hosting, response, requestId, cancellationToken);

        // 4. Parse structured JSON
        LlmClassificationJson? classification = TryParseJson(response.Text);
        if (classification is not null && IsChatIntent(classification.Intent))
        {
            return await AnswerAsync(
                request, requestId, modelId, hosting, ChatModelClass.Light, IntentTier.Tier2, cancellationToken);
        }

        if (classification is null)
        {
            _logger?.LogWarning("Tier 2 model returned invalid JSON response: '{Text}'", response.Text);
            return new RouteResult
            {
                Status = RouteStatus.Escalate,
                Tier = IntentTier.Tier2,
                Reason = "Tier 2 model returned non-JSON or invalid format."
            };
        }

        if (string.IsNullOrWhiteSpace(classification.Intent) ||
            classification.Confidence < _options.Router.Tier2ConfidenceThreshold)
        {
            _logger?.LogDebug(
                "Tier 2 model confidence {Confidence:F2} below threshold {Threshold:F2} for intent '{Intent}'",
                classification.Confidence,
                _options.Router.Tier2ConfidenceThreshold,
                classification.Intent);

            return new RouteResult
            {
                Status = RouteStatus.Escalate,
                Tier = IntentTier.Tier2,
                Reason = $"Tier 2 confidence {classification.Confidence:F2} below threshold {_options.Router.Tier2ConfidenceThreshold:F2}."
            };
        }

        var matchedResult = new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier2,
            Intent = classification.Intent,
            Parameters = classification.Parameters,
            Confidence = classification.Confidence,
            MatchedRuleName = $"Tier2:{classification.Intent}",
            Reason = classification.Reason ?? "Classified by Tier 2 light model"
        };

        await TryCacheResultAsync(request.Text, matchedResult, cancellationToken);

        return matchedResult;
    }

    private async Task<RouteResult> ExecuteTier3Async(
        RouteRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        string modelId = _options.Router.Tier3ModelId ?? "tier3-heavy";
        ModelHostingType hosting = _options.Router.Tier3HostingType ?? DetermineHostingType(modelId, ModelHostingType.RemoteCloud);

        int estimatedInput = 300 + (request.Text.Length / 3);
        int estimatedOutput = 150;

        // 1. Authorize in Budget Governor
        var budgetRequest = new BudgetRequest
        {
            ModelId = modelId,
            HostingType = hosting,
            EstimatedInputTokens = estimatedInput,
            EstimatedOutputTokens = estimatedOutput,
            RequestId = requestId
        };

        BudgetDecision auth = await _budgetGovernor.TryAuthorizeAsync(budgetRequest, cancellationToken);
        if (!auth.IsAllowed)
        {
            _logger?.LogWarning("Token budget denied for Tier 3 model '{ModelId}': {Reason} - {Message}",
                modelId, auth.DenialReason, auth.Message);

            return new RouteResult
            {
                Status = RouteStatus.BudgetDenied,
                Tier = IntentTier.Tier3,
                Reason = $"Token budget denied for Tier 3: {auth.DenialReason} - {auth.Message}"
            };
        }

        // 2. Call heavy model with transient retry
        ChatModelResponse response;
        try
        {
            response = await CallWithRetriesAsync(
                new ChatModelRequest
                {
                    ModelClass = ChatModelClass.Heavy,
                    ModelId = modelId,
                    SystemPrompt = DefaultSystemPrompt,
                    Prompt = request.Text
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Tier 3 heavy model call failed.");
            return new RouteResult
            {
                Status = RouteStatus.Failed,
                Tier = IntentTier.Tier3,
                Reason = $"Tier 3 model failure: {ex.Message}"
            };
        }

        // 3. Record actual usage
        await RecordUsageAsync(modelId, hosting, response, requestId, cancellationToken);

        // 4. Parse structured JSON. Обычный вопрос не остаётся служебной строкой про порог уверенности.
        LlmClassificationJson? classification = TryParseJson(response.Text);
        bool hostCommand = classification is not null &&
            !string.IsNullOrWhiteSpace(classification.Intent) &&
            !IsChatIntent(classification.Intent) &&
            classification.Confidence >= _options.Router.Tier3ConfidenceThreshold;

        if (!hostCommand)
        {
            if (classification is null && !string.IsNullOrWhiteSpace(PresentAnswer(response.Text)))
            {
                return ChatAnswer(IntentTier.Tier3, PresentAnswer(response.Text));
            }

            _logger?.LogInformation(
                "Tier 3 did not classify a host command (intent '{Intent}', confidence {Confidence:F2}). Answering the user.",
                classification?.Intent,
                classification?.Confidence ?? 0);

            return await AnswerAsync(
                request, requestId, modelId, hosting, ChatModelClass.Heavy, IntentTier.Tier3, cancellationToken);
        }

        var matchedResult = new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier3,
            Intent = classification!.Intent,
            Parameters = classification.Parameters,
            Confidence = classification.Confidence,
            MatchedRuleName = $"Tier3:{classification.Intent}",
            Reason = classification.Reason ?? "Classified by Tier 3 heavy model"
        };

        await TryCacheResultAsync(request.Text, matchedResult, cancellationToken);

        return matchedResult;
    }

    private async Task<RouteResult> AnswerAsync(
        RouteRequest request,
        string requestId,
        string modelId,
        ModelHostingType hosting,
        ChatModelClass modelClass,
        IntentTier tier,
        CancellationToken cancellationToken)
    {
        var budgetRequest = new BudgetRequest
        {
            ModelId = modelId,
            HostingType = hosting,
            EstimatedInputTokens = 200 + (request.Text.Length / 3),
            EstimatedOutputTokens = 200,
            RequestId = requestId
        };

        BudgetDecision auth = await _budgetGovernor.TryAuthorizeAsync(budgetRequest, cancellationToken);
        if (!auth.IsAllowed)
        {
            return new RouteResult
            {
                Status = RouteStatus.BudgetDenied,
                Tier = tier,
                Reason = $"Token budget denied: {auth.DenialReason} - {auth.Message}"
            };
        }

        ChatModelResponse response;
        try
        {
            response = await CallWithRetriesAsync(
                new ChatModelRequest
                {
                    ModelClass = modelClass,
                    ModelId = modelId,
                    SystemPrompt = AnswerSystemPrompt,
                    Prompt = request.Text
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Chat answer call failed.");
            return new RouteResult
            {
                Status = RouteStatus.Failed,
                Tier = tier,
                Reason = $"Chat answer failure: {ex.Message}"
            };
        }

        await RecordUsageAsync(modelId, hosting, response, requestId, cancellationToken);
        string answer = PresentAnswer(response.Text);
        if (string.IsNullOrWhiteSpace(answer))
        {
            return new RouteResult
            {
                Status = RouteStatus.Failed,
                Tier = tier,
                Reason = "Chat model returned an empty answer."
            };
        }

        return ChatAnswer(tier, answer);
    }

    private static RouteResult ChatAnswer(IntentTier tier, string answer) =>
        new()
        {
            Status = RouteStatus.Matched,
            Tier = tier,
            Intent = "chat.reply",
            ResponseText = answer,
            Confidence = 1,
            MatchedRuleName = "chat.reply",
            Reason = "Answered by the chat model"
        };

    private static bool IsChatIntent(string? intent) =>
        string.Equals(intent, "chat.reply", StringComparison.OrdinalIgnoreCase);

    private static string PresentAnswer(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string withoutThought = System.Text.RegularExpressions.Regex.Replace(
            raw,
            "<think>.*?</think>",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return withoutThought.Trim();
    }

    private async Task<ChatModelResponse> CallWithRetriesAsync(
        ChatModelRequest request,
        CancellationToken cancellationToken)
    {
        // Egress (TASK-072): текст пользователя уходит внешнему провайдеру — сначала санитизация.
        // Blocked = fail-closed: вызов модели не выполняется вообще.
        if (_egressSanitizer is not null)
        {
            EgressSanitizeResult sanitized = await _egressSanitizer.SanitizeAsync(
                EgressPayload.Text(request.Prompt, EgressDestination.ExternalApi, "Tier23LlmRouter"), cancellationToken);
            if (sanitized.Outcome == EgressSanitizeOutcome.Blocked)
            {
                throw new InvalidOperationException("Egress sanitizer blocked the model request.");
            }
            if (sanitized.SanitizedPayload?.TextContent is { } cleaned && cleaned != request.Prompt)
            {
                request = request with { Prompt = cleaned };
            }
        }

        int maxRetries = Math.Max(0, _options.Router.LlmMaxRetries);
        int attempts = 0;

        while (true)
        {
            attempts++;
            try
            {
                return await _chatModelProvider.CompleteAsync(request, cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex) && attempts <= maxRetries)
            {
                int delayMs = 100 * (1 << (attempts - 1));
                _logger?.LogWarning(
                    ex,
                    "Transient model provider failure on attempt {Attempt}/{MaxRetries}. Retrying in {DelayMs}ms...",
                    attempts,
                    maxRetries,
                    delayMs);

                await Task.Delay(delayMs, cancellationToken);
            }
        }
    }

    private async Task RecordUsageAsync(
        string modelId,
        ModelHostingType hosting,
        ChatModelResponse response,
        string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var usage = new TokenUsageDto
            {
                ModelId = response.ModelId ?? modelId,
                HostingType = hosting,
                InputTokens = response.InputTokens,
                OutputTokens = response.OutputTokens,
                CostUsd = 0m, // Settled by governor with pricing provider
                OccurredAtUtc = DateTimeOffset.UtcNow,
                RequestId = requestId
            };

            await _budgetGovernor.RecordUsageAsync(usage, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to record usage in TokenBudgetGovernor.");
        }
    }

    private async Task TryCacheResultAsync(
        string text,
        RouteResult result,
        CancellationToken cancellationToken)
    {
        if (_options.Router.SemanticCacheEnabled && _semanticCache is not null && _embeddingProvider is not null)
        {
            try
            {
                float[] embedding = await _embeddingProvider.EmbedAsync(text, cancellationToken);
                await _semanticCache.PutAsync(embedding, result, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to cache LLM routing decision in semantic cache.");
            }
        }
    }

    private static LlmClassificationJson? TryParseJson(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        string cleanText = rawText.Trim();

        // Strip markdown ```json ... ``` blocks if present
        if (cleanText.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewline = cleanText.IndexOf('\n');
            if (firstNewline > 0)
            {
                cleanText = cleanText[firstNewline..].Trim();
            }
            if (cleanText.EndsWith("```", StringComparison.Ordinal))
            {
                cleanText = cleanText[..^3].Trim();
            }
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<LlmClassificationJson>(cleanText, options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is TimeoutException ||
        ex is HttpRequestException ||
        ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("retry", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("transient", StringComparison.OrdinalIgnoreCase);

    private static ModelHostingType DetermineHostingType(string modelId, ModelHostingType defaultType)
    {
        if (modelId.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
        {
            return ModelHostingType.RemoteCloud;
        }

        return defaultType;
    }
}
