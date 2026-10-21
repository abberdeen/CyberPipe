using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CyberPipe.Data.Tests;

public sealed class SqliteChainExecutionRepositoryTests
{
    private static async Task<(SqliteTestDatabase Db, ChainExecutionStoreService Exec)> CreateAsync()
    {
        SqliteTestDatabase db = await SqliteTestDatabase.CreateAsync();
        var runs = new ChainRunRecordService(db.DbFactory);
        var checkpoints = new CheckpointStoreService(db.DbFactory);
        return (db, new ChainExecutionStoreService(runs, checkpoints));
    }

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

    [SqliteIntegrationFact]
    public async Task SqliteChainExecution_CreateCheckpointStatus_RoundTrip()
    {
        (SqliteTestDatabase db, ChainExecutionStoreService exec) = await CreateAsync();
        await using var _ = db;
        ChainDefinitionDto chain = await new ChainCatalogService(db.DbFactory).AddAsync(Chain("exec-roundtrip"));
        ChainId chainId = chain.Id;
        var runId = new RunId(Guid.NewGuid());

        ChainRunState created = await exec.CreateRunAsync(runId, chainId, Trigger(chainId));
        Assert.Equal(ChainRunStatus.Running, created.Status);

        // Идемпотентность: повтор с тем же ключом возвращает тот же запуск.
        ChainRunState again = await exec.CreateRunAsync(new RunId(Guid.NewGuid()), chainId, Trigger(chainId));
        Assert.Equal(runId, again.RunId);

        await exec.SaveCheckpointAsync(new ChainCheckpointDto
        {
            RunId = runId, ChainId = chainId, Step = ChainStepKind.Script,
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
    }

    [SqliteIntegrationFact]
    public async Task ListUnfinishedRuns_PagesWithinMaxLimit()
    {
        (SqliteTestDatabase db, ChainExecutionStoreService exec) = await CreateAsync();
        await using var _ = db;
        ChainDefinitionDto chain = await new ChainCatalogService(db.DbFactory).AddAsync(Chain("exec-paging"));
        ChainId chainId = chain.Id;
        for (int i = 0; i < 201; i++)
            await exec.CreateRunAsync(new RunId(Guid.NewGuid()), chainId, Trigger(chainId, $"k{i}"));

        IReadOnlyList<RunId> unfinished = await exec.ListUnfinishedRunsAsync();

        Assert.Equal(201, unfinished.Count);
        Assert.Equal(201, unfinished.Distinct().Count());
    }
}
