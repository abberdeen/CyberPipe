# FIX-TRACKER — доводка по итогам аудита реальной готовности

Аудит 2026-10-20: статусы Done в задачах означают «логика + юнит-тесты», но не «работает в production-композиции». Ниже — фактические разрывы. Статусы: `TODO` / `IN-PROGRESS` / `DONE` / `BLOCKED-ENV` (нужна живая среда: Postgres, Windows-служба, Telegram, ONNX-модель).

## Блокеры продукта

| ID | Разрыв | Задачи | Статус |
|---|---|---|---|
| FIX-01 | CLI: ГОТОВО и проверено вживую — pipe-RPC (route/budget/chain/hitl/scheduler) в Service + адаптеры в ServiceControl; живой прогон со службой и Postgres: status=Running, budget из PG, chain list, ask доходит до губернатора. Остался только SetupWizard в CLI (null) | TASK-062, TASK-093 (F-CLI-SERVICE-CONTROL снят) | DONE (кроме setup) |
| FIX-02 | Desktop: ГОТОВО по коду — `CreateDefault()` на Pipe* адаптерах, Standalone* классы удалены из production-пути. GUI вживую не открывался | TASK-061 | DONE (код), BLOCKED-ENV (GUI) |
| FIX-03 | Персистентность: Budget — ГОТОВО (`PostgresBudgetStore` поверх `ITokenBudgetRepository`, регистрация в Data DI раньше Router TryAdd). Осталось: ChainExecution/PackageRegistry=in-memory, личные репозитории=in-memory | TASK-033 (done), 042, 044, 051 | PARTIAL |
| FIX-04 | Voice: ГОТОВО в композиции — реестр строится от `IBrainstemWorker` (Brainstem STT + ffmpeg), Service регистрирует супервизор. Живой Whisper не проверен (нет бинаря воркера в среде) | TASK-052 | DONE (код), BLOCKED-ENV (live) |
| FIX-05 | Security: EgressSanitizer подключён в Tier23 (Blocked = модель не вызывается, тест есть). Осталось: AntiWipeGuard / AudioContentSafety без вызывающих | TASK-072 (done), 070, 071 | PARTIAL |
| FIX-06 | Подключено: BrainstemSupervisor (hosted-обёртка, не роняет службу), Quartz SyncWithRepository при старте, IDiagnosticsService в DI, StopAsync реально гасит Scheduler. Осталось: webhook — нет HTTP-роута | TASK-022, 045, 004 (done), 041 | PARTIAL |

## Дефекты внутри «готовых» задач

| ID | Дефект | Задача | Статус |
|---|---|---|---|
| FIX-07 | SemanticCache: ГОТОВО — `IEmbeddingStore.DeleteOldestAsync` (PG + фейки), вытеснение в Put, регрессионный тест | TASK-034 | DONE (live PG не проверен) |
| FIX-08 | EmbeddingStore.Search: ГОТОВО — порог MinScore в WHERE (`distance <= 1-MinScore`) до LIMIT. HNSW-индекса по-прежнему нет | TASK-012 | DONE (SQL), TODO (HNSW) |
| FIX-09 | Tier23/`CostUsd=0`: ГОТОВО на уровне губернатора — фактическая стоимость считается по IModelPriceProvider при записи (тест: $0 → $2.00 settled) | TASK-032, 033 | DONE |
| FIX-10 | Tier1: токенизатор — FNV-хеш, не WordPiece multilingual-e5; семантика на реальной модели не та, что в тестах | TASK-031 | TODO |
| FIX-11 | Sandbox: Deny-ACL отсутствует; fallback на plain CreateProcessW нарушает no-fallback; CreateRestrictedToken без SID | TASK-023 | TODO |
| FIX-12 | Service restart: в режиме службы — Exit(1) под SCM recovery (restart/5000 в install-скрипте); в консоли — честная остановка с предупреждением. Живой рестарт не проверен | TASK-020 | DONE (код), BLOCKED-ENV (live) |
| FIX-13 | Setup: PG-конструктор DefaultSetupStateRepository не подключён (in-memory ctor); Desktop показывает только баннер мастера | TASK-063 | TODO |
| FIX-14 | Telegram: Photo не маппится, /status захардкожен, /stop не прерывает задачу; без токена — Fake-адаптер | TASK-060 | TODO |
| FIX-15 | Models: installer без Avalonia View и не инстанцируется в Desktop; GPU у advisor всегда null → VRAM-путь мёртв | TASK-081, 082 | TODO |

## Не проверено никем (нужна среда)

| ID | Проверка | Статус |
|---|---|---|
| FIX-16 | Живой Postgres+pgvector: Data интеграция ГОТОВО — 75/75 тестов против контейнера pgvector:5434 (CYBERPIPE_TEST_POSTGRES). E2E-сценарии служб вживую прогнаны через CLI (см. FIX-01) | PARTIAL (E2E suite не гонялась целиком) |
| FIX-17 | Живые Telegram + LLM в E2E (сейчас фейки по дизайну), HITL live-path дефекты из TASK-092 | BLOCKED-ENV |
| FIX-18 | Windows: установка службы/перезагрузка, реальная ONNX e5, observability-стек (SigNoz заменён Grafana LGTM) | BLOCKED-ENV |
| FIX-19 | TASK-093: clean-install smoke, ручной чек-лист, dirtyTree в манифесте | BLOCKED-ENV |

## Порядок выполнения

FIX-07, FIX-08 (изолированные дефекты с юнит-тестами) → FIX-09 → FIX-04 → FIX-03 → FIX-05 → FIX-06 → FIX-01 (транспорт) → FIX-02 → FIX-11..15 → FIX-19.

## Найдено при доводке (не было в аудите)

- Production-композита `IIntentRouter` не существовало: каскад Tier0→Tier1→Tier23 жил только в E2E-харнессе. Добавлен `CompositeIntentRouter` в Router + регистрация в DI.
- `BrainstemSupervisor.StartAsync` блокировал старт хоста ожиданием воркера (15+ с) — hosted-обёртка переведена на фоновый запуск.
- CLI `chain` падал необработанным исключением при недоступном репозитории — добавлен try/catch с честной ошибкой.
