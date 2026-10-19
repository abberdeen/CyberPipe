# CyberPipe.ServiceControl — Клиент управления службой для Desktop и CLI

## Назначение

Библиотека управления и наблюдения за службой CyberPipe (TASK-021). Обеспечивает безопасную поверхность управления для Avalonia Desktop (TASK-061) и CLI (TASK-062):

- Запуск (`StartAsync`), остановка (`StopAsync`), перезапуск (`RestartAsync`) и опрос статуса (`GetStatusAsync`).
- Потоковое наблюдение за жизненным циклом (`WatchStatusAsync`) с адаптивным бэкоффом без busy-loop.
- Архитектурная изоляция: Desktop не имеет прямого доступа к базе данных PostgreSQL и секретам; взаимодействие происходит исключительно через Named Pipe (TASK-020) и Service Control Manager (SCM).
- UI-независимое ядро состояния: `ServiceControlViewModel` (реализует `INotifyPropertyChanged`).

Архитектурный контекст: решения Q24 (`docs/plans/DECISIONS.md`) и ADR-006 (`docs/plans/initial_arch/17-adr-mvp.md`).

---

## Таблица исходов команд (`ServiceControlOutcome`)

| Исход | Значение | Описание и реакция UI |
|---|---|---|
| `Ok` | 0 | Команда выполнена успешно. Служба перешла в целевое состояние. |
| `ServiceNotInstalled` | 1 | Служба не зарегистрирована в Windows SCM. UI отображает предложение запустить `scripts/install-service.ps1` от имени Администратора. |
| `AccessDenied` | 2 | Недостаточно прав для управления службой через SCM (Win32 error 5). UI предлагает повысить привилегии через UAC только на момент действия. |
| `Timeout` | 3 | Истёк тайм-аут ожидания ответа канала или перехода состояния в SCM. UI выводит предупреждение без зависания интерфейса. |
| `ChannelUnavailable` | 4 | Канал Named Pipe недоступен (служба остановлена или завершилась сбоем). Контроллер автоматически запрашивает статус через SCM. |
| `Faulted` | 5 | Операция завершилась внутренней ошибкой (например, сбой стадии остановки при перезапуске). Сообщение об ошибке выводится в `LastError`. |

---

## Порядок выполнения операций

### 1. Запуск (`StartAsync`)
1. Проверка текущего статуса через Named Pipe ping. Если служба уже `Running`, возвращается `Ok`.
2. Если канал недоступен, проверка наличия службы в SCM (`IScmManager.IsServiceInstalled`).
3. Если служба отсутствует — возвращается `ServiceNotInstalled`.
4. Старт службы через Windows SCM (`IScmManager.StartServiceAsync`). При отказе в доступе — возврат `AccessDenied`.
5. Ожидание ответа health-check по Named Pipe с ограничением по времени (15 секунд).

### 2. Остановка (`StopAsync`)
1. Попытка корректной остановки через Named Pipe `RequestStopAsync`.
2. Ожидание завершения службы и перехода в статус `Stopped` в SCM.
3. Если канал недоступен — резервная остановка через SCM.

### 3. Перезапуск (`RestartAsync`)
1. Вызов `StopAsync()`. Если остановка завершилась ошибкой, возвращается `Faulted` с деталями фазы остановки.
2. Вызов `StartAsync()`. Если запуск завершился ошибкой, возвращается `Faulted` с деталями фазы запуска.
3. При успехе обеих фаз возвращается `Ok`.

---

## Использование в Desktop UI

```csharp
var client = new ServiceControlPipeClient();
var scm = new WindowsScmManager();
var controller = new ServiceControllerManager(client, scm, options);
var viewModel = new ServiceControlViewModel(controller);

// Привязка в Avalonia XAML:
// <Button Command="{Binding StartServiceAsync}" IsEnabled="{Binding CanStart}" Content="Запустить" />
// <Button Command="{Binding StopServiceAsync}" IsEnabled="{Binding CanStop}" Content="Остановить" />
// <TextBlock Text="{Binding State}" />
// <TextBlock Text="{Binding ActionPrompt}" />
```

---

## Автоматизированные тесты

```powershell
dotnet test CyberPipe.slnx --filter "FullyQualifiedName~ServiceControl"
```
