# 06. Структура Solution и проектов (`CyberPipe.slnx`)

Структура решения упрощена и оптимизирована для **максимальной прозрачности, скорости сборки и отсутствия лишней прослойки абстракций**. Выделены четкие модули для каскадного роутера (0-токен Fast-Path), безопасных скиллов умного помощника (`Skills.Host`) и контроллера бюджета (`Security.Budget`).

---

### Полное дерево проектов Solution

```text
CyberPipe.slnx
├── src/
│   ├── Shared/
│   │   └── CyberPipe.Contracts/               # Уровень 0 цепочка: 0 внешних зависимостей! Единственная зависимость UI и Brainstem!
│   │       ├── Protos/                        # gRPC контракты (gateway.proto, telemetry.proto, budget.proto)
│   │       ├── Messages/                      # События и команды для шины MassTransit (InMemory, SQLite, RabbitMQ)
│   │       ├── Enums/                         # RiskLevel, IntentTier, TaskLifecycleState, MemoryTrustSource, MessageBusTransportType
│   │       ├── Hardware/                      # IHardwareCapabilitiesProbe, ISystemResourceMonitor, HostHardwareProfile
│   │       ├── Platform/                      # Кроссплатформенные контракты ОС: IAudioDeviceController, IScreenOcrProvider,
│   │       │                                  # ISecretVaultPlatform, IBrainstemIsolationPlatform, IDaemonInstaller, IDevToolProcessRunner,
│   │       │                                  # ISubsystemDatabaseManager, IMessageBusProviderManager, IRabbitMqSupervisionService,
│   │       │                                  # ICenterInfrastructureSupervisor, IGlobalStateHub, INetrunnerWorkflowEngine, INetrunnerPackageValidator
│   │       └── Models/                        # DTO: SecurityInspectionReport, BudgetStatusDto, TelemetryDigest, BusHealthStatus,
│   │                                          # SubsystemDatabaseDescriptor, NetrunnerManifestDto, NetrunnerWorkflowDefinition
│   │
│   ├── Security/                              # Детерминированная защита (0 токенов), Ресурсы и Контроль Бюджета
│   │   ├── CyberPipe.Security.Telemetry/      # Уровень 1 цепочка (зависит только от Contracts):
│   │   │                                      # IsolatedWorkerBase, QuotaBoundedStream, HardwareCapabilitiesProbe,
│   │   │                                      # SystemResourceMonitor, ToolOutputTruncator и дайджест песочниц
│   │   ├── CyberPipe.Security.Guards/         # HostSafetyGuard (Anti-Wipe), MediaSafetyGuard (Volume Clamp + Анти-18+), AST & OSINT
│   │   ├── CyberPipe.Security.Budget/         # TokenBudgetGovernor ($ + MaxOutputTokens для RemoteCloud, 0$ для Local), Kill-Switch
│   │   └── CyberPipe.Security.Sanitization/   # Выходной шлюз очистки секретов (Egress [REDACTED]) и платформенный Vault (DPAPI/LibSecret/Keychain)
│   │
│   ├── RouterAndSkills/                       # Ядро Умного Помощника (Быстрый путь без сжигания токенов)
│   │   ├── CyberPipe.Router/                  # Voice-to-Intent + 4-Tier Router (Regex, ONNX SIMD, Настраиваемые Local/Remote Tiers)
│   │   ├── CyberPipe.Skills.Host/             # Строго типизированные безопасные C#-скиллы помощника (без Process.Start!):
│   │   │   ├── MediaAndWorkspace/             # Музыка, Volume Clamp, Рабочие профили (/mode coding/call), Lock/Sleep
│   │   │   ├── Personal/                      # Второй мозг (Inbox), Умные напоминания, Финансы, Подписки, Фокус
│   │   │   ├── DevAndInfra/                   # Git Repo Radar (LibGit2Sharp), BuildAndTestWatchdog (через IDevToolProcessRunner),
│   │   │   │                                  # Мониторинг серверов/Docker/SSL, Память фиксов
│   │   │   └── Business/                      # Личная CRM, Трекер Follow-up, Запись созвонов (IAudioDeviceController), Брифинги, Шаблоны КП
│   │   └── CyberPipe.Skills.Netrunner/        # Ассистент Netrunner (см. разд. 16): движок каскадных задач цепочка Trigger → Skill → Script → Delivery (Skill/Netrunner/LLM/HITL),
│   │                                          # валидатор пакетов (netrunner.manifest.json + JSON Schema + SHA-256), Roslyn/Wasm ETL за 0 токенов
│   │
│   ├── Core/                                  # Оркестрация, Подбор Моделей и Сложные Агенты (Tier 3)
│   │   ├── CyberPipe.Core.Orchestration/      # OrchestratorEngine, ModelCompatibilityAdvisorService, ProactiveScheduler,
│   │   │                                      # MassTransitBusManager (InMemory, SQLite masstransit.db, RabbitMQ), GlobalStateHub
│   │   └── CyberPipe.Core.Agents/             # Воркеры Tier 3 (Coder, ContractAuditor, MeetingSummarizer, Researcher)
│   │
│   ├── Execution/                             # Песочницы, Изолированный Воркер и Контролируемый Раннер Инструментов
│   │   ├── CyberPipe.Execution.Abstractions/  # Интерфейсы ISandboxProvider, ISandboxInstance
│   │   ├── CyberPipe.Execution.Providers/     # BrainstemProcessSupervisor (AppContainer/RestrictedToken/bwrap/Seatbelt + JobObject),
│   │   │                                      # OsRestrictedSandboxProvider, Wasmtime, Docker, DevToolProcessRunner (git/dotnet whitelist)
│   │   └── CyberPipe.Brainstem/               # Изолированный процесс-исполнитель для Whisper.net и PdfPig/OpenXml (./worker/CyberPipe.Brainstem, см. разд. 15)
│   │
│   ├── Gateway/                               # Входной периметр и Каналы связи
│   │   ├── CyberPipe.Gateway.Telegram/        # Быстрый Telegram-бот: Голос/Файлы, Whitelist AuthGuard, /stop, кнопки Паузы
│   │   └── CyberPipe.Gateway.Api/             # Локальный gRPC сервер (Named Pipes/UDS) + локальный хост безопасного Read-Only Quartz.NET Dashboard (127.0.0.1)
│   │
│   ├── Storage/                               # Легковесное, надежное и изолированное локальное хранилище (SQLite WAL)
│   │   └── CyberPipe.Storage.Sqlite/          # Центральный мастер cyberpipe_master.db (общие настройки + реестр SubsystemDatabaseRegistry),
│   │                                          # сервис SubsystemDatabaseManagerService (генерация паролей в SecretsVault, provisioning, миграции),
│   │                                          # изолированные базы подсистем data/subsystems/*.db (Quartz.NET, masstransit, knowledge, crm, finance, budget, health, netrunner)
│   │
│   ├── UI/                                    # Мультиплатформенный интерфейс управления
│   │   ├── CyberPipe.UI/                      # Разметка XAML, ViewModels, Выбор моделей по Tiers (Local vs Remote), Дашборд бюджета
│   │   └── CyberPipe.UI.Desktop/              # Десктопный клиент (CyberPipe.UI.Desktop)
│   │
│   └── Host/
│       └── CyberPipe/                         # Основной хост и CLI-диспетчер (CyberPipe: DaemonHostMode + CliClientMode, см. разд. 15)
│
└── tests/
    ├── CyberPipe.Tests.Architecture/          # ArchUnitNET: статическая проверка цепочка всех сборок, запрета Process в Skills и UnboundedChannel
    ├── CyberPipe.Tests.Isolation/             # Интеграционные тесты изоляции ОС (AppContainer/RestrictedToken/bwrap/Seatbelt, Job Object, ACL, IPC)
    ├── CyberPipe.Tests.SafetyAndBudget/       # Тесты Anti-Wipe, Volume Clamp перед Play, NSFW-фильтра, Hard Tool Stripping, MaxOutputTokens и /stop
    ├── CyberPipe.Tests.Router/                # Бенчмарки BenchmarkDotNet для Tier 0/1, ролевого кэша и тесты IModelCompatibilityAdvisor
    └── CyberPipe.Tests.Sanitization/          # Тесты Egress-вырезания ключей, запрета слепой подстановки секретов и сжатия телеметрии
```

---

### Архитектурные правила связей (Строгий ациклический граф цепочка, проверяется в CI):

1. **Уровень 0 (`CyberPipe.Contracts`)** не зависит ни от одного другого проекта решения и содержит все общие DTO, перечисления и платформенные контракты (`IHardwareCapabilitiesProbe`, `IDevToolProcessRunner`, `IBrainstemIsolationPlatform` и др.).
2. **Уровень 1 (`CyberPipe.Security.Telemetry`)** зависит только от `CyberPipe.Contracts` и предоставляет базовые примитивы устойчивости (`IsolatedWorkerBase`, `QuotaBoundedStream`), что полностью исключает циклические зависимости между `Storage.Sqlite`, `Skills.Host` и `Core.Orchestration`.
3. **`CyberPipe.UI`** и **`CyberPipe.Brainstem`** ссылаются исключительно на `CyberPipe.Contracts` и не имеют ссылок на сборки демона (`Core`, `Skills`, `Gateway`, `Storage`, `Security`, `Execution.Providers`). Состав зависимостей `Brainstem` и проверка папки публикации описаны в [`15-cli-and-execution-modes.md` (разделы 9, 11)](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md).
4. **`CyberPipe.Skills.Host`** не имеет права ссылаться на `CyberPipe.Execution.*` или напрямую вызывать `System.Diagnostics.Process` / `CliWrap`. Для запуска строго фиксированных инструментов разработки (`dotnet build/test`, `git`) скиллы обращаются к интерфейсу `IDevToolProcessRunner` из `CyberPipe.Contracts`, реализация которого (`DevToolProcessRunner` в `CyberPipe.Execution.Providers`) проверяет белый список исполняемых файлов (только `git` и `dotnet` с фиксированным списком безопасных подкоманд) и директорий из `GitRepositoriesRoot`, а запуск выполняет только через ограниченный раннер внутри песочницы/изоляции — произвольные цели сборки и хуки вне песочницы запрещены (AUD-08).
5. **`CyberPipe.Core.Agents`** вызываются только после прохождения **`CyberPipe.Router`**, **`TokenBudgetGovernor`** и **`CentralDataSanitizer`**.
6. **Разделение `Local` и `RemoteCloud` через `TierModelDescriptor`:** Сервис `ModelCompatibilityAdvisorService` (`CyberPipe.Core.Orchestration`) получает параметры ПК через контракт `IHardwareCapabilitiesProbe` (`CyberPipe.Contracts.Hardware`, реализован в `CyberPipe.Security.Telemetry`, см. [`13-model-advisor-and-tier-configuration.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/13-model-advisor-and-tier-configuration.md)).
7. **Устойчивость воркеров и межпроцессная изоляция:** Правила наследования от `CyberPipe.Security.Telemetry.Resilience.IsolatedWorkerBase`, запрета `Channel.CreateUnbounded<T>()` и изоляции `CyberPipe.Brainstem` регламентированы в [`14-fault-isolation-and-resilience.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/14-fault-isolation-and-resilience.md) и [`15-cli-and-execution-modes.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md).


