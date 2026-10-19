# 14. Устойчивость ядра `CyberPipe.exe` к сбоям, Контроль Буферов и Внутрипроцессная Изоляция (`Bulkhead`)

Так как **`CyberPipe.exe`** (`src/Host/CyberPipe`) объединяет фоновые воркеры, маршрутизатор, локальный сервер IPC, `Telegram`-шлюз и доступ к `PostgreSQL`, стандартные настройки .NET создают риски каскадного отказа:
1. Необработанное исключение в любом `BackgroundService` по поведению .NET по умолчанию (`StopHost`) останавливает весь процесс хоста.
2. Зависший или ресурсоемкий фоновый модуль может исчерпать пул потоков или общую очередь, заблокировав соседние подсистемы (*Noisy Neighbor*).
3. Неограниченное чтение внешних файлов, потоков или IPC-сообщений ведет к `OutOfMemoryException`.
4. Ошибки памяти в неуправляемом коде (`AccessViolationException`) или парсерах сложных внешних форматов не перехватываются стандартным `try/catch` внутри того же процесса.

Ниже описаны **4 внутрипроцессных контура устойчивости `CyberPipe.exe`**, а также точка стыковки с **5-м межпроцессным контуром (`CyberPipe.Brainstem.exe`)**, спецификация которого вынесена в [`15-cli-and-execution-modes.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md).

---

## Общая схема контуров устойчивости `CyberPipe.exe`

```mermaid
flowchart TD
    subgraph CyberPipeProcess ["Процесс хоста: CyberPipe.exe"]
        
        subgraph BootShield ["1. SafeBoot & DI Shield (Контроль старта)"]
            HostOpts["HostOptions:<br/>BackgroundServiceExceptionBehavior.Ignore<br/>StartupTimeout = 10s"]
            SafeDI["SafePluginLoader:<br/>Lazy-инициализация, запрет I/O в конструкторах,<br/>сбойный модуль ➔ статус DisabledFaulted"]
        end

        subgraph Bulkheads ["2. Изолированные Отсеки (Bulkhead Lanes)"]
            LaneFast["Отсек 0/1: Fast-Path & KillSwitch<br/>(Выделенный приоритетный канал)"]
            LaneGate["Отсек Б: Telegram & IPC Gateway<br/>(BoundedChannel: 500, MaxFrame: 4 MB)"]
            LaneSkills["Отсек 2: Фоновые Скиллы и Watchdogs<br/>(BoundedChannel: 100, Semaphore: Max 3)"]
            LaneStt["Отсек 3А: Stateful Voice STT Pool<br/>(Whisper.net в Brainstem, Semaphore: Max 1)"]
            LaneDoc["Отсек 3Б: SingleUse Document Parser Pool<br/>(PdfPig/OpenXml в Brainstem, Semaphore: Max 2)"]
        end

        subgraph Supervisor ["3. Надзиратель Воркеров (IsolatedWorkerBase & Circuit Breaker)"]
            IsoWorker["CyberPipe.Security.Telemetry.Resilience.IsolatedWorkerBase:<br/>• Перехват управляемых Exception + таймаут итерации<br/>• 3 сбоя подряд за 1 мин ➔ перевод воркера в Карантин<br/>• Соседние модули продолжают работу"]
        end

        subgraph MemoryGuard ["4. Квоты Буферов и Памяти (Bounded Streams)"]
            RingBuf["Только BoundedChannel (запрет Unbounded)<br/>+ ArrayPool<byte> + RecyclableMemoryStream"]
            StreamCap["QuotaBoundedStream:<br/>Прерывание чтения потока при превышении 16 МБ"]
        end
    end

    subgraph SubprocessJail ["5. Межпроцессная изоляция (См. 15-cli-and-execution-modes.md)"]
        JobObj["./worker/CyberPipe.Brainstem<br/>(IBrainstemIsolationPlatform: AppContainer / RestrictedSID / bwrap / Seatbelt + Named Pipe IPC)"]
    end

    BootShield --> Bulkheads
    Bulkheads --> Supervisor
    Supervisor --> MemoryGuard
    LaneStt -->|Голосовая транскрибация (Тёплый воркер)| JobObj
    LaneDoc -->|Недоверенный PDF/DOCX (SingleUseProcess)| JobObj
```

---

## Уровень 1. Контроль старта и регистрации DI (`SafeBoot & Zero-I/O Constructors`)

Чтобы ошибка конфигурации отдельного скилла или недоступность сети при запуске не блокировала старт `CyberPipe.exe`:

1. **Отключение остановки хоста при сбое фоновой службы (`BackgroundServiceExceptionBehavior.Ignore`):**
   ```csharp
   builder.Services.Configure<HostOptions>(options =>
   {
       options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
       options.StartupTimeout = TimeSpan.FromSeconds(10);
       options.ShutdownTimeout = TimeSpan.FromSeconds(5);
   });
   ```
2. **Запрет блокирующего ввода-вывода в конструкторах (`Zero-I/O in Constructors`):**
   * В конструкторах классов DI запрещено открывать файлы, сокеты, подключаться к БД или загружать модели.
   * Инициализация выполняется лениво (`Lazy<T>`) или в асинхронном методе `InitializeAsync(CancellationToken ct)` с таймаутом `5 секунд`.
3. **Безопасная регистрация модулей (`SafeModuleRegistration`):**
   * Каждый прикладной модуль инициализируется через `TryInitializeModuleAsync<TModule>()`. При ошибке конфигурации модуль переводится в состояние `ModuleState.DisabledFaulted` с записью в диагностический журнал (`CyberPipe triage`), а остальные подсистемы продолжают запуск.

---

## Уровень 2. Разделение ресурсов между соседями (`Bulkhead Isolation` + `IsolatedWorkerBase`)

Чтобы зависший фоновый опрос не блокировал обработку команд управления:

1. **Единый базовый класс `CyberPipe.Security.Telemetry.Resilience.IsolatedWorkerBase` вместо прямого наследования от `BackgroundService`:**
   * Базовый класс `IsolatedWorkerBase` размещен в проекте Уровня 1 `CyberPipe.Security.Telemetry` (который зависит только от `CyberPipe.Contracts`), поэтому доступен всем фоновым службам в `Core`, `Gateway` и `Skills` без циклических зависимостей и принудительно проверяется тестом `ArchUnitNET`.
   * Каждая итерация цикла воркера выполняется внутри блока перехвата управляемых исключений со связанным `CancellationTokenSource` и лимитом времени на итерацию (`30 сек`).
   * **Автоматический `Circuit Breaker` (Карантин):** После **3 последовательных ошибок за 1 минуту** `IsolatedWorkerBase` прекращает повторные запуски сбойного воркера, переводит его в состояние **`Quarantined`** и отправляет уведомление в Telegram/UI.
2. **Раздельные отсеки исполнения (`Bulkhead Lanes`):**
   * **Отсек 0 (`Priority Kill-Switch & Control`):** Выделенный канал для `/stop`, `/pause` и запросов статуса.
   * **Отсек 1 (`Fast-Path Tier 0/1`):** Пул для быстрых детерминированных команд с целевым таймаутом `500 мс`.
   * **Отсек 2 (`Background Watchdogs & Cron`):** Ограничен семафором `SemaphoreSlim(maxCount: 3)`.
   * **Отсек 3А (`Stateful Voice STT Pool` — `WhisperTranscribe` в `Brainstem`):** Выделенный семафор `SemaphoreSlim(maxCount: 1)` для долгоживущего «теплого» процесса распознавания речи (`IdleTimeout = 45 мин`, `MaxTasksPerProcess = 500`), чтобы парсинг тяжелых документов никогда не вытеснял голосовое управление и не вызывал холодный старт `Whisper.net`.
   * **Отсек 3Б (`SingleUse Untrusted Document Parser Pool` — `DocumentExtractText` в `Brainstem`):** Независимый семафор `SemaphoreSlim(maxCount: 2)` для одноразовых процессов парсинга внешних PDF/DOCX (`SingleUseProcess = true`, `MaxTasksPerProcess = 1`, лимит памяти `512 МБ`).

---

## Уровень 3. Квотирование буферов и потоков (`Memory & Stream Quotas`)

1. **Использование только ограниченных очередей (`Channel.CreateBounded<T>`):**
   * Вызовы `Channel.CreateUnbounded<T>()` запрещены правилом `ArchUnitNET` (`No_Unbounded_Channels_Allowed`).
   * Все каналы создаются с явно заданной емкостью (`BoundedChannelOptions`) и политикой `DropOldest` (для телеметрии) или `Wait` с таймаутом (для команд).
2. **Ограничения кадра IPC и потокового чтения (`QuotaBoundedStream`):**
   * Размер сообщений gRPC ограничен `4 МБ`.
   * Чтение внешних файлов и потоков выполняется через обертку `CyberPipe.Security.Telemetry.Resilience.QuotaBoundedStream(maxBytes: 16 * 1024 * 1024)` с использованием пулов буферов `ArrayPool<byte>.Shared`. При превышении лимита чтение прерывается с `QuotaExceededException`.
3. **Сброс кэша при росте потребления памяти:**
   * При превышении порогового рабочего набора процесса `ISystemResourceMonitor` инициирует `MemoryCache.Compact(1.0)` и отправляет сигнал выгрузки прогретого STT-воркера при критическом дефиците RAM хоста.

---

## Уровень 4. Защита от «Отравленных сообщений» (`Poison-Pill Flight Recorder`)

Для предотвращения циклов падения и перезапуска (`Crash-Loop`) на одном и том же проблемном входе без деградации задержки быстрых команд и без ложных блокировок при нажатии `/stop`:
1. **Разделение контуров учета (`In-Memory Fast-Path` vs `PostgreSQL InFlightOperations`):**
   * **Для быстрых команд `Tier 0 / Tier 1`:** чтобы не добавлять `2x fsync` (`INSERT + COMMIT` и `DELETE + COMMIT`) на горячем пути `< 5 мс`, отслеживание выполняется в оперативной памяти через `ConcurrentDictionary<string, InFlightEntry>` со скользящим окном TTL `5 минут`.
   * **Для тяжелых и внешних задач (файлы, вызовы `CyberPipe.Brainstem`, `Tier 2 / Tier 3`):** перед запуском в таблицу `InFlightOperations` (`PostgreSQL`) синхронно записывается `ContentHash`, `HandlerName`, `State = Running` и `StartedAtUtc`.
2. **Защита от ложного карантина (`Self-DoS Prevention`) и критерии `Poisoned`:**
   * В блоке `finally` при штатном завершении, при отмене пользователем (`OperationCanceledException` от `/stop`) или при обычной управляемой ошибке валидации/сети (`HttpRequestException`, `TimeoutException`) запись со статусом `Running` **гарантированно удаляется** из `InFlightOperations` без инкремента счетчика сбоев — поэтому отмена задачи через `/stop` или временный сбой сети никогда не блокируют повторную отправку того же текста.
   * Счетчик фатальных падений `AttemptCount` инкрементируется **только в двух случаях**:
     1. При старте демона (или супервизора `Brainstem`) обнаружена «осиротевшая» запись в статусе `State = Running`, оставшаяся после внезапного убийства процесса (`ProcessCrash` / `AccessViolationException` / `OutOfMemory`).
     2. Обработчик явно выбросил `CorruptedPayloadException` на битом заголовке контейнера.
   * Если для данного `ContentHash` накопилось `AttemptCount >= 2`, входное сообщение переводится в статус `Poisoned`, не передается в обработчик и помещается в карантин (`CyberPipe triage`).

---

## Уровень 5. Межпроцессная изоляция в `CyberPipe.Brainstem`

Задачи с риском повреждения нативной памяти (`AccessViolationException`), непредсказуемым потреблением RAM/CPU (`Whisper.net`) или разбором сложных недоверенных файлов (`PdfPig` / `OpenXml`) выносятся из процесса `CyberPipe` в отдельный изолированный процесс **`worker/CyberPipe.Brainstem`**.

Полная техническая спецификация этого контура приведена в [**`15-cli-and-execution-modes.md`**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md):
* Кроссплатформенная изоляция `IBrainstemIsolationPlatform` (Windows `AppContainer` и `CreateRestrictedToken` с `SidsToRestrict` + `Deny ACE` в DACL `data/`, `Process SACL NO_READ_UP`; Linux `bwrap` + `cgroups v2`; macOS `Seatbelt`) — см. [раздел 3](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md#L50-L95).
* Аутентификация Named Pipe по `PID` и `SessionNonce` — см. [раздел 4](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md#L96-L120).
* Контракты протокола, таймауты и отмена задач — см. [раздел 5](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md#L121-L175).
* Разделение на два пула воркеров (`Stateful Voice STT Pool` и `SingleUse Document Parser Pool`), ограничения `Windows Job Object` / `cgroups v2` и обязательные интеграционные проверки изоляции в CI — см. [разделы 6–9](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md#L176-L255).


