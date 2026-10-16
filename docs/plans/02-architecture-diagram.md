# 02. Диаграмма взаимодействия и защищенных потоков данных

В обновленной архитектуре **CyberPipe** каждый узел рассматривается как потенциально недоверенная среда. Все каналы связи и хранилища защищены сквозным шифрованием, поток данных проходит через обязательный шлюз очистки секретов и авторизованный хаб состояний, а за состоянием хоста следит Realtime Singleton мониторинга ресурсов.

---

### Архитектурная схема защищенного контура

```mermaid
flowchart TD
    subgraph UI_Layer ["Presentation Layer (Avalonia UI 11+ Multiplatform)"]
        UI_Desktop["CyberPipe.UI.Desktop (Win/macOS/Linux)"]
        UI_Web["CyberPipe.UI.Browser (WebAssembly)"]
    end

    subgraph Hardware_Monitor ["Realtime Singleton Subsystem"]
        ResMonitor["ISystemResourceMonitor (Singleton)<br/>• CPU, RAM, Disk, Active Sandboxes<br/>• Rate-limited & Role-scoped stream"]
    end

    subgraph Security_Perimeter ["Gateway & Perimeter Security"]
        GatewayApi["CyberPipe.Gateway.Api (TLS 1.3 / mTLS / JWT)"]
        Sanitizer["Central Data & Log Sanitizer (Secret Redactor)"]
    end

    subgraph State_Management ["Authorized State Management"]
        StateHub["Unified Application State Hub (ABAC / Scoped Views)"]
    end

    subgraph Encrypted_Bus ["Untrusted Broker Layer (RabbitMQ + MassTransit)"]
        direction TB
        E2E_Encrypt["Message-Level Encryption (AES-256-GCM / Envelope)"]
        Queues[("Durable Encrypted Queues: Inbound / Proposals / Events")]
        Outbox[("Transactional Outbox (Encrypted At Rest)")]
    end

    subgraph Agent_Core ["Zero-Trust Decision Core"]
        CEO["CEO / Orchestrator Agent (Decision Arbiter)"]
        Workers["Worker Agents (Pure Reasoners, No Direct Tools)"]
    end

    subgraph Security_Inspector ["SecurityAgent (Stealth & Non-Blocking)"]
        SecAgent["SecurityAgent (Async Inspector)"]
        StealthProxy["Stealth OSINT Gateway (Passive DNS / Threat APIs)"]
        SecSandbox["Ephemeral Analysis Sandbox (Throwaway)"]
    end

    subgraph Execution_Abstraction ["Pluggable Sandbox Engine (ISandboxProvider)"]
        ExecEngine["Sandbox Router / Factory"]
        DockerImpl["Docker / Podman Backend"]
        WasmImpl["WebAssembly (Wasmtime) Backend"]
        ProcessImpl["CliWrap (Restricted Process) Backend"]
        
        HostCollector["Out-of-Band Telemetry Collector<br/>(veth tap, mount diffs outside sandbox)"]
        TelemetryPreProcessor["Host Telemetry Pre-Processor<br/>(Deduplication, Bloom filter, 1GB -> 30KB Digest)"]
    end

    subgraph Storage_Layer ["Encrypted Storage (At-Rest)"]
        DB[("Encrypted PostgreSQL / pgvector / SQLite")]
        QuarantineStore[("Quarantine Raw Dumps Store (Encrypted TTL)")]
    end

    %% UI Connections
    UI_Desktop -->|gRPC / TLS 1.3| GatewayApi
    UI_Web -->|gRPC-Web / TLS 1.3| GatewayApi
    GatewayApi -->|Metrics Stream| UI_Desktop
    GatewayApi -->|Metrics Stream| UI_Web

    %% Resource Monitor Connections
    ResMonitor -.->|Stream Metrics| GatewayApi
    ResMonitor -.->|Check Capacity (RAM/CPU)| CEO
    ResMonitor -.->|Spike & Mining Anomaly Alert| SecAgent

    %% Gateway to State & Bus
    GatewayApi --> Sanitizer
    Sanitizer --> GatewayApi
    GatewayApi -->|Authorized Read/Write| StateHub
    GatewayApi -->|Publish Encrypted| E2E_Encrypt
    E2E_Encrypt --> Queues

    %% Bus to Agents
    Queues --> CEO
    CEO -->|Assign Sub-task| Workers
    Workers -->|Publish Proposal| Queues

    %% State Hub Scoping
    StateHub -.->|Scoped State Only (Need-to-Know)| CEO
    StateHub -.->|Restricted Scope (No PII/Secrets)| SecAgent
    StateHub -.->|Task-Only Scope| Workers

    %% SecurityAgent Inspection Flow
    CEO -.->|Async Non-Blocking Notify| Queues
    Queues --> SecAgent
    SecAgent -->|Passive Lookups Only (No Direct Ping/Hit)| StealthProxy
    SecAgent -.->|If dynamic unpack needed| SecSandbox
    SecAgent -->|Publish SecurityInspectionReport| Queues

    %% Authorized Execution Flow & Out-of-band Telemetry
    CEO -->|If Approved by Policy & User| ExecEngine
    ExecEngine --> DockerImpl
    ExecEngine --> WasmImpl
    ExecEngine --> ProcessImpl

    DockerImpl -.->|Network & File Tap| HostCollector
    ProcessImpl -.->|Process & Mount Tap| HostCollector
    HostCollector --> TelemetryPreProcessor
    TelemetryPreProcessor -->|Save Raw Compressed (TTL 24h)| QuarantineStore
    TelemetryPreProcessor -->|Publish Compact 30KB Digest| Queues
    Queues -->|Digest for Heuristic Audit| SecAgent

    %% Outbox & DB
    Outbox <--> DB
    Queues <--> Outbox
```

---

### Принципы движения данных и телеметрии:

1. **Непрерывная санитизация (Sanitizer Everywhere):**
   * Все строки, промпты, аргументы команд и логи очищаются до передачи адресату. Секреты (API ключи, токены, пароли) заменяются плейсхолдерами `[REDACTED_SECRET_ID]`.
2. **Мониторинг ресурсов (Resource Monitor Singleton):**
   * Защищает сервер от переполнения памяти, DoS-атак и скрытого майнинга. CEO запрашивает синглтон перед запуском ресурсоемких песочниц, а UI строит живые графики.
3. **Эвристический конвейер телеметрии (1 ГБ логов ➔ 30 КБ дайджест):**
   * Внеполосный сбор перехватывает сетевые вызовы и файловые изменения снаружи песочницы.
   * Хостовый препроцессор отфильтровывает системный шум и передает в шину компактный `ExecutionTelemetryDigest` с перечнем уникальных внешних подключений, хешей файлов и эвристических маркеров аномалий.
4. **Изоляция SecurityAgent (OPSEC):**
   * SecurityAgent никогда не обращается к проверяемому хосту напрямую, а опрашивает сторонние базы (URLhaus, VirusTotal, AbuseIPDB, DoH).
5. **Авторизованный доступ к состоянию (State Hub):**
   * State Hub возвращает проекции состояния на основе контекста вызова (`ClaimsPrincipal` / `AgentClearanceLevel`).
