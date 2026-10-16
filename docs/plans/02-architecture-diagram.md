# 02. Диаграмма взаимодействия и потоков данных

В архитектуре **CyberPipe** слои строго разграничены. Презентационный уровень изолирован защищенным периметром Gateway, а все внутренние процессы взаимодействуют через персистентную шину сообщений RabbitMQ с поддержкой Transactional Outbox.

---

### Схема архитектурных слоев и маршрутизации

```mermaid
flowchart TD
    subgraph Presentation ["Presentation Layer (Avalonia UI Multiplatform)"]
        UI_Desktop["CyberPipe.UI.Desktop (Win/macOS/Linux)"]
        UI_Web["CyberPipe.UI.Browser (WebAssembly)"]
    end

    subgraph Contracts ["Contracts Boundary"]
        ProtoContract["CyberPipe.Contracts (gRPC Protos, DTOs, Enums)"]
    end

    subgraph GatewayLayer ["Gateway & Security Boundary"]
        GatewayApi["CyberPipe.Gateway.Api (TLS 1.3, JWT / mTLS)"]
        Channels["Channel Adapters (Telegram, Discord, Webhooks)"]
    end

    subgraph Broker ["Durable Messaging (RabbitMQ + MassTransit)"]
        InboundQueue["gateway.inbound (Persistent)"]
        ProposalQueue["agent.proposals (Persistent)"]
        SecurityQueue["security.inspections (Persistent)"]
        ExecutionQueue["execution.commands (Persistent)"]
        OutboxTable[("Transactional Outbox DB")]
    end

    subgraph Orchestration ["Orchestration & Decision Layer"]
        CEO["CEO / Orchestrator Agent"]
        Security["SecurityAgent (Inspector & Auditor)"]
        Workers["Worker Agents (Coder, Researcher - No Direct Tools)"]
    end

    subgraph ExecutionLayer ["Protected Execution Sandbox"]
        ExecEngine["Execution Engine (Docker, CliWrap, MCP)"]
    end

    UI_Desktop -->|gRPC / TLS| GatewayApi
    UI_Web -->|gRPC-Web / SignalR| GatewayApi
    UI_Desktop -.->|Depends ONLY on| ProtoContract
    UI_Web -.->|Depends ONLY on| ProtoContract

    GatewayApi --> InboundQueue
    Channels --> InboundQueue

    InboundQueue --> CEO
    CEO -->|Assign Sub-task| Workers
    Workers -->|Propose Tool Execution| ProposalQueue
    ProposalQueue --> CEO

    CEO -->|Inspect Proposal / Content| SecurityQueue
    SecurityQueue --> Security
    Security -->|Security Risk Report & Audit Log| CEO

    CEO -->|If Approved by Policy/User| ExecutionQueue
    ExecutionQueue --> ExecEngine

    OutboxTable <--> Broker
```

---

### Описание жизненного цикла пользовательского запроса:
1. Запрос поступает из Avalonia UI (через gRPC-стрим) или из внешнего канала (Telegram/Discord).
2. Gateway валидирует права и помещает задачу в персистентную очередь `gateway.inbound` через Outbox-транзакцию.
3. **CEO Agent** считывает задачу, декомпозирует её и создает изолированные воркеры с чистым контекстом.
4. Воркер в процессе ReAct-цикла решает вызвать инструмент и публикует предложение `ProposeToolExecution` в очередь `agent.proposals`.
5. CEO перенаправляет предложение на инспекцию в `SecurityAgent`.
6. `SecurityAgent` проводит многоуровневый анализ и возвращает отчет `SecurityInspectionReport`.
7. CEO Agent решает: исполнить команду в `ExecutionEngine`, запросить ручное одобрение у человека в UI или отклонить.
