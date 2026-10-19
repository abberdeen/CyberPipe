# TASK-002 — Контракты и интерфейсы (DTO, интерфейсы, абстракции сообщений)

## Цель

Определить в `CyberPipe.Contracts` все общие DTO, перечисления и интерфейсы, через которые общаются Service, Desktop, CLI, Brainstem, каналы и цепочки, включая собственные абстракции обмена сообщениями с заменяемым транспортом.

## Контекст

- Канон: [DECISIONS.md](../../plans/DECISIONS.md), [17-adr-mvp.md](../../plans/initial_arch/17-adr-mvp.md) (ADR-001, ADR-002, ADR-003, ADR-008). Старые тексты про фиксированный брокер, отдельные базы подсистем и роль Guest не действуют.
- Обмен сообщениями пишется только через собственные интерфейсы проекта: бизнес-логика не зависит от реализации транспорта; конкретный брокер не фиксируется и не является обязательным.
- Цепочки только `Trigger -> Skill -> Script -> Delivery`; триггеры: Webhook, Cron, function call, сообщение из чата. Контракты цепочек нужны здесь как типы; определение и исполнение цепочек — TASK-040, TASK-044.
- Роли Guest нет: в контрактах допустим только Owner (проверка владельца — TASK-073).
- Токен-бюджет по умолчанию (Q23): дневной кап $0.50, soft 4000 / hard 12000 токенов, 3 облачных / 5 локальных ходов — всё конфигурируемо; типы лимитов определяются здесь, применение — TASK-033.
- 4 яруса маршрутизации: Tier0 regex -> Tier1 ONNX-эмбеддинг + семантический кэш -> Tier2 лёгкая LLM -> Tier3 тяжёлая LLM; типы ярусов и результатов маршрутизации определяются здесь, реализация — TASK-030..TASK-034.

## Объём работ

1. Перечисления и базовые типы (to-be-created, `src/CyberPipe.Contracts/`):
   - `IntentTier` (Tier0..Tier3), `RiskLevel`, `TaskLifecycleState` (Created/Running/WaitingApproval/Completed/Failed/Cancelled), `ChainTriggerKind` (Webhook/Cron/FunctionCall/Chat), `ChannelKind` (Telegram/Desktop/Cli).
   - Идентификаторы: `ChainId`, `RunId`, `MessageId` (типизированные обёртки над `Guid`/`string`).
2. DTO сообщений канала: входящее сообщение (канал, идентификатор владельца, текст/вложение, время), исходящее сообщение (текст, вложения, кнопки действий), запрос и результат HITL-подтверждения (действие, контекст, решение Owner: одобрено/отклонено; исполнение — TASK-046).
3. Абстракции обмена сообщениями (собственные интерфейсы, без привязки к транспорту):
   - `IMessageBus` (публикация/подписка на типизированные сообщения), `IMessageHandler<TMessage>`, `IMessageTransport` (точка замены реализации транспорта).
   - Контракт запрещает утечку типов конкретного брокера в сигнатуры.
4. Интерфейсы маршрутизации и бюджета: `IIntentRouter` (запрос -> ярус + результат), `ITokenBudgetGovernor` (запрос оценки/резерва бюджета, статус бюджета `BudgetStatusDto`), DTO расхода токенов и стоимости.
5. Контракты цепочек: `ChainDefinitionDto` (триггер, навык, скрипт, доставка), `ChainRunContext`, `ChainCheckpointDto` (состояние шага для хранения в PostgreSQL; хранение — TASK-044), `DeliveryRequest`/`DeliveryResult`.
6. Контракты скриптовых пакетов как типы: `NetrunnerManifestDto` с полями `runtime.language`, `runtime.languageVersion`, `runtime.environment`, `runtime.environmentVersion`, `runtime.entrypoint` (значение — файл `skill-hack.*`); валидация — TASK-042.
7. Контракты моделей как типы: `ModelDescriptorDto` (локальная/облачная, провайдер, контекст, цена), `TierModelAssignment`; каталоги и установка — TASK-080, TASK-081.
8. Общие типы ошибок: `ErrorDto` (код, сообщение, детали), результат-тип `Result<T>` для межпроцессных вызовов Service <-> Desktop/CLI.
9. Интерфейс управления службой: `IServiceControl` (Start/Stop/Restart/Status) — контракт для TASK-021; реализация управления — там же.

## Вне рамок

- Любые реализации интерфейсов, DI-регистрации и транспорт сообщений (TASK-090 и профильные задачи).
- Хранение чего-либо в PostgreSQL (TASK-010..TASK-013); контракты не ссылаются на драйверы БД.
- Валидация манифестов, исполнение скриптов и цепочек (TASK-042, TASK-043, TASK-044).
- Сериализационные транспортные детали (gRPC/pipe-протоколы) сверх DTO; формат сериализации выбирается в TASK-090.

## Зависимости

- TASK-001

## Контракты и интерфейсы

- Проект `CyberPipe.Contracts` (to-be-created) — единственный носитель контрактов; ноль ссылок на проекты решения и на пакеты конкретных транспортов/брокеров.
- Публичные сигнатуры: `IMessageBus`, `IMessageTransport`, `IIntentRouter`, `ITokenBudgetGovernor`, `IServiceControl`; DTO: `BudgetStatusDto`, `ChainDefinitionDto`, `ChainCheckpointDto`, `NetrunnerManifestDto`, `ModelDescriptorDto`, `ErrorDto`, `Result<T>`.
- В контрактах нет типов Guest, DevTools/Git/Build и фиксированного брокера.

## Результаты

- Проект `CyberPipe.Contracts` со всеми типами из раздела «Объём работ».
- XML-документация на каждом публичном типе: назначение и потребитель (какая задача реализует/использует).

## Критерии приёмки

- `dotnet build CyberPipe.slnx` проходит без предупреждений.
- Архитектурный тест подтверждает: `CyberPipe.Contracts` не ссылается на проекты решения и пакеты транспортов.
- Поиск по проекту не находит идентификаторов, связанных с Guest, DevTools/Git/Build, SQLite, Hangfire и конкретными брокерами.
- Каждый интерфейс из «Контракты и интерфейсы» имеет хотя бы одну задачу-потребителя в глобальной карте задач.

## Тестирование

- Уровень: unit + architecture.
- Как: тесты сериализации DTO (round-trip JSON для `NetrunnerManifestDto`, `ChainDefinitionDto`, `BudgetStatusDto`) в `CyberPipe.Tests.Unit`; архитектурный тест на отсутствие запрещённых ссылок. Реализации интерфейсов на этом этапе не тестируются — используются fake-реализации в задачах-потребителях.

## Команды проверки

```powershell
dotnet build CyberPipe.slnx
dotnet test tests/CyberPipe.Tests.Unit
dotnet test tests/CyberPipe.Tests.Architecture
```

## Статус: Planned
