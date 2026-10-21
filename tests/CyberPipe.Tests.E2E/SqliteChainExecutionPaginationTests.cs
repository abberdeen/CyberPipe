namespace CyberPipe.Tests.E2E;

using CyberPipe.Contracts;
using CyberPipe.Data;
using Xunit;

/// <summary>
/// Пагинация <see cref="ChainExecutionStoreService"/> обязана заканчиваться:
/// чтение идёт страницами и останавливается по TotalCount.
/// </summary>
public sealed class SqliteChainExecutionPaginationTests : IAsyncLifetime
{
    private static readonly ChainId Chain = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

    private TestDatabase? _db;

    private ChainExecutionStoreService Executions => _db!.Executions;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
        await _db.Catalog.UpsertAsync(new ChainDefinitionDto
        {
            Id = Chain,
            Name = "Pagination chain",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            RetryPolicy = new ChainRetryPolicyDto { MaxAttempts = 1, BackoffMs = 1 },
            Trigger = new ChainTriggerDto { Kind = ChainTriggerKind.FunctionCall, FunctionName = "pagination.test" },
            Skill = new ChainSkillStepDto { SkillId = "test.skill", InputMapping = new Dictionary<string, string>() },
            Script = new ChainScriptStepDto { PackageSlug = "pkg", PackageVersion = "1.0.0" },
            Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Cli, Target = "test", MessageTemplate = "done" },
        });
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task FindRunByIdempotencyKey_finds_run_on_second_page()
    {
        var expected = new RunId(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        for (int i = 0; i < 200; i++)
        {
            await AddRunAsync(new RunId(Guid.NewGuid()), "other", ChainRunStatus.Completed);
        }

        await AddRunAsync(expected, "target", ChainRunStatus.Completed);

        RunId? found = await Executions.FindRunByIdempotencyKeyAsync(Chain, "target");

        Assert.Equal(expected, found);
    }

    [Fact(Timeout = 30000)]
    public async Task FindRunByIdempotencyKey_stops_on_exact_page_boundary()
    {
        for (int i = 0; i < 200; i++)
        {
            await AddRunAsync(new RunId(Guid.NewGuid()), "other", ChainRunStatus.Completed);
        }

        RunId? found = await Executions.FindRunByIdempotencyKeyAsync(Chain, "missing");

        Assert.Null(found);
    }

    [Fact(Timeout = 30000)]
    public async Task ListUnfinishedRuns_returns_only_unfinished()
    {
        RunId running = new(Guid.NewGuid());
        await AddRunAsync(running, "running-key", ChainRunStatus.Running);
        await AddRunAsync(new RunId(Guid.NewGuid()), "done-key", ChainRunStatus.Completed);
        await AddRunAsync(new RunId(Guid.NewGuid()), "failed-key", ChainRunStatus.Failed);

        IReadOnlyList<RunId> unfinished = await Executions.ListUnfinishedRunsAsync();

        RunId only = Assert.Single(unfinished);
        Assert.Equal(running, only);
    }

    private async Task AddRunAsync(RunId id, string idempotencyKey, ChainRunStatus status)
    {
        await _db!.Runs.AddAsync(new ChainRunRecord
        {
            Id = id,
            ChainId = Chain,
            IdempotencyKey = idempotencyKey,
            Status = status,
            Source = "pagination-test",
            StartedAtUtc = DateTimeOffset.UtcNow,
        });
    }
}
