namespace CyberPipe.Tests.E2E;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CyberPipe.Chains;
using CyberPipe.Chains.Packages;
using CyberPipe.Chains.Runners;
using CyberPipe.Chains.Triggers;
using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Security;
using CyberPipe.Service.Composition;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// PostgreSQL-адаптер <see cref="IChainExecutionRepository"/> поверх настоящих
/// PG-репозиториев (TASK-092, фикстура тестового проекта): прогоны — через
/// <see cref="IChainRunRepository"/>, чекпоинты — через <see cref="ICheckpointRepository"/>.
/// Семантика повторяет <c>InMemoryChainExecutionRepository</c> (идемпотентность
/// по (ChainId, IdempotencyKey), терминальные статусы, порядок чекпоинтов).
/// Полный <see cref="TriggerEvent"/> кэшируется в памяти; после рестарта хоста
/// событие восстанавливается из записи (вид триггера в конвейере не используется,
/// нужны только нагрузка, ключ идемпотентности и источник).
/// </summary>
internal sealed class PostgresChainExecutionRepository(
    IChainRunRepository runs,
    ICheckpointRepository checkpoints) : IChainExecutionRepository
{
    private static readonly PagedQuery BigPage = new() { Offset = 0, Limit = 200, SortBy = "created_at", Descending = false };

    private static readonly IReadOnlySet<ChainRunStatus> UnfinishedStatuses =
        new HashSet<ChainRunStatus>
        {
            ChainRunStatus.Running,
            ChainRunStatus.WaitingForHitl,
            ChainRunStatus.WaitingForRetry,
        };

    private readonly ConcurrentDictionary<RunId, TriggerEvent> _triggers = new();

    public async Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        ChainRunRecord? record = await runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        IReadOnlyList<ChainCheckpointDto> steps = await GetCheckpointsAsync(runId, cancellationToken)
            .ConfigureAwait(false);
        return MapState(record, GetTrigger(record), steps);
    }

    public async Task<RunId?> FindRunByIdempotencyKeyAsync(ChainId chainId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        PagedResult<ChainRunRecord> page = await runs.ListAsync(BigPage, chainId, cancellationToken)
            .ConfigureAwait(false);
        ChainRunRecord? found = page.Items.FirstOrDefault(
            r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
        return found?.Id;
    }

    public async Task<ChainRunState> CreateRunAsync(RunId runId, ChainId chainId, TriggerEvent trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var now = DateTimeOffset.UtcNow;
        ChainRunRecord saved = await runs.AddAsync(new ChainRunRecord
        {
            Id = runId,
            ChainId = chainId,
            TriggerEventId = trigger.EventId,
            IdempotencyKey = trigger.IdempotencyKey,
            Status = ChainRunStatus.Running,
            Source = trigger.Source,
            PayloadJson = trigger.PayloadJson,
            StartedAtUtc = now,
        }, cancellationToken).ConfigureAwait(false);

        _triggers[saved.Id] = trigger;
        return MapState(saved, trigger, []);
    }

    public async Task UpdateRunStatusAsync(RunId runId, ChainRunStatus status, ErrorDto? error = null, CancellationToken cancellationToken = default)
    {
        ChainRunRecord? current = await runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            throw new KeyNotFoundException($"Run '{runId}' not found.");
        }

        ChainRunRecord updated = current with
        {
            Status = status,
            Error = error ?? current.Error,
            FinishedAtUtc = status is ChainRunStatus.Completed or ChainRunStatus.Failed
                or ChainRunStatus.Cancelled or ChainRunStatus.CancelledByCrash
                ? DateTimeOffset.UtcNow
                : current.FinishedAtUtc,
        };
        await runs.UpdateAsync(updated, current.RowVersion, cancellationToken).ConfigureAwait(false);
    }

    public Task SaveCheckpointAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return checkpoints.AddAsync(checkpoint, cancellationToken);
    }

    public async Task<IReadOnlyList<ChainCheckpointDto>> GetCheckpointsAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        PagedResult<CheckpointRecord> page = await checkpoints.ListAsync(BigPage, runId, cancellationToken)
            .ConfigureAwait(false);
        return page.Items.Select(r => r.Checkpoint).OrderBy(c => c.Step).ToArray();
    }

    public async Task<IReadOnlyList<RunId>> ListUnfinishedRunsAsync(CancellationToken cancellationToken = default)
    {
        PagedResult<ChainRunRecord> page = await runs.ListAsync(BigPage, null, cancellationToken)
            .ConfigureAwait(false);
        return page.Items.Where(r => UnfinishedStatuses.Contains(r.Status)).Select(r => r.Id).ToArray();
    }

    private TriggerEvent GetTrigger(ChainRunRecord record)
    {
        if (_triggers.TryGetValue(record.Id, out TriggerEvent? cached))
        {
            return cached;
        }

        return new TriggerEvent
        {
            EventId = record.TriggerEventId ?? Guid.Empty,
            ChainId = record.ChainId,
            Kind = default,
            OccurredAtUtc = record.StartedAtUtc,
            PayloadJson = record.PayloadJson,
            FileRefs = [],
            IdempotencyKey = record.IdempotencyKey,
            Source = record.Source,
        };
    }

    private static ChainRunState MapState(ChainRunRecord record, TriggerEvent trigger, IReadOnlyList<ChainCheckpointDto> steps) =>
        new()
        {
            RunId = record.Id,
            ChainId = record.ChainId,
            Status = record.Status,
            Trigger = trigger,
            StartedAtUtc = record.StartedAtUtc,
            FinishedAtUtc = record.FinishedAtUtc,
            Error = record.Error,
            Checkpoints = steps,
        };
}

/// <summary>
/// Считающий декоратор реестра раннеров (TASK-092): все раннеры настоящие,
/// декоратор только считает вызовы <c>RunAsync</c> по языку.
/// </summary>
internal sealed class CountingScriptRunnerRegistry(IScriptRunnerRegistry inner) : IScriptRunnerRegistry
{
    private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.OrdinalIgnoreCase);

    public int TotalCalls => _calls.Values.Sum();

    public int GetCallCount(string language) =>
        _calls.TryGetValue(language, out int count) ? count : 0;

    public IScriptRunner? FindRunner(string language, string environment) =>
        inner.FindRunner(language, environment);

    public void RegisterRunner(IScriptRunner runner) => inner.RegisterRunner(runner);

    public IReadOnlyCollection<IScriptRunner> GetAllRunners() => inner.GetAllRunners();

    public Task<ScriptRunResult> RunAsync(ScriptRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string key = request.Manifest?.Runtime?.Language ?? "unknown";
        _calls.AddOrUpdate(key, 1, (_, count) => count + 1);
        return inner.RunAsync(request, cancellationToken);
    }

    public Task<ScriptRunResult> DryRunPackageAsync(string packageDir, string workspaceDir, CancellationToken cancellationToken = default) =>
        inner.DryRunPackageAsync(packageDir, workspaceDir, cancellationToken);
}

/// <summary>
/// Считающий фейк скиллов цепочек (TASK-092): два навыка с разными маркерами,
/// выход содержит маркер, который скрипт отражает в свой выход.
/// </summary>
internal sealed class ChainCountingSkillInvoker : ISkillInvoker
{
    private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);

    public int GetCallCount(string skillId) =>
        _calls.TryGetValue(skillId, out int count) ? count : 0;

    public int TotalCalls => _calls.Values.Sum();

    public Task<SkillInvocationResult> InvokeSkillAsync(SkillInvocationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _calls.AddOrUpdate(request.SkillId, 1, (_, count) => count + 1);
        string marker = request.SkillId switch
        {
            "test.chain.skill-a" => "A",
            "test.chain.skill-b" => "B",
            _ => "?",
        };
        return Task.FromResult(new SkillInvocationResult
        {
            IsSuccess = true,
            OutputJson = "{\"normalized\":\"hello-" + marker + "\",\"marker\":\"" + marker + "\"}",
        });
    }
}

/// <summary>
/// Захватывающий фейк доставки цепочек (TASK-092).
/// </summary>
internal sealed class ChainCapturingDeliveryPort : IDeliveryPort
{
    private readonly object _sync = new();
    private readonly List<DeliveryRequest> _requests = [];

    public int Invocations
    {
        get
        {
            lock (_sync)
            {
                return _requests.Count;
            }
        }
    }

    public IReadOnlyList<DeliveryRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToList();
            }
        }
    }

    public Task<DeliveryResult> SendAsync(DeliveryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            _requests.Add(request);
        }

        return Task.FromResult(new DeliveryResult { IsSuccess = true });
    }
}

/// <summary>
/// Захватывающий отправитель HITL-карточек (TASK-092): фиксирует запросы,
/// через него тесты узнают ApprovalId созданных подтверждений.
/// </summary>
internal sealed class CapturingHitlCardSender : IHitlCardSender
{
    private readonly object _sync = new();
    private readonly List<HitlApprovalRequest> _requests = [];

    public ChannelKind Channel => ChannelKind.Telegram;

    public IReadOnlyList<HitlApprovalRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToList();
            }
        }
    }

    public Task SendCardAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            _requests.Add(request);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Прямой лаунчер процессов для E2E (TASK-092): честно исполняет контракт
/// <see cref="ISandboxLauncher"/> (рабочий каталог, allowlist-окружение поверх
/// системного, тайм-аут с убийством, захват вывода и кода возврата), но без
/// Restricted Token / Job Object — их покрывает собственный набор SandboxTests.
/// Прецедент: собственные тесты TASK-043 тоже идут через фейк-лаунчер.
/// </summary>
internal sealed class DirectProcessSandboxLauncher : ISandboxLauncher
{
    public async Task<SandboxLaunchResult> RunAsync(SandboxLaunchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var start = new ProcessStartInfo(request.ExecutablePath)
        {
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in request.Arguments)
        {
            start.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in request.Environment)
        {
            start.Environment[key] = value;
        }

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception ex)
        {
            return new SandboxLaunchResult(null, false, false, string.Empty, string.Empty, $"LaunchFailed: {ex.Message}");
        }

        if (process is null)
        {
            return new SandboxLaunchResult(null, false, false, string.Empty, string.Empty, "LaunchFailed: process not started.");
        }

        using (process)
        {
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMilliseconds(request.TimeoutMs), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                }

                return new SandboxLaunchResult(null, true, false, string.Empty, string.Empty, null);
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            return new SandboxLaunchResult(process.ExitCode, false, false, stdout, stderr, null);
        }
    }
}

/// <summary>
/// Сборщик фикстурного пакета skill-hack.* (TASK-092): настоящий пакет на диске
/// с валидным манифестом (все пять полей runtime.*), детерминированным скриптом
/// и посчитанными SHA-256. Флаги fail/defer/flaky.flag в каталоге
/// %TEMP%/cyberpipe-e2e-flags (вне пакета — иначе ломается integrity)
/// меняют поведение скрипта для негативных веток.
/// </summary>
internal static class ChainPackageFixture
{
    public const string Slug = "e2e-chain-pkg";
    public const string Version = "1.0.0";
    public const string Entrypoint = "skill-hack.py";

    public static string PackageDir { get; } =
        Path.Combine(Path.GetTempPath(), "cyberpipe-e2e-chain-pkg", Slug, Version);

    public static string EnsurePackage()
    {
        Directory.CreateDirectory(PackageDir);
        Directory.CreateDirectory(Path.Combine(PackageDir, "tests"));
        foreach (string stray in new[] { "fail.flag", "defer.flag" })
        {
            string strayPath = Path.Combine(PackageDir, stray);
            if (File.Exists(strayPath))
            {
                File.Delete(strayPath);
            }
        }

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SKILL.md"] = "# E2E Chain Package\nDeterministic echo script for chain E2E.",
            ["schema.input.json"] = """
                {
                  "$schema": "https://json-schema.org/draft/2020-12/schema",
                  "type": "object"
                }
                """,
            ["schema.output.json"] = """
                {
                  "$schema": "https://json-schema.org/draft/2020-12/schema",
                  "type": "object",
                  "required": ["script"],
                  "properties": {
                    "script": { "type": "string" }
                  }
                }
                """,
            ["tests/sample.fixture.json"] = """
                {
                  "sampleInput": { "normalized": "hello-T", "marker": "T" },
                  "expectedOutput": { "script": "processed-T" }
                }
                """,
            [Entrypoint] = """
                import json
                import os
                import sys
                import tempfile

                flags = os.path.join(tempfile.gettempdir(), 'cyberpipe-e2e-flags')

                def flag(name):
                    return os.path.exists(os.path.join(flags, name))

                with open('input.json', encoding='utf-8') as f:
                    payload = json.load(f)

                if flag('flaky.flag'):
                    marker_path = os.path.join(flags, 'flaky-attempt.marker')
                    if not os.path.exists(marker_path):
                        open(marker_path, 'w').write('1')
                        print('e2e flaky first-attempt failure', file=sys.stderr)
                        sys.exit(1)

                if flag('defer.flag'):
                    print(json.dumps({
                        "$netrunnerControl": "DeferRetry",
                        "retryAfterSeconds": 3600,
                        "reason": "e2e-interrupt"
                    }))
                    sys.exit(0)

                if flag('fail.flag'):
                    print('e2e failure', file=sys.stderr)
                    sys.exit(1)

                marker = str(payload.get('marker', '?'))
                out = {"script": "processed-" + marker}
                with open('output.json', 'w', encoding='utf-8') as f:
                    json.dump(out, f)
                print(json.dumps(out))
                """,
        };

        foreach (var (relPath, content) in files)
        {
            string full = Path.Combine(PackageDir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string relPath in files.Keys)
        {
            hashes[relPath] = NetrunnerPackageValidator.ComputeSha256(Path.Combine(PackageDir, relPath));
        }

        var manifestDict = new Dictionary<string, object>
        {
            ["slug"] = Slug,
            ["name"] = "E2E Chain Package",
            ["version"] = Version,
            ["description"] = "Deterministic echo script for chain E2E scenarios.",
            ["runtime"] = new Dictionary<string, object>
            {
                ["language"] = "python",
                ["languageVersion"] = "3.10",
                ["environment"] = "cpython",
                ["environmentVersion"] = "3.10.0",
                ["entrypoint"] = Entrypoint,
                ["timeoutMs"] = 30000,
                ["deterministic"] = true,
            },
            ["io"] = new Dictionary<string, object>
            {
                ["inputSchemaFile"] = "schema.input.json",
                ["outputSchemaFile"] = "schema.output.json",
                ["inputFormats"] = new[] { "json" },
                ["outputFormats"] = new[] { "json" },
                ["inputFiles"] = Array.Empty<object>(),
                ["outputFiles"] = Array.Empty<object>(),
            },
            ["permissions"] = new Dictionary<string, object>
            {
                ["allowNetwork"] = false,
                ["allowedDomains"] = Array.Empty<string>(),
                ["filesystemScope"] = "IsolatedRunWorkspaceOnly",
                ["requiredVaultSecretRefs"] = Array.Empty<string>(),
            },
            ["integrity"] = new Dictionary<string, object>
            {
                ["filesSha256"] = hashes,
            },
        };

        string manifestJson = JsonSerializer.Serialize(manifestDict, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(PackageDir, "netrunner.manifest.json"), manifestJson);
        return PackageDir;
    }

    public static void SetFailFlag(bool present) => SetFlag("fail.flag", present);

    public static void SetDeferFlag(bool present) => SetFlag("defer.flag", present);

    public static void SetFlakyFlag(bool present)
    {
        // Маркер первой попытки живёт рядом с флагами; перед flaky-прогоном
        // его удаляем, иначе скрипт с первого раза увидит «уже падал».
        string marker = Path.Combine(Path.GetTempPath(), "cyberpipe-e2e-flags", "flaky-attempt.marker");
        if (present && File.Exists(marker))
        {
            File.Delete(marker);
        }

        SetFlag("flaky.flag", present);
        if (!present && File.Exists(marker))
        {
            File.Delete(marker);
        }
    }

    private static void SetFlag(string name, bool present)
    {
        string dir = Path.Combine(Path.GetTempPath(), "cyberpipe-e2e-flags");
        string path = Path.Combine(dir, name);
        if (present)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, "e2e");
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static void ClearFlags()
    {
        SetFlag("fail.flag", false);
        SetFlag("defer.flag", false);
        SetFlakyFlag(false);
    }
}

/// <summary>
/// Хост E2E-сценариев цепочек (TASK-092): полная композиция TASK-090 на тестовой
/// PostgreSQL. Настоящие: определения (PG), валидаторы, раннеры (включая Python
/// через песочницу), исполнитель, PG-адаптер запусков/чекпоинтов, Quartz, HITL.
/// Фейки: скиллы, доставка, пакетный реестр поверх настоящего пакета на диске.
/// </summary>
internal sealed class ChainE2EHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public IServiceProvider Services => _provider;

    public ChainCountingSkillInvoker Skills { get; }

    public ChainCapturingDeliveryPort Delivery { get; }

    public CapturingHitlCardSender HitlCards { get; }

    public CountingScriptRunnerRegistry Scripts { get; }

    public IChainExecutor Executor { get; }

    public IChainDefinitionRepository Definitions { get; }

    public IChainExecutionRepository Executions { get; }

    public ICheckpointRepository Checkpoints { get; }

    public IHitlApprovalService Hitl { get; }

    public IHitlApprovalRepository HitlRepository { get; }

    public ITriggerInvoker FunctionCalls { get; }

    public WebhookTriggerHandler Webhooks { get; }

    public IIdempotencyStore Idempotency { get; }

    public IChainSchedulerService SchedulerService { get; }

    private ChainE2EHost(
        ServiceProvider provider,
        ChainCountingSkillInvoker skills,
        ChainCapturingDeliveryPort delivery,
        CountingScriptRunnerRegistry scripts,
        CapturingHitlCardSender hitlCards)
    {
        _provider = provider;
        Skills = skills;
        Delivery = delivery;
        Scripts = scripts;
        HitlCards = hitlCards;
        Executor = provider.GetRequiredService<IChainExecutor>();
        Definitions = provider.GetRequiredService<IChainDefinitionRepository>();
        Executions = provider.GetRequiredService<IChainExecutionRepository>();
        Checkpoints = provider.GetRequiredService<ICheckpointRepository>();
        Hitl = provider.GetRequiredService<IHitlApprovalService>();
        HitlRepository = provider.GetRequiredService<IHitlApprovalRepository>();
        FunctionCalls = provider.GetRequiredService<ITriggerInvoker>();
        Webhooks = provider.GetRequiredService<WebhookTriggerHandler>();
        Idempotency = provider.GetRequiredService<IIdempotencyStore>();
        SchedulerService = provider.GetRequiredService<IChainSchedulerService>();
    }

    public static async Task<ChainE2EHost> CreateAsync(string ownerId = "111", bool cleanDefinitions = true)
    {
        await E2EDatabase.EnsureReadyAsync().ConfigureAwait(false);
        string packageDir = ChainPackageFixture.EnsurePackage();
        // Флаги НЕ чистим здесь: тесты ставят fail/defer-флаги, скрипт читает их
        // во время прогона; зачистка при создании хоста стирала бы флаги,
        // поставленные до CreateAsync. Каждый тест чистит флаги сам (finally).

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
        };

        string secretPath = Path.Combine(
            Path.GetTempPath(), "cyberpipe-e2e-chain-secrets-" + Guid.NewGuid().ToString("N") + ".json");
        var secrets = new FileSecretStore(secretPath);
        await secrets.SetAsync(SecretKeys.DatabasePassword, SecretValue.FromPlainText(E2EDatabase.Password))
            .ConfigureAwait(false);

        var skills = new ChainCountingSkillInvoker();
        var delivery = new ChainCapturingDeliveryPort();
        var hitlCards = new CapturingHitlCardSender();

        var services = new ServiceCollection();
        services.AddCyberPipeService(options);
        services.AddSingleton<ISecretStore>(secrets);
        services.AddSingleton<ISandboxLauncher, DirectProcessSandboxLauncher>();
        services.AddSingleton<IHitlCardSender>(hitlCards);
        services.AddSingleton<IChainExecutionRepository>(sp =>
            new PostgresChainExecutionRepository(
                sp.GetRequiredService<IChainRunRepository>(),
                sp.GetRequiredService<ICheckpointRepository>()));
        services.AddSingleton<ISkillInvoker>(skills);
        services.AddSingleton<IDeliveryPort>(delivery);
        services.AddSingleton<IScriptRunnerRegistry>(sp =>
        {
            var inner = new ScriptRunnerRegistry(sp.GetService<INetrunnerPackageValidator>());
            ISandboxLauncher? sandbox = sp.GetService<ISandboxLauncher>();
            ISchemaValidator schemaValidator = sp.GetRequiredService<ISchemaValidator>();
            if (sandbox is not null)
            {
                inner.RegisterRunner(new CSharpScriptRunner(sandbox, schemaValidator));
                inner.RegisterRunner(new PythonScriptRunner(sandbox, schemaValidator));
                inner.RegisterRunner(new JavaScriptScriptRunner(sandbox, schemaValidator));
            }

            return new CountingScriptRunnerRegistry(inner);
        });

        ServiceProvider provider = services.BuildServiceProvider();
        try
        {
            ISchemaMigrator migrator = provider.GetRequiredService<ISchemaMigrator>();
            await migrator.MigrateAsync().ConfigureAwait(false);

            if (cleanDefinitions)
            {
                // Изоляция: общая тестовая БД переживает прогоны, определения
                // накапливаются; chat-слушатель срабатывает на ВСЕ включённые
                // chat-цепочки (префикс-фильтр), stale-цепочки давали лишние
                // запуски (4 вместо 1). Отключаем чужие цепочки через интерфейс
                // репозитория; запуски/чекпоинты прошлых прогонов при этом
                // не мешают (идемпотентность и выборки идут по своим id).
                // Рестарт-тесты передают cleanDefinitions: false, чтобы
                // определение и приостановленный запуск пережили пересоздание.
                IChainDefinitionRepository definitions =
                    provider.GetRequiredService<IChainDefinitionRepository>();
                IReadOnlyList<ChainDefinitionDto> enabled =
                    await definitions.ListEnabledAsync(CancellationToken.None).ConfigureAwait(false);
                foreach (ChainDefinitionDto existing in enabled)
                {
                    await definitions.SetEnabledAsync(existing.Id, false, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                // Гигиена запусков: общая БД переживает убитые прогоны
                // (тестхосты, прибитые посреди E2E), оставляя нетерминальные
                // строки. Blanket-RecoverAsync рестарт-тестов поднимал бы
                // чужой висящий HITL (новый approval без аппрувера — вечный
                // waiter) и перевыполнял чужие defer-прогоны. Гасим всё
                // незавершённое как CancelledByCrash; свой приостановленный
                // запуск рестарт-тесты создают ПОСЛЕ этого места, а их второй
                // хост идёт с cleanDefinitions: false и зачистку пропускает.
                IChainExecutionRepository executions =
                    provider.GetRequiredService<IChainExecutionRepository>();
                IReadOnlyList<RunId> unfinished =
                    await executions.ListUnfinishedRunsAsync(CancellationToken.None).ConfigureAwait(false);
                var crashError = new ErrorDto
                {
                    Code = "Crash.CancelledByCrash",
                    Message = "Run cancelled by E2E fixture hygiene (leftover of a killed run).",
                };
                foreach (RunId staleRunId in unfinished)
                {
                    await executions.UpdateRunStatusAsync(
                        staleRunId, ChainRunStatus.CancelledByCrash, crashError, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            var validator = new NetrunnerPackageValidator();
            PackageValidationResult packageValidation = validator.ValidatePackage(packageDir);
            if (!packageValidation.IsValid || packageValidation.Manifest is null)
            {
                throw new InvalidOperationException(
                    "E2E fixture package is invalid: " +
                    string.Join("; ", packageValidation.Errors.Select(e => e.Message)));
            }

            IPackageRegistryRepository packages = provider.GetRequiredService<IPackageRegistryRepository>();
            string manifestHash = NetrunnerPackageValidator.ComputeSha256(
                Path.Combine(packageDir, "netrunner.manifest.json"));
            await packages.RegisterPackageAsync(packageValidation.Manifest, packageDir, manifestHash)
                .ConfigureAwait(false);

            var scripts = (CountingScriptRunnerRegistry)provider.GetRequiredService<IScriptRunnerRegistry>();
            return new ChainE2EHost(provider, skills, delivery, scripts, hitlCards);
        }
        catch (Exception)
        {
            await provider.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ChainDefinitionDto BuildDefinition(
        ChainId chainId,
        ChainTriggerDto trigger,
        string skillId,
        string deliveryTarget,
        bool requiresHitl = false)
    {
        return new ChainDefinitionDto
        {
            Id = chainId,
            Name = "E2E chain " + chainId.Value.ToString("N")[..8],
            IsEnabled = true,
            RequiresHitl = requiresHitl,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            RetryPolicy = new ChainRetryPolicyDto { MaxAttempts = 1, BackoffMs = 1 },
            Trigger = trigger,
            Skill = new ChainSkillStepDto
            {
                SkillId = skillId,
                InputMapping = new Dictionary<string, string> { ["text"] = "{{trigger}}" },
            },
            Script = new ChainScriptStepDto
            {
                PackageSlug = ChainPackageFixture.Slug,
                PackageVersion = ChainPackageFixture.Version,
            },
            Delivery = new ChainDeliveryStepDto
            {
                Channel = ChannelKind.Telegram,
                Target = deliveryTarget,
                MessageTemplate = "Done: {{script}}",
            },
        };
    }

    public async Task RegisterAsync(ChainDefinitionDto definition)
    {
        var validator = new ChainDefinitionValidator();
        ChainValidationResult validation = validator.Validate(definition);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Message)));
        await Definitions.UpsertAsync(definition).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
