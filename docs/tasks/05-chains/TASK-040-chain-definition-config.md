# TASK-040 — Определение цепочки конфигурацией (ChainDefinition)

## Цель

Один проверяемый результат: формат определения цепочки `Trigger → Skill → Script → Delivery` в конфигурации (JSON) + валидатор, который принимает только линейную цепочку и отклоняет всё остальное с кодами ошибок.

## Контекст

- Канон: DECISIONS Q5/Q20 и ADR-003 — только цепочка Trigger → Skill → Script → Delivery, цепочек сколько угодно, визуального редактора нет, цепочки описываются конфигурацией.
- Старый документ 16 (DAG, Condition/Parallel/Llm-узлы, edges) перекрыт DECISIONS и здесь не применяется.
- Определения хранятся в единственной PostgreSQL (доступ — через репозиторий TASK-011), файл конфигурации — формат импорта/экспорта и первичной загрузки, не отдельное хранилище состояния.

## Объём работ

- DTO `ChainDefinition` (в `CyberPipe.Contracts`, TASK-002; здесь — требования к нему и использование): `Id`, `Name`, `IsEnabled`, `Trigger`, `Skill`, `Script`, `Delivery`, `RequiresHitl`, `RetryPolicy`, `MisfirePolicy`, `Version`, `CreatedAtUtc`, `UpdatedAtUtc`.
- Секции ровно четыре, по одной на шаг:
  - `Trigger`: `Kind` (`Webhook|Cron|FunctionCall|Chat`), параметры вида (путь webhook, cron-выражение строкой, имя функции, фильтр чата/команды).
  - `Skill`: `SkillId` (идентификатор зарегистрированного C#-скилла), `InputMapping` (ключ → значение/шаблон `{{ trigger.payload.* }}`).
  - `Script`: `PackageSlug`, `PackageVersion`, `InputMapping`, `FileInputBindings`.
  - `Delivery`: `Channel` (логический канал доставки из TASK-002, не конкретный брокер), `Target`, `MessageTemplate`, `SendFiles`.
- `ChainDefinitionValidator`: обязательность всех четырёх секций; ровно один триггер, один скилл, один скрипт, один канал доставки; `Kind` — только из четырёх известных; slug — kebab-case, версия — SemVer; шаблоны маппинга ссылаются только на `trigger.*`, `skill.*`, `script.*`; неизвестные поля верхнего уровня отклоняются.
- Явный запрет в валидаторе: поля/типы ветвлений, параллелизма, условий-переходов, LLM-шагов, списки `nodes`/`edges` — ошибка `ChainDefinition.BranchingNotSupported` (не молчаливое игнорирование).
- Сериализация/десериализация JSON (System.Text.Json), `ChainDefinitionLoader`: чтение из конфигурации и из репозитория, слияние запрещено — источник один на вызов, приоритет задаёт вызывающий код.
- Версионирование: правка включённой цепочки увеличивает `Version`; отключённая цепочка (`IsEnabled=false`) валидна, но не запускается.

## Вне рамок

- Исполнение цепочки (TASK-044), регистрация триггеров (TASK-041), валидация пакета скрипта (TASK-042), расписание Quartz (TASK-045).
- Визуальный редактор, импорт форматов n8n/чужих workflow, ветвления и подцепочки.
- UI редактирования (Avalonia показывает/правит этот JSON позже, TASK-061).

## Зависимости

- TASK-002

## Контракты и интерфейсы

- `ChainDefinition`, `ChainTriggerKind`, `ChainRetryPolicy`, `ChainMisfirePolicy` (DTO/enum, TASK-002).
- `IChainDefinitionValidator.Validate(ChainDefinition) → ChainValidationResult` (`IsValid`, `IReadOnlyList<ChainError{Code, Path, Message}>`).
- `IChainDefinitionRepository` (реализация — TASK-011): `GetByIdAsync`, `ListEnabledAsync`, `UpsertAsync`, `SetEnabledAsync`; здесь только потребление через фейк.
- Коды ошибок: `ChainDefinition.MissingStep`, `ChainDefinition.UnknownTriggerKind`, `ChainDefinition.BranchingNotSupported`, `ChainDefinition.InvalidSlug`, `ChainDefinition.InvalidVersion`, `ChainDefinition.UnknownMappingSource`.
- Предлагаемое размещение (to-be-created): `src/RouterAndSkills/CyberPipe.Skills.Netrunner/Chains/`; DTO — `src/Shared/CyberPipe.Contracts/` (TASK-002).

## Результаты

- Формат ChainDefinition (JSON) с примером валидной цепочки каждого вида триггера (4 фикстуры).
- Валидатор + загрузчик, тестовый проект с фикстурами валидных/невалидных определений.

## Критерии приёмки

- [ ] Валидная цепочка каждого из 4 видов триггера проходит валидацию; цепочка без любого из 4 шагов отклоняется с `MissingStep` и путём поля.
- [ ] Определение со списком `nodes`/`edges`, условием-переходом или вторым скриптом отклоняется с `BranchingNotSupported`.
- [ ] Неизвестный `Trigger.Kind`, битый slug/SemVer, маппинг на несуществующий источник (`{{ llm.* }}`) отклоняются с соответствующими кодами.
- [ ] Round-trip JSON: `Deserialize(Serialize(def))` равен исходному; отключённая цепочка валидируется, но не попадает в `ListEnabledAsync` фейка.
- [ ] Ни одного упоминания DAG/графа/n8n в публичных именах типов.

## Тестирование

- Уровень: unit.
- Как: фикстуры JSON (валидные ×4, невалидные по одному на каждый код ошибки); фейк `IChainDefinitionRepository` в памяти; тесты round-trip и кодов ошибок. PostgreSQL не требуется.

## Команды проверки

```powershell
dotnet build CyberPipe.slnx
dotnet test tests/CyberPipe.Tests.Chains/ --filter "FullyQualifiedName~ChainDefinition"
```

(Путь тестового проекта — to-be-created, имя фиксируется в TASK-001.)

## Статус: Planned
