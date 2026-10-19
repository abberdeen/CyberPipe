# TASK-021 — Управление Service из Desktop (Start/Stop/Restart)

## Цель

Дать Desktop безопасную поверхность управления службой CyberPipe: Start, Stop, Restart и честный статус, без прямого доступа Desktop к данным, конфигурации секретов и внутренностям Service.

## Контекст

- Решение Q24 и ADR-006: Desktop управляет Service, Service работает независимо. Закрытие Desktop службу не останавливает.
- Управляющий канал и его хостинг даёт TASK-020 (Named Pipe, локально, ACL владельца). Эта задача — клиент канала, командная логика и модель состояния для UI. Сами экраны Avalonia — TASK-061, здесь только презентационная логика без привязки к окнам.
- Только Owner: управление доступно владельцу машины, проверки владельца — TASK-073 (здесь интерфейс-заглушка, fail-closed при недоступности проверки).

## Объём работ

- Проект `src/Host/CyberPipe.ServiceControl/CyberPipe.ServiceControl.csproj` (к созданию): клиент `IServiceControlChannel` (TASK-002) поверх Named Pipe из TASK-020.
- Команды: `StartAsync`, `StopAsync`, `RestartAsync`, `GetStatusAsync` с тайм-аутами, отменой и различением исходов: `Ok`, `ServiceNotInstalled`, `AccessDenied`, `Timeout`, `ChannelUnavailable`, `Faulted`.
- Start/Stop на уровне ОС: если служба остановлена и канал недоступен, Start идёт через Windows Service Control Manager (требует повышения прав — запрос UAC только в момент действия, без постоянного админа у Desktop). Если служба не установлена — отдельное состояние и подсказка запустить установщик, а не «ошибка».
- Наблюдение статуса: подписка на `ServiceStateChanged` (TASK-020) + fallback-опрос с бэкоффом при разрыве канала, без busy-loop.
- Модель для UI (без Avalonia-типов): `ServiceControlViewModel`-ядро — текущее состояние, доступные действия, последняя ошибка, признак «идёт операция». Привязка к Avalonia — TASK-061.
- Все действия пишутся в лог (TASK-004) без секретов.

## Вне рамок

- Экраны, стили и навигация Avalonia (TASK-061), мастер первого запуска (TASK-063).
- Установка/удаление службы (TASK-020), Brainstem (TASK-022), песочница (TASK-023).
- Управление PostgreSQL, моделями, цепочками и планировщиком.
- Удалённое управление с другой машины: канал только локальный.

## Зависимости

- TASK-002
- TASK-020

## Контракты и интерфейсы

```csharp
// CyberPipe.Contracts (TASK-002), потребление; реализация клиента — эта задача
public enum ServiceControlOutcome
{
    Ok, ServiceNotInstalled, AccessDenied, Timeout, ChannelUnavailable, Faulted
}

public sealed record ServiceControlResult(ServiceControlOutcome Outcome, string? Error);

public interface IServiceController
{
    Task<ServiceStatusDto> GetStatusAsync(CancellationToken ct);
    Task<ServiceControlResult> StartAsync(CancellationToken ct);
    Task<ServiceControlResult> StopAsync(CancellationToken ct);
    Task<ServiceControlResult> RestartAsync(CancellationToken ct);
    IAsyncEnumerable<ServiceStatusDto> WatchStatusAsync(CancellationToken ct);
}
```

- `RestartAsync` = Stop с ожиданием `Stopped`, затем Start с ожиданием `Running`; частичный успех честно возвращается как `Faulted` с описанием стадии.
- Desktop никогда не открывает файлы данных и БД напрямую: единственный путь — канал TASK-020.

## Результаты

- `src/Host/CyberPipe.ServiceControl/` (к созданию): клиент канала, SCM-старт, наблюдение статуса, UI-независимое ядро состояния.
- Таблица исходов команд задокументирована в `README` проекта (к созданию).

## Критерии приёмки

- Из Desktop (через ядро состояния): остановленная служба запускается, запущенная останавливается и перезапускается; статус совпадает с `Get-Service CyberPipe`.
- Служба не установлена: UI-ядро показывает `ServiceNotInstalled`, а не исключение и не вечный спиннер.
- Нет прав: `AccessDenied`, предложение повысить права только на это действие.
- Канал упал посреди операции: `ChannelUnavailable`/`Timeout` за конечное время, Desktop не зависает.
- Закрытие Desktop работающую службу не останавливает; повторный запуск Desktop подхватывает актуальный статус без опроса в тупике.

## Тестирование

- Уровень: unit + component.
- Unit: маппинг ответов канала и SCM в `ServiceControlOutcome`, логика Restart при сбое Stop, бэкофф наблюдения (фейковые часы/задержки).
- Component: фейковый `IServiceControlChannel` (in-memory): Start/Stop/Restart, разрыв канала, статус не установлена.
- Интеграция с реальной службой — ручная на Windows вместе с TASK-020 (`Category=ManualWindows`), здесь не дублируется.
- Моки: канал, SCM-адаптер и проверка владельца — фейки по контрактам TASK-002.

## Команды проверки

```powershell
dotnet build src/Host/CyberPipe.ServiceControl/CyberPipe.ServiceControl.csproj -c Release
dotnet test CyberPipe.slnx --filter "FullyQualifiedName~ServiceControl"
```

Пути проектов — к созданию; команды заработают после TASK-001 и TASK-020.

## Статус: Planned
