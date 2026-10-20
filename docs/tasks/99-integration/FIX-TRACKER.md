# FIX-TRACKER — доводка по итогам аудита реальной готовности

Аудит 2026-10-20: статусы Done в задачах означают «логика + юнит-тесты», но не «работает в production-композиции». Ниже — фактические разрывы. Статусы: `TODO` / `IN-PROGRESS` / `DONE` / `BLOCKED-ENV` (нужна живая среда: Postgres, Windows-служба, Telegram, ONNX-модель).

## Блокеры продукта

| ID | Разрыв | Задачи | Статус |
|---|---|---|---|
| FIX-01 | CLI: `CliContext` только с Options — Router/ServiceControl/Hitl/Setup = null, боевой `ask/service` = ServiceUnavailable. Нужен транспорт к Service (расширение pipe-протокола TASK-020 командами route/budget/chain/hitl) + клиент в CLI | TASK-062, TASK-093 (F-CLI-SERVICE-CONTROL) | TODO |
| FIX-02 | Desktop боевой вход `CreateDefault()` на Standalone* (эхо-роутер, захардкоженный бюджет, no-op HITL/планировщик). Нужен тот же транспорт, что FIX-01 | TASK-061 | TODO (зависит от FIX-01) |
| FIX-03 | Персистентность: Budget=`InMemoryBudgetStore`, ChainExecution/PackageRegistry=in-memory, личные репозитории (заметки/финансы/контакты)=in-memory. Postgres backing не подключён, данные теряются при рестарте | TASK-033, 042, 044, 051 | TODO |
| FIX-04 | Voice: пустой `VoiceSkillRegistry()`, Whisper-движок (Brainstem STT) не зарегистрирован — `voice.transcribe` отсутствует в композиции | TASK-052 | TODO |
| FIX-05 | Security без вызывающих: AntiWipeGuard / AudioContentSafety / EgressSanitizer никто не вызывает вне Security (0 вызовов в src) | TASK-070, 071, 072 | TODO |
| FIX-06 | Не подключено: BrainstemSupervisor не hosted-сервис; Quartz — никто не вызывает ScheduleChainAsync; webhook — нет HTTP-роута; IDiagnosticsService не в DI | TASK-022, 045, 041, 004 | TODO |

## Дефекты внутри «готовых» задач

| ID | Дефект | Задача | Статус |
|---|---|---|---|
| FIX-07 | SemanticCache: лимит записей — log-only no-op, вытеснения нет | TASK-034 | IN-PROGRESS |
| FIX-08 | EmbeddingStore.Search: `LIMIT TopK` в SQL до фильтра MinScore → возвращает меньше TopK; нет HNSW | TASK-012 | IN-PROGRESS |
| FIX-09 | Tier23: `CostUsd=0` всегда — стоимость не учитывается; CatalogModelPriceProvider есть в ServiceComposition, но не в Router DI по умолчанию | TASK-032, 033 | TODO |
| FIX-10 | Tier1: токенизатор — FNV-хеш, не WordPiece multilingual-e5; семантика на реальной модели не та, что в тестах | TASK-031 | TODO |
| FIX-11 | Sandbox: Deny-ACL отсутствует; fallback на plain CreateProcessW нарушает no-fallback; CreateRestrictedToken без SID | TASK-023 | TODO |
| FIX-12 | Service restart = stop (оба callback — StopApplication) | TASK-020 | TODO |
| FIX-13 | Setup: PG-конструктор DefaultSetupStateRepository не подключён (in-memory ctor); Desktop показывает только баннер мастера | TASK-063 | TODO |
| FIX-14 | Telegram: Photo не маппится, /status захардкожен, /stop не прерывает задачу; без токена — Fake-адаптер | TASK-060 | TODO |
| FIX-15 | Models: installer без Avalonia View и не инстанцируется в Desktop; GPU у advisor всегда null → VRAM-путь мёртв | TASK-081, 082 | TODO |

## Не проверено никем (нужна среда)

| ID | Проверка | Статус |
|---|---|---|
| FIX-16 | Живой Postgres+pgvector: интеграционные тесты Data/E2E (gated по env) | BLOCKED-ENV |
| FIX-17 | Живые Telegram + LLM в E2E (сейчас фейки по дизайну), HITL live-path дефекты из TASK-092 | BLOCKED-ENV |
| FIX-18 | Windows: установка службы/перезагрузка, реальная ONNX e5, observability-стек (SigNoz заменён Grafana LGTM) | BLOCKED-ENV |
| FIX-19 | TASK-093: clean-install smoke, ручной чек-лист, dirtyTree в манифесте | BLOCKED-ENV |

## Порядок выполнения

FIX-07, FIX-08 (изолированные дефекты с юнит-тестами) → FIX-09 → FIX-04 → FIX-03 → FIX-05 → FIX-06 → FIX-01 (транспорт) → FIX-02 → FIX-11..15 → FIX-19.
