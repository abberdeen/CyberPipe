using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>
/// Каталог цепочек. Потребители зависят от этого интерфейса, а не от репозитория
/// и unit-of-work. Реализация читает и пишет EF Core напрямую.
/// </summary>
public interface IChainCatalogService : IChainDefinitionRepository
{
    /// <summary>Возвращает цепочку по идентификатору; отсутствие — null.</summary>
    Task<ChainDefinitionDto?> GetAsync(ChainId id, CancellationToken cancellationToken = default);

    /// <summary>Страница цепочек с обязательной сортировкой из белого списка.</summary>
    Task<PagedResult<ChainDefinitionDto>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default);

    /// <summary>Добавляет определение цепочки.</summary>
    Task<ChainDefinitionDto> AddAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default);

    /// <summary>Обновляет определение цепочки.</summary>
    Task<ChainDefinitionDto> UpdateAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default);

    /// <summary>Удаляет цепочку. true, если строка была удалена.</summary>
    Task<bool> DeleteAsync(ChainId id, CancellationToken cancellationToken = default);
}

/// <summary>Записи запусков цепочек. Каждый метод владеет своим контекстом EF Core.</summary>
public interface IChainRunRecordService
{
    /// <summary>Возвращает запуск по идентификатору; отсутствие — null.</summary>
    Task<ChainRunRecord?> GetAsync(RunId id, CancellationToken cancellationToken = default);

    /// <summary>Страница запусков, опционально внутри одной цепочки.</summary>
    Task<PagedResult<ChainRunRecord>> ListAsync(PagedQuery query, ChainId? chainId = null, CancellationToken cancellationToken = default);

    /// <summary>Добавляет запуск.</summary>
    Task<ChainRunRecord> AddAsync(ChainRunRecord run, CancellationToken cancellationToken = default);

    /// <summary>Обновляет запуск, если версия строки совпала.</summary>
    Task<ChainRunRecord> UpdateAsync(ChainRunRecord run, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>Удаляет запуск. true, если строка была удалена.</summary>
    Task<bool> DeleteAsync(RunId id, CancellationToken cancellationToken = default);
}

/// <summary>Контрольные точки запусков. Каждый метод владеет своим контекстом EF Core.</summary>
public interface ICheckpointStoreService
{
    /// <summary>Возвращает чекпоинт по идентификатору строки; отсутствие — null.</summary>
    Task<CheckpointRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает чекпоинт запуска и шага; отсутствие — null.</summary>
    Task<CheckpointRecord?> GetByRunAndStepAsync(RunId runId, ChainStepKind step, CancellationToken cancellationToken = default);

    /// <summary>Страница чекпоинтов, опционально одного запуска.</summary>
    Task<PagedResult<CheckpointRecord>> ListAsync(PagedQuery query, RunId? runId = null, CancellationToken cancellationToken = default);

    /// <summary>Добавляет чекпоинт.</summary>
    Task<CheckpointRecord> AddAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default);

    /// <summary>Обновляет чекпоинт, если версия строки совпала.</summary>
    Task<CheckpointRecord> UpdateAsync(CheckpointRecord record, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>Удаляет чекпоинт. true, если строка была удалена.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Книга одобрений. Каждый метод владеет своим контекстом EF Core.</summary>
public interface IApprovalLedgerService
{
    /// <summary>Возвращает одобрение по идентификатору; отсутствие — null.</summary>
    Task<ApprovalRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Страница одобрений с фильтрами статуса и запуска.</summary>
    Task<PagedResult<ApprovalRecord>> ListAsync(PagedQuery query, ApprovalStatus? status = null, RunId? runId = null, CancellationToken cancellationToken = default);

    /// <summary>Добавляет одобрение.</summary>
    Task<ApprovalRecord> AddAsync(ApprovalRecord approval, CancellationToken cancellationToken = default);

    /// <summary>Обновляет одобрение, если версия строки совпала.</summary>
    Task<ApprovalRecord> UpdateAsync(ApprovalRecord approval, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>Удаляет одобрение. true, если строка была удалена.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Книга токен-бюджета. Каждый метод владеет своим контекстом EF Core.</summary>
public interface ITokenBudgetLedgerService
{
    /// <summary>Возвращает запись книги по идентификатору; отсутствие — null.</summary>
    Task<TokenBudgetEntryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Страница записей книги с фильтрами даты и модели.</summary>
    Task<PagedResult<TokenBudgetEntryRecord>> ListAsync(PagedQuery query, DateOnly? budgetDate = null, string? modelId = null, CancellationToken cancellationToken = default);

    /// <summary>Добавляет запись книги.</summary>
    Task<TokenBudgetEntryRecord> AddAsync(TokenBudgetEntryRecord entry, CancellationToken cancellationToken = default);

    /// <summary>Обновляет запись книги, если версия строки совпала.</summary>
    Task<TokenBudgetEntryRecord> UpdateAsync(TokenBudgetEntryRecord entry, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>Удаляет запись книги. true, если строка была удалена.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Дневной итог книги.</summary>
    Task<TokenBudgetDailySummary> GetDailySummaryAsync(DateOnly budgetDate, CancellationToken cancellationToken = default);

    /// <summary>Добавляет запись фактического расхода.</summary>
    Task<TokenBudgetEntryRecord> AddUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default);
}

/// <summary>Настройки ключ-значение. Каждый метод владеет своим контекстом EF Core.</summary>
public interface IKvSettingsService
{
    /// <summary>Возвращает настройку по ключу; отсутствие — null.</summary>
    Task<KeyValueSettingRecord?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Страница настроек.</summary>
    Task<PagedResult<KeyValueSettingRecord>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default);

    /// <summary>Добавляет настройку.</summary>
    Task<KeyValueSettingRecord> AddAsync(KeyValueSettingRecord setting, CancellationToken cancellationToken = default);

    /// <summary>Создаёт или заменяет настройку.</summary>
    Task<KeyValueSettingRecord> SetAsync(KeyValueSettingRecord setting, CancellationToken cancellationToken = default);

    /// <summary>Обновляет настройку, если версия строки совпала.</summary>
    Task<KeyValueSettingRecord> UpdateAsync(KeyValueSettingRecord setting, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>Удаляет настройку. true, если строка была удалена.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Журнал аудита. Каждый метод владеет своим контекстом EF Core.</summary>
public interface IAuditLogService
{
    /// <summary>Возвращает запись журнала по идентификатору; отсутствие — null.</summary>
    Task<AuditLogRecord?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Страница журнала с фильтрами актора, действия и сущности.</summary>
    Task<PagedResult<AuditLogRecord>> ListAsync(PagedQuery query, string? actor = null, string? action = null, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default);

    /// <summary>Добавляет запись журнала.</summary>
    Task<AuditLogRecord> AddAsync(AuditLogRecord record, CancellationToken cancellationToken = default);

    /// <summary>Обновляет запись журнала. true, если строка была изменена.</summary>
    Task<bool> UpdateAsync(AuditLogRecord record, CancellationToken cancellationToken = default);

    /// <summary>Удаляет запись журнала. true, если строка была удалена.</summary>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Хранилище выполнения цепочек: запуски и чекпоинты.
/// Потребители зависят от этого интерфейса, а не от репозитория выполнения.
/// </summary>
public interface IChainExecutionStoreService : IChainExecutionRepository;

/// <summary>
/// Хранилище HITL-подтверждений.
/// Потребители зависят от этого интерфейса, а не от репозитория подтверждений.
/// </summary>
public interface IHitlApprovalStoreService : IHitlApprovalRepository
{
    /// <summary>Доводит схему подтверждений до актуальных миграций.</summary>
    Task EnsureTableCreatedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Реестр Netrunner-пакетов.
/// Потребители зависят от этого интерфейса, а не от репозитория реестра.
/// </summary>
public interface INetrunnerPackageStoreService : IPackageRegistryRepository;

/// <summary>Заметки владельца. Реализация — EF Core, потребители берут этот интерфейс.</summary>
public interface INoteStoreService : INoteRepository;

/// <summary>Финансовая книга владельца. Реализация — EF Core, потребители берут этот интерфейс.</summary>
public interface IFinanceLedgerService : IFinanceRepository;

/// <summary>Напоминания владельца. Реализация — EF Core, потребители берут этот интерфейс.</summary>
public interface IReminderStoreService : IReminderRepository;

/// <summary>Контакты владельца. Реализация — EF Core, потребители берут этот интерфейс.</summary>
public interface IContactStoreService : IContactRepository;

/// <summary>Хранилище эмбеддингов. Реализация — EF Core, потребители берут этот интерфейс.</summary>
public interface IEmbeddingStoreService : IEmbeddingStore;

/// <summary>Дневной бюджет губернатора. Реализация — книга токен-бюджета на EF Core.</summary>
public interface IBudgetLedgerService : IBudgetStore;
