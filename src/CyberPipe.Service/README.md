# CyberPipe.Service — Windows Service Host

## Назначение

Хост фоновой службы CyberPipe (TASK-020). Отвечает за постоянную работу ассистента на машине пользователя без привязки к сессии UI:

- Устанавливается как служба Windows (по умолчанию в `Program Files\CyberPipe\Service`).
- Аккаунт службы по умолчанию: `NT AUTHORITY\LocalService` (минимальные привилегии).
- Запуск: `Automatic (Delayed Start)`.
- Не имеет графического интерфейса: всё взаимодействие с пользователем происходит через Desktop-клиент (TASK-021, TASK-061) или Telegram (TASK-060).
- Предоставляет внутренний локальный канал управления Named Pipe `\\.\pipe\CyberPipe.Service.Control.v1` с ограниченными правами (ACL: текущий владелец, SYSTEM, LocalService).

Архитектурный контекст: решения Q24 (`docs/plans/DECISIONS.md`) и ADR-006 (`docs/plans/initial_arch/17-adr-mvp.md`).

---

## Архитектура хоста

```text
[ Desktop UI / CLI ]
        │
        │ Named Pipe: \\.\pipe\CyberPipe.Service.Control.v1 (JSON-lines)
        ▼
[ CyberPipe.Service ]
   ├─ ServiceStatusTracker       (состояние: Running, Stopping, Stopped, Faulted)
   ├─ ServiceControlPipeServer   (обработка Ping, Stop, Restart)
   ├─ CyberPipeWorker            (фоновый жизненный цикл BackgroundService)
   └─ Serilog / Diagnostics      (структурированное логирование с маскировкой секретов)
```

### Жизненный цикл

1. **Старт (`StartAsync`)**:
   - `ServiceStatusTracker` переводится в `Running`.
   - Запускается `ServiceControlPipeServer` на канале `HostingOptions.PipeName`.
   - Регистрируется `StartedAtUtc` и текущая версия сборки.

2. **Запрос остановки (`StopAsync` / `RequestStop`)**:
   - `ServiceStatusTracker` переводится в `Stopping`.
   - Канал Named Pipe корректно закрывается без утечек хэндлов.
   - Ожидание завершения фоновых воркеров до `HostingOptions.ShutdownTimeoutSeconds` (по умолчанию 30 секунд).
   - `ServiceStatusTracker` переводится в `Stopped`.

3. **Сбой (`MarkFaulted`)**:
   - При неперехваченном исключении статус переводится в `Faulted` с сохранением `LastError`.

---

## Режим разработки (`--console`)

Для локальной разработки, отладки и тестирования без регистрации службы Windows:

```powershell
dotnet run --project src/CyberPipe.Service/CyberPipe.Service.csproj -- --console
```

В консольном режиме служба выводит цветные логи прямо в терминал и отвечает на команды через Named Pipe. Завершение работы: `Ctrl+C`.

## Диагностика конфигурации (`--validate-config`, TASK-090)

```powershell
dotnet run --project src/CyberPipe.Service/CyberPipe.Service.csproj -- --validate-config
```

Проверяет связность DI-графа композиции, наличие каталогов моделей 15+8 и отсутствие запрещённых сервисов. Каналы не запускаются, запись в БД не выполняется. Код выхода 0 — конфигурация валидна, 1 — найдены ошибки (перечислены в выводе).

---

## Установка и удаление службы Windows

Скрипты расположены в папке `scripts/`:

### Установка (от имени Администратора):

```powershell
# Стандартная установка в Program Files и запуск службы CyberPipe
powershell -ExecutionPolicy Bypass -File .\scripts\install-service.ps1
```

Параметры скрипта установки:
- `-ServiceName`: Имя службы (по умолчанию `CyberPipe`).
- `-InstallPath`: Каталог публикации (по умолчанию `C:\Program Files\CyberPipe\Service`).
- `-ServiceAccount`: Аккаунт Windows (по умолчанию `NT AUTHORITY\LocalService`).
- `-DelayedAuto`: Отложенный автостарт службы (по умолчанию `$true`).

### Удаление (от имени Администратора):

```powershell
# Остановка и удаление регистрации службы
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall-service.ps1
```

> **Безопасность данных**: удаление службы никогда не удаляет базу данных PostgreSQL и пользовательские настройки в `%APPDATA%\CyberPipe`.

---

## Проверка работоспособности на Windows (Manual Testing)

1. **Проверка статуса через PowerShell**:
   ```powershell
   Get-Service CyberPipe
   ```

2. **Проверка через диспетчер служб**:
   - Откройте `services.msc`.
   - Найдите службу `CyberPipe Background Service`.
   - Убедитесь в типе запуска `Automatic (Delayed Start)` и статусе `Running`.

3. **Перезапуск службы**:
   ```powershell
   Restart-Service CyberPipe
   ```

4. **Проверка логов**:
   Логи пишутся в `%APPDATA%\CyberPipe\logs\Service_*.log`.

---

## Автоматизированные тесты

```powershell
# Запуск тестов хоста службы и канала управления
dotnet test CyberPipe.slnx --filter "FullyQualifiedName~ServiceHost"

# Полный прогон тестов
dotnet test CyberPipe.slnx
```
