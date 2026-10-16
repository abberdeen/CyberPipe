# 02. Диаграмма взаимодействия и защищенных потоков данных

В обновленной архитектуре **CyberPipe** каждый узел рассматривается как потенциально недоверенная среда. Все каналы связи и хранилища защищены сквозным шифрованием, а поток данных проходит через обязательный шлюз очистки секретов и авторизованный хаб состояний.

---

### Архитектурная схема защищенного контура

```mermaid
flowchart TD
    subgraph UI_Layer ["Presentation Layer (Avalonia UI 11+ Multiplatform)"]
        UI_Desktop["CyberPipe.UI.Desktop (Win/macOS/Linux)"]
        UI_Web["CyberPipe.UI.Browser (WebAssembly)"]
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
    end

    subgraph Storage_Layer ["Encrypted Storage (At-Rest)"]
        DB[("Encrypted PostgreSQL / pgvector / SQLite")]
    end

    %% UI Connections
    UI_Desktop -->|gRPC / TLS 1.3| GatewayApi
    UI_Web -->|gRPC-Web / TLS 1.3| GatewayApi

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

    %% Authorized Execution Flow
    CEO -->|If Approved by Policy & User| ExecEngine
    ExecEngine --> DockerImpl
    ExecEngine --> WasmImpl
    ExecEngine --> ProcessImpl

    %% Outbox & DB
    Outbox <--> DB
    Queues <--> Outbox
```

---

### Принципы движения данных:

1. **Непрерывная санитизация (Sanitizer Everywhere):**
   * Все строки, промпты, аргументы команд и логи очищаются до передачи адресату. Секреты (API ключи, токены, пароли) заменяются плейсхолдерами `[REDACTED_SECRET_ID]`.
2. **Асинхронность и сохранность последовательности:**
   * Взаимодействие построено на **MassTransit State Machine (Saga)**. Все шаги имеют `CorrelationId`.
   * CEO Agent не держит блокирующий вызов (thread wait). Он переводит сессию в состояние `WaitingForSecurityAssessment` или `ExecutingTool`, освобождая ресурсы потока.
3. **Изоляция SecurityAgent (OPSEC):**
   * SecurityAgent никогда не обращается к проверяемому хосту напрямую.
   * Проверка URL: опрос базы URLhaus, VirusTotal API, Google Safe Browsing.
   * Проверка IP: опрос AbuseIPDB, Shodan API, пассивного DNS.
   * Проверка Email: проверка формата, MX-записи через безопасные рекурсивные DNS (Cloudflare/Quad9), базы утечек (HIBP API).
4. **Авторизованный доступ к состоянию (State Hub):**
   * State Hub возвращает проекции состояния на основе контекста вызова (`ClaimsPrincipal` / `AgentClearanceLevel`).
   * Если SecurityAgent запрашивает профиль пользователя, возвращаются только атрибуты задачи, но персональные данные (имя, контакты, личные токены) физически исключаются из DTO ответа.
