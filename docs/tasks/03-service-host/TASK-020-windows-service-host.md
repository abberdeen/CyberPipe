# TASK-020 — Windows Service Host

## Цель

Поднять Service как отдельную службу Windows: ставится в `Program Files`, стартует без UI и без залогиненного пользователя, переживает перезапуск Desktop и отвечает на проверку живости.

## Контекст

- Решение Q24 (`docs/plans/DECISIONS.md`) и ADR-006 (`docs/plans/initial_arch/17-adr-mvp.md`): Service и Desktop — раздельные процессы. Service владеет фоновой работой, Desktop только управляет.
- Код отсутствует, `CyberPipe.slnx` пуст. Проекты помечены «к созданию».
- Quartz.NET (TASK-045), цепочки (TASK-044) и каналы (TASK-060 и далее) — потребители хоста, а не часть этой задачи. Здесь только процесс, жизненный цикл и точка входа композиции (полная сборка DI — TASK-090).
- Только Owner, роли Guest нет.

## Объём работ

- Проект `src/Host/CyberPipe.Service/CyberPipe.Service.csproj` (к созданию): Worker Service на .NET 10 с `Microsoft.Extensions.Hosting.WindowsServices`, `UseWindowsService()`, имя службы `CyberPipe`.
- Установщик: публикация в `Program Files\CyberPipe\Service`, регистрация службы (скрипт PowerShell `install-service.ps1` / `uninstall-service.ps1`, к созданию), старт `Automatic (Delayed Start)`, аккаунт службы — `LocalService` (минимальные права; повышение только с обоснованием в коде установки).
- Жизненный цикл: `StartAsync`/`StopAsync` с тайм-аутом остановки из конфигурации, корректный `StopHost` при остановке службы, запись причины остановки в лог.
- Внутренний управляющий канал для Desktop (контракт TASK-002): Named Pipe `\\.\pipe\CyberPipe.Service.Control.v1`, только локально, ACL — текущий пользователь-владелец и SYSTEM. Команды канала реализует TASK-021; здесь — хостинг канала, аутентификация владельца (TASK-073 подключается позже, здесь проверка SID владельца как заглушка интерфейса) и health-endpoint: `Ping -> ServiceStatusDto`.
- Конфигурация и логи — через TASK-003 и TASK-004, без своих механизмов.
- Консольный режим `--console` для отладки и ручных тестов на Windows (тот же хост без регистрации службы).
- Health-check: процесс жив, канал отвечает, версия сборки в ответе.

## Вне рамок

- UI и кнопки управления (TASK-021, TASK-061), Brainstem (TASK-022), песочница (TASK-023).
- Планировщик, цепочки, роутер, каналы связи, модели.
- Автообновление службы, установка PostgreSQL (TASK-010).
- Кроссплатформенные демоны (systemd/launchd): Avalonia для всех ОС, но служба в этой задаче — только Windows.

## Зависимости

- TASK-001
- TASK-002
- TASK-003
- TASK-004

## Контракты и интерфейсы

Используются контракты TASK-002, новые публичные контракты задача не вводит, кроме внутреннего хостинга:

```csharp
// CyberPipe.Contracts (TASK-002), потребление без изменения
public interface IServiceControlChannel
{
    Task<ServiceStatusDto> PingAsync(CancellationToken ct);
    Task RequestStopAsync(CancellationToken ct);
    Task RequestRestartAsync(CancellationToken ct);
}

public sealed record ServiceStatusDto(
    string State,          // Running | Stopping | Stopped | Faulted
    string Version,
    DateTimeOffset StartedAtUtc,
    string? LastError);
```

- Транспорт канала — деталь реализации за `IServiceControlChannel`, фиксированный брокер не вводится.
- Событие смены состояния публикуется через абстракцию сообщений TASK-002: `ServiceStateChanged(State, AtUtc)`.

## Результаты

- `src/Host/CyberPipe.Service/` (к созданию): служба, `--console` режим, скрипты установки/удаления.
- Служба ставится в `Program Files`, стартует автоматически, отвечает на `Ping`.
- Инструкция ручной проверки на Windows в `README` проекта (к созданию).

## Критерии приёмки

- После установки служба видна в `services.msc` как `CyberPipe`, статус Running, стартует после перезагрузки без входа пользователя.
- `Ping` через Named Pipe возвращает `Running` и версию; при остановке службы `Ping` недоступен, а не зависает.
- Остановка службы завершает хост за время из конфигурации и пишет причину в лог (TASK-004).
- Удаление скриптом убирает службу и не трогает данные PostgreSQL и пользовательские файлы.
- Desktop не нужен для работы службы: закрытие Desktop службу не останавливает.

## Тестирование

- Уровень: unit + component + ручной интеграционный на Windows.
- Unit: маппинг состояния хоста в `ServiceStatusDto`, тайм-аут остановки (фейковый хост).
- Component: хост в `--console` режиме с фейковым `IServiceControlChannel`-клиентом: Ping, RequestStop, повторный Ping после остановки.
- Integration (вручную, Windows, помечается `Category=ManualWindows`): установка, перезагрузка или `Restart-Service`, удаление. В CI не запускается.
- Моки: канал и конфигурация — фейки из контрактов TASK-002/TASK-003.

## Команды проверки

```powershell
dotnet build src/Host/CyberPipe.Service/CyberPipe.Service.csproj -c Release
dotnet test CyberPipe.slnx --filter "FullyQualifiedName~ServiceHost"
# Вручную на Windows (от администратора):
# .\install-service.ps1; Get-Service CyberPipe; .\uninstall-service.ps1
```

Пути проектов — к созданию; команды заработают после TASK-001.

## Статус: Done
