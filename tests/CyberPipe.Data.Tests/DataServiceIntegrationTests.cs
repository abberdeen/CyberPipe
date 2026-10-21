using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CyberPipe.Data.Tests;

[Trait("Category", "Integration")]
public sealed class DataServiceIntegrationTests
{
    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteTestDatabase Database { get; private set; } = null!;

        public IDbContextFactory<CyberPipeDbContext> DbFactory => Database.DbFactory;

        public static async Task<Fixture> CreateAsync()
        {
            var fx = new Fixture { Database = await SqliteTestDatabase.CreateAsync() };
            return fx;
        }

        public async ValueTask DisposeAsync() => await Database.DisposeAsync();
    }

    private static Task<Fixture> CreateAsync() => Fixture.CreateAsync();

    private static ChainDefinitionDto Chain(string name)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ChainDefinitionDto
        {
            Id = ChainId.New(),
            Name = name,
            Trigger = new ChainTriggerDto { Kind = ChainTriggerKind.Webhook, WebhookPath = "/hook/" + name },
            Skill = new ChainSkillStepDto { SkillId = "skill-1" },
            Script = new ChainScriptStepDto { PackageSlug = "pkg", PackageVersion = "1.0.0" },
            Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Cli, Target = "owner", MessageTemplate = "done" },
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static TriggerEvent Trigger(ChainId chainId, string key = "k1") => new()
    {
        EventId = Guid.NewGuid(),
        ChainId = chainId,
        Kind = ChainTriggerKind.FunctionCall,
        OccurredAtUtc = DateTimeOffset.UtcNow,
        PayloadJson = "{}",
        Source = "test",
        IdempotencyKey = key,
    };

    private static async Task<(ChainDefinitionDto Chain, ChainRunRecord Run)> SeedChainAndRunAsync(
        ChainCatalogService chains, ChainRunRecordService runs, string name = "seed")
    {
        ChainDefinitionDto chain = await chains.AddAsync(Chain(name));
        ChainRunRecord run = await runs.AddAsync(new ChainRunRecord
        {
            ChainId = chain.Id,
            IdempotencyKey = "key-" + name,
            Status = ChainRunStatus.Running,
            Source = "test",
        });
        return (chain, run);
    }

    [SqliteIntegrationFact]
    public async Task Chain_CrudPaginationAndMissingSemantics()
    {
        await using Fixture fx = await CreateAsync();
        var chains = new ChainCatalogService(fx.DbFactory);
        Assert.Null(await chains.GetAsync(ChainId.New()));
        var added = new List<ChainDefinitionDto>();
        for (int i = 0; i < 5; i++)
            added.Add(await chains.AddAsync(Chain("chain-" + i)));

        // Идемпотентный повтор Add с тем же Id возвращает существующую без дубля.
        ChainDefinitionDto again = await chains.AddAsync(added[0] with { Name = "changed-on-repeat" });
        Assert.Equal(added[0].Name, again.Name);
        Assert.Equal(5, (await chains.ListAsync(new PagedQuery { Limit = 50 })).TotalCount);

        PagedResult<ChainDefinitionDto> page1 = await chains.ListAsync(new PagedQuery { Limit = 2, Offset = 0, SortBy = "name", Descending = false });
        PagedResult<ChainDefinitionDto> page2 = await chains.ListAsync(new PagedQuery { Limit = 2, Offset = 2, SortBy = "name", Descending = false });
        PagedResult<ChainDefinitionDto> page3 = await chains.ListAsync(new PagedQuery { Limit = 2, Offset = 4, SortBy = "name", Descending = false });
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(["chain-0", "chain-1"], page1.Items.Select(c => c.Name));
        Assert.Equal(["chain-2", "chain-3"], page2.Items.Select(c => c.Name));
        Assert.Equal(["chain-4"], page3.Items.Select(c => c.Name));
        Assert.Empty(page1.Items.Select(c => c.Id).Intersect(page2.Items.Select(c => c.Id)));

        ChainDefinitionDto updated = await chains.UpdateAsync(added[0] with { Name = "chain-renamed" });
        Assert.Equal("chain-renamed", (await chains.GetAsync(added[0].Id))!.Name);
        Assert.True(await chains.DeleteAsync(added[0].Id));
        Assert.False(await chains.DeleteAsync(added[0].Id));
        Assert.Null(await chains.GetAsync(added[0].Id));
        await Assert.ThrowsAsync<ArgumentException>(() => chains.ListAsync(new PagedQuery { SortBy = "name; DROP TABLE chains" }));
        // Обновление отсутствующей цепочки — конфликт конкуренции, а не молчаливое создание.
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => chains.UpdateAsync(added[0]));
    }

    [SqliteIntegrationFact]
    public async Task ChainCatalog_UpsertSetEnabledListEnabledAndGetById()
    {
        await using Fixture fx = await CreateAsync();
        var chains = new ChainCatalogService(fx.DbFactory);

        ChainDefinitionDto created = await chains.UpsertAsync(Chain("upsert-target"));
        ChainDefinitionDto reupserted = await chains.UpsertAsync(created with { Name = "upsert-renamed" });
        Assert.Equal(created.Id, reupserted.Id);
        Assert.Equal("upsert-renamed", reupserted.Name);

        ChainDefinitionDto? byId = await chains.GetByIdAsync(created.Id);
        Assert.NotNull(byId);
        Assert.Equal(reupserted.Id, byId.Id);
        Assert.Equal("upsert-renamed", byId.Name);
        Assert.Null(await chains.GetByIdAsync(ChainId.New()));

        Assert.True(await chains.SetEnabledAsync(created.Id, false));
        Assert.DoesNotContain(await chains.ListEnabledAsync(), c => c.Id == created.Id);
        Assert.True(await chains.SetEnabledAsync(created.Id, true));
        Assert.Contains(await chains.ListEnabledAsync(), c => c.Id == created.Id);
        Assert.False(await chains.SetEnabledAsync(ChainId.New(), true));
    }

    [SqliteIntegrationFact]
    public async Task RunCheckpointApprovalBudgetSettingsAudit_Crud()
    {
        await using Fixture fx = await CreateAsync();
        var chains = new ChainCatalogService(fx.DbFactory);
        var runs = new ChainRunRecordService(fx.DbFactory);
        var checkpoints = new CheckpointStoreService(fx.DbFactory);
        var approvals = new ApprovalLedgerService(fx.DbFactory);
        var budget = new TokenBudgetLedgerService(fx.DbFactory);
        var settings = new KvSettingsService(fx.DbFactory);
        var audit = new AuditLogService(fx.DbFactory);
        (ChainDefinitionDto chain, ChainRunRecord run) = await SeedChainAndRunAsync(chains, runs);

        // Идемпотентность запуска по (chain, idempotency_key).
        ChainRunRecord sameRun = await runs.AddAsync(new ChainRunRecord
        {
            ChainId = chain.Id, IdempotencyKey = "key-seed", Status = ChainRunStatus.Completed, Source = "other",
        });
        Assert.Equal(run.Id, sameRun.Id);
        Assert.Equal(1, (await runs.ListAsync(new PagedQuery(), chain.Id)).TotalCount);
        Assert.Null(await runs.GetAsync(new RunId(Guid.NewGuid())));
        Assert.True(await runs.DeleteAsync((await runs.AddAsync(new ChainRunRecord
        {
            ChainId = chain.Id, IdempotencyKey = "key-temp", Status = ChainRunStatus.Running, Source = "test",
        })).Id));

        var checkpointDto = new ChainCheckpointDto
        {
            RunId = run.Id, ChainId = chain.Id, Step = ChainStepKind.Skill, State = ChainStepState.Running,
            AttemptCount = 1, InputJson = "{\"a\":1}", CheckpointAtUtc = DateTimeOffset.UtcNow,
        };
        CheckpointRecord checkpoint = await checkpoints.AddAsync(checkpointDto);
        CheckpointRecord checkpointAgain = await checkpoints.AddAsync(checkpointDto with { State = ChainStepState.Completed });
        Assert.Equal(checkpoint.Id, checkpointAgain.Id);
        Assert.Equal(ChainStepState.Completed, (await checkpoints.GetByRunAndStepAsync(run.Id, ChainStepKind.Skill))!.Checkpoint.State);
        Assert.Null(await checkpoints.GetAsync(Guid.NewGuid()));
        Assert.True(await checkpoints.DeleteAsync(checkpoint.Id));
        Assert.False(await checkpoints.DeleteAsync(checkpoint.Id));

        ApprovalRecord approval = await approvals.AddAsync(new ApprovalRecord
        {
            RunId = run.Id, ChainId = chain.Id, Step = "Hitl", PayloadJson = "{\"x\":1}",
        });
        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        ApprovalRecord decided = await approvals.UpdateAsync(
            approval with { Status = ApprovalStatus.Approved, Decision = HitlDecision.Approved, DecidedAtUtc = DateTimeOffset.UtcNow },
            approval.RowVersion);
        Assert.Equal(ApprovalStatus.Approved, decided.Status);
        Assert.Single((await approvals.ListAsync(new PagedQuery(), ApprovalStatus.Approved)).Items);
        Assert.Null(await approvals.GetAsync(Guid.NewGuid()));
        Assert.True(await approvals.DeleteAsync(approval.Id));
        Assert.False(await approvals.DeleteAsync(approval.Id));

        TokenBudgetEntryRecord entry = await budget.AddUsageAsync(new TokenUsageDto
        {
            ModelId = "model-1", HostingType = ModelHostingType.RemoteCloud, InputTokens = 100, OutputTokens = 40,
            CostUsd = 0.02m, OccurredAtUtc = DateTimeOffset.UtcNow, RequestId = "req-1",
        });
        TokenBudgetDailySummary summary = await budget.GetDailySummaryAsync(entry.BudgetDate);
        Assert.Equal(0.02m, summary.TotalCostUsd);
        Assert.Equal(100, summary.TotalInputTokens);
        Assert.Equal(1, summary.EntryCount);
        Assert.True(await budget.DeleteAsync(entry.Id));
        Assert.False(await budget.DeleteAsync(entry.Id));

        KeyValueSettingRecord setting = await settings.AddAsync(new KeyValueSettingRecord { Key = "repo.test", ValueJson = "\"v1\"" });
        KeyValueSettingRecord settingAgain = await settings.AddAsync(new KeyValueSettingRecord { Key = "repo.test", ValueJson = "\"v2\"" });
        Assert.Equal("\"v1\"", settingAgain.ValueJson);
        KeyValueSettingRecord overwritten = await settings.SetAsync(new KeyValueSettingRecord { Key = "repo.test", ValueJson = "\"v2\"" });
        Assert.Equal("\"v2\"", overwritten.ValueJson);
        Assert.Null(await settings.GetAsync("repo.missing"));
        Assert.True(await settings.DeleteAsync("repo.test"));
        Assert.False(await settings.DeleteAsync("repo.test"));

        AuditLogRecord auditRecord = await audit.AddAsync(new AuditLogRecord
        {
            Actor = "owner", Action = "chain.created", EntityType = "chain", EntityId = chain.Id.ToString(), DetailsJson = "{\"ok\":true}",
        });
        Assert.True(auditRecord.Id > 0);
        Assert.Equal("chain.created", (await audit.GetAsync(auditRecord.Id))!.Action);
        Assert.Single((await audit.ListAsync(new PagedQuery(), actor: "owner")).Items);
        Assert.Null(await audit.GetAsync(long.MaxValue));
        Assert.True(await audit.UpdateAsync(auditRecord with { DetailsJson = "{\"ok\":false}" }));
        Assert.False(await audit.UpdateAsync(auditRecord with { Id = long.MaxValue }));
        Assert.True(await audit.DeleteAsync(auditRecord.Id));
        Assert.False(await audit.DeleteAsync(auditRecord.Id));
    }

    [SqliteIntegrationFact]
    public async Task OptimisticConcurrency_StaleVersion_Throws()
    {
        await using Fixture fx = await CreateAsync();
        var approvals = new ApprovalLedgerService(fx.DbFactory);
        var settings = new KvSettingsService(fx.DbFactory);
        ApprovalRecord approval = await approvals.AddAsync(new ApprovalRecord { Step = "Hitl" });
        ApprovalRecord first = await approvals.UpdateAsync(approval with { Status = ApprovalStatus.Approved }, approval.RowVersion);
        Assert.NotEqual(approval.RowVersion, first.RowVersion);
        OptimisticConcurrencyException ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            approvals.UpdateAsync(approval with { Status = ApprovalStatus.Rejected }, approval.RowVersion));
        Assert.Equal("approvals", ex.Aggregate);
        Assert.Equal(ApprovalStatus.Approved, (await approvals.GetAsync(approval.Id))!.Status);

        KeyValueSettingRecord setting = await settings.SetAsync(new KeyValueSettingRecord { Key = "occ.test", ValueJson = "1" });
        KeyValueSettingRecord fresh = await settings.UpdateAsync(setting with { ValueJson = "2" }, setting.RowVersion);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            settings.UpdateAsync(setting with { ValueJson = "3" }, setting.RowVersion));
        Assert.Equal("2", (await settings.GetAsync("occ.test"))!.ValueJson);
    }

    [SqliteIntegrationFact]
    public async Task ExecutionStore_MultiWrite_StaysConsistent()
    {
        await using Fixture fx = await CreateAsync();
        var chains = new ChainCatalogService(fx.DbFactory);
        var runs = new ChainRunRecordService(fx.DbFactory);
        var checkpoints = new CheckpointStoreService(fx.DbFactory);
        var exec = new ChainExecutionStoreService(runs, checkpoints);
        ChainDefinitionDto chain = await chains.AddAsync(Chain("exec-multi"));

        var runId = new RunId(Guid.NewGuid());
        ChainRunState created = await exec.CreateRunAsync(runId, chain.Id, Trigger(chain.Id));
        Assert.Equal(ChainRunStatus.Running, created.Status);

        // Идемпотентность: повтор с тем же ключом возвращает тот же запуск.
        ChainRunState again = await exec.CreateRunAsync(new RunId(Guid.NewGuid()), chain.Id, Trigger(chain.Id));
        Assert.Equal(runId, again.RunId);

        await exec.SaveCheckpointAsync(new ChainCheckpointDto
        {
            RunId = runId, ChainId = chain.Id, Step = ChainStepKind.Script,
            State = ChainStepState.Completed, CheckpointAtUtc = DateTimeOffset.UtcNow,
        });
        await exec.UpdateRunStatusAsync(runId, ChainRunStatus.Completed);

        ChainRunState? loaded = await exec.GetRunAsync(runId);
        Assert.NotNull(loaded);
        Assert.Equal(ChainRunStatus.Completed, loaded.Status);
        Assert.NotNull(loaded.FinishedAtUtc);
        Assert.Single(loaded.Checkpoints);
        Assert.Equal(ChainStepKind.Script, loaded.Checkpoints[0].Step);
        Assert.Empty(await exec.ListUnfinishedRunsAsync());
        Assert.Null(await exec.GetRunAsync(new RunId(Guid.NewGuid())));
    }

    [SqliteIntegrationFact]
    public async Task CancelledMethod_ThrowsAndLeavesNoRow()
    {
        await using Fixture fx = await CreateAsync();
        var chains = new ChainCatalogService(fx.DbFactory);
        ChainDefinitionDto chain = Chain("cancelled-commit");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chains.AddAsync(chain, cts.Token));
        Assert.Null(await chains.GetAsync(chain.Id));
    }

    [SqliteIntegrationFact]
    public async Task Injection_IsStoredLiterally_AndSlowLog_HasNoSecretValues()
    {
        await using Fixture fx = await CreateAsync();
        var chainsLogger = new CapturingLogger<ChainCatalogService>();
        var settingsLogger = new CapturingLogger<KvSettingsService>();
        var metrics = new DataServiceMetrics();
        var options = new RepositoryOptions { SlowQueryThreshold = TimeSpan.Zero };
        var chains = new ChainCatalogService(fx.DbFactory, options, chainsLogger, metrics);
        var settings = new KvSettingsService(fx.DbFactory, options, settingsLogger, metrics);
        const string injection = "x' OR 1=1; DROP TABLE chains; --";
        ChainDefinitionDto chain = await chains.AddAsync(Chain(injection));
        Assert.Equal(injection, (await chains.GetAsync(chain.Id))!.Name);
        Assert.Equal(1, (await chains.ListAsync(new PagedQuery())).TotalCount);

        const string secretValue = "top-secret-value-123";
        await settings.SetAsync(new KeyValueSettingRecord { Key = "secret.check", ValueJson = $"\"{secretValue}\"" });
        List<string> all = [.. chainsLogger.Messages, .. settingsLogger.Messages];
        Assert.NotEmpty(all);
        Assert.Contains(all, m => m.Contains("chains.", StringComparison.Ordinal));
        Assert.Contains(all, m => m.Contains("settings.", StringComparison.Ordinal));
        Assert.True(metrics.SlowQueryCount > 0);
        Assert.DoesNotContain(all, m => m.Contains(secretValue, StringComparison.Ordinal));
        Assert.DoesNotContain(all, m => m.Contains("chain.created", StringComparison.Ordinal) && m.Contains(secretValue, StringComparison.Ordinal));
    }

    [SqliteIntegrationFact]
    public async Task HitlApprovalStoreService_AtomicTryResolve_OnlyOneSucceeds()
    {
        await using Fixture fx = await CreateAsync();
        var hitl = new HitlApprovalStoreService(fx.DbFactory);
        await hitl.EnsureTableCreatedAsync();

        var approvalId = Guid.NewGuid();
        var runId = new RunId(Guid.NewGuid());
        var chainId = new ChainId(Guid.NewGuid());
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var record = new HitlApprovalRecord
        {
            ApprovalId = approvalId,
            RunId = runId,
            ChainId = chainId,
            Title = "SQLite HITL Test",
            PreviewText = "Preview data",
            PayloadJson = "{\"count\": 10}",
            Status = HitlStatus.Pending,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(2),
        };

        await hitl.CreateAsync(record);
        HitlApprovalRecord? created = await hitl.GetAsync(approvalId);
        Assert.NotNull(created);
        Assert.Equal(HitlStatus.Pending, created.Status);

        Task<bool> task1 = Task.Run(() => hitl.TryResolveAsync(approvalId, new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Approved,
            OwnerId = "owner-tg",
            Channel = ChannelKind.Telegram,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        }));

        Task<bool> task2 = Task.Run(() => hitl.TryResolveAsync(approvalId, new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Rejected,
            Reason = "Late cancel",
            OwnerId = "owner-desktop",
            Channel = ChannelKind.Desktop,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        }));

        bool[] results = await Task.WhenAll(task1, task2);

        Assert.Single(results, r => r);
        Assert.Single(results, r => !r);

        HitlApprovalRecord? resolved = await hitl.GetAsync(approvalId);
        Assert.NotNull(resolved);
        Assert.Equal(HitlStatus.Resolved, resolved.Status);
    }

    [SqliteIntegrationFact]
    public async Task EnsureTableCreated_OnFreshDatabase_BuildsEfSchema()
    {
        // Свежая база без SQL-каталога: схему целиком создаёт EF-миграция InitialCreate.
        await using SqliteTestDatabase db = await SqliteTestDatabase.CreateBootstrappedAsync();
        var hitl = new HitlApprovalStoreService(db.DbFactory);
        await hitl.EnsureTableCreatedAsync();

        var chains = new ChainCatalogService(db.DbFactory);
        ChainDefinitionDto saved = await chains.AddAsync(Chain("fresh-db"));
        Assert.NotNull(await chains.GetAsync(saved.Id));

        var notes = new NoteStoreService(db.DbFactory);
        NoteRecord created = await notes.CreateAsync("Fresh", "ef-only schema");
        Assert.NotNull(await notes.GetAsync(created.NoteId));
    }
}
