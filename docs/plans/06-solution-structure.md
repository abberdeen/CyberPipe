# 06. Структура Solution и проектов (`CyberPipe.slnx`)

Структура решения упрощена и оптимизирована для **максимальной прозрачности, скорости сборки и отсутствия лишней прослойки абстракций**. Выделены четкие модули для каскадного роутера (0-токен Fast-Path), безопасных скиллов умного помощника (`Skills.Host`) и контроллера бюджета (`Security.Budget`).

---

### Полное дерево проектов Solution

```text
CyberPipe.slnx
├── src/
│   ├── Shared/
│   │   └── CyberPipe.Contracts/               # КРИТИЧЕСКИ ВАЖНО: Единственная зависимость UI!
│   │       ├── Protos/                        # gRPC контракты (gateway.proto, telemetry.proto, budget.proto)
│   │       ├── Messages/                      # События для внутрипроцессной шины Channels
│   │       ├── Enums/                         # RiskLevel, IntentTier, TaskLifecycleState, MemoryTrustSource
│   │       └── Models/                        # DTO: SecurityInspectionReport, BudgetStatusDto, TelemetryDigest
│   │
│   ├── Security/                              # Детерминированная защита (0 токенов) и Контроль Бюджета
│   │   ├── CyberPipe.Security.Guards/         # HostSafetyGuard (Anti-Wipe), MediaSafetyGuard (Volume Clamp + Анти-18+), AST & OSINT
│   │   ├── CyberPipe.Security.Budget/         # TokenBudgetGovernor ($ для RemoteCloud, 0$ для Local), Velocity Circuit Breaker, Kill-Switch
│   │   ├── CyberPipe.Security.Sanitization/   # Центральный сервис очистки секретов ([REDACTED]) и локальный Vault (DPAPI/AES-GCM)
│   │   └── CyberPipe.Security.Telemetry/      # ISystemResourceMonitor, IHardwareCapabilitiesProbe (RAM/VRAM/CPU) и дайджест песочниц
│   │
│   ├── RouterAndSkills/                       # Ядро Умного Помощника (Быстрый путь без сжигания токенов)
│   │   ├── CyberPipe.Router/                  # Voice-to-Intent + 4-Tier Router (Regex, ONNX SIMD, Настраиваемые Local/Remote Tiers)
│   │   └── CyberPipe.Skills.Host/             # Строго типизированные безопасные C#-скиллы помощника:
│   │       ├── MediaAndWorkspace/             # Музыка, Volume Clamp, Рабочие профили (/mode coding/call), Lock/Sleep
│   │       ├── Personal/                      # Второй мозг (Inbox), Умные напоминания, Финансы, Подписки, Фокус
│   │       ├── DevAndInfra/                   # Git Repo Radar (/repos, /standup), Мониторинг серверов/Docker/SSL, Память фиксов
│   │       └── Business/                      # Личная CRM, Трекер Follow-up, Запись созвонов (NAudio), Брифинги, Шаблоны КП
│   │
│   ├── Core/                                  # Оркестрация, Подбор Моделей и Сложные Агенты (Tier 3)
│   │   ├── CyberPipe.Core.Orchestration/      # OrchestratorEngine, IModelCompatibilityAdvisor (15 Local + Cloud Curated), ProactiveScheduler
│   │   └── CyberPipe.Core.Agents/             # Воркеры Tier 3 (Coder, ContractAuditor, MeetingSummarizer, Researcher)
│   │
│   ├── Execution/                             # Песочницы и Изолированный Воркер
│   │   ├── CyberPipe.Execution.Abstractions/  # Интерфейсы ISandboxProvider, ISandboxInstance
│   │   ├── CyberPipe.Execution.Providers/     # BrainstemProcessSupervisor (JobObject + IPC), CliWrap, Wasmtime, Docker
│   │   └── CyberPipe.Brainstem/               # Изолированный процесс-исполнитель (./worker/CyberPipe.Brainstem.exe, см. разд. 15)
│   │
│   ├── Gateway/                               # Входной периметр и Каналы связи
│   │   ├── CyberPipe.Gateway.Telegram/        # Быстрый Telegram-бот: Голос/Файлы, Whitelist AuthGuard, /stop, кнопки Паузы
│   │   └── CyberPipe.Gateway.Api/             # Локальный gRPC сервер (Named Pipes) для Avalonia UI и CLI
│   │
│   ├── Storage/                               # Легковесное и надежное локальное хранение
│   │   └── CyberPipe.Storage.Sqlite/          # SQLite (WAL + FTS5): KnowledgeVault, Личная CRM, Финансы, Семантический кэш, Бюджет
│   │
│   ├── UI/                                    # Мультиплатформенный интерфейс управления
│   │   ├── CyberPipe.UI/                      # Разметка XAML, ViewModels, Выбор моделей по Tiers (Local vs Remote), Дашборд бюджета
│   │   └── CyberPipe.UI.Desktop/              # Десктопный клиент (CyberPipe.UI.Desktop.exe)
│   │
│   └── Host/
│       └── CyberPipe/                         # Основной хост и CLI-диспетчер (CyberPipe.exe: DaemonHostMode + CliClientMode, см. разд. 15)
│
└── tests/
    ├── CyberPipe.Tests.Architecture/          # ArchUnitNET: статическая проверка графа зависимостей проектов
    ├── CyberPipe.Tests.Isolation/             # Интеграционные тесты токенов Windows, Job Object, ACL, IPC и папки публикации (см. разд. 15)
    ├── CyberPipe.Tests.SafetyAndBudget/       # Тесты Anti-Wipe, Volume Clamp (60%/25%), NSFW-фильтра, авто-паузы бюджета и /stop
    ├── CyberPipe.Tests.Router/                # Бенчмарки BenchmarkDotNet для Tier 0/1 и тесты IModelCompatibilityAdvisor
    └── CyberPipe.Tests.Sanitization/          # Тесты вырезания API-ключей, паролей и сжатия телеметрии
```

---

### Архитектурные правила связей (проверяются в CI):

1. **`CyberPipe.Contracts`** не зависит ни от одного другого проекта решения.
2. **`CyberPipe.UI`** и **`CyberPipe.Brainstem`** не имеют ссылок на сборки демона (`Core`, `Skills`, `Gateway`, `Storage`, `Security`). Обоснование компромиссов, состав зависимостей `Brainstem` и проверка папки публикации описаны в [`15-cli-and-execution-modes.md` (разделы 9, 11)](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md).
3. **`CyberPipe.Skills.Host`** не имеет права запускать произвольные процессы (`Process.Start`) и вызывается только после прохождения `HostAndMediaSafetyGuard`.
4. **`CyberPipe.Core.Agents`** вызываются только после прохождения **`CyberPipe.Router`** и **`TokenBudgetGovernor`**.
5. **Разделение `Local` и `RemoteCloud` через `TierModelDescriptor`:** Сервис `IModelCompatibilityAdvisor` получает параметры ПК через контракт `IHardwareCapabilitiesProbe` (см. [`13-model-advisor-and-tier-configuration.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/13-model-advisor-and-tier-configuration.md)).
6. **Устойчивость воркеров и межпроцессная изоляция:** Правила `IsolatedWorkerBase`, `BoundedChannel` и изоляции `CyberPipe.Brainstem.exe` регламентированы в [`14-fault-isolation-and-resilience.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/14-fault-isolation-and-resilience.md) и [`15-cli-and-execution-modes.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md).
