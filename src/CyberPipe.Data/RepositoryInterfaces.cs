using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>Репозиторий определений цепочек (app.chains). DTO — ChainDefinitionDto из TASK-002.</summary>
public interface IChainRepository
{
    /// <summary>Возвращает цепочку по идентификатору; отсутствие — null без исключения.</summary>
    /// <param name="id">Идентификатор цепочки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Цепочка или null.</returns>
    Task<ChainDefinitionDto?> GetAsync(ChainId id, CancellationToken cancellationToken = default);

    /// <summary>Страница цепочек с обязательной сортировкой из белого списка (created_at, updated_at, name, version).</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница цепочек.</returns>
    Task<PagedResult<ChainDefinitionDto>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет цепочку. Идемпотентность: повтор с тем же Id возвращает существующую запись
    /// без перезаписи (результат AlreadyExists выражен возвратом существующей записи).
    /// </summary>
    /// <param name="chain">Определение цепочки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённое определение с фактическими метками времени.</returns>
    Task<ChainDefinitionDto> AddAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default);

    /// <summary>Обновляет цепочку целиком; updated_at выставляет база.</summary>
    /// <param name="chain">Новое определение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённое определение.</returns>
    /// <exception cref="OptimisticConcurrencyException">Запись отсутствует (трактуется как конфликт версий).</exception>
    Task<ChainDefinitionDto> UpdateAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default);

    /// <summary>Удаляет цепочку; отсутствие — false без исключения, повторный вызов — false.</summary>
    /// <param name="id">Идентификатор цепочки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    Task<bool> DeleteAsync(ChainId id, CancellationToken cancellationToken = default);
}

/// <summary>Репозиторий запусков цепочек (app.chain_runs), часть агрегата цепочки.</summary>
public interface IChainRunRepository
{
    /// <summary>Возвращает запуск; отсутствие — null.</summary>
    /// <param name="id">Идентификатор запуска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запуск или null.</returns>
    Task<ChainRunRecord?> GetAsync(RunId id, CancellationToken cancellationToken = default);

    /// <summary>Страница запусков; сортировка из белого списка (created_at, updated_at, started_at, status).</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="chainId">Необязательный фильтр по цепочке.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница запусков.</returns>
    Task<PagedResult<ChainRunRecord>> ListAsync(PagedQuery query, ChainId? chainId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет запуск. Идемпотентность: уникальный ключ (ChainId, IdempotencyKey) — повтор
    /// возвращает существующую запись без создания второй (AlreadyExists возвратом существующей).
    /// </summary>
    /// <param name="run">Запуск.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённый или существующий запуск.</returns>
    Task<ChainRunRecord> AddAsync(ChainRunRecord run, CancellationToken cancellationToken = default);

    /// <summary>Обновляет запуск при совпадении expectedRowVersion с updated_at записи.</summary>
    /// <param name="run">Новое состояние запуска.</param>
    /// <param name="expectedRowVersion">Версия строки, полученная при чтении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённый запуск с новой версией.</returns>
    /// <exception cref="OptimisticConcurrencyException">Версия устарела или запись отсутствует.</exception>
    Task<ChainRunRecord> UpdateAsync(ChainRunRecord run, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>Удаляет запуск; отсутствие — false.</summary>
    /// <param name="id">Идентификатор запуска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    Task<bool> DeleteAsync(RunId id, CancellationToken cancellationToken = default);
}

/// <summary>Репозиторий контрольных точек (app.chain_checkpoints). DTO — ChainCheckpointDto из TASK-002.</summary>
public interface ICheckpointRepository
{
    /// <summary>Возвращает чекпоинт по идентификатору строки; отсутствие — null.</summary>
    /// <param name="id">Идентификатор строки чекпоинта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Чекпоинт или null.</returns>
    Task<CheckpointRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает чекпоинт запуска по шагу; отсутствие — null.</summary>
    /// <param name="runId">Идентификатор запуска.</param>
    /// <param name="step">Шаг цепочки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Чекпоинт или null.</returns>
    Task<CheckpointRecord?> GetByRunAndStepAsync(RunId runId, ChainStepKind step, CancellationToken cancellationToken = default);

    /// <summary>Страница чекпоинтов; сортировка из белого списка (created_at, updated_at, checkpoint_at, step).</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="runId">Необязательный фильтр по запуску.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница чекпоинтов.</returns>
    Task<PagedResult<CheckpointRecord>> ListAsync(PagedQuery query, RunId? runId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет чекпоинт. Идемпотентность: уникальный ключ (RunId, Step) — повтор обновляет
    /// состояние существующей строки и возвращает её, а не создаёт дубль.
    /// </summary>
    /// <param name="checkpoint">DTO чекпоинта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённый чекпоинт с идентификатором и версией.</returns>
    Task<CheckpointRecord> AddAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default);

    /// <summary>Обновляет чекпоинт при совпадении expectedRowVersion с updated_at записи.</summary>
    /// <param name="record">Новое состояние чекпоинта.</param>
    /// <param name="expectedRowVersion">Версия строки, полученная при чтении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённый чекпоинт с новой версией.</returns>
    /// <exception cref="OptimisticConcurrencyException">Версия устарела или запись отсутствует.</exception>
    Task<CheckpointRecord> UpdateAsync(CheckpointRecord record, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>Удаляет чекпоинт; отсутствие — false.</summary>
    /// <param name="id">Идентификатор строки чекпоинта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Репозиторий HITL-одобрений (app.approvals); бизнес-правила решений — TASK-046.</summary>
public interface IApprovalRepository
{
    /// <summary>Возвращает одобрение; отсутствие — null.</summary>
    /// <param name="id">Идентификатор одобрения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Одобрение или null.</returns>
    Task<ApprovalRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Страница одобрений; сортировка из белого списка (created_at, updated_at, expires_at, status).</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="status">Необязательный фильтр по статусу.</param>
    /// <param name="runId">Необязательный фильтр по запуску.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница одобрений.</returns>
    Task<PagedResult<ApprovalRecord>> ListAsync(PagedQuery query, ApprovalStatus? status = null, RunId? runId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет одобрение. Идемпотентность: повтор с тем же непустым Id возвращает существующую
    /// запись без перезаписи (AlreadyExists возвратом существующей).
    /// </summary>
    /// <param name="approval">Одобрение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённое или существующее одобрение.</returns>
    Task<ApprovalRecord> AddAsync(ApprovalRecord approval, CancellationToken cancellationToken = default);

    /// <summary>Обновляет одобрение при совпадении expectedRowVersion с updated_at записи.</summary>
    /// <param name="approval">Новое состояние одобрения.</param>
    /// <param name="expectedRowVersion">Версия строки, полученная при чтении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённое одобрение с новой версией.</returns>
    /// <exception cref="OptimisticConcurrencyException">Версия устарела или запись отсутствует.</exception>
    Task<ApprovalRecord> UpdateAsync(ApprovalRecord approval, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>Удаляет одобрение; отсутствие — false.</summary>
    /// <param name="id">Идентификатор одобрения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Репозиторий книги токен-бюджета (app.token_budget_ledger); правила бюджета — TASK-033.</summary>
public interface ITokenBudgetRepository
{
    /// <summary>Возвращает запись книги; отсутствие — null.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запись или null.</returns>
    Task<TokenBudgetEntryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Страница записей книги; сортировка из белого списка (created_at, updated_at, budget_date, cost_usd).</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="budgetDate">Необязательный фильтр по дате бюджета.</param>
    /// <param name="modelId">Необязательный фильтр по модели.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница записей.</returns>
    Task<PagedResult<TokenBudgetEntryRecord>> ListAsync(PagedQuery query, DateOnly? budgetDate = null, string? modelId = null, CancellationToken cancellationToken = default);

    /// <summary>Добавляет запись книги; повтор с тем же непустым Id возвращает существующую запись.</summary>
    /// <param name="entry">Запись книги.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая или существующая запись.</returns>
    Task<TokenBudgetEntryRecord> AddAsync(TokenBudgetEntryRecord entry, CancellationToken cancellationToken = default);

    /// <summary>Добавляет фактический расход модели как запись книги вида usage.</summary>
    /// <param name="usage">Фактический расход из TASK-002.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая запись книги.</returns>
    Task<TokenBudgetEntryRecord> AddUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default);

    /// <summary>Обновляет запись книги при совпадении expectedRowVersion с updated_at.</summary>
    /// <param name="entry">Новое состояние записи.</param>
    /// <param name="expectedRowVersion">Версия строки, полученная при чтении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённая запись с новой версией.</returns>
    /// <exception cref="OptimisticConcurrencyException">Версия устарела или запись отсутствует.</exception>
    Task<TokenBudgetEntryRecord> UpdateAsync(TokenBudgetEntryRecord entry, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>Удаляет запись книги; отсутствие — false.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Дневной итог книги: суммарная стоимость и токены за дату.</summary>
    /// <param name="budgetDate">Дата бюджета.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Итог дня; нули, если записей нет.</returns>
    Task<TokenBudgetDailySummary> GetDailySummaryAsync(DateOnly budgetDate, CancellationToken cancellationToken = default);
}

/// <summary>Репозиторий настроек ключ-значение (app.kv_settings).</summary>
public interface IKeyValueSettingsRepository
{
    /// <summary>Возвращает настройку по ключу; отсутствие — null.</summary>
    /// <param name="key">Ключ настройки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Настройка или null.</returns>
    Task<KeyValueSettingRecord?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Страница настроек; сортировка из белого списка (key, created_at, updated_at).</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница настроек.</returns>
    Task<PagedResult<KeyValueSettingRecord>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет настройку. Идемпотентность: существующий ключ возвращает существующую запись
    /// без перезаписи (AlreadyExists возвратом существующей); для перезаписи — UpdateAsync/SetAsync.
    /// </summary>
    /// <param name="setting">Настройка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая или существующая настройка.</returns>
    Task<KeyValueSettingRecord> AddAsync(KeyValueSettingRecord setting, CancellationToken cancellationToken = default);

    /// <summary>Обновляет настройку при совпадении expectedRowVersion с updated_at записи.</summary>
    /// <param name="setting">Новое состояние настройки.</param>
    /// <param name="expectedRowVersion">Версия строки, полученная при чтении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённая настройка с новой версией.</returns>
    /// <exception cref="OptimisticConcurrencyException">Версия устарела или запись отсутствует.</exception>
    Task<KeyValueSettingRecord> UpdateAsync(KeyValueSettingRecord setting, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>Создаёт или перезаписывает настройку одним запросом (upsert по ключу).</summary>
    /// <param name="setting">Настройка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая настройка.</returns>
    Task<KeyValueSettingRecord> SetAsync(KeyValueSettingRecord setting, CancellationToken cancellationToken = default);

    /// <summary>Удаляет настройку; отсутствие — false.</summary>
    /// <param name="key">Ключ настройки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Репозиторий журнала аудита (app.audit_log); политика аудита — TASK-070–TASK-073.</summary>
public interface IAuditLogRepository
{
    /// <summary>Возвращает запись аудита; отсутствие — null.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запись или null.</returns>
    Task<AuditLogRecord?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Страница аудита; сортировка из белого списка (created_at, id, action, actor).</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="actor">Необязательный фильтр по исполнителю.</param>
    /// <param name="action">Необязательный фильтр по действию.</param>
    /// <param name="entityType">Необязательный фильтр по типу сущности.</param>
    /// <param name="entityId">Необязательный фильтр по идентификатору сущности.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница записей аудита.</returns>
    Task<PagedResult<AuditLogRecord>> ListAsync(PagedQuery query, string? actor = null, string? action = null, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default);

    /// <summary>Добавляет запись аудита; идентификатор генерирует база.</summary>
    /// <param name="record">Запись аудита без идентификатора.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая запись с идентификатором.</returns>
    Task<AuditLogRecord> AddAsync(AuditLogRecord record, CancellationToken cancellationToken = default);

    /// <summary>Обновляет поля записи аудита по идентификатору (техническая правка; у таблицы нет updated_at).</summary>
    /// <param name="record">Новое состояние записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись существовала и обновлена.</returns>
    Task<bool> UpdateAsync(AuditLogRecord record, CancellationToken cancellationToken = default);

    /// <summary>Удаляет запись аудита; отсутствие — false.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
