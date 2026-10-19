namespace CyberPipe.Tests.E2E;

using System.Diagnostics;
using System.Net.Sockets;
using CyberPipe.Channels.Telegram;
using CyberPipe.Channels.Telegram.Adapter;
using CyberPipe.Channels.Telegram.Model;
using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Router;
using CyberPipe.Security;
using CyberPipe.Service.Composition;
using CyberPipe.Service.Messaging;
using CyberPipe.Skills.Host.Registry;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Тестовая PostgreSQL+pgvector для E2E (TASK-091): переиспользует локальный
/// контейнер `cyberpipe-test-db` (порт 5433), при отсутствии поднимает его через
/// docker с теми же параметрами. Без docker и без БД тесты пропускаются.
/// </summary>
internal static class E2EDatabase
{
    public const string Host = "localhost";
    public const int Port = 5433;
    public const string Database = "cyberpipe_test";
    public const string Username = "cyberpipe";
    public const string Password = "cyberpipe-test-pw";
    public const string ContainerName = "cyberpipe-test-db";

    public static async Task EnsureReadyAsync()
    {
        if (await IsReachableAsync().ConfigureAwait(false))
        {
            return;
        }

        if (!IsDockerAvailable())
        {
            throw new InvalidOperationException(
                "E2E needs local PostgreSQL+pgvector at localhost:5433 and docker is unavailable. " +
                "Start it via docker or install pgvector/pgvector:pg16 locally.");
        }

        if (RunDocker("start " + ContainerName).ExitCode != 0)
        {
            RunDocker("run -d --name " + ContainerName +
                " -e POSTGRES_USER=" + Username +
                " -e POSTGRES_PASSWORD=" + Password +
                " -e POSTGRES_DB=" + Database +
                " -p 5433:5432 pgvector/pgvector:pg16");
        }

        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await IsReachableAsync().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(1000).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "E2E PostgreSQL container did not become reachable at localhost:5433 within 2 minutes.");
    }

    private static async Task<bool> IsReachableAsync()
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(Host, Port, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            return RunDocker("--version").ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static (int ExitCode, string Output) RunDocker(string arguments)
    {
        var start = new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start docker.");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(60000);
        return (process.ExitCode, output);
    }
}

/// <summary>
/// Детерминированный фейк эмбеддингов той же малой размерности (TASK-091):
/// эталонные тексты намерений отображаются в ортогональные векторы,
/// неизвестные тексты — в равномерный вектор с низкой косинусной близостью.
/// </summary>
internal sealed class MapEmbeddingProvider : IEmbeddingProvider
{
    public const int Dimension = 5;

    private static readonly string[] Intents =
        ["system.help", "system.status", "system.cancel", "diagnostics.run", "knowledge.search"];

    private static readonly float[] Epsilon = [0.01f, 0.01f, 0.01f, 0.01f, 0.01f];

    private readonly Dictionary<string, float[]> _vectors = new(StringComparer.Ordinal);

    public int EmbedCalls { get; private set; }

    public MapEmbeddingProvider()
    {
        foreach (SemanticExampleDefinition example in DefaultSemanticExamples.GetDefaults())
        {
            int slot = Array.IndexOf(Intents, example.Intent);
            if (slot < 0)
            {
                continue;
            }

            var vector = new float[Dimension];
            vector[slot] = 1f;
            _vectors[example.Text] = vector;
        }
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        EmbedCalls++;
        if (text is not null && _vectors.TryGetValue(text, out float[]? vector))
        {
            return Task.FromResult((float[])vector.Clone());
        }

        return Task.FromResult((float[])Epsilon.Clone());
    }
}

/// <summary>
/// Счётный фейк LLM-провайдера (TASK-091): классификация задаётся тестом.
/// </summary>
internal sealed class CountingChatModelProvider : IChatModelProvider
{
    private readonly Dictionary<ChatModelClass, int> _callCounts = [];

    public string ClassificationJson { get; set; } =
        """{"intent":"system.help","confidence":0.96,"parameters":{}}""";

    public int GetCallCount(ChatModelClass modelClass) =>
        _callCounts.TryGetValue(modelClass, out int count) ? count : 0;

    public int TotalCalls => _callCounts.Values.Sum();

    public Task<ChatModelResponse> CompleteAsync(ChatModelRequest request, CancellationToken cancellationToken = default)
    {
        _callCounts[request.ModelClass] = GetCallCount(request.ModelClass) + 1;
        return Task.FromResult(new ChatModelResponse
        {
            Text = ClassificationJson,
            InputTokens = 100,
            OutputTokens = 50,
            ModelId = request.ModelClass == ChatModelClass.Light ? "test-light" : "test-heavy",
        });
    }
}

/// <summary>
/// Записывающий стаб исполнителя цепочек для E2E канала (TASK-091):
/// цепочки — предмет TASK-092, здесь важны только вызовы Resume/Cancel из HITL.
/// </summary>
internal sealed class RecordingChainExecutor : IChainExecutor
{
    public int ResumeCalls { get; private set; }

    public int CancelCalls { get; private set; }

    public List<RunId> ResumedRuns { get; } = [];

    public List<RunId> CancelledRuns { get; } = [];

    public Task<RunId> StartAsync(TriggerEvent triggerEvent, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RunId(Guid.NewGuid()));

    public Task ResumeAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        ResumeCalls++;
        ResumedRuns.Add(runId);
        return Task.CompletedTask;
    }

    public Task CancelAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        CancelCalls++;
        CancelledRuns.Add(runId);
        return Task.CompletedTask;
    }

    public Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ChainRunState?>(null);
}

/// <summary>
/// Тестовый навык с побочным эффектом для E2E (TASK-091): устанавливается
/// только через HITL-подтверждение, исполнение считается.
/// </summary>
internal sealed class TestVolumeSkill : IHostSkill
{
    public string SkillId => "test.volume.set";

    public string Name => "Test volume setter";

    public string Description => "Test-only volume skill with a side effect for E2E approval flow.";

    public int Invocations { get; private set; }

    public string LastLevel { get; private set; } = string.Empty;

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        Invocations++;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("level", out var level))
            {
                LastLevel = level.GetString() ?? level.GetRawText();
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return Task.FromResult(HostSkillResult.Ok(
            "{\"level\":\"" + LastLevel + "\"}",
            $"Volume set to {LastLevel}."));
    }
}

/// <summary>
/// Тестовый навык без побочных эффектов для быстрого пути E2E (TASK-091).
/// </summary>
internal sealed class TestHelpSkill : IHostSkill
{
    public string SkillId => "test.help";

    public string Name => "Test help";

    public string Description => "Test-only help skill without side effects.";

    public int Invocations { get; private set; }

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        Invocations++;
        return Task.FromResult(HostSkillResult.Ok("{}", "Available: volume, help."));
    }
}

/// <summary>
/// E2E-ассистент поверх реальной композиции (TASK-091): проверка Owner,
/// маршрутизация Tier0 → Tier1 → Tier2/3, губернатор бюджета, HITL через гейт,
/// исполнение через реальный <see cref="ISkillInvoker"/>, ответы через канал.
/// Дедупликация повторной доставки — по контент-ключу сообщения (канал
/// присваивает входящим случайный <see cref="MessageId"/>, транспортного
/// идентификатора в контракте нет).
/// </summary>
internal sealed class E2EAssistant : IMessageHandler<IncomingMessage>
{
    private readonly IOwnerAuthorizer _authorizer;
    private readonly Tier0RegexRouter _tier0;
    private readonly Tier1SemanticRouter _tier1;
    private readonly Tier23LlmRouter _tier23;
    private readonly ITokenBudgetGovernor _governor;
    private readonly ISkillInvoker _skills;
    private readonly TelegramChannelService _channel;
    private readonly IHitlApprovalGate _gate;
    private readonly CyberPipeOptions _options;
    private readonly ModelHostingType _skillHosting;
    private readonly object _sync = new();
    private readonly Queue<string> _recentKeys = new();
    private readonly HashSet<string> _seenKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, TaskCompletionSource<HitlApprovalResult>> _decisions = [];

    private TimeSpan? _hitlLifetimeOverride;

    public int RouterCalls { get; private set; }

    /// <summary>Переопределяет время жизни HITL-запроса (для теста таймаута).</summary>
    public void OverrideHitlLifetime(TimeSpan lifetime)
    {
        lock (_sync)
        {
            _hitlLifetimeOverride = lifetime;
        }
    }

    public E2EAssistant(
        IOwnerAuthorizer authorizer,
        Tier0RegexRouter tier0,
        Tier1SemanticRouter tier1,
        Tier23LlmRouter tier23,
        ITokenBudgetGovernor governor,
        ISkillInvoker skills,
        TelegramChannelService channel,
        IHitlApprovalGate gate,
        CyberPipeOptions options,
        ModelHostingType skillHosting = ModelHostingType.Local)
    {
        _authorizer = authorizer;
        _tier0 = tier0;
        _tier1 = tier1;
        _tier23 = tier23;
        _governor = governor;
        _skills = skills;
        _channel = channel;
        _gate = gate;
        _options = options;
        _skillHosting = skillHosting;
    }

    public Task<HitlApprovalResult> WaitDecisionAsync(Guid approvalId, TimeSpan timeout)
    {
        TaskCompletionSource<HitlApprovalResult> tcs;
        lock (_sync)
        {
            if (!_decisions.TryGetValue(approvalId, out tcs!))
            {
                tcs = new TaskCompletionSource<HitlApprovalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                _decisions[approvalId] = tcs;
            }
        }

        return tcs.Task.WaitAsync(timeout);
    }

    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken = default)
    {
        if (message is null)
        {
            return;
        }

        string dedupKey = $"{message.Channel}|{message.SenderId}|{message.ChatId}|{message.Text}|{message.SentAtUtc:O}";
        lock (_sync)
        {
            if (!_seenKeys.Add(dedupKey))
            {
                return;
            }

            _recentKeys.Enqueue(dedupKey);
            while (_recentKeys.Count > 1000 && _recentKeys.TryDequeue(out string? old))
            {
                _seenKeys.Remove(old);
            }
        }

        OwnerAuthDecision auth = await _authorizer.AuthorizeAsync(
            ChannelPrincipal.FromChannel(message.Channel, message.SenderId),
            cancellationToken).ConfigureAwait(false);
        if (!auth.IsAuthorized)
        {
            return;
        }

        RouterCalls++;

        RouteResult route = await _tier0.TryRouteAsync(
            new RouteRequest { Text = message.Text ?? string.Empty, Channel = message.Channel },
            cancellationToken).ConfigureAwait(false);
        if (route.Status != RouteStatus.Matched)
        {
            route = await _tier1.TryRouteAsync(
                new RouteRequest { Text = message.Text ?? string.Empty, Channel = message.Channel },
                cancellationToken).ConfigureAwait(false);
        }

        if (route.Status != RouteStatus.Matched)
        {
            route = await _tier23.TryRouteAsync(
                new RouteRequest { Text = message.Text ?? string.Empty, Channel = message.Channel },
                cancellationToken).ConfigureAwait(false);
        }

        if (route.Status == RouteStatus.BudgetDenied)
        {
            await ReplyAsync(message, BudgetRefusal(), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (route.Status != RouteStatus.Matched)
        {
            await ReplyAsync(message, "Не понял команду.", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(route.Intent, "media.volume", StringComparison.OrdinalIgnoreCase))
        {
            await HandleVolumeAsync(message, route, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(route.Intent, "system.help", StringComparison.OrdinalIgnoreCase))
        {
            await HandleHelpAsync(message, cancellationToken).ConfigureAwait(false);
            return;
        }

        await ReplyAsync(message, "Не понял команду.", cancellationToken).ConfigureAwait(false);
    }

    public Guid? LastApprovalId
    {
        get
        {
            lock (_sync)
            {
                return _lastApprovalId;
            }
        }
    }

    private Guid? _lastApprovalId;

    private async Task HandleVolumeAsync(IncomingMessage message, RouteResult route, CancellationToken ct)
    {
        string level = route.Parameters.TryGetValue("level", out string? parsed) ? parsed : "?";

        if (!await CheckSkillBudgetAsync(message, ct).ConfigureAwait(false))
        {
            return;
        }

        TimeSpan lifetime = TimeSpan.FromMinutes(5);
        lock (_sync)
        {
            if (_hitlLifetimeOverride is not null)
            {
                lifetime = _hitlLifetimeOverride.Value;
            }
        }

        var request = new HitlApprovalRequest
        {
            ApprovalId = Guid.NewGuid(),
            RunId = new RunId(Guid.NewGuid()),
            ChainId = new ChainId(Guid.NewGuid()),
            Title = "Volume change",
            PreviewText = $"Set volume to {level}",
            PayloadJson = "{\"skill\":\"test.volume.set\",\"level\":\"" + level + "\"}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(lifetime),
        };

        lock (_sync)
        {
            _lastApprovalId = request.ApprovalId;
        }

        TaskCompletionSource<HitlApprovalResult> tcs;
        lock (_sync)
        {
            if (!_decisions.TryGetValue(request.ApprovalId, out tcs!))
            {
                tcs = new TaskCompletionSource<HitlApprovalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                _decisions[request.ApprovalId] = tcs;
            }
        }

        _ = Task.Run(async () =>
        {
            HitlApprovalResult decision;
            try
            {
                decision = await _gate.RequestApprovalAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                decision = new HitlApprovalResult
                {
                    ApprovalId = request.ApprovalId,
                    Decision = HitlDecision.Rejected,
                    Reason = ex.Message,
                    OwnerId = "system",
                    Channel = message.Channel,
                    DecidedAtUtc = DateTimeOffset.UtcNow,
                };
            }

            try
            {
                if (decision.Decision == HitlDecision.Approved)
                {
                    SkillInvocationResult result = await _skills.InvokeSkillAsync(new SkillInvocationRequest
                    {
                        SkillId = "test.volume.set",
                        PayloadJson = "{\"level\":\"" + level + "\"}",
                        RunId = new RunId(Guid.NewGuid()),
                    }).ConfigureAwait(false);
                    await RecordSkillUsageAsync(ct).ConfigureAwait(false);
                    await ReplyAsync(message, result.IsSuccess ? $"Volume set to {level}." : "Skill failed.", ct)
                        .ConfigureAwait(false);
                }
                else if (decision.Decision == HitlDecision.Rejected)
                {
                    await ReplyAsync(message, "Отклонено владельцем, действие не выполнено.", ct).ConfigureAwait(false);
                }
                else
                {
                    await ReplyAsync(message, "Время подтверждения вышло, действие не выполнено.", ct).ConfigureAwait(false);
                }
            }
            finally
            {
                tcs.TrySetResult(decision);
            }
        }, CancellationToken.None);
    }

    private async Task HandleHelpAsync(IncomingMessage message, CancellationToken ct)
    {
        if (!await CheckSkillBudgetAsync(message, ct).ConfigureAwait(false))
        {
            return;
        }

        SkillInvocationResult result = await _skills.InvokeSkillAsync(new SkillInvocationRequest
        {
            SkillId = "test.help",
            PayloadJson = "{}",
            RunId = new RunId(Guid.NewGuid()),
        }).ConfigureAwait(false);
        await RecordSkillUsageAsync(ct).ConfigureAwait(false);
        await ReplyAsync(message, result.IsSuccess ? "Available: volume, help." : "Skill failed.", ct)
            .ConfigureAwait(false);
    }

    private async Task<bool> CheckSkillBudgetAsync(IncomingMessage message, CancellationToken ct)
    {
        BudgetDecision budget = await _governor.TryAuthorizeAsync(new BudgetRequest
        {
            ModelId = "e2e-skill",
            HostingType = _skillHosting,
            EstimatedInputTokens = 100,
            EstimatedOutputTokens = 50,
        }, ct).ConfigureAwait(false);
        if (!budget.IsAllowed)
        {
            await ReplyAsync(message, BudgetRefusal(), ct).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private Task RecordSkillUsageAsync(CancellationToken ct) =>
        _governor.RecordUsageAsync(new TokenUsageDto
        {
            ModelId = "e2e-skill",
            HostingType = _skillHosting,
            InputTokens = 100,
            OutputTokens = 50,
            CostUsd = 0m,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        }, ct);

    private string BudgetRefusal() =>
        $"Превышен бюджет: отказ. Лимиты: ${_options.Budget.DailyCapUsd:F2}/день, {_options.Budget.SoftTokenLimit} токенов.";

    private Task ReplyAsync(IncomingMessage message, string text, CancellationToken ct) =>
        _channel.HandleAsync(new OutgoingMessage
        {
            Channel = message.Channel,
            Target = message.ChatId ?? message.SenderId,
            Text = text,
        }, ct);
}

/// <summary>
/// Хост E2E-сценария (TASK-091): полная композиция TASK-090 на тестовой
/// PostgreSQL, фейки только на границах (эмбеддинг, LLM, исполнитель цепочек,
/// Telegram-адаптер без токена, файловое хранилище секретов).
/// </summary>
internal sealed class E2EHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public FakeTelegramBotClientAdapter Adapter { get; }

    public E2EAssistant Assistant { get; }

    public TestVolumeSkill VolumeSkill { get; }

    public TestHelpSkill HelpSkill { get; }

    public MapEmbeddingProvider Embeddings { get; }

    public CountingChatModelProvider Llm { get; }

    public RecordingChainExecutor ExecutorStub { get; }

    public ITokenBudgetGovernor Governor { get; }

    public IHitlApprovalGate Gate { get; }

    public IHitlApprovalService Hitl { get; }

    public IHitlApprovalRepository HitlRepository { get; }

    public ISemanticCache SemanticCache { get; }

    public TelegramChannelService Channel { get; }

    public long ChatId { get; }

    public long OwnerUserId { get; }

    private E2EHost(
        ServiceProvider provider,
        FakeTelegramBotClientAdapter adapter,
        E2EAssistant assistant,
        ISemanticCache semanticCache,
        TestVolumeSkill volumeSkill,
        TestHelpSkill helpSkill,
        MapEmbeddingProvider embeddings,
        CountingChatModelProvider llm,
        RecordingChainExecutor executorStub,
        ITokenBudgetGovernor governor,
        IHitlApprovalGate gate,
        IHitlApprovalService hitl,
        IHitlApprovalRepository hitlRepository,
        TelegramChannelService channel,
        long chatId,
        long ownerUserId)
    {
        _provider = provider;
        Adapter = adapter;
        Assistant = assistant;
        SemanticCache = semanticCache;
        VolumeSkill = volumeSkill;
        HelpSkill = helpSkill;
        Embeddings = embeddings;
        Llm = llm;
        ExecutorStub = executorStub;
        Governor = governor;
        Gate = gate;
        Hitl = hitl;
        HitlRepository = hitlRepository;
        Channel = channel;
        ChatId = chatId;
        OwnerUserId = ownerUserId;
    }

    public static async Task<E2EHost> CreateAsync(
        string ownerId,
        long chatId,
        decimal dailyCapUsd = 100m,
        ModelHostingType skillHosting = ModelHostingType.Local)
    {
        await E2EDatabase.EnsureReadyAsync().ConfigureAwait(false);

        var options = new CyberPipeOptions
        {
            Database = new DatabaseOptions
            {
                Host = E2EDatabase.Host,
                Port = E2EDatabase.Port,
                Database = E2EDatabase.Database,
                Username = E2EDatabase.Username,
            },
            Owner = new OwnerOptions { TelegramOwnerIds = [ownerId] },
            Budget = new BudgetOptions { DailyCapUsd = dailyCapUsd },
        };

        string secretPath = Path.Combine(
            Path.GetTempPath(), "cyberpipe-e2e-secrets-" + Guid.NewGuid().ToString("N") + ".json");
        var secrets = new FileSecretStore(secretPath);
        await secrets.SetAsync(SecretKeys.DatabasePassword, SecretValue.FromPlainText(E2EDatabase.Password))
            .ConfigureAwait(false);

        var host = new E2EHostBuilder(options, secrets, chatId, long.Parse(ownerId), skillHosting);
        return await host.BuildAsync().ConfigureAwait(false);
    }

    public async Task FeedTextAsync(long userId, string text)
    {
        Adapter.EnqueueTextMessage(ChatId, userId, text);
        IReadOnlyList<TelegramUpdate> updates = await Adapter.GetUpdatesAsync().ConfigureAwait(false);
        Assert.Single(updates);
        await Channel.ProcessUpdateAsync(updates[0], CancellationToken.None).ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);
    }

    public async Task FeedCallbackAsync(string callbackId, long userId, string data)
    {
        Adapter.EnqueueCallbackQuery(callbackId, ChatId, userId, data);
        IReadOnlyList<TelegramUpdate> updates = await Adapter.GetUpdatesAsync().ConfigureAwait(false);
        Assert.Single(updates);
        await Channel.ProcessUpdateAsync(updates[0], CancellationToken.None).ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);
    }

    public async Task FeedUpdatesAsync(params TelegramUpdate[] updates)
    {
        foreach (TelegramUpdate update in updates)
        {
            await Channel.ProcessUpdateAsync(update, CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private sealed class E2EHostBuilder(
        CyberPipeOptions options,
        FileSecretStore secrets,
        long chatId,
        long ownerUserId,
        ModelHostingType skillHosting)
    {
        public async Task<E2EHost> BuildAsync()
        {
            var services = new ServiceCollection();
            services.AddCyberPipeService(options);
            services.AddSingleton<ISecretStore>(secrets);

            var volumeSkill = new TestVolumeSkill();
            var helpSkill = new TestHelpSkill();
            var embeddings = new MapEmbeddingProvider();
            var llm = new CountingChatModelProvider();
            var executorStub = new RecordingChainExecutor();
            services.AddSingleton<IEmbeddingProvider>(embeddings);
            services.AddSingleton<IChatModelProvider>(llm);
            services.AddSingleton<IChainExecutor>(executorStub);
            services.AddSingleton(new TelegramOptions { OwnerChatId = chatId });

            ServiceProvider provider = services.BuildServiceProvider();

            try
            {
                ISchemaMigrator migrator = provider.GetRequiredService<ISchemaMigrator>();
                await migrator.MigrateAsync().ConfigureAwait(false);

                await provider.GetRequiredService<ISemanticCache>().ClearAsync().ConfigureAwait(false);

                provider.GetRequiredService<IHostSkillRegistry>().RegisterSkill(volumeSkill);
                provider.GetRequiredService<IHostSkillRegistry>().RegisterSkill(helpSkill);

                var assistant = new E2EAssistant(
                    provider.GetRequiredService<IOwnerAuthorizer>(),
                    provider.GetRequiredService<Tier0RegexRouter>(),
                    provider.GetRequiredService<Tier1SemanticRouter>(),
                    provider.GetRequiredService<Tier23LlmRouter>(),
                    provider.GetRequiredService<ITokenBudgetGovernor>(),
                    provider.GetRequiredService<ISkillInvoker>(),
                    provider.GetRequiredService<TelegramChannelService>(),
                    provider.GetRequiredService<IHitlApprovalGate>(),
                    options,
                    skillHosting);

                provider.GetRequiredService<IMessageBus>().Subscribe<IncomingMessage>(assistant);

                var adapter = (FakeTelegramBotClientAdapter)provider
                    .GetRequiredService<ITelegramBotClientAdapter>();

                return new E2EHost(
                    provider,
                    adapter,
                    assistant,
                    provider.GetRequiredService<ISemanticCache>(),
                    volumeSkill,
                    helpSkill,
                    embeddings,
                    llm,
                    executorStub,
                    provider.GetRequiredService<ITokenBudgetGovernor>(),
                    provider.GetRequiredService<IHitlApprovalGate>(),
                    provider.GetRequiredService<IHitlApprovalService>(),
                    provider.GetRequiredService<IHitlApprovalRepository>(),
                    provider.GetRequiredService<TelegramChannelService>(),
                    chatId,
                    ownerUserId);
            }
            catch (Exception)
            {
                await provider.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}

/// <summary>
/// Атрибут факта E2E (TASK-091): пропуск на discovery, если нет ни тестовой
/// PostgreSQL+pgvector на localhost:5433, ни docker для её поднятия.
/// </summary>
internal sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (!IsTcpReachable() && !IsDockerPresent())
        {
            Skip = "E2E needs local PostgreSQL+pgvector at localhost:5433 or docker to provision it.";
        }
        else if (!IsPythonPresent())
        {
            Skip = "E2E needs a 'python' interpreter for the real netrunner runner.";
        }
    }

    private static bool IsTcpReachable()
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(E2EDatabase.Host, E2EDatabase.Port);
            return task.Wait(TimeSpan.FromSeconds(3)) && client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsDockerPresent() => RunCommand("docker", "--version");

    private static bool IsPythonPresent() => RunCommand("python", "--version");

    private static bool RunCommand(string fileName, string arguments)
    {
        try
        {
            var start = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using Process process = Process.Start(start)!;
            process.WaitForExit(15000);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
