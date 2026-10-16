# 06. Структура Solution и проектов (`CyberPipe.slnx`)

Рекомендуемая структура проектов обеспечивает четкие границы ответственности (Clean Architecture), исключает циклические зависимости и физически изолирует пользовательский интерфейс.

---

### Карта проектов Solution

```text
CyberPipe.slnx
├── src/
│   ├── Shared/
│   │   └── CyberPipe.Contracts/               # КРИТИЧЕСКИ ВАЖНО: Единственная зависимость UI!
│   │       ├── Protos/                        # gRPC контракты (gateway.proto, events.proto)
│   │       ├── Events/                        # MassTransit события (TaskCreated, ProposalApproved)
│   │       ├── Commands/                      # Команды шины (ExecuteTask, ApproveProposal)
│   │       ├── Enums/                         # RiskLevel, AgentRole, TaskStatus
│   │       └── Models/                        # DTO: RiskReportDto, AgentStateDto
│   │
│   ├── UI/                                    # Мультиплатформенный изолированный интерфейс
│   │   ├── CyberPipe.UI/                      # Общая разметка XAML, ViewModels, gRPC Services
│   │   ├── CyberPipe.UI.Desktop/              # Точка запуска Desktop (Windows, macOS, Linux)
│   │   └── CyberPipe.UI.Browser/              # Точка запуска Web (WebAssembly / Wasm)
│   │
│   ├── Gateway/                               # Входной защищенный периметр
│   │   ├── CyberPipe.Gateway.Api/             # ASP.NET Core gRPC / SignalR Server, TLS 1.3, JWT Auth
│   │   ├── CyberPipe.Gateway.Channels/        # Адаптеры каналов (Telegram.Bot, Discord.Net)
│   │   └── CyberPipe.Gateway.Infrastructure/  # Продюсеры сообщений в RabbitMQ
│   │
│   ├── Agents/                                # Агентный интеллект
│   │   ├── CyberPipe.Agents.Ceo/              # CEO Orchestrator (Policy Engine, Dispatcher, Session State)
│   │   ├── CyberPipe.Agents.Security/         # SecurityAgent (URL, IP, AST сканеры, Audit Store)
│   │   └── CyberPipe.Agents.Workers/          # Специализированные воркеры (генерируют только Proposals)
│   │
│   ├── Execution/                             # Слой физического исполнения (закрыт от воркеров)
│   │   ├── CyberPipe.Execution.Abstractions/  # Интерфейсы IToolExecutor, ISandboxProvider
│   │   └── CyberPipe.Execution.Sandbox/       # Реализация Docker.DotNet, CliWrap, MCP Runtime
│   │
│   ├── Infrastructure/                        # Транспорт и персистентность
│   │   ├── CyberPipe.Infrastructure.Bus/      # Настройка MassTransit, RabbitMQ топологий, DLQ
│   │   └── CyberPipe.Infrastructure.Storage/  # EF Core, Transactional Outbox, PostgreSQL/SQLite, Аудит
│   │
│   └── Host/
│       └── CyberPipe.Daemon/                  # Главный демон / фоновая служба (запуск MassTransit & Host)
│
└── tests/
    ├── CyberPipe.Tests.Architecture/          # ArchUnitNET тесты на соблюдение чистоты зависимостей UI
    ├── CyberPipe.Tests.Security/              # Тестирование AST-парсеров, детекта вредоносных скриптов и IP
    ├── CyberPipe.Tests.Unit/                  # Модульные тесты планировщика и логики CEO
    └── CyberPipe.Tests.Integration/           # Интеграционные тесты RabbitMQ и Outbox при сбоях
```

---

### Границы зависимостей (Dependency Graph)

* `CyberPipe.Contracts` не зависит ни от кого (чистый .NET 10 standard / DTO).
* `CyberPipe.UI` зависит **ТОЛЬКО** от `CyberPipe.Contracts` и библиотек Avalonia.
* `CyberPipe.Gateway.Api` зависит от `CyberPipe.Contracts` и `CyberPipe.Infrastructure.Bus`.
* `CyberPipe.Agents.Workers` зависит от `CyberPipe.Contracts` и `Microsoft.Extensions.AI`. Воркеры **не зависят** от `CyberPipe.Execution.*`!
* `CyberPipe.Agents.Ceo` координирует воркеров и общается с шиной.
* `CyberPipe.Execution.Sandbox` управляется исключительно вызовами из `CyberPipe.Agents.Ceo` (через события или защищенные интерфейсы).
