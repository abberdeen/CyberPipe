namespace CyberPipe.Tests.E2E;

using CyberPipe.Contracts;
using CyberPipe.Data;
using Xunit;

/// <summary>
/// Пагинация <see cref="StubChainExecutionRepository"/> обязана заканчиваться:
/// полная страница при завышенном TotalCount раньше крутила цикл без выхода.
/// </summary>
public sealed class SqliteChainExecutionPaginationTests
{
    private static readonly ChainId Chain = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

    [Fact(Timeout = 5000)]
    public async Task FindRunByIdempotencyKey_stops_when_every_page_is_full()
    {
        var runs = new ScriptedChainRunRepository(_ => FullPage(Chain, "other"));
        var repository = new StubChainExecutionRepository(runs, new UnusedCheckpointRepository());

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.FindRunByIdempotencyKeyAsync(Chain, "missing"));

        Assert.Equal(StubChainExecutionRepository.MaxRunListPages, runs.ListCalls);
        Assert.Contains(StubChainExecutionRepository.MaxRunListPages.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task ListUnfinishedRuns_stops_when_every_page_is_full()
    {
        var runs = new ScriptedChainRunRepository(_ => FullPage(Chain, "other", ChainRunStatus.Running));
        var repository = new StubChainExecutionRepository(runs, new UnusedCheckpointRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.ListUnfinishedRunsAsync());

        Assert.Equal(StubChainExecutionRepository.MaxRunListPages, runs.ListCalls);
    }

    [Fact]
    public async Task FindRunByIdempotencyKey_stops_on_exact_page_boundary()
    {
        var runs = new ScriptedChainRunRepository(_ => FullPage(Chain, "other", totalCount: StubChainExecutionRepository.RunPageSize));
        var repository = new StubChainExecutionRepository(runs, new UnusedCheckpointRepository());

        RunId? found = await repository.FindRunByIdempotencyKeyAsync(Chain, "missing");

        Assert.Null(found);
        Assert.Equal(1, runs.ListCalls);
    }

    [Fact]
    public async Task FindRunByIdempotencyKey_reads_the_next_page_and_stops()
    {
        var expected = new RunId(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var runs = new ScriptedChainRunRepository(query =>
        {
            if (query.Offset == 0)
            {
                return FullPage(Chain, "other", totalCount: StubChainExecutionRepository.RunPageSize + 1);
            }

            return new PagedResult<ChainRunRecord>
            {
                Items =
                [
                    Run(Chain, expected, "target"),
                ],
                TotalCount = StubChainExecutionRepository.RunPageSize + 1,
                Offset = query.Offset,
                Limit = query.Limit,
            };
        });
        var repository = new StubChainExecutionRepository(runs, new UnusedCheckpointRepository());

        RunId? found = await repository.FindRunByIdempotencyKeyAsync(Chain, "target");

        Assert.Equal(expected, found);
        Assert.Equal(2, runs.ListCalls);
    }

    private static PagedResult<ChainRunRecord> FullPage(
        ChainId chainId,
        string idempotencyKey,
        ChainRunStatus status = ChainRunStatus.Completed,
        long totalCount = long.MaxValue)
    {
        ChainRunRecord[] items = new ChainRunRecord[StubChainExecutionRepository.RunPageSize];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = Run(chainId, new RunId(Guid.NewGuid()), idempotencyKey, status);
        }

        return new PagedResult<ChainRunRecord>
        {
            Items = items,
            TotalCount = totalCount,
            Offset = 0,
            Limit = StubChainExecutionRepository.RunPageSize,
        };
    }

    private static ChainRunRecord Run(
        ChainId chainId,
        RunId id,
        string idempotencyKey,
        ChainRunStatus status = ChainRunStatus.Completed) =>
        new()
        {
            Id = id,
            ChainId = chainId,
            IdempotencyKey = idempotencyKey,
            Status = status,
            Source = "pagination-test",
        };

    private sealed class ScriptedChainRunRepository(Func<PagedQuery, PagedResult<ChainRunRecord>> list) : IChainRunRepository
    {
        public int ListCalls { get; private set; }

        public Task<PagedResult<ChainRunRecord>> ListAsync(
            PagedQuery query, ChainId? chainId = null, CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult(list(query));
        }

        public Task<ChainRunRecord?> GetAsync(RunId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ChainRunRecord> AddAsync(ChainRunRecord run, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ChainRunRecord> UpdateAsync(ChainRunRecord run, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(RunId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedCheckpointRepository : ICheckpointRepository
    {
        public Task<CheckpointRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CheckpointRecord?> GetByRunAndStepAsync(RunId runId, ChainStepKind step, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<CheckpointRecord>> ListAsync(PagedQuery query, RunId? runId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CheckpointRecord> AddAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CheckpointRecord> UpdateAsync(CheckpointRecord record, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
