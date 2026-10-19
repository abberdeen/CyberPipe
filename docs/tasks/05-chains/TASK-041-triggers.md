# TASK-041 — Триггеры: Webhook, Cron, function call, сообщение из чата

## Цель

Один проверяемый результат: четыре триггера цепочки, каждый приводит внешнее событие к единому `TriggerEvent` и передаёт его исполнителю; неизвестные и повторные события не запускают цепочку дважды.

## Контекст

- Канон: ADR-003 — известные триггеры только Webhook, Cron, function call, сообщение из чата.
- Cron сам по себе расписанием не управляет: расписанием владеет TASK-045 (Quartz.NET), здесь — только приём его события и регистрация cron-триггера цепочки.
- Webhook принимает Service-хост (TASK-020); здесь — обработчик маршрута и нормализация, хостинг не реализуется.
- Сообщения чата приходят через собственные интерфейсы сообщений TASK-002 (Telegram — первый канал, TASK-060; здесь фейк канала).

## Объём работ

- `TriggerEvent` (контракт TASK-002): `EventId`, `ChainId`, `Kind`, `OccurredAtUtc`, `PayloadJson`, `FileRefs`, `IdempotencyKey`, `Source` (webhook path / cron job id / function name / channel+chat id).
- Интерфейс `IChainTrigger` + `TriggerRegistry`: регистрация/отмена по `ChainDefinition.Trigger` (TASK-040), запуск только включённых цепочек.
- Webhook: обработчик `POST /chains/{chainId}/webhook` (контракт обработчика, не хост): проверка секрета по ссылке на секрет (значение не в конфиге цепочки), лимит размера тела (настройка, по умолчанию 1 МБ), не-JSON тело → `400`, неверный секрет → `401`, неизвестная/отключённая цепочка → `404`/`409`.
- Cron: `ICronTriggerRegistrar.RegisterCron(chainId, cronExpression)` — делегат в TASK-045; входящий вызов `FireCron(chainId, scheduledAtUtc)` от планировщика → `TriggerEvent`. Невалидное cron-выражение отклоняется при регистрации (формат проверяет TASK-045, здесь пробрасывается его ошибка).
- Function call: внутрипроцессный `ITriggerInvoker.InvokeFunctionCall(chainId, payloadJson)` для вызова цепочки из кода/CLI; синхронно возвращает `RunId`, не тело результата цепочки.
- Chat: подписка на нормализованные входящие сообщения (`IInboundMessageSource`, TASK-002): совпадение по фильтру цепочки (канал, команда/префикс, chat id) → `TriggerEvent`; чужие сообщения игнорируются без запуска.
- Дедупликация: повтор события с тем же `IdempotencyKey` в окне хранения (настройка, по умолчанию 24 ч, хранилище — порт с фейком в тестах) не создаёт второй запуск; webhook без ключа получает ключ из хэша тела+пути (фиксируется в коде и тесте).

## Вне рамок

- Реализация Quartz и cron-расписаний (TASK-045), исполнение цепочки (TASK-044), HTTP-хостинг и служба (TASK-020), реальный Telegram (TASK-060), аутентификация Owner на периметре (TASK-073; здесь только проверка секрета webhook).

## Зависимости

- TASK-002
- TASK-040

## Контракты и интерфейсы

- `TriggerEvent`, `IChainTrigger`, `ITriggerInvoker`, `ICronTriggerRegistrar` (TASK-002 / эта задача — реализация).
- `IChainExecutor.StartAsync(TriggerEvent) → RunId` — порт потребителя (реализация TASK-044; здесь фейк, фиксирующий вызовы).
- `IIdempotencyStore.TryRegister(key) → bool` (порт; PostgreSQL-реализация — через TASK-011 позже, в тестах фейк).
- Предлагаемое размещение (to-be-created): `src/RouterAndSkills/CyberPipe.Skills.Netrunner/Triggers/`.

## Результаты

- Реестр триггеров, 4 реализации триггеров, дедупликация, фикстуры событий каждого вида.

## Критерии приёмки

- [x] Каждое из 4 событий превращается в `TriggerEvent` с верными `Kind`, `ChainId`, `PayloadJson` и вызывает фейк-исполнитель ровно один раз.
- [x] Повтор webhook с тем же ключом идемпотентности второго запуска не создаёт; другое тело — создаёт.
- [x] Webhook: неверный секрет → 401, отключённая цепочка → 409, тело сверх лимита → 413 (или `400` — одно значение, зафиксированное тестом), исполнитель не вызван.
- [x] Chat-сообщение без совпадения с фильтром и function call на неизвестную цепочку запуска не создают (второй случай — явная ошибка `ChainNotFound`).
- [x] Отмена регистрации триггера останавливает дальнейшие запуски по всем 4 видам.

## Тестирование

- Уровень: unit + component.
- Как: фейки `IChainExecutor`, `IInboundMessageSource`, `IIdempotencyStore`, `ICronTriggerRegistrar`; webhook-обработчик тестируется напрямую вызовами без реального HTTP-сервера и без PostgreSQL/Quartz.

## Команды проверки

```powershell
dotnet build CyberPipe.slnx
dotnet test tests/CyberPipe.Tests.Unit/ --filter "FullyQualifiedName~ChainTrigger"
```

## Статус: Done

