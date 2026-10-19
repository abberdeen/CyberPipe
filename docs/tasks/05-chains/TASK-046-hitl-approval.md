# TASK-046 — HITL-подтверждения (Approve / Edit / Reject / Timeout)

## Цель

Один проверяемый результат: цепочка с `RequiresHitl` встаёт на паузу с сохранённым в PostgreSQL запросом подтверждения, владелец через Telegram или Avalonia (порты каналов) нажимает Approve/Edit/Reject — и цепочка продолжается ровно со следующего шага, отклоняется или завершается по таймауту, в том числе после перезапуска приложения.

## Контекст

- Канон: контекст проекта — HITL approvals via Telegram/Avalonia; документ 16 (§1, §3 `HitlApprovalNode`) в части кнопок и чекпоинта применим, в части DAG — перекрыт DECISIONS: HITL — не узел графа, а гейт между Script и Delivery в линейной цепочке (TASK-044).
- Только Owner (Q18, Guest нет): решение принимает только владелец; проверка Owner — порт аутентификации TASK-073, здесь фейк.
- Реальные каналы — TASK-060 (Telegram) и TASK-061 (Avalonia); здесь порты доставки запросов и приёма решений + фейки обоих каналов.

## Объём работ

- Реализация порта `IHitlApprovalGate` (TASK-044): `RequestApprovalAsync(HitlApprovalRequest) → HitlDecision` не блокирует поток исполнителя: исполнитель уже сохранил чекпоинт `WaitingForHitl` (TASK-044); гейт создаёт запрос и возвращает управление, возобновление — отдельным вызовом `IChainExecutor.ResumeAsync` после решения.
- `HitlApprovalRequest`: `ApprovalId`, `RunId`, `ChainId`, `Title`, `PreviewText` (из выхода Script по шаблону цепочки), `PayloadJson` (то, что уйдёт в Delivery), `AttachFiles[]`, `ExpiresAtUtc` (по умолчанию из настройки, ориентир — 24 ч), `CreatedAtUtc`.
- `IHitlApprovalService`:
  - `CreateAsync(request)` — запись в PostgreSQL, рассылка карточки через порты `IHitlCardSender` (Telegram-порт, Avalonia-порт; оба вызываются, отказ одного не отменяет запрос — фиксируется в журнале запроса).
  - `ApproveAsync(approvalId, ownerId)`, `EditAsync(approvalId, ownerId, editedPayloadJson)`, `RejectAsync(approvalId, ownerId, reason?)` — каждое: проверка Owner (порт), проверка статуса (решение по уже решённому/истёкшему запросу → ошибка `Hitl.AlreadyResolved` / `Hitl.Expired`, состояние не меняется), запись решения, вызов возобновления.
  - `ExpireOverdueAsync(nowUtc)` — просроченные `Pending` → `Expired`; вызываемая по расписанию операция (регистрация в Quartz — TASK-045, здесь только метод).
- Исходы и продолжение (контракт с TASK-044): `Approved` → `ResumeAsync` с исходным payload; `Edited` → payload заменяется отредактированным, правка валидируется по `schema.output.json` пакета (порт валидатора TASK-042/043; невалидная правка → `Hitl.InvalidEditedPayload`, запрос остаётся `Pending`); `Rejected` → запуск завершается `Cancelled` с причиной `HitlRejected`, Delivery не вызывается; `Expired` → запуск `Cancelled` с причиной `HitlTimeout` (безопасное поведение по умолчанию; иное — только настройкой цепочки `OnHitlTimeout=DeliverAsIs` — нет, такой настройки не вводить: таймаут всегда отмена).
- Аудит: кто/когда/какое решение/канал решения (`Telegram|Avalonia`), хранится с запросом; повторное решение из второго канала после первого — `AlreadyResolved`.

## Вне рамок

- Чекпоинт и возобновление исполнителя (TASK-044; здесь только вызовы его портов), реальные кнопки Telegram/Avalonia и их UI (TASK-060/061), аутентификация Owner (TASK-073), подтверждение регистрации новых пакетов скриптов (другая карточка, вне цепочек).

## Зависимости

- TASK-002
- TASK-011
- TASK-044

## Контракты и интерфейсы

- `IHitlApprovalGate`, `HitlApprovalRequest`, `HitlDecision` (`Approved|Edited|Rejected|Expired`, `EditedPayloadJson?`) — TASK-002/044, реализация здесь.
- `IHitlApprovalService`, `IHitlCardSender` (`Channel: Telegram|Avalonia`, порт), `IOwnerGuard` (порт TASK-073, фейк в тестах).
- `IHitlApprovalRepository` (реализация TASK-011): `CreateAsync`, `GetAsync`, `TryResolveAsync(approvalId, decision)` (атомарно, только из `Pending`), `ListPendingExpiredAsync(nowUtc)`. Таблица (предложение для TASK-011): `chain_hitl_approvals`.
- Предлагаемое размещение (to-be-created): `src/RouterAndSkills/CyberPipe.Skills.Netrunner/Hitl/`.

## Результаты

- Сервис подтверждений, реализация гейта, фейки каналов/Owner/исполнителя, сценарии четырёх исходов.

## Критерии приёмки

- [x] Approve: фейк-исполнитель получил `ResumeAsync` один раз, Delivery в сценарии TASK-044 достижима; решение, канал и время записаны.
- [x] Edit валидным payload: возобновление с отредактированным payload; Edit невалидным по схеме пакета: `InvalidEditedPayload`, запрос остался `Pending`, возобновления нет.
- [x] Reject: возобновления на Delivery нет, запуск завершён как отменённый по `HitlRejected` (через фейк-порт исполнителя), причина сохранена.
- [x] Истечение срока: `ExpireOverdueAsync` переводит запрос в `Expired`, запуск отменён по `HitlTimeout`; позднее нажатие Approve → `Hitl.Expired`, состояние не меняется.
- [x] Не-Owner (фейк `IOwnerGuard` отклоняет): любое решение → ошибка доступа, запрос остаётся `Pending`; второе решение после первого (другой канал) → `AlreadyResolved`.
- [x] «Перезапуск»: новый экземпляр сервиса на том же фейк/реальном репозитории видит `Pending`-запрос и принимает по нему решение.

## Тестирование

- Уровень: component + integration (репозиторий).
- Как: фейки `IHitlCardSender` ×2, `IOwnerGuard`, `IChainExecutor`; in-memory репозиторий для component-тестов; интеграционный тест атомарного `TryResolveAsync` на реальной PostgreSQL (TASK-010/011) — два параллельных решения, ровно одно успешно. Без реальных Telegram/Avalonia.

## Команды проверки

```powershell
dotnet build CyberPipe.slnx
dotnet test tests/CyberPipe.Tests.Unit/ --filter "FullyQualifiedName~HitlApproval"
dotnet test tests/CyberPipe.Data.Tests/ --filter "FullyQualifiedName~HitlApprovalRepository"
```

## Статус: Done
