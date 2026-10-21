using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>
/// Транзакционная граница TASK-013: один контекст и одна транзакция на несколько репозиториев.
/// Репозитории этого unit-of-work получают контекст только из активного unit-of-work;
/// вне его каждый репозиторий работает на собственном контексте фабрики.
/// Отмена токена не переживается транзакцией: Commit/Rollback при отменённом токене откатывают.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Репозиторий цепочек на транзакции этого unit-of-work.</summary>
    IChainRepository Chains { get; }

    /// <summary>Репозиторий запусков на транзакции этого unit-of-work.</summary>
    IChainRunRepository ChainRuns { get; }

    /// <summary>Репозиторий контрольных точек на транзакции этого unit-of-work.</summary>
    ICheckpointRepository Checkpoints { get; }

    /// <summary>Репозиторий одобрений на транзакции этого unit-of-work.</summary>
    IApprovalRepository Approvals { get; }

    /// <summary>Репозиторий книги бюджета на транзакции этого unit-of-work.</summary>
    ITokenBudgetRepository TokenBudget { get; }

    /// <summary>Репозиторий настроек на транзакции этого unit-of-work.</summary>
    IKeyValueSettingsRepository Settings { get; }

    /// <summary>Репозиторий аудита на транзакции этого unit-of-work.</summary>
    IAuditLogRepository AuditLog { get; }

    /// <summary>Векторное хранилище TASK-012; векторные операции делегируются ему, а не дублируются.</summary>
    IEmbeddingStore Embeddings { get; }

    /// <summary>Транзакция начата и ещё не завершена.</summary>
    bool IsActive { get; }

    /// <summary>Транзакция завершена коммитом или откатом.</summary>
    bool IsCompleted { get; }

    /// <summary>Открывает контекст и начинает транзакцию; репозитории подхватывают её амбиентно.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача начала транзакции.</returns>
    Task BeginAsync(CancellationToken cancellationToken = default);

    /// <summary>Фиксирует все записи unit-of-work целиком.</summary>
    /// <param name="cancellationToken">Токен отмены; при отменённом токене транзакция откатывается.</param>
    /// <returns>Задача коммита.</returns>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Откатывает все записи unit-of-work целиком.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отката.</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>Фабрика unit-of-work: единственный вход в мульти-репозиторные транзакции.</summary>
public interface IUnitOfWorkFactory
{
    /// <summary>Создаёт unit-of-work; транзакция начинается вызовом BeginAsync на нём.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Новый unit-of-work.</returns>
    Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Набор репозиториев и векторного хранилища на общих опциях, метриках и фабрике контекстов.</summary>
public sealed record RepositorySet
{
    /// <summary>Репозиторий цепочек.</summary>
    public required IChainRepository Chains { get; init; }

    /// <summary>Репозиторий запусков.</summary>
    public required IChainRunRepository ChainRuns { get; init; }

    /// <summary>Репозиторий контрольных точек.</summary>
    public required ICheckpointRepository Checkpoints { get; init; }

    /// <summary>Репозиторий одобрений.</summary>
    public required IApprovalRepository Approvals { get; init; }

    /// <summary>Репозиторий книги бюджета.</summary>
    public required ITokenBudgetRepository TokenBudget { get; init; }

    /// <summary>Репозиторий настроек.</summary>
    public required IKeyValueSettingsRepository Settings { get; init; }

    /// <summary>Репозиторий аудита.</summary>
    public required IAuditLogRepository AuditLog { get; init; }

    /// <summary>Векторное хранилище TASK-012.</summary>
    public required IEmbeddingStore Embeddings { get; init; }

    /// <summary>Общие счётчики транзакций и медленных запросов.</summary>
    public required UnitOfWorkMetrics Metrics { get; init; }

    internal IDbContextFactory<CyberPipeDbContext>? DbFactory { get; init; }
    internal RepositoryOptions? BoundOptions { get; init; }
    internal ILogger? BoundLogger { get; init; }

    internal RepositorySet BindTo(Func<UnitOfWorkSession?> sessionProvider)
    {
        if (DbFactory is null)
            return this;
        RepositorySet bound = Create(DbFactory, Embeddings, BoundOptions, BoundLogger, Metrics);
        (bound.Chains as RepositoryBase)!.SessionProvider = sessionProvider;
        (bound.ChainRuns as RepositoryBase)!.SessionProvider = sessionProvider;
        (bound.Checkpoints as RepositoryBase)!.SessionProvider = sessionProvider;
        (bound.Approvals as RepositoryBase)!.SessionProvider = sessionProvider;
        (bound.TokenBudget as RepositoryBase)!.SessionProvider = sessionProvider;
        (bound.Settings as RepositoryBase)!.SessionProvider = sessionProvider;
        (bound.AuditLog as RepositoryBase)!.SessionProvider = sessionProvider;
        return bound;
    }

    /// <summary>Создаёт набор репозиториев поверх фабрики контекстов SQLite.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="embeddingStore">Векторное хранилище; null — SqliteEmbeddingStore поверх той же фабрики.</param>
    /// <param name="options">Опции репозиториев.</param>
    /// <param name="logger">Логгер медленных запросов.</param>
    /// <param name="metrics">Общие счётчики; null — новый экземпляр.</param>
    /// <returns>Набор репозиториев.</returns>
    public static RepositorySet Create(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        IEmbeddingStore? embeddingStore = null,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        UnitOfWorkMetrics shared = metrics ?? new UnitOfWorkMetrics();
        return new RepositorySet
        {
            Chains = new ChainRepository(dbFactory, options, logger, shared),
            ChainRuns = new ChainRunRepository(dbFactory, options, logger, shared),
            Checkpoints = new CheckpointRepository(dbFactory, options, logger, shared),
            Approvals = new ApprovalRepository(dbFactory, options, logger, shared),
            TokenBudget = new TokenBudgetRepository(dbFactory, options, logger, shared),
            Settings = new KeyValueSettingsRepository(dbFactory, options, logger, shared),
            AuditLog = new AuditLogRepository(dbFactory, options, logger, shared),
            Embeddings = embeddingStore ?? new SqliteEmbeddingStore(
                dbFactory, options ?? new RepositoryOptions(), null, logger, shared),
            Metrics = shared,
            DbFactory = dbFactory,
            BoundOptions = options,
            BoundLogger = logger,
        };
    }
}

/// <summary>Фабрика <see cref="IUnitOfWork" /> поверх фабрики контекстов SQLite.</summary>
public sealed class UnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositorySet _repositories;

    /// <summary>Создаёт фабрику с готовым набором репозиториев.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite.</param>
    /// <param name="repositories">Набор репозиториев с общими метриками.</param>
    public UnitOfWorkFactory(IDbContextFactory<CyberPipeDbContext> dbFactory, RepositorySet repositories)
    {
        _dbFactory = dbFactory;
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
    }

    /// <summary>Создаёт фабрику и набор репозиториев по умолчанию.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite.</param>
    /// <param name="embeddingStore">Векторное хранилище TASK-012.</param>
    /// <param name="options">Опции репозиториев.</param>
    /// <param name="logger">Логгер медленных запросов.</param>
    public UnitOfWorkFactory(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        IEmbeddingStore? embeddingStore = null,
        RepositoryOptions? options = null,
        ILogger? logger = null)
        : this(dbFactory, RepositorySet.Create(dbFactory, embeddingStore, options, logger))
    {
    }

    /// <inheritdoc />
    public Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IUnitOfWork unitOfWork = new UnitOfWork(_dbFactory, _repositories);
        return Task.FromResult(unitOfWork);
    }
}

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositorySet _repositories;
    private CyberPipeDbContext? _context;
    private IDbContextTransaction? _transaction;
    private UnitOfWorkSession? _session;
    private bool _completed;

    public UnitOfWork(IDbContextFactory<CyberPipeDbContext> dbFactory, RepositorySet repositories)
    {
        _dbFactory = dbFactory;
        _repositories = repositories.BindTo(() => _session);
    }

    public IChainRepository Chains => _repositories.Chains;
    public IChainRunRepository ChainRuns => _repositories.ChainRuns;
    public ICheckpointRepository Checkpoints => _repositories.Checkpoints;
    public IApprovalRepository Approvals => _repositories.Approvals;
    public ITokenBudgetRepository TokenBudget => _repositories.TokenBudget;
    public IKeyValueSettingsRepository Settings => _repositories.Settings;
    public IAuditLogRepository AuditLog => _repositories.AuditLog;
    public IEmbeddingStore Embeddings => _repositories.Embeddings;
    public bool IsActive => _transaction is not null && !_completed;
    public bool IsCompleted => _completed;

    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
            throw new InvalidOperationException("Unit of work is already completed.");
        if (_transaction is not null)
            throw new InvalidOperationException("Unit of work transaction is already begun.");
        _context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await _context.DisposeAsync().ConfigureAwait(false);
            _context = null;
            throw;
        }

        _session = new UnitOfWorkSession { Context = _context };
        AmbientUnitOfWork.Set(_session);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        if (cancellationToken.IsCancellationRequested)
        {
            await RollbackCoreAsync(default).ConfigureAwait(false);
            throw new OperationCanceledException(cancellationToken);
        }

        try
        {
            await _context!.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);
            _repositories.Metrics.IncrementCommitted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackCoreAsync(default).ConfigureAwait(false);
            throw;
        }

        await CompleteAsync().ConfigureAwait(false);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await RollbackCoreAsync(cancellationToken).ConfigureAwait(false);
        await CompleteAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed && _transaction is not null)
        {
            // Автоматический откат при исключении/выходе из using без коммита.
            try
            {
                await RollbackCoreAsync().ConfigureAwait(false);
            }
            catch
            {
                // Соединение могло быть оборвано отменой: база откатит транзакцию сама.
            }
        }

        await CompleteAsync().ConfigureAwait(false);
    }

    private async Task RollbackCoreAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            return;
        try
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _repositories.Metrics.IncrementRolledBack();
        }
    }

    private async Task CompleteAsync()
    {
        if (AmbientUnitOfWork.Current == _session)
            AmbientUnitOfWork.Set(null);
        _session = null;
        _completed = true;
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }

        if (_context is not null)
        {
            await _context.DisposeAsync().ConfigureAwait(false);
            _context = null;
        }
    }

    private void EnsureActive()
    {
        if (_completed || _transaction is null)
            throw new InvalidOperationException("Unit of work has no active transaction. Call BeginAsync first.");
    }
}
