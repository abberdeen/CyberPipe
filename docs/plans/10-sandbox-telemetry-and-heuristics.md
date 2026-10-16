# 10. Мониторинг ресурсов и эвристический анализ телеметрии песочниц

В данном документе описаны две критически важные подсистемы:
1. **Realtime Singleton мониторинга системных ресурсов** с авторизованным доступом.
2. **Безопасный перехват и эвристический анализ сетевой и файловой активности** изолированных сред (решение проблемы «1 ГБ логов»).

---

## 1. Realtime Singleton системных ресурсов (`ISystemResourceMonitor`)

Для предотвращения деградации хоста и DoS-атак в системе развертывается потокобезопасный синглтон-сервис, собирающий телеметрию оборудования и контейнеров в реальном времени.

### Архитектура доступа и авторизации

Сервис доступен только доверенным потребителям, причем каждый получает строго релевантный срез данных:

```mermaid
flowchart TD
    MonitorSingleton["Realtime Singleton: SystemResourceMonitor<br/>(Background Hardware / OS Poller)"]

    subgraph AuthorizedConsumers ["Авторизованные потребители"]
        CEO["CEO Orchestrator<br/>(Оценка свободных ресурсов для шедулинга задач)"]
        UI["Avalonia UI<br/>(Realtime графики CPU/RAM/Диска по gRPC стриму)"]
        Security["SecurityAgent<br/>(Детект аномалий: майнинг, DoS, скачки CPU/RAM)"]
    end

    MonitorSingleton -->|Срез доступности: CanSpawn(1.5GB RAM?)| CEO
    MonitorSingleton -->|Срез визуализации: 60 FPS метрики CPU/RAM/IO| UI
    MonitorSingleton -->|Срез безопасности: Аномалии и утилизация процессов| Security
```

### Спецификация интерфейса

```csharp
namespace CyberPipe.State.Hub.Telemetry;

public interface ISystemResourceMonitor
{
    // Запрос текущего состояния с проверкой допуска
    ValueTask<SystemResourceSnapshot> GetCurrentMetricsAsync(
        AgentSecurityContext callerContext, 
        CancellationToken ct = default
    );

    // Реактивный стрим для UI и SecurityAgent (IAsyncEnumerable)
    IAsyncEnumerable<SystemResourceMetricsChunk> StreamMetricsAsync(
        AgentSecurityContext callerContext,
        TimeSpan sampleInterval,
        CancellationToken ct = default
    );

    // Быстрая проверка для CEO перед запуском тяжелой задачи
    ValueTask<bool> HasAvailableCapacityAsync(
        ResourceRequirement requirement, 
        AgentSecurityContext callerContext
    );
}

public record SystemResourceSnapshot(
    double CpuTotalLoadPercentage,
    long RamAvailableBytes,
    long RamTotalBytes,
    long DiskFreeSpaceBytes,
    int ActiveSandboxesCount,
    IReadOnlyDictionary<string, double> SandboxCpuUsage,
    DateTimeOffset TimestampUtc
);
```

---

## 2. Безопасный перехват сетевой и файловой активности песочниц

### Проблема недоверия к изолированной среде
> [!CAUTION]
> **Нельзя доверять логам изнутри песочницы!**  
> Если вредоносный код получает управление внутри контейнера, он может подделать `/var/log`, отключить демоны аудита или организовать атаку **Log Injection / Terminal Escape Injection**.

### Решение: Внеполосный сбор (Out-of-Band Capture)
Сбор сетевых пакетов и файловых дельт производится **снаружи** силами хостового движка песочниц (`SandboxHostCollector`):
1. **Сетевые дампы:** Перехват на уровне виртуального сетевого интерфейса моста (`veth` / `bridge` в Docker/Podman или сокетные перехватчики WASI в Wasmtime).
2. **Файловая активность:** Отслеживание изменений на уровне монтированного тома через Read-Only снапшоты, eBPF (на Linux) или `FileSystemWatcher` за пределами контейнера.

```
 ┌────────────────────────────────────────────────────────────────────────┐
 │                              HOST LAYER                                │
 │                                                                        │
 │  ┌─────────────────────────┐               ┌────────────────────────┐  │
 │  │    ISandboxInstance     │               │ SandboxTelemetryStream │  │
 │  │   (Docker / Wasmtime)   │               │   (veth tap / mounts)  │  │
 │  └────────────┬────────────┘               └───────────┬────────────┘  │
 │               │                                        │               │
 │               │  Снаружи (Out-of-Band)                 │               │
 └───────────────┼────────────────────────────────────────┼───────────────┘
                 │                                        ▼
                 │                      ┌──────────────────────────────────┐
                 │                      │   TELEMETRY PRE-PROCESSOR        │
                 │                      │   (Двухуровневая фильтрация)     │
                 │                      └─────────────────┬────────────────┘
                 ▼                                        ▼
      [Исполнение кода]                 [Генерация 30 КБ Digest Артефакта]
                                                          │
                                                          ▼
                                        ┌──────────────────────────────────┐
                                        │   SecurityAgent (Эвристика)      │
                                        │   - Анализ графа связей          │
                                        │   - Опрос OSINT по подозрениям   │
                                        └──────────────────────────────────┘
```

---

## 3. Решение проблемы «1 ГБ логов»: Двухуровневый эвристический конвейер

SecurityAgent **не должен читать 1 ГБ сырых данных**. Вместо этого конвейер делится на два уровня:

```
 [1 ГБ сырых событий сети и ФС]
            │
            ▼
 ┌──────────────────────────────────────────────────────────────┐
 │ УРОВЕНЬ 1: Host Telemetry Pre-Processor (Потоковый C# фильтр)│
 │  1. Дедупликация (Фильтры Блума для повторяющихся DNS/IO)    │
 │  2. Исключение белого шума (/lib, /usr, .NET runtime loads)   │
 │  3. Вычисление статистических аномалий (Z-Score сетевых пиков)│
 │  4. Хеширование только новых/измененных файлов (SHA256)      │
 └──────────────────────────────┬───────────────────────────────┘
                                │ Сжатие данных в 10 000 раз!
                                ▼
 ┌──────────────────────────────────────────────────────────────┐
 │ УРОВЕНЬ 2: ExecutionTelemetryDigest (~30-50 КБ JSON)         │
 │  - Список уникальных внешних IP/Доменов и портов             │
 │  - Дельта файлов: что создано, удалено, изменено             │
 │  - Список маркеров опасности (Heuristic Triggers)            │
 └──────────────────────────────┬───────────────────────────────┘
                                │
                                ▼
 ┌──────────────────────────────────────────────────────────────┐
 │ УРОВЕНЬ 3: SecurityAgent (Интеллектуальная эвристика)        │
 │  - Проверяет только подозрительные внешние IP через OSINT    │
 │  - Оценивает поведенческий паттерн (C2 / Ransomware / Exfil) │
 │  - Формирует SecurityInspectionReport для CEO                │
 └──────────────────────────────────────────────────────────────┘
```

### Модель сжатого дайджеста (`ExecutionTelemetryDigest`)

Вместо гигабайта сырых данных SecurityAgent получает компактный и безопасный объект:

```csharp
namespace CyberPipe.Contracts.Models.Telemetry;

public record ExecutionTelemetryDigest(
    Guid CorrelationId,
    Guid SandboxInstanceId,
    TimeSpan ExecutionDuration,
    
    // Сетевая эвристика
    NetworkSummary Network,
    
    // Файловая эвристика
    FileSystemSummary FileSystem,
    
    // Автоматически выявленные аномалии на уровне ядра
    IReadOnlyList<TelemetryAnomaly> DetectedAnomalies,
    
    // Ссылка на зашифрованный исходный архив лога в карантине (если понадобится forensic)
    Guid EncryptedRawLogArtifactId
);

public record NetworkSummary(
    int TotalRequestsCount,
    IReadOnlyList<NetworkConnectionEntry> UniqueOutboundEndpoints, // IP, Domain, Port, BytesSent
    IReadOnlyList<string> DnsQueriesResolved
);

public record FileSystemSummary(
    int TotalFilesReadCount,
    IReadOnlyList<FileMutationEntry> CreatedFiles,   // Путь, Размер, SHA256 хеш
    IReadOnlyList<FileMutationEntry> ModifiedFiles,
    IReadOnlyList<string> DeletedFiles,
    IReadOnlyList<string> PermissionDeniedAttempts // Попытки побега из рабочей папки
);

public record TelemetryAnomaly(
    string AnomalyCode, // "UNEXPECTED_OUTBOUND_PORT", "HIGH_ENTROPY_FILE_WRITTEN", "DIRECTORY_TRAVERSAL"
    string Description,
    RiskLevel Severity
);
```

---

## 4. Эвристические правила SecurityAgent

Получив компактный `ExecutionTelemetryDigest`, SecurityAgent применяет специализированные правила:

1. **Детект эксфильтрации данных (Data Exfiltration):**
   * Объем отправленных байт (`BytesSent`) на внешний IP превышает размер входных параметров задачи.
   * Отправка данных на порты, отличные от 80/443 (например, 1337, 4444, 8080).
2. **Детект вредоносного шифровальщика (Ransomware Heuristic):**
   * Создание множества файлов с высокой энтропией (зашифрованные файлы) за короткий интервал времени.
   * Массовое удаление исходных файлов с последующей записью новых расширений.
3. **Детект Command & Control (C2 Beaconing):**
   * Регулярные DNS-запросы к случайно сгенерированным доменам (DGA — Domain Generation Algorithms).
4. **Попытки побега из песочницы (Sandbox Escape Attempts):**
   * Наличие записей в `PermissionDeniedAttempts` с путями `/proc`, `/sys`, `/etc/shadow`, `C:\Windows\System32`, `docker.sock`.

---

## 5. Безопасность обмена данными и защита от отравления логами

1. **Quarantine Storage (Карантинное хранилище):**
   * Исходный сырой лог (1 ГБ) не уничтожается сразу — он сжимается (zstd) и помещается в шифрованное карантинное хранилище с коротким TTL (например, 24 часа) на случай расследования (Forensics).
2. **Строгая валидация типов (No Raw Script Evaluation):**
   * SecurityAgent парсит только строго типизированный C# DTO `ExecutionTelemetryDigest`.
   * Никакой сырой текст из логов не передается напрямую в системные интерпретаторы хоста.
3. **Санитизация строк дайджеста:**
   * Все пути файлов и доменные имена проходят через `CyberPipe.Security.Sanitization` для экранирования управляющих символов и терминальных escape-последовательностей (ANSI escape injection).

---

## 6. Дисковая гигиена песочниц и проактивный рециклинг ресурсов

Для предотвращения износа SSD и утечек памяти воркеров:
1. **Content-Addressable Storage (CAS) для инструментов и плагинов:**
   * Все бинарники, плагины и зависимости хранятся в едином неизменяемом хранилище по их SHA-256 хешу.
   * Песочницы монтируют эти зависимости в режиме `read-only`, исключая повторную запись гигабайтов на диск при каждом запуске.
2. **Виртуальные диски в оперативной памяти (`tmpfs`):**
   * Все временные файлы выполнения задач пишутся в `tmpfs` с жесткой квотой (128 МБ). Физический SSD не нагружается временными операциями.
3. **Детерминированная RAII-очистка (`EphemeralWorkspace`):**
   * Любая рабочая директория песочницы создается через структуру:
     ```csharp
     await using var workspace = await _sandboxProvider.CreateEphemeralWorkspaceAsync(ct);
     ```
     Интерфейс `IAsyncDisposable` гарантирует принудительное и полное удаление папки с диска даже в случае аварийной отмены задачи или фатального сбоя процесса.
4. **Проактивный рециклинг ресурсов:**
   * Если синглтон `SystemResourceMonitor` фиксирует превышение порогового лимита памяти отдельным контейнером/воркером, задача доводится до ближайшего чекпоинта саги, а контейнер мягко перезапускается с чистым пулом памяти, исключая падение основного шлюза.

