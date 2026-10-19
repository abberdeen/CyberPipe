# TASK-045 — Планировщик Quartz.NET в той же PostgreSQL (Cron-цепочки)

## Цель

Один проверяемый результат: Cron-цепочки регистрируются в Quartz.NET с хранением в той же PostgreSQL, срабатывают в срок, управляются (pause/resume/delete/list) и при пропущенном запуске ведут себя по заданной `misfirePolicy`.

## Контекст

- Канон: DECISIONS Q7/Q22 и ADR-002 — планировщик только Quartz.NET (не Hangfire, лицензионные ограничения), данные Quartz — в том же PostgreSQL, встроенного дашборда нет (управление позже в Avalonia UI, TASK-061; здесь программный интерфейс).
- Связь с цепочками: Quartz владеет расписанием, TASK-041 владеет триггером. Срабатывание Quartz вызывает `FireCron` триггера; регистрация идёт через `ICronTriggerRegistrar` из TASK-041.
- Подключение и миграции PostgreSQL — TASK-010/TASK-011; здесь только Quartz-таблицы в той же БД (официальный SQL Quartz для PostgreSQL) и свой сервис управления.

## Объём работ

- Конфигурация Quartz: `StdSchedulerFactory`, AdoJobStore (`QRTZ_` таблицы, тот же connection string, что и приложение), сериализация job data — JSON; кластеризация не требуется (один Service), но `instanceId` задаётся настройкой.
- `IChainSchedulerService`:
  - `ScheduleChainAsync(chainId, cronExpression)` — job+trigger, ключ детерминированно из `chainId` (повторная регистрация = замена расписания, не дубликат).
  - `PauseChainAsync` / `ResumeChainAsync` / `UnscheduleChainAsync` / `ListScheduledAsync → [{ChainId, Cron, NextFireTimeUtc, IsPaused}]`.
  - `TriggerChainNowAsync(chainId)` — ручной разовый запуск через тот же `FireCron`.
- Job `ChainCronJob` (Quartz `IJob`, `DisallowConcurrentExecution`): вызывает `ICronTriggerRegistrar.FireCron(chainId, scheduledFireTimeUtc)`; сам цепочку не исполняет.
- Валидация cron: формат Quartz (6–7 полей, секунды обязательны); невалидное выражение → ошибка `Scheduler.InvalidCronExpression` при регистрации, job не создаётся. Проверка `CronExpression.IsSatisfiedBy` не используется как замена парсера Quartz.
- Misfire: `MisfirePolicy` цепочки (TASK-040): `FireOnceNow` (по умолчанию) — один догоняющий запуск; `SkipIfLateByMinutes(N)` — пропуск при опоздании больше N; `AskUser` — не запускать, а отправить запрос через порт доставки (фейк в тестах; решение пользователя вне задачи). Политика применяется в `ChainCronJob`/listener при обнаружении пропущенного срабатывания.
- Синхронизация при старте: включённые цепочки с `Trigger.Kind=Cron` (репозиторий TASK-040/011) регистрируются, отключённые/удалённые — снимаются; расхождение Quartz и репозитория устраняется в пользу репозитория.
- Отложенное возобновление для `DeferRetry` (TASK-044): реализация порта `IDelayedResumeScheduler.ScheduleResume(runId, stepId, after)` одноразовым trigger Quartz.

## Вне рамок

- Исполнение цепочки (TASK-044), приём события Cron (TASK-041), UI расписаний (TASK-061), создание Quartz-таблиц вручную (только официальный скрипт Quartz + миграция TASK-011), системные встроенные задачи вне цепочек (другие области регистрируют свои job тем же сервисом позже).

## Зависимости

- TASK-002
- TASK-010
- TASK-011
- TASK-040
- TASK-041

## Контракты и интерфейсы

- `IChainSchedulerService`, `ScheduledChainInfo` (эта задача).
- `ICronTriggerRegistrar` (TASK-041), `IDelayedResumeScheduler` (TASK-002, реализация здесь), `IChainDefinitionRepository` (TASK-011).
- Ключи Quartz: job `chain/{chainId}`, trigger `chain/{chainId}/cron`, `resume/{runId}/{stepId}`.
- Предлагаемое размещение (to-be-created): `src/RouterAndSkills/CyberPipe.Skills.Netrunner/Scheduling/` или хостовый проект Service (TASK-020) — фиксируется при реализации, контракт от размещения не зависит.

## Результаты

- Сервис планирования, `ChainCronJob`, синхронизация при старте, интеграционные тесты на тестовой PostgreSQL.

## Критерии приёмки

- [x] Зарегистрированная цепочка с cron `0 * * * * ?` срабатывает в ближайшую минуту: фейк `FireCron` вызван с верным `chainId` и плановым временем.
- [x] Повторный `ScheduleChainAsync` с другим выражением заменяет расписание (один job, новое `NextFireTimeUtc`); `Pause` останавливает срабатывания, `Resume` возобновляет, `Unschedule` удаляет job и trigger.
- [x] Невалидные выражения (`"ежедневно"`, `"* * * *"`) отклонены с `InvalidCronExpression`, job не создан.
- [x] Misfire: пропущенное окно при `FireOnceNow` — один вызов `FireCron`; при `SkipIfLateByMinutes(5)` и опоздании 10 мин — ни одного; при `AskUser` — ни одного вызова и один запрос через фейк-порт доставки.
- [x] После «рестарта» сервиса (новый scheduler на той же БД) расписание и `NextFireTimeUtc` сохранены; отключённая в репозитории цепочка снята с расписания.
- [x] `ScheduleResume(runId, stepId, 60s)`: фейк возобновления вызван один раз примерно через 60 с (в тесте — укороченный интервал).

## Тестирование

- Уровень: integration.
- Как: реальный Quartz с AdoJobStore на тестовой PostgreSQL (та же схема, что TASK-010/011; Testcontainers или локальная тестовая БД — фиксируется при реализации), фейк `ICronTriggerRegistrar`; короткие cron-интервалы (секундные выражения Quartz), без ожидания минут в unit-тестах валидации cron (чистый парсер).

## Команды проверки

```powershell
dotnet build CyberPipe.slnx
dotnet test tests/CyberPipe.Tests.Chains/ --filter "FullyQualifiedName~CronValidation"
dotnet test tests/CyberPipe.Tests.Chains.Integration/ --filter "FullyQualifiedName~QuartzScheduling"
```

(Пути тестовых проектов — to-be-created, имена фиксируются в TASK-001.)

## Статус: Done
