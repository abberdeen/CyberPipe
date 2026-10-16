# 06. Структура Solution и проектов (`CyberPipe.slnx`)

Реорганизованная структура проектов строго разделяет зоны ответственности, изолирует криптографические операции, санитизацию, подключаемые песочницы и авторизованный доступ к состоянию.

---

### Полное дерево проектов Solution

```text
CyberPipe.slnx
├── src/
│   ├── Shared/
│   │   └── CyberPipe.Contracts/               # КРИТИЧЕСКИ ВАЖНО: Единственная зависимость UI!
│   │       ├── Protos/                        # gRPC контракты (gateway.proto, events.proto, state.proto)
│   │       ├── Messages/                      # MassTransit DTO команд и событий шины
│   │       ├── Enums/                         # RiskLevel, SandboxType, AgentClearanceLevel
│   │       └── Models/                        # DTO: SecurityReportDto, ScopedStateDto
│   │
│   ├── Security/                              # Модули сквозной безопасности и защиты данных
│   │   ├── CyberPipe.Security.Sanitization/   # Центральный сервис очистки данных и маскирования секретов
│   │   └── CyberPipe.Security.Cryptography/   # E2EE: AES-256-GCM, деривация ключей, Envelope Encryption
│   │
│   ├── State/                                 # Авторизованное состояние приложения
│   │   └── CyberPipe.State.Hub/               # Единый хаб состояния с политиками ABAC / Need-to-Know
│   │
│   ├── UI/                                    # Мультиплатформенный изолированный интерфейс
│   │   ├── CyberPipe.UI/                      # Общая разметка XAML, ViewModels, gRPC Services
│   │   ├── CyberPipe.UI.Desktop/              # Точка запуска Desktop (Windows, macOS, Linux)
│   │   └── CyberPipe.UI.Browser/              # Точка запуска Web (WebAssembly / Wasm)
│   │
│   ├── Gateway/                               # Входной защищенный периметр
│   │   ├── CyberPipe.Gateway.Api/             # ASP.NET Core gRPC / SignalR Server, TLS 1.3, JWT Auth
│   │   ├── CyberPipe.Gateway.Channels/        # Адаптеры каналов (Telegram, Discord, Webhooks)
│   │   └── CyberPipe.Gateway.Infrastructure/  # Продюсеры зашифрованных сообщений в RabbitMQ
│   │
│   ├── Agents/                                # Агентный интеллект
│   │   ├── CyberPipe.Agents.Ceo/              # CEO Orchestrator (Policy Engine, Dispatcher, Sagas)
│   │   ├── CyberPipe.Agents.Security/         # SecurityAgent (Stealth OSINT, Email, AST-парсеры)
│   │   └── CyberPipe.Agents.Workers/          # Воркеры: Coder, Researcher (генерируют только Proposals)
│   │
│   ├── Execution/                             # Абстракция подключаемых песочниц (Pluggable Sandbox)
│   │   ├── CyberPipe.Execution.Abstractions/  # Интерфейсы ISandboxProvider, ISandboxInstance
│   │   ├── CyberPipe.Execution.Docker/        # Бэкенд на Docker.DotNet (Docker / Podman)
│   │   ├── CyberPipe.Execution.Wasm/          # Бэкенд на Wasmtime (изолированный WebAssembly)
│   │   └── CyberPipe.Execution.Process/       # Бэкенд на CliWrap (ограниченные процессы ОС)
│   │
│   ├── Infrastructure/                        # Транспорт и шифрованное хранение данных
│   │   ├── CyberPipe.Infrastructure.Bus/      # MassTransit + RabbitMQ, шифрованные очереди, саги
│   │   └── CyberPipe.Infrastructure.Storage/  # EF Core, поколоночное шифрование, Outbox, pgvector/SQLite
│   │
│   └── Host/
│       └── CyberPipe.Daemon/                  # Главная серверная служба (запуск MassTransit, API и агентов)
│
└── tests/
    ├── CyberPipe.Tests.Architecture/          # ArchUnitNET тесты (проверка изоляции UI и воркеров)
    ├── CyberPipe.Tests.Sanitization/          # Тесты очистки секретов, токенов, PII
    ├── CyberPipe.Tests.Security/              # Тестирование AST-парсеров, детекта SSRF, фишинга, email
    ├── CyberPipe.Tests.Cryptography/          # Тестирование шифрования полезной нагрузки сообщений и БД
    └── CyberPipe.Tests.Integration/           # Интеграционные тесты саг RabbitMQ и сбоев брокера
```

---

### Архитектурные правила связей проектов:

1. **`CyberPipe.Contracts`** не ссылается ни на один проект внутри решения.
2. **`CyberPipe.UI`** ссылается **исключительно** на `CyberPipe.Contracts` и Avalonia.
3. **`CyberPipe.Security.Sanitization`** — утилитная библиотека, подключаемая всеми инфраструктурными и агентными модулями (логгеры, шина, шлюз).
4. **`CyberPipe.Agents.Workers`** не имеют ссылок на `CyberPipe.Execution.*` или `CyberPipe.Infrastructure.Storage`.
5. **`CyberPipe.Execution.Abstractions`** определяет контракты песочниц, а конкретные реализации (`Docker`, `Wasm`, `Process`) регистрируются в DI через конфигурацию (Zero Hardcoding).
