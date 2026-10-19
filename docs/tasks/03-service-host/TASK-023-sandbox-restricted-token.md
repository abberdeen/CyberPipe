# TASK-023 — Sandbox: Restricted Token + Job Object + Deny-ACL

## Цель

Дать единый способ запускать недоверенные процессы (Brainstem, скрипты цепочек) с урезанными правами Windows: Restricted Token, Job Object с настраиваемым лимитом памяти и Deny-ACL на рабочие каталоги — так, чтобы сбой или вредоносный процесс не мог выйти за пределы своей папки и лимитов.

## Контекст

- Решение Q13 и ADR-008: механизм — Restricted Token + Job Object + Deny-ACL, не AppContainer (см. `docs/plans/initial_arch/10-sandbox-telemetry-and-heuristics.md`; при расхождениях приоритет у DECISIONS/ADR).
- Лимит памяти настраивается (Q9/ADR-004: `maxMemoryMb` может задавать пользователь, пустое значение — системное умолчание из TASK-003). Фиксированный clamp в коде запрещён.
- Потребители: Brainstem (TASK-022) и раннеры скриптов (TASK-043). Эта задача — только примитив запуска, без заданий, цепочек и нагрузок.
- Только Windows в этой задаче: ручной фокус тестирования — Windows. На прочих ОС контракт возвращает честную ошибку «не поддерживается», без тихой деградации в обычный запуск.

## Объём работ

- Проект `src/Execution/CyberPipe.Execution.Sandbox/CyberPipe.Execution.Sandbox.csproj` (к созданию): `ISandboxLauncher` по контракту TASK-002.
- Restricted Token: создание токена из текущего процесса, отключение привилегий, restricting SID (DenyOnly) на запрещённые группы, `CreateProcessAsUser`/`CreateRestrictedToken` через P/Invoke с RAII-обёртками и закрытием хэндлов при любом исходе.
- Job Object: `JOB_OBJECT_LIMIT_JOB_MEMORY`/`PROCESS_MEMORY` (лимит из запроса, байты), `KILL_ON_JOB_CLOSE`, лимит активных процессов = 1, CPU-лимит — только если задан в запросе; назначение процесса в Job до возобновления потока (`CREATE_SUSPENDED` → Assign → Resume).
- Deny-ACL: на рабочий каталог запуска — явный Deny для токена процесса на всё вне рабочего каталога обеспечивается набором: уникальный рабочий каталог запуска, ACL только для SID процесса и SYSTEM, запрет записи в каталог установки `Program Files`.
- Запрос запуска: исполняемый файл (абсолютный путь, проверка существования), аргументы списком (без конкатенации в командную строку), рабочий каталог, переменные окружения allowlist (без наследования секретов родителя), тайм-аут, лимит памяти МБ (null = умолчание конфигурации).
- Результат: exit code, признак `KilledByJobLimit`/`TimedOut`, захваченные stdout/stderr с ограничением размера (потолок из конфигурации, обрезка с пометкой).
- Ошибки как данные: `SandboxNotSupported`, `ExecutableNotFound`, `AccessDenied`, `LaunchFailed` — без исключений через границу контракта, кроме отмены.

## Вне рамок

- Brainstem и его супервизор (TASK-022), раннеры языков и компиляторы (TASK-043), валидатор манифестов (TASK-042).
- Эвристики безопасности контента и аудио (TASK-071), anti-wipe (TASK-070), egress-санитизация (TASK-072): песочница — примитив, а не политика.
- AppContainer, контейнеры, WSL, виртуализация; кроссплатформенные песочницы (bwrap/Seatbelt).
- Сетевые ограничения процесса сверх Deny-ACL файловой системы (файрвол — вне MVP этой задачи).

## Зависимости

- TASK-002

## Контракты и интерфейсы

```csharp
// CyberPipe.Contracts (TASK-002), потребление без изменения
public sealed record SandboxLaunchRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    int TimeoutMs,
    int? MaxMemoryMb);

public sealed record SandboxLaunchResult(
    int? ExitCode,
    bool TimedOut,
    bool KilledByJobLimit,
    string Stdout,
    string Stderr,
    string? Error);

public interface ISandboxLauncher
{
    Task<SandboxLaunchResult> RunAsync(SandboxLaunchRequest request, CancellationToken ct);
}
```

- `MaxMemoryMb = null` означает умолчание из конфигурации (TASK-003); вызывающий код (TASK-022, TASK-043) может переопределить per-launch.
- Никакой путь внутри реализации не запускает процесс без Job Object: fallback «обычный Process.Start» запрещён, при невозможности песочницы — `LaunchFailed`.

## Результаты

- `src/Execution/CyberPipe.Execution.Sandbox/` (к созданию): Restricted Token + Job Object + Deny-ACL лаунчер за `ISandboxLauncher`.
- Тестовое консольное приложение-нагрузка (к созданию, только тесты): печать в stdout, зависание, выделение памяти, попытка записи вне рабочего каталога.

## Критерии приёмки

- Обычный процесс в песочнице: stdout/stderr получены, exit code совпадает.
- Процесс, выделивший память сверх лимита, завершается Job Object: `KilledByJobLimit = true`, родитель (Service) жив.
- Процесс-зависший убивается по `TimeoutMs`: `TimedOut = true`, хэндлы и Job закрыты, зомби-процессов нет.
- Попытка записи вне рабочего каталога из песочницы запрещена (AccessDenied внутри процесса), запись внутрь — разрешена.
- Секреты родителя не видны: переменная окружения, не переданная в запросе, в процессе отсутствует.
- Не Windows или нет прав на Restricted Token: честный `LaunchFailed`/`SandboxNotSupported`, обычного запуска не происходит.
- Повторные запуски не утекают хэндлами (счётчик хэндлов процесса стабилен на серии запусков).

## Тестирование

- Уровень: unit + integration (Windows).
- Unit: валидация запроса (пути, аргументы, лимиты), маппинг исходов Win32-ошибок в `Error`, расчёт байтов лимита из `MaxMemoryMb` и умолчания.
- Integration (`Category=ManualWindows` для ручных сценариев, базовый набор — CI на Windows): реальная нагрузка-процесс: лимит памяти, тайм-аут, Deny-ACL запись наружу, изоляция окружения, серия запусков без утечки хэндлов.
- Моки: для потребителей песочница заменяется фейковым `ISandboxLauncher`; сама задача тестируется на реальном Win32.

## Команды проверки

```powershell
dotnet build src/Execution/CyberPipe.Execution.Sandbox/CyberPipe.Execution.Sandbox.csproj -c Release
dotnet test CyberPipe.slnx --filter "FullyQualifiedName~Sandbox"
```

Пути проектов — к созданию; команды заработают после TASK-001.

## Статус: Done
