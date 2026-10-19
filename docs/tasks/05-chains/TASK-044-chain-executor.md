# TASK-044 — Исполнитель цепочки Trigger → Skill → Script → Delivery с чекпоинтами в PostgreSQL

## Цель

Один проверяемый результат: исполнитель проходит линейную цепочку строго по шагам Trigger → Skill → Script → (HITL-гейт, если включён) → Delivery, пишет чекпоинт после каждого шага в PostgreSQL и после рестарта продолжает с первого незавершённого шага, не повторяя завершённые.

## Контекст

- Канон: ADR-003 — только линейная цепочка, никаких ветвлений/параллелизма/LLM-шагов внутри; документ 16 в части DAG перекрыт DECISIONS, в части чекпоинтов и восстановления (§4, §4.1) — применим с заменой отдельной базы на таблицы единой PostgreSQL.
- Чекпоинты и состояние — только PostgreSQL (TASK-011, таблицы области цепочек); ничего не хранится только в файлах, артефакты файлов шагов — в рабочем каталоге запуска со ссылками и SHA-256 в БД.
- Доставка — через порт доставки собственных интерфейсов сообщений (TASK-002); реальный Telegram — TASK-060, здесь фейк.

## Объём работ

- `IChainExecutor` (порт из TASK-041): `StartAsync(TriggerEvent) → RunId`, `ResumeAsync(RunId)`, `CancelAsync(RunId)`, `GetRunAsync(RunId) → ChainRunState`.
- Статусы запуска: `Running`, `WaitingForHitl`, `WaitingForRetry`, `Completed`, `Failed`, `Cancelled`, `CancelledByCrash`; статусы шага: `Pending`, `Running`, `Completed`, `Failed`, `Skipped`, `WaitingApproval`.
- Пошаговое исполнение (ровно в этом порядке, без пропусков и веток):
  1. Trigger: фиксация `TriggerEvent` как вход запуска.
  2. Skill: вызов зарегистрированного C#-скилла по `SkillId` (порт `ISkillInvoker`, фейк в тестах) с `InputMapping`; выход — JSON + файлы.
  3. Script: `IScriptRunner.RunAsync` (TASK-043) на пакете из TASK-042; вход — маппинг из выходов trigger/skill.
  4. HITL-гейт: если `ChainDefinition.RequiresHitl` — чекпоинт `WaitingForHitl` и вызов порта `IHitlApprovalGate.RequestApprovalAsync(...)` (реализация TASK-046); без HITL шаг отсутствует, а не помечается веткой.
  5. Delivery: `IDeliveryPort.SendAsync(channel, target, template+выход script, файлы)`; ошибка доставки — `Failed` с шагом Delivery, повтор всей цепочки не выполняется.
- Чекпоинт после каждого шага одной транзакцией репозитория: статус шага, вход/выход JSON, манифест файлов (путь, SHA-256, размер), `AttemptCount`, времена; снапшот запуска обновляется до старта следующего шага.
- Ошибка шага: `retryPolicy` цепочки (`maxAttempts`, `backoffMs`) — повтор только упавшего шага; исчерпание → запуск `Failed`, Delivery не вызывается. `DeferRetry` от скрипта → `WaitingForRetry` + регистрация отложенного возобновления через порт планировщика (TASK-045; фейк в тестах), процесс/поток не удерживается.
- Восстановление при старте (`ChainRecoveryService`): сканер незавершённых запусков; политика из настройки `Pipeline:CrashRecoveryPolicy`: `ResumeFromCheckpoint` — продолжить с первого незавершённого шага (временный выход прерванного шага очищается, завершённые шаги не повторяются); `StopAndMarkCancelledByCrash` — запуск и прерванный шаг → `CancelledByCrash`, файлы завершённых шагов сохраняются.
- Идемпотентность: `StartAsync` с уже известным `TriggerEvent.IdempotencyKey` возвращает существующий `RunId`; отмена `CancelAsync` останавливает текущий шаг токеном и фиксирует `Cancelled`.

## Вне рамок

- Триггеры (TASK-041), раннеры и песочница (TASK-043/TASK-023), реализация HITL и кнопок (TASK-046), планировщик (TASK-045), реальные скиллы областей 06 и каналы TASK-060/061/062 (только порты/фейки), LLM-шаги и TokenBudgetGovernor (область 04; в цепочке их нет).

## Зависимости

- TASK-002
- TASK-011
- TASK-040
- TASK-041
- TASK-042
- TASK-043

## Контракты и интерфейсы

- `IChainExecutor`, `ChainRunState`, `ChainRunStatus`, `ChainStepState` (TASK-002 / эта задача).
- Порты-потребители: `ISkillInvoker`, `IDeliveryPort`, `IHitlApprovalGate`, `IDelayedResumeScheduler` (все — TASK-002, реализации/фейки вне задачи).
- `IChainRunRepository` (реализация TASK-011): `CreateRunAsync`, `SaveStepCheckpointAsync`, `LoadRunAsync`, `ListUnfinishedAsync`, `TryCreateByIdempotencyKeyAsync`. Таблицы (предложение для TASK-011): `chain_runs`, `chain_step_runs`.
- Предлагаемое размещение (to-be-created): `src/RouterAndSkills/CyberPipe.Skills.Netrunner/Execution/`.

## Результаты

- Исполнитель + сервис восстановления, фейки всех портов, сценарии: успешная цепочка, падение на каждом шаге, рестарт посреди цепочки.

## Критерии приёмки

- [ ] Успешный прогон: шаги выполнены в порядке Skill → Script → Delivery ровно по одному разу, статусы чекпоинтов в фейк-репозитории соответствуют, запуск `Completed`.
- [ ] Падение Script (фейк-ошибка): Delivery не вызван, запуск `Failed`, повтор — только шаг Script по `retryPolicy`.
- [ ] Имитация рестарта после чекпоинта Script: `ResumeAsync`/recovery продолжает с Delivery, Skill и Script повторно не вызываются (счётчики фейков).
- [ ] Политика `StopAndMarkCancelledByCrash`: незавершённый запуск получает `CancelledByCrash`, ни один шаг повторно не выполнен.
- [ ] `RequiresHitl=true`: без решения гейта Delivery не вызван, запуск в `WaitingForHitl`; после `Approved` (фейк-гейт) — Delivery вызван один раз.
- [ ] Повторный `StartAsync` с тем же ключом идемпотентности возвращает тот же `RunId`, второго запуска нет.

## Тестирование

- Уровень: component + integration (репозиторий).
- Как: фейки `ISkillInvoker`/`IScriptRunner`/`IDeliveryPort`/`IHitlApprovalGate` со счётчиками вызовов; in-memory фейк `IChainRunRepository` для component-тестов и интеграционный тест на реальной PostgreSQL (TASK-010/011, Testcontainers или локальная тестовая БД — фиксируется при реализации) для транзакционности чекпоинта. «Рестарт» — новый экземпляр исполнителя на том же репозитории.

## Команды проверки

```powershell
dotnet build CyberPipe.slnx
dotnet test tests/CyberPipe.Tests.Chains/ --filter "FullyQualifiedName~ChainExecutor"
dotnet test tests/CyberPipe.Tests.Chains.Integration/ --filter "FullyQualifiedName~ChainCheckpoint"
```

(Пути тестовых проектов — to-be-created, имена фиксируются в TASK-001.)

## Статус: Planned
