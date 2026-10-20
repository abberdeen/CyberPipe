using CyberPipe.Contracts;
using Xunit;

namespace CyberPipe.Data.Tests;

public sealed class PostgresChainExecutionRepositoryTests
{
    private sealed class FakeRunRepository : IChainRunRepository
    {
        public Dictionary<RunId, ChainRunRecord> Runs { get; } = new();

        public Task<ChainRunRecord?> GetAsync(RunId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Runs.TryGetValue(id, out ChainRunRecord? r) ? r : null);

        public Task<PagedResult<ChainRunRecord>> ListAsync(PagedQuery query, ChainId? chainId = null, CancellationToken cancellationToken = default)
        {
            List<ChainRunRecord> items = Runs.Values
                .Where(r => chainId is null || r.ChainId == chainId.Value)
                .Skip(query.Offset).Take(query.Limit).ToList();
            return Task.FromResult(new PagedResult<ChainRunRecord> { Items = items, TotalCount = items.Count, Offset = query.Offset, Limit = query.Limit });
        }

        public Task<ChainRunRecord> AddAsync(ChainRunRecord run, CancellationToken cancellationToken = default)
        {
            Runs[run.Id] = run;
            return Task.FromResult(run);
        }

        public Task<ChainRunRecord> UpdateAsync(ChainRunRecord run, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
        {
            Runs[run.Id] = run with { RowVersion = DateTimeOffset.UtcNow };
            return Task.FromResult(Runs[run.Id]);
        }

        public Task<bool> DeleteAsync(RunId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Runs.Remove(id));
    }

    private sealed class FakeCheckpointRepository : ICheckpointRepository
    {
        public Dictionary<(RunId, ChainStepKind), CheckpointRecord> Items { get; } = new();

        public Task<CheckpointRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CheckpointRecord?>(null);

        public Task<CheckpointRecord?> GetByRunAndStepAsync(RunId runId, ChainStepKind step, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.TryGetValue((runId, step), out CheckpointRecord? r) ? r : null);

        public Task<PagedResult<CheckpointRecord>> ListAsync(PagedQuery query, RunId? runId = null, CancellationToken cancellationToken = default)
        {
            List<CheckpointRecord> items = Items.Values.Where(r => runId is null || r.Checkpoint.RunId == runId.Value).ToList();
            return Task.FromResult(new PagedResult<CheckpointRecord> { Items = items, TotalCount = items.Count, Offset = query.Offset, Limit = query.Limit });
        }

        public Task<CheckpointRecord> AddAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default)
        {
            var record = new CheckpointRecord { Id = Guid.NewGuid(), Checkpoint = checkpoint, CreatedAtUtc = DateTimeOffset.UtcNow, RowVersion = DateTimeOffset.UtcNow };
            Items[(checkpoint.RunId, checkpoint.Step)] = record;
            return Task.FromResult(record);
        }

        public Task<CheckpointRecord> UpdateAsync(CheckpointRecord record, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
        {
            Items[(record.Checkpoint.RunId, record.Checkpoint.Step)] = record;
            return Task.FromResult(record);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
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

    [Fact]
    public async Task PostgresChainExecution_CreateCheckpointStatus_RoundTrip()
    {
        var repo = new PostgresChainExecutionRepository(new FakeRunRepository(), new FakeCheckpointRepository());
        var chainId = new ChainId(Guid.NewGuid());
        var runId = new RunId(Guid.NewGuid());

        ChainRunState created = await repo.CreateRunAsync(runId, chainId, Trigger(chainId));
        Assert.Equal(ChainRunStatus.Running, created.Status);

        // Идемпотентность: повтор с тем же ключом возвращает тот же запуск.
        ChainRunState again = await repo.CreateRunAsync(new RunId(Guid.NewGuid()), chainId, Trigger(chainId));
        Assert.Equal(runId, again.RunId);

        await repo.SaveCheckpointAsync(new ChainCheckpointDto
        {
            RunId = runId, ChainId = chainId, Step = ChainStepKind.Script,
            State = ChainStepState.Completed, CheckpointAtUtc = DateTimeOffset.UtcNow,
        });
        await repo.UpdateRunStatusAsync(runId, ChainRunStatus.Completed);

        ChainRunState? loaded = await repo.GetRunAsync(runId);
        Assert.NotNull(loaded);
        Assert.Equal(ChainRunStatus.Completed, loaded.Status);
        Assert.NotNull(loaded.FinishedAtUtc);
        Assert.Single(loaded.Checkpoints);
        Assert.Equal(ChainStepKind.Script, loaded.Checkpoints[0].Step);
        Assert.Empty(await repo.ListUnfinishedRunsAsync());
    }
}
