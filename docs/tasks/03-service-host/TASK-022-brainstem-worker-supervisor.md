# TASK-022 — Супервизор Brainstem-воркера

## Цель

Запускать тяжёлую и рискованную работу в отдельном изолированном процессе Brainstem: супервизор в Service стартует воркер, следит за живостью, перезапускает при падении с лимитом попыток и гарантированно гасит его при остановке Service.

## Контекст

- Brainstem — изолированный воркер вне процесса Service: падение, зависание или утечка памяти воркера не должны ронять Service (см. `docs/plans/initial_arch/14-fault-isolation-and-resilience.md`, `15-cli-and-execution-modes.md`; решения DECISIONS/ADR имеют приоритет при расхождениях).
- Запуск воркера — только через песочницу TASK-023 (Restricted Token + Job Object + Deny-ACL, настраиваемый лимит памяти). Прямой `Process.Start` воркера в этой задаче запрещён.
- Потребители воркера (Whisper TASK-052, парсинг документов, скрипты TASK-043) появятся позже: здесь — жизненный цикл и транспорт заданий по контрактам TASK-002, без самих нагрузок.
- Messaging — абстракции TASK-002, фиксированного брокера нет.

## Объём работ

- Проект воркера `src/Execution/CyberPipe.Brainstem/CyberPipe.Brainstem.csproj` (к созданию): консольный процесс, точка входа принимает `--pipe <name>` и `--job <id>`, поднимает канал к супервизору, шлёт heartbeat, выполняет `Ping`-задание, завершается по команде или при разрыве канала.
- Проект супервизора `src/Execution/CyberPipe.Brainstem.Supervisor/` (к созданию) внутри Service (TASK-020): `IBrainstemSupervisor` — старт/стоп воркера через `ISandboxLauncher` (TASK-023), один воркер на роль (роль — строка из конфигурации, напр. `default`), без пула сверх конфигурации.
- Heartbeat: интервал и тайм-аут из конфигурации (TASK-003); пропуск heartbeat дольше тайм-аута = воркер считается зависшим: terminate через Job Object, перезапуск по политике ниже.
- Политика перезапуска: экспоненциальный бэкофф, максимум попыток из конфигурации; при исчерпании — состояние `Faulted`, событие владельцу через абстракцию сообщений TASK-002, без бесконечного цикла рестартов.
- Задания: минимальная диспетчеризация по контракту TASK-002 (`BrainstemJobRequest/Result`: id, вид задания, payload-байты, тайм-аут): принять, дождаться результата или тайм-аута, вернуть ошибку воркера как данные. Реестр реальных обработчиков нагрузок — не здесь.
- Грациозная остановка: при `StopAsync` Service супервизор шлёт воркеру завершение, ждёт до тайм-аута, затем Job Object terminate. Сирот после остановки Service не остаётся.

## Вне рамок

- Сама песочница и её примитивы (TASK-023) — здесь только потребление.
- Нагрузки воркера: Whisper (TASK-052), парсеры документов, раннеры скриптов (TASK-043).
- Планирование заданий, очереди цепочек, ретраи бизнес-логики (TASK-044, TASK-045).
- Модели и их установка (TASK-081): воркер не качает и не настраивает модели.

## Зависимости

- TASK-002
- TASK-020
- TASK-023

## Контракты и интерфейсы

```csharp
// CyberPipe.Contracts (TASK-002), потребление без изменения
public sealed record BrainstemJobRequest(string JobId, string Kind, byte[] Payload, int TimeoutMs);
public sealed record BrainstemJobResult(string JobId, bool Ok, byte[]? Payload, string? Error);

public interface IBrainstemWorker
{
    Task<BrainstemJobResult> ExecuteAsync(BrainstemJobRequest request, CancellationToken ct);
}

// Реализация супервизора — эта задача (внутри Service)
public interface IBrainstemSupervisor : IBrainstemWorker
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    string State { get; } // Stopped | Starting | Running | Restarting | Faulted
}
```

- Канал супервизор↔воркер: локальный (Named Pipe), транспорт скрыт за контрактами TASK-002.
- События через абстракцию сообщений TASK-002: `BrainstemStateChanged(State, Role, AtUtc)`, `BrainstemRestartExhausted(Role, Attempts)`.

## Результаты

- `src/Execution/CyberPipe.Brainstem/` (к созданию): процесс-воркер с heartbeat и `Ping`-заданием.
- `src/Execution/CyberPipe.Brainstem.Supervisor/` (к созданию): супервизор в Service, политика перезапуска из конфигурации.
- Задание в воркер и обратно проходит сквозь песочницу TASK-023.

## Критерии приёмки

- Service стартовал — воркер запущен через песочницу, `Ping`-задание возвращает результат.
- Воркер убит извне (kill по PID): супервизор видит потерю heartbeat, перезапускает воркера, задания снова проходят.
- Воркер зависает (не отвечает на heartbeat): terminate по тайм-ауту и перезапуск, Service при этом не перезапускается.
- Воркер падает в цикле: после лимита попыток — `Faulted` и событие владельцу, цикла рестартов нет, Service жив.
- Остановка Service гасит воркер: дочерних процессов Brainstem после остановки нет.
- Лимит памяти воркера из конфигурации реально применён (воркер, превысивший лимит, завершается Job Object, супервизор это видит как падение).

## Тестирование

- Уровень: unit + component + integration (Windows).
- Unit: политика перезапуска (бэкофф, лимит, сброс счётчика после стабильной работы) на фейковых часах; маппинг тайм-аута задания в ошибку.
- Component: фейковый воркер (in-process двойник канала): задания, heartbeat-потеря, Faulted после лимита.
- Integration (`Category=ManualWindows` для ручных, остальное — в CI на Windows): реальный процесс воркера через песочницу: kill, зависание тестовым заданием `Hang`, превышение лимита памяти тестовым заданием `Allocate`.
- Моки: `ISandboxLauncher` — фейк для unit/component; реальная песочница — только integration.

## Команды проверки

```powershell
dotnet build src/Execution/CyberPipe.Brainstem/CyberPipe.Brainstem.csproj -c Release
dotnet test CyberPipe.slnx --filter "FullyQualifiedName~Brainstem"
```

Пути проектов — к созданию; команды заработают после TASK-001, TASK-020 и TASK-023.

## Статус: Done
