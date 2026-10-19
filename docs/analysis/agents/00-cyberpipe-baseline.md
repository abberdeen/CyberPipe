# 00. CyberPipe (CyberPipeline) — базовый профиль продукта

> Дата составления: 2026-10-19. Статус продукта: **pre-implementation, docs-only**.
> Источники: [README архитектурного плана](../../plans/initial_arch/README.md),
> [16 — Netrunner ETL и движок workflow](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md),
> 40 отчётов в этой папке (`01`–`40`, см. [research-index.csv](research-index.csv)).
> Файл `research-index.csv` в репозитории до этой работы отсутствовал; перечень из 40 продуктов
> взят из задания на анализ, а не из CSV. Все возможности ниже — **запланированные (PLANNED)**,
> а не реализованные: в репозитории нет кода `.cs`, `CyberPipe.slnx` пуст (`<Solution />`).

## 1. Назначение

CyberPipe — локальный персональный ассистент и оркестратор автономных агентов на .NET 10.
Управление — через Telegram и Avalonia UI. Ключевая идея — 4-уровневый локальный роутинг
намерений, где бытовые задачи выполняются за 0 облачных токенов, а LLM подключается только
к очищенным данным и под контролем бюджета.

Источник: [README.md:16](../../plans/initial_arch/README.md) — «Сверхбыстрый, надежный и
экономный к токенам умный помощник и оркестратор автономных агентов на платформе .NET 10
с управлением через Telegram и Avalonia UI, 4-уровневым локальным маршрутизатором намерений
(0 токенов на бытовые задачи)…».

Внутри CyberPipe выделена подсистема **Netrunner** — детерминированный ETL + Mini-n8n DAG:

- стандартизированные пакеты ETL-скриптов (пользовательские или один раз синтезированные Tier 3),
выполняющие парсинг/очистку/трансформацию за 0 токенов;
- движок каскадных задач `SkillNode → NetrunnerScriptNode → ConditionNode → LlmProcessNode → HitlApprovalNode → DeliveryNode` с ветвлениями, параллелизмом и HITL.

Источник: [16-netrunner:3-5](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md).

## 2. Целевая аудитория

| Аудитория | Обоснование | Источник |
|---|---|---|
| Владелец одного ПК (Windows 10/11) | Локальный персональный ассистент, целевая ОС MVP — Windows; Linux/macOS позже | [README.md:9-14](../../plans/initial_arch/README.md) |
| Технический пользователь / разработчик | Инженерные скиллы: Git Radar, Build/Test Watchdog, мониторинг серверов | [README.md:56](../../plans/initial_arch/README.md) (раздел 12 плана) |
| Пользователь с бизнес-задачами одного человека | Personal CRM, запись и выжимка созвонов, проверка договоров | [README.md:56](../../plans/initial_arch/README.md) |

Командная/SaaS-аудитория в плане **не заявлена**: продукт одиночный, локальный, без
мультитенантности. Это осознанное отличие от n8n/Activepieces/Dify (см. их отчёты, §7).

## 3. Ключевые сценарии (запланированные)

| # | Сценарий | Как работает по плану | Источник |
|---|---|---|---|
| S1 | Бытовые команды за 0 токенов | Музыка, звук, таймеры — Fast-Path < 20 мс локально, без LLM | [README.md:26](../../plans/initial_arch/README.md) |
| S2 | Ежедневный мониторинг данных | Cron → SkillNode (fetch) → NetrunnerScriptNode (ETL 5000 строк → 3 записи) → ConditionNode → LlmProcessNode только при аномалии → HITL → DeliveryNode в Telegram | [16-netrunner:270](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) |
| S3 | Автосинтез нового ETL-пакета | Tier 3 генерирует 6 файлов пакета → validator + fixture-тест → HITL-регистрация → вечная работа за 0 токенов | [16-netrunner:565-579](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) |
| S4 | Критичное действие под контролем | HitlApprovalNode: пауза, чекпоинт в `netrunner.db`, кнопки в Telegram/UI, resume без повтора пройденных шагов | [16-netrunner:552-555](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) |
| S5 | Восстановление после сбоя питания | SQLite WAL + пошаговые чекпоинты; политика `ResumeFromCheckpoint` или `StopAndMarkCancelledByCrash` | [16-netrunner:507-550](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) |
| S6 | Личные/инженерные/бизнес-скиллы | Голос Whisper.net локально, «Второй мозг» (FTS5+SIMD), проактивные брифинги, CRM, созвоны | [README.md:56](../../plans/initial_arch/README.md) |

## 4. Запланированные возможности (PLANNED, не реализовано)

| Возможность | План | Источник |
|---|---|---|
| 4-tier роутинг: Tier0 regex → Tier1 ONNX (`multilingual-e5-small`) → Tier2 лёгкая LLM → Tier3 сложная LLM | Ядро продукта; Tier0/Tier1 — 0 токенов | [README.md:16,32](../../plans/initial_arch/README.md); [16:229](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) |
| TokenBudgetGovernor | Авто-пауза графа (`PausedByBudget`), кнопка «продолжить (+N токенов)», стоп-кран `/stop` < 50 мс локально | [README.md:16,32](../../plans/initial_arch/README.md); [16:593](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) |
| Netrunner Mini-n8n DAG | 8 типов узлов, 4 паттерна ветвления, `pollUntil`/DeferRetry (exit 75), лимиты графа | [16:251-264, 354-427, 582-596](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) |
| Стандарт пакетов скриптов | 6 обязательных файлов, SHA-256, JSON Schema Draft 2020-12, fixture Dry-Run Gate | [16:68-81, 230-233](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md); skill `netrunner-script-package` |
| Brainstem-песочница | Отдельный процесс `CyberPipe.Brainstem.exe`, AppContainer/Restricted Token (Win), bwrap (Linux), Seatbelt (macOS); прямой `Process.Start` запрещён арх-тестом | [README.md:65](../../plans/initial_arch/README.md); [16:242-247](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) |
| Безопасность хоста | Anti-Wipe & Media/Volume Guard, Volume Clamp 60%/25%, фильтр 18+/NSFW, whitelist Telegram Owner/Guest, запрет Raw Shell из чата | [README.md:23,35](../../plans/initial_arch/README.md) |
| Хранение | SQLite WAL: `cyberpipe_master.db` + `data/subsystems/*.db`, SQLCipher (гипотеза до PoC), DPAPI/LibSecret/Keychain | [README.md:9-14,47](../../plans/initial_arch/README.md) |
| Модели Local vs RemoteCloud | Curated-каталог 15 локальных + 8 облачных, `IHardwareCapabilitiesProbe` + `IModelCompatibilityAdvisor` | [README.md:59](../../plans/initial_arch/README.md) |
| UI | Avalonia: дашборд экономии токенов, конфигуратор Tiers, панель HITL | [README.md:41](../../plans/initial_arch/README.md) |
| Устойчивость ядра | Bulkhead Lanes, BoundedChannel, `InFlightOperations`, Resource Circuit Breaker, ToolOutputTruncator 1500 симв. | [README.md:50,62](../../plans/initial_arch/README.md) |

## 5. Архитектура (план)

```text
Telegram / Avalonia UI
        |
CyberPipe.exe ── Tier0 regex ── Tier1 ONNX+SemanticCache ── Tier2/Tier3 LLM (Local|RemoteCloud)
        |                TokenBudgetGovernor (пауза/стоп/учёт на каждом LLM-шаге)
        ├── Netrunner DAG (netrunner.db, SQLite WAL, чекпоинт после каждого узла)
        │     SkillNode → NetrunnerScriptNode → ConditionNode → LlmProcessNode → HitlApprovalNode → DeliveryNode
        ├── CyberPipe.Brainstem.exe (изолированный воркер: скрипты, Whisper, PDF/DOCX пулы)
        └── Хранилища: cyberpipe_master.db + data/subsystems/*.db (ISubsystemDatabaseManager)
Шина: MassTransit InMemory (по умолчанию) / SQLite WAL / RabbitMQ только как enterprise-опция
```

Источники: [README.md:9-16, 26-32, 64-68](../../plans/initial_arch/README.md);
[16:430-505](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md).

## 6. Ограничения и честный статус

| Ограничение | Факт | Источник |
|---|---|---|
| Кода нет | Docs-only; реализация только после этапа 0 (ADR + реестр БД + PoC шифрования/изоляции) | [README.md:3-8](../../plans/initial_arch/README.md) |
| Платформа MVP | Только Windows 10/11; Linux/macOS — после первой рабочей версии | [README.md:12-13](../../plans/initial_arch/README.md) |
| Шифрование не доказано | SQLCipher — гипотеза до PoC | [README.md:11-12](../../plans/initial_arch/README.md) |
| SLO условны | Сеть Telegram исключена из SLO; Fast-Path < 20 мс и `/stop` < 50 мс — локальные замеры по методике раздела 15 | [README.md:13-14, 26, 32](../../plans/initial_arch/README.md) |
| Один пользователь | Мультитенантности, командного доступа и облачной синхронизации в плане нет | Вывод из [README.md:16,35](../../plans/initial_arch/README.md) (whitelist Owner/Guest) |
| Зависимость от внешних LLM | Tier2/Tier3 RemoteCloud требует сети и API-ключей; офлайн гарантированы только Tier0/Tier1 + локальные модели | [README.md:59](../../plans/initial_arch/README.md) |

## 7. Открытые вопросы

| # | Вопрос | Где зафиксирован |
|---|---|---|
| Q1 | Подтвердит ли PoC SQLCipher-провайдер для SQLite? | [README.md:11](../../plans/initial_arch/README.md) |
| Q2 | Достаточна ли InMemory-шина MassTransit без брокера для всех сценариев Netrunner? | [README.md:9-10](../../plans/initial_arch/README.md) |
| Q3 | Как поведёт себя Tier1 ONNX-роутинг на реальном потоке бытовых команд (точность intent)? Метрики в плане не заданы | Отсутствует в [README.md](../../plans/initial_arch/README.md) и [16](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) — пробел плана |
| Q4 | Нужен ли визуальный редактор DAG в Avalonia или достаточно JSON + Telegram-кнопок? У конкурентов (n8n/Dify) canvas — ядро UX | Сопоставление §7 отчётов n8n/dify против [16:268](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) (только JSON) |
| Q5 | Как тестировать качество Tier2/Tier3-промптов (evals)? У Pydantic AI это первоклассная фича, в плане eval-контура нет | [pydantic-ai.md](pydantic-ai.md) §7 против плана |
| Q6 | Политика при исчерпании бюджета посреди DAG, кроме паузы: приоритеты сценариев, деградация на локальную модель? | Частично: [16:593](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md); приоритеты не определены |
| Q7 | Миграции состояния при смене ID моделей/провайдеров (урок переименования CoPaw→QwenPaw) | [copaw.md](copaw.md) §7 |
