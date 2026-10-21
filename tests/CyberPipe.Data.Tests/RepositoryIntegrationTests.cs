using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CyberPipe.Data.Tests;

[Trait("Category", "Integration")]
public sealed class RepositoryIntegrationTests
{
    internal sealed class CapturingDataLogger : ILogger
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

        public RepositorySet Repos(RepositoryOptions? options = null, ILogger? logger = null, UnitOfWorkMetrics? metrics = null) =>
            RepositorySet.Create(DbFactory, options: options, logger: logger, metrics: metrics);

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

    private static async Task<(ChainDefinitionDto Chain, ChainRunRecord Run)> SeedChainAndRunAsync(RepositorySet repos, string name = "seed")
    {
        ChainDefinitionDto chain = await repos.Chains.AddAsync(Chain(name));
        ChainRunRecord run = await repos.ChainRuns.AddAsync(new ChainRunRecord
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
        RepositorySet repos = fx.Repos();
        Assert.Null(await repos.Chains.GetAsync(ChainId.New()));
        var added = new List<ChainDefinitionDto>();
        for (int i = 0; i < 5; i++)
            added.Add(await repos.Chains.AddAsync(Chain("chain-" + i)));

        // Идемпотентный повтор Add с тем же Id возвращает существующую без дубля.
        ChainDefinitionDto again = await repos.Chains.AddAsync(added[0] with { Name = "changed-on-repeat" });
        Assert.Equal(added[0].Name, again.Name);
        Assert.Equal(5, (await repos.Chains.ListAsync(new PagedQuery { Limit = 50 })).TotalCount);

        PagedResult<ChainDefinitionDto> page1 = await repos.Chains.ListAsync(new PagedQuery { Limit = 2, Offset = 0, SortBy = "name", Descending = false });
        PagedResult<ChainDefinitionDto> page2 = await repos.Chains.ListAsync(new PagedQuery { Limit = 2, Offset = 2, SortBy = "name", Descending = false });
        PagedResult<ChainDefinitionDto> page3 = await repos.Chains.ListAsync(new PagedQuery { Limit = 2, Offset = 4, SortBy = "name", Descending = false });
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(["chain-0", "chain-1"], page1.Items.Select(c => c.Name));
        Assert.Equal(["chain-2", "chain-3"], page2.Items.Select(c => c.Name));
        Assert.Equal(["chain-4"], page3.Items.Select(c => c.Name));
        Assert.Empty(page1.Items.Select(c => c.Id).Intersect(page2.Items.Select(c => c.Id)));

        ChainDefinitionDto updated = await repos.Chains.UpdateAsync(added[0] with { Name = "chain-renamed" });
        Assert.Equal("chain-renamed", (await repos.Chains.GetAsync(added[0].Id))!.Name);
        Assert.True(await repos.Chains.DeleteAsync(added[0].Id));
        Assert.False(await repos.Chains.DeleteAsync(added[0].Id));
        Assert.Null(await repos.Chains.GetAsync(added[0].Id));
        await Assert.ThrowsAsync<ArgumentException>(() => repos.Chains.ListAsync(new PagedQuery { SortBy = "name; DROP TABLE chains" }));
    }

    [SqliteIntegrationFact]
    public async Task RunCheckpointApprovalBudgetSettingsAudit_Crud()
    {
        await using Fixture fx = await CreateAsync();
        RepositorySet repos = fx.Repos();
        (ChainDefinitionDto chain, ChainRunRecord run) = await SeedChainAndRunAsync(repos);

        // Идемпотентность запуска по (chain, idempotency_key).
        ChainRunRecord sameRun = await repos.ChainRuns.AddAsync(new ChainRunRecord
        {
            ChainId = chain.Id, IdempotencyKey = "key-seed", Status = ChainRunStatus.Completed, Source = "other",
        });
        Assert.Equal(run.Id, sameRun.Id);
        Assert.Equal(1, (await repos.ChainRuns.ListAsync(new PagedQuery(), chain.Id)).TotalCount);

        var checkpointDto = new ChainCheckpointDto
        {
            RunId = run.Id, ChainId = chain.Id, Step = ChainStepKind.Skill, State = ChainStepState.Running,
            AttemptCount = 1, InputJson = "{\"a\":1}", CheckpointAtUtc = DateTimeOffset.UtcNow,
        };
        CheckpointRecord checkpoint = await repos.Checkpoints.AddAsync(checkpointDto);
        CheckpointRecord checkpointAgain = await repos.Checkpoints.AddAsync(checkpointDto with { State = ChainStepState.Completed });
        Assert.Equal(checkpoint.Id, checkpointAgain.Id);
        Assert.Equal(ChainStepState.Completed, (await repos.Checkpoints.GetByRunAndStepAsync(run.Id, ChainStepKind.Skill))!.Checkpoint.State);
        Assert.Null(await repos.Checkpoints.GetAsync(Guid.NewGuid()));
        Assert.True(await repos.Checkpoints.DeleteAsync(checkpoint.Id));
        Assert.False(await repos.Checkpoints.DeleteAsync(checkpoint.Id));

        ApprovalRecord approval = await repos.Approvals.AddAsync(new ApprovalRecord
        {
            RunId = run.Id, ChainId = chain.Id, Step = "Hitl", PayloadJson = "{\"x\":1}",
        });
        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        ApprovalRecord decided = await repos.Approvals.UpdateAsync(
            approval with { Status = ApprovalStatus.Approved, Decision = HitlDecision.Approved, DecidedAtUtc = DateTimeOffset.UtcNow },
            approval.RowVersion);
        Assert.Equal(ApprovalStatus.Approved, decided.Status);
        Assert.Single((await repos.Approvals.ListAsync(new PagedQuery(), ApprovalStatus.Approved)).Items);
        Assert.Null(await repos.Approvals.GetAsync(Guid.NewGuid()));
        Assert.True(await repos.Approvals.DeleteAsync(approval.Id));
        Assert.False(await repos.Approvals.DeleteAsync(approval.Id));

        TokenBudgetEntryRecord entry = await repos.TokenBudget.AddUsageAsync(new TokenUsageDto
        {
            ModelId = "model-1", HostingType = ModelHostingType.RemoteCloud, InputTokens = 100, OutputTokens = 40,
            CostUsd = 0.02m, OccurredAtUtc = DateTimeOffset.UtcNow, RequestId = "req-1",
        });
        TokenBudgetDailySummary summary = await repos.TokenBudget.GetDailySummaryAsync(entry.BudgetDate);
        Assert.Equal(0.02m, summary.TotalCostUsd);
        Assert.Equal(100, summary.TotalInputTokens);
        Assert.Equal(1, summary.EntryCount);
        Assert.True(await repos.TokenBudget.DeleteAsync(entry.Id));
        Assert.False(await repos.TokenBudget.DeleteAsync(entry.Id));

        KeyValueSettingRecord setting = await repos.Settings.AddAsync(new KeyValueSettingRecord { Key = "repo.test", ValueJson = "\"v1\"" });
        KeyValueSettingRecord settingAgain = await repos.Settings.AddAsync(new KeyValueSettingRecord { Key = "repo.test", ValueJson = "\"v2\"" });
        Assert.Equal("\"v1\"", settingAgain.ValueJson);
        KeyValueSettingRecord overwritten = await repos.Settings.SetAsync(new KeyValueSettingRecord { Key = "repo.test", ValueJson = "\"v2\"" });
        Assert.Equal("\"v2\"", overwritten.ValueJson);
        Assert.Null(await repos.Settings.GetAsync("repo.missing"));
        Assert.True(await repos.Settings.DeleteAsync("repo.test"));
        Assert.False(await repos.Settings.DeleteAsync("repo.test"));

        AuditLogRecord audit = await repos.AuditLog.AddAsync(new AuditLogRecord
        {
            Actor = "owner", Action = "chain.created", EntityType = "chain", EntityId = chain.Id.ToString(), DetailsJson = "{\"ok\":true}",
        });
        Assert.True(audit.Id > 0);
        Assert.Equal("chain.created", (await repos.AuditLog.GetAsync(audit.Id))!.Action);
        Assert.Single((await repos.AuditLog.ListAsync(new PagedQuery(), actor: "owner")).Items);
        Assert.Null(await repos.AuditLog.GetAsync(long.MaxValue));
        Assert.True(await repos.AuditLog.DeleteAsync(audit.Id));
        Assert.False(await repos.AuditLog.DeleteAsync(audit.Id));
    }

    [SqliteIntegrationFact]
    public async Task OptimisticConcurrency_StaleVersion_Throws()
    {
        await using Fixture fx = await CreateAsync();
        RepositorySet repos = fx.Repos();
        ApprovalRecord approval = await repos.Approvals.AddAsync(new ApprovalRecord { Step = "Hitl" });
        ApprovalRecord first = await repos.Approvals.UpdateAsync(approval with { Status = ApprovalStatus.Approved }, approval.RowVersion);
        Assert.NotEqual(approval.RowVersion, first.RowVersion);
        OptimisticConcurrencyException ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            repos.Approvals.UpdateAsync(approval with { Status = ApprovalStatus.Rejected }, approval.RowVersion));
        Assert.Equal("approvals", ex.Aggregate);
        Assert.Equal(ApprovalStatus.Approved, (await repos.Approvals.GetAsync(approval.Id))!.Status);

        KeyValueSettingRecord setting = await repos.Settings.SetAsync(new KeyValueSettingRecord { Key = "occ.test", ValueJson = "1" });
        KeyValueSettingRecord fresh = await repos.Settings.UpdateAsync(setting with { ValueJson = "2" }, setting.RowVersion);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            repos.Settings.UpdateAsync(setting with { ValueJson = "3" }, setting.RowVersion));
        Assert.Equal("2", (await repos.Settings.GetAsync("occ.test"))!.ValueJson);
    }

    [SqliteIntegrationFact]
    public async Task UnitOfWork_TwoRepositories_RollbackLeavesNeither_AndCommitLeavesBoth()
    {
        await using Fixture fx = await CreateAsync();
        var metrics = new UnitOfWorkMetrics();
        RepositorySet repos = fx.Repos(metrics: metrics);
        var factory = new UnitOfWorkFactory(fx.DbFactory, repos);
        ChainDefinitionDto doomed = Chain("doomed");

        long rolledBefore = metrics.RolledBackTransactions;
        await using (IUnitOfWork uow = await factory.CreateAsync())
        {
            await uow.BeginAsync();
            await uow.Chains.AddAsync(doomed);
            await uow.AuditLog.AddAsync(new AuditLogRecord { Actor = "owner", Action = "doomed.action" });
            // Исключение после первой записи: выход из using без коммита откатывает обе записи.
        }
        Assert.Null(await repos.Chains.GetAsync(doomed.Id));
        Assert.Equal(0, (await repos.AuditLog.ListAsync(new PagedQuery(), action: "doomed.action")).TotalCount);
        Assert.True(metrics.RolledBackTransactions > rolledBefore);

        long committedBefore = metrics.CommittedTransactions;
        ChainDefinitionDto kept = Chain("kept");
        await using (IUnitOfWork uow = await factory.CreateAsync())
        {
            await uow.BeginAsync();
            await uow.Chains.AddAsync(kept);
            await uow.AuditLog.AddAsync(new AuditLogRecord { Actor = "owner", Action = "kept.action" });
            await uow.CommitAsync();
            Assert.True(uow.IsCompleted);
        }
        Assert.NotNull(await repos.Chains.GetAsync(kept.Id));
        Assert.Equal(1, (await repos.AuditLog.ListAsync(new PagedQuery(), action: "kept.action")).TotalCount);
        Assert.True(metrics.CommittedTransactions > committedBefore);
    }

    [SqliteIntegrationFact]
    public async Task UnitOfWork_CancelledCommit_RollsBack()
    {
        await using Fixture fx = await CreateAsync();
        RepositorySet repos = fx.Repos();
        var factory = new UnitOfWorkFactory(fx.DbFactory, repos);
        ChainDefinitionDto chain = Chain("cancelled-commit");
        using var cts = new CancellationTokenSource();
        await using IUnitOfWork uow = await factory.CreateAsync();
        await uow.BeginAsync();
        await uow.Chains.AddAsync(chain);
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => uow.CommitAsync(cts.Token));
        Assert.Null(await repos.Chains.GetAsync(chain.Id));
    }

    [SqliteIntegrationFact]
    public async Task Injection_IsStoredLiterally_AndSlowLog_HasNoSecretValues()
    {
        await using Fixture fx = await CreateAsync();
        var logger = new CapturingDataLogger();
        RepositorySet repos = fx.Repos(new RepositoryOptions { SlowQueryThreshold = TimeSpan.Zero }, logger);
        const string injection = "x' OR 1=1; DROP TABLE chains; --";
        ChainDefinitionDto chain = await repos.Chains.AddAsync(Chain(injection));
        Assert.Equal(injection, (await repos.Chains.GetAsync(chain.Id))!.Name);
        Assert.Equal(1, (await repos.Chains.ListAsync(new PagedQuery())).TotalCount);

        const string secretValue = "top-secret-value-123";
        await repos.Settings.SetAsync(new KeyValueSettingRecord { Key = "secret.check", ValueJson = $"\"{secretValue}\"" });
        Assert.NotEmpty(logger.Messages);
        Assert.Contains(logger.Messages, m => m.Contains("settings.", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Contains(secretValue, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("chain.created", StringComparison.Ordinal) && m.Contains(secretValue, StringComparison.Ordinal));
    }

    [SqliteIntegrationFact]
    public async Task SqliteHitlApprovalRepository_AtomicTryResolve_OnlyOneSucceeds()
    {
        await using Fixture fx = await CreateAsync();
        var hitlRepo = new SqliteHitlApprovalRepository(fx.DbFactory);
        await hitlRepo.EnsureTableCreatedAsync();

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

        await hitlRepo.CreateAsync(record);
        HitlApprovalRecord? created = await hitlRepo.GetAsync(approvalId);
        Assert.NotNull(created);
        Assert.Equal(HitlStatus.Pending, created.Status);

        Task<bool> task1 = Task.Run(() => hitlRepo.TryResolveAsync(approvalId, new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Approved,
            OwnerId = "owner-tg",
            Channel = ChannelKind.Telegram,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        }));

        Task<bool> task2 = Task.Run(() => hitlRepo.TryResolveAsync(approvalId, new HitlApprovalResult
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

        HitlApprovalRecord? resolved = await hitlRepo.GetAsync(approvalId);
        Assert.NotNull(resolved);
        Assert.Equal(HitlStatus.Resolved, resolved.Status);
    }

    [SqliteIntegrationFact]
    public async Task EnsureTableCreated_OnFreshDatabase_BuildsEfSchema()
    {
        // Свежая база без SQL-каталога: схему целиком создаёт EF-миграция InitialCreate.
        await using SqliteTestDatabase db = await SqliteTestDatabase.CreateBootstrappedAsync();
        var hitlRepo = new SqliteHitlApprovalRepository(db.DbFactory);
        await hitlRepo.EnsureTableCreatedAsync();

        RepositorySet repos = RepositorySet.Create(db.DbFactory);
        ChainDefinitionDto saved = await repos.Chains.AddAsync(Chain("fresh-db"));
        Assert.NotNull(await repos.Chains.GetAsync(saved.Id));

        var note = new SqliteNoteRepository(db.DbFactory);
        NoteRecord created = await note.CreateAsync("Fresh", "ef-only schema");
        Assert.NotNull(await note.GetAsync(created.NoteId));
    }
}
