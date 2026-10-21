# CyberPipe.Data — bootstrap SQLite WAL (TASK-010)

Единственная база CyberPipe — встроенный файл SQLite в режиме WAL (`data/cyberpipe.db`
по умолчанию). Сервера, пользователей и паролей нет. Этот проект отвечает только за
первый запуск: создание каталога и файла, включение WAL/внешних ключей/busy timeout,
проверку доступности (`SELECT 1`) и health-check для Service. Схема и миграции — TASK-011,
векторный поиск — TASK-012, репозитории — TASK-013.

## Как это работает

- `SqliteOptions` — путь файла, busy timeout, таймаут команды. Секретов в опциях нет.
  Маппинг из секции `Database` — `SqliteOptions.From(DatabaseOptions)`.
- `IDatabaseBootstrapper.EnsureReadyAsync` → `BootstrapResult { Status: Ready | Unavailable,
  ReasonCode, ServerVersion }`. Секретов и строки подключения в результате, исключениях
  (`SqliteUnavailableException`) и логах нет. Состояние `NeedsPassword` зарезервировано
  и для SQLite не наступает.
- Файл недоступен (каталог не создаётся, readonly, повреждение) → `Unavailable` с кодом
  причины (`ServerUnavailable`, `InsufficientPrivilege`, `DatabaseCorrupt`, `Timeout`).
- Повторный запуск идемпотентен: файл не пересоздаёт, данные не сбрасывает.
- Соединения для TASK-011–TASK-013 — только через `IDatabaseConnectionFactory`
  (`SqliteConnectionFactory`): каждое открытое соединение включает `journal_mode=WAL`,
  `foreign_keys=ON`, `synchronous=NORMAL`, `busy_timeout`.
- `SqliteHealthProbe` реализует `IDatabaseHealthProbe` (TASK-004): `Healthy`, иначе
  `Degraded` с кодом причины без секретов. Годится для TASK-020 и TASK-063.

## Проверка установки

Ничего устанавливать не нужно: драйвер SQLite (`Microsoft.Data.Sqlite`) встроен
в поставку. Файл базы создаётся при первом запуске рядом с рабочим каталогом.

```powershell
dotnet test tests/CyberPipe.Data.Tests --filter "Category!=Integration"
dotnet test tests/CyberPipe.Data.Tests --filter "Category=Integration"
```

Интеграционные тесты используют временные файлы SQLite и не требуют сервера.

## Схема и миграции (TASK-011)

- Миграции лежат в `Migrations/NNNN_<slug>.sql` с парными `NNNN_<slug>.down.sql` у обратимых
  миграций (обратимость объявляется заголовком `-- reversible: true`) и встраиваются в сборку
  как ресурсы. История — `schema_migrations(version, applied_at, checksum)`, контрольная
  сумма — SHA-256 нормализованного SQL вверх.
- `ISchemaMigrator.MigrateAsync` применяет миграции по порядку, каждую в отдельной транзакции,
  под процессным мьютексом мигратора: конкурентный запуск ждёт, а не падает дублями.
  Изменённый файл уже применённой миграции — `SchemaChecksumMismatchException`
  с номером версии. `ValidateAsync` — режим ValidateOnly: только возвращает расхождения.
  `RollbackLastAsync` откатывает последнюю обратимую миграцию, не трогая строки таблиц более
  ранних миграций. Мигратор применим при старте Service после bootstrap TASK-010 через
  `IDatabaseConnectionFactory`; составной корень подключит его в TASK-090.
- Схема одна (main): без `CREATE SCHEMA`/`CREATE EXTENSION`, таблицы без префиксов.
  Таблица `quartz` не создаётся: Quartz.NET хранит свои таблицы (`qrtz_*`) в том же файле
  через `QuartzSqliteInitializer` из TASK-045.
- Типы SQLite: `uuid` → `text`, `jsonb` → `text` (JSON), `timestamptz` → `text` (ISO-8601 UTC),
  `date` → `text`, `bigint identity` → `integer primary key autoincrement`,
  `numeric` → `real`, `boolean` → `integer 0/1`, `text[]` → `text` (JSON-массив),
  `vector` → `text` (JSON-массив float). Внешние ключи с `ON DELETE CASCADE` работают
  при `PRAGMA foreign_keys=ON` (фабрика включает на каждом соединении).
- Таблицы: `embeddings` (вектор текстом без фиксированной размерности: размерность задаёт
  колонка `dimension` из конфигурации модели TASK-012; ANN-индекса нет — поиск полным
  перебором в C#), `semantic_cache`, `chains`/`chain_runs`/`chain_checkpoints` (только
  структура под TASK-044), `approvals`, `token_budget_ledger`, `kv_settings`, `audit_log`.
  Seed — только технический в `kv_settings` (`schema.version`), без секретов
  и пользовательских данных.
- Миграции только аддитивные для выпущенных версий; переименование/удаление — expand-contract
  новой миграцией.

## Репозитории и доступ к данным (TASK-013)

- Типизированные репозитории по агрегатам TASK-011: `IChainRepository` (таблица `chains`,
  определение — `ChainDefinitionDto` целиком в `definition_json`), `IChainRunRepository`
  (`chain_runs`), `ICheckpointRepository` (`chain_checkpoints`, DTO — `ChainCheckpointDto`),
  `IApprovalRepository` (`approvals`), `ITokenBudgetRepository` (`token_budget_ledger`,
  включая `AddUsageAsync(TokenUsageDto)` и дневной итог), `IKeyValueSettingsRepository`
  (`kv_settings`), `IAuditLogRepository` (`audit_log`). Набор собирается
  `RepositorySet.Create(factory, ...)`; векторные операции не дублируются, а делегируются
  `IEmbeddingStore` (TASK-012) через `RepositorySet.Embeddings` / `IUnitOfWork.Embeddings`.
  В публичных контрактах типов драйвера нет; весь SQL сосредоточен в этом проекте.
- Семантика отсутствия и идемпотентности зафиксирована в XML-документации методов:
  `Get` отсутствующей записи — `null`, повторный `Delete` — `false`, без исключений;
  повторный `Add` цепочек/одобрений/бюджета/настроек с тем же ключом возвращает существующую
  запись (AlreadyExists), запуск — по уникальному `(chain_id, idempotency_key)`, чекпоинт —
  upsert по `(run_id, step)`.
- Транзакции: `IUnitOfWorkFactory.CreateAsync` → `IUnitOfWork.BeginAsync/CommitAsync/
  RollbackAsync`; репозитории `IUnitOfWork.*` привязаны к одному соединению/транзакции,
  вне unit-of-work каждый репозиторий работает на собственном соединении фабрики TASK-010.
  Выход из `await using` без коммита, исключение и отмена токена при коммите откатывают
  все записи целиком. Счётчики коммитов/откатов и медленных запросов — `UnitOfWorkMetrics`
  (`IUnitOfWorkMetrics`) для диагностики Service.
- Безопасность и конкуренция: только параметризованные запросы (пользовательский
  ввод никогда не конкатенируется в SQL), сортировка пагинации — обязательна и только из
  белого списка полей репозитория (`PagedQuery.SortBy`, по умолчанию `created_at`, плюс
  детерминированный tie-breaker), обновление одобрений/чекпоинтов/запусков/бюджета/настроек —
  оптимистично по `updated_at` как row_version: устаревшая версия даёт
  `OptimisticConcurrencyException(aggregate, id)`. Метки времени для конкурентных записей
  выставляются кодом (ISO-8601 с миллисекундами), а не `CURRENT_TIMESTAMP`, чтобы сравнение
  версий было точным. Медленные запросы логируются Warning
  только именем операции и длительностью (порог — `RepositoryOptions.SlowQueryThreshold`);
  значения параметров, в том числе секреты, в логи не попадают. JSON-поля — через
  `RepositoryJson` с явными опциями `System.Text.Json`.
- Чтение меток: SQLite возвращает `CURRENT_TIMESTAMP` строкой без смещения, поэтому
  ридеры (`ReadTime`/`ReadDate`/`ReadGuid`) терпимы к формату: `DateTimeOffset`, `DateTime`,
  ISO-строка.
- Тесты: unit маппинга/валидации, integration (`Category=Integration`, временные файлы
  SQLite) — CRUD каждого репозитория, пагинация, откат двух репозиториев, оптимистичная
  конкуренция, идемпотентность, отсутствие секретов в логе медленных запросов.

## Хранилище эмбеддингов (TASK-012)

- `IEmbeddingStore` / `SqliteEmbeddingStore` поверх таблицы `embeddings`: `UpsertAsync`,
  `UpsertManyAsync` (одна транзакция, идемпотентно по `(namespace, source_ref, model_id)`),
  `SearchAsync`, `DeleteAsync`, `DeleteByOwnerScopeAsync` (все записи namespace), `CountAsync`.
  Пространства имён: `router`, `semantic-cache`, `knowledge`, `safety` (`EmbeddingNamespaces`);
  допустимо любое непустое имя — изоляция обеспечивается фильтром по namespace.
- Вектор хранится текстом (JSON-массив float). Поиск: кандидаты фильтруются SQL
  (namespace/dimension/model), косинусное сходство считается в C# полным перебором
  (`CosineScore.Similarity`), сортировка по убыванию Score, фильтр `MinScore`
  (пустой результат вместо «хоть какого-нибудь»), `TopK`, необязательный `ModelId`.
  Запрос дополнительно фильтруется по `dimension`, чтобы разноразмерные модели
  не смешивались.
- Размерность не зашита в код: `EmbeddingStoreOptions.ModelDimensions` (ModelId → размерность
  из конфигурации модели) имеет приоритет, иначе ожидаемая размерность — поле `Dimension`
  записи (0 = длина вектора). Несовпадение — `EmbeddingDimensionMismatchException(expected,
  actual, modelId)` без обрезки/паддинга; пустой вектор и `TopK <= 0` — `ArgumentException`
  до обращения к БД (`EmbeddingValidation`). Нормализация векторов — ответственность
  вызывающего; хранилище хранит вектор как есть.
- Стратегия поиска (`Exact`/`ApproximateHnsw`) — конфигурацией без смены контракта:
  SQLite выполняет одинаковый точный перебор в обоих режимах, поэтому контракт
  и результаты не зависят от стратегии.
