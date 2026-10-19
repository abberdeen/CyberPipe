# 05-chains — Цепочки Trigger → Skill → Script → Delivery

## Назначение

Линейные цепочки автоматизации CyberPipe (.NET 10): определение цепочки конфигурацией, четыре триггера, валидатор пакетов скриптов `skill-hack.*`, раннеры скриптов, исполнитель с чекпоинтами в PostgreSQL, расписание Quartz.NET и HITL-подтверждения.

Канон: [DECISIONS.md](../../plans/DECISIONS.md) (Q5/Q20, Q8, Q7) и [17-adr-mvp.md](../../plans/initial_arch/17-adr-mvp.md) (ADR-002, ADR-003, ADR-004) перекрывают старый текст [16-netrunner-etl-and-workflow-engine.md](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md).

## Жёсткие правила области

- Только цепочка **Trigger → Skill → Script → Delivery**. Цепочек много. Визуального редактора нет, цепочек-графов, ветвлений, параллельных веток и LLM-шагов внутри цепочки нет: термины Mini-n8n/DAG/workflow/граф из планов удалены.
- Триггеры только: Webhook, Cron, function call, сообщение из чата.
- Точка входа пакета — `skill-hack.*`, не `main.*`. В `netrunner.manifest.json` обязательны `runtime.language`, `runtime.languageVersion`, `runtime.environment`, `runtime.environmentVersion`, `runtime.entrypoint`.
- Шаблон в `.agents/skills/netrunner-script-package/` (read-only) устарел (`main.*`, Hangfire, отдельная `netrunner.db`): не использовать как канон, верны ADR-003/ADR-004.
- Quartz.NET хранит данные в той же единственной PostgreSQL + pgvector. Отдельных баз подсистем нет.
- Сообщения и доставка — только через собственные интерфейсы проекта (TASK-002), конкретный брокер не фиксируется.
- Только Owner, Guest нет. DevTools/Git/Build-скиллов нет.
- Лимит памяти песочницы задаётся настройкой (без жёстких 64 МБ), таймауты — настройкой пакета.
- SLO-цифры — цели, не ворота приёмки.

## Задачи

| ID | Файл | Результат | Статус |
|---|---|---|---|
| TASK-040 | [TASK-040-chain-definition-config.md](TASK-040-chain-definition-config.md) | Формат и валидатор определения цепочки (конфигурация) | Planned |
| TASK-041 | [TASK-041-triggers.md](TASK-041-triggers.md) | Триггеры Webhook/Cron/function call/chat → единый `TriggerEvent` | Planned |
| TASK-042 | [TASK-042-skill-package-manifest-validator.md](TASK-042-skill-package-manifest-validator.md) | Валидатор пакета `skill-hack.*` и `netrunner.manifest.json` | Planned |
| TASK-043 | [TASK-043-script-runners.md](TASK-043-script-runners.md) | Раннеры скриптов по языкам/средам через песочницу | Planned |
| TASK-044 | [TASK-044-chain-executor.md](TASK-044-chain-executor.md) | Исполнитель цепочки с чекпоинтами в PostgreSQL | Planned |
| TASK-045 | [TASK-045-quartz-scheduling.md](TASK-045-quartz-scheduling.md) | Quartz.NET в той же PostgreSQL, управление Cron-цепочками | Planned |
| TASK-046 | [TASK-046-hitl-approval.md](TASK-046-hitl-approval.md) | HITL: Approve/Edit/Reject/Timeout через Telegram/Avalonia | Planned |

Статус `Ready` не выставлен ни одной задаче: у каждой есть зависимость (правило области — `Ready` только без зависимостей).

## Порядок и зависимости

```text
TASK-002 ──► TASK-040 ──► TASK-041 ──► TASK-044 ──► TASK-046
TASK-002 ──► TASK-042 ──► TASK-043 ──► TASK-044
TASK-002, TASK-010, TASK-011, TASK-040, TASK-041 ──► TASK-045 ──(событие Cron)──► TASK-041/TASK-044
TASK-002, TASK-011 ──► TASK-044 (чекпоинты), TASK-046 (запросы подтверждения)
```

Циклов нет: TASK-044 зависит от порта `IHitlApprovalGate` из TASK-002, а не от TASK-046; TASK-046 зависит от TASK-044 (порт возобновления). TASK-045 зависит от TASK-041 только контрактом `ICronTriggerRegistrar`, событие Cron принимает TASK-041.

## Общие замечания

- Все пути проектов помечены to-be-created: кода ещё нет, `CyberPipe.slnx` пуст. Ориентир по раскладке — [06-solution-structure.md](../../plans/initial_arch/06-solution-structure.md) с правками DECISIONS (проект цепочек — `src/RouterAndSkills/CyberPipe.Skills.Netrunner/`, to-be-created).
- Команды проверки — стандартные `dotnet build` / `dotnet test`; точные имена тестовых проектов фиксируются при создании (TASK-001).
- Чужие области не трогаем: реальные Telegram/Avalonia/CLI — TASK-060/061/062 (здесь только порты и фейки), песочница Restricted Token+Job Object — TASK-023 (здесь только порт `ISandboxProvider` и фейк), роутер Tier 0–3 — область 04 (цепочка запускается триггером, а не роутером).
