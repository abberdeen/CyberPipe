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
│   ├── Execution/                             # Изолированные песочницы и Стерильный Воркер (Brainstem)
│   │   ├── CyberPipe.Execution.Abstractions/  # Интерфейсы ISandboxProvider, ISandboxInstance
│   │   ├── CyberPipe.Execution.Providers/     # Реализации: JobObjectRunner, CliWrap, Wasmtime, Docker (опционально)
│   │   └── CyberPipe.Brainstem/               # Стерильный субпроцесс (./worker/CyberPipe.Brainstem.exe: Low Integrity, 0 ссылок на демон!)
│   │
│   ├── Gateway/                               # Входной периметр и Каналы связи
│   │   ├── CyberPipe.Gateway.Telegram/        # Быстрый Telegram-бот: Голос/Файлы, Whitelist AuthGuard, /stop, кнопки Паузы
│   │   └── CyberPipe.Gateway.Api/             # Локальный gRPC сервер для Avalonia UI и CLI
│   │
│   ├── Storage/                               # Легковесное и надежное локальное хранение
│   │   └── CyberPipe.Storage.Sqlite/          # SQLite (WAL + FTS5): KnowledgeVault, Личная CRM, Финансы, Семантический кэш, Бюджет
│   │
│   ├── UI/                                    # Мультиплатформенный интерфейс управления
│   │   ├── CyberPipe.UI/                      # Разметка XAML, ViewModels, Выбор моделей по Tiers (Local vs Remote), Дашборд бюджета
│   │   └── CyberPipe.UI.Desktop/              # Десктопный клиент (CyberPipe.UI.Desktop.exe: Трей, Попап Alt+Space, Дашборд)
│   │
│   └── Host/
│       └── CyberPipe/                         # Главный бинарник ядра (CyberPipe.exe — работает в 2 режимах от аргументов запуска):
│           ├── Modes/DaemonHostMode.cs        # Режим 1: Фоновый демон (SafeBoot DI, Bulkhead-отсеки, IsolatedWorkerBase, gRPC)
│           └── Modes/CliClientMode.cs         # Режим 2: Системный CLI & TUI (doctor, status, models scan, infer, tui, --json)
│
└── tests/
    ├── CyberPipe.Tests.Architecture/          # ArchUnitNET: изоляция UI и Brainstem, запрет Raw Shell, UnboundedChannel и голого BackgroundService
    ├── CyberPipe.Tests.SafetyAndBudget/       # Тесты Anti-Wipe, Volume Clamp (60%/25%), NSFW-фильтра, авто-паузы бюджета и /stop
    ├── CyberPipe.Tests.Router/                # Бенчмарки скорости Tier 0 (< 1 мс), Tier 1 ONNX (< 15 мс) и тесты IModelCompatibilityAdvisor
    └── CyberPipe.Tests.Sanitization/          # Тесты вырезания API-ключей, паролей и сжатия 1 ГБ телеметрии в 30 КБ
```

---

### Жесткие архитектурные правила связей (проверяются автоматически):

1. **`CyberPipe.Contracts`** не зависит ни от одного другого проекта.
2. **`CyberPipe.UI` (`CyberPipe.UI.Desktop.exe`)** и **`CyberPipe.Brainstem` (`worker/CyberPipe.Brainstem.exe`)** ссылаются **только** на `CyberPipe.Contracts` — внутри `Brainstem` физически отсутствуют `.dll` и классы демона (защита от `System.Reflection`).
3. **`CyberPipe.Skills.Host`** (музыка, громкость, таймеры) не имеет права запускать произвольные процессы (`Process.Start`) и вызывается **исключительно после прохождения `HostAndMediaSafetyGuard`**.
4. **`CyberPipe.Core.Agents`** не могут вызываться напрямую из `Gateway` — любой запрос обязан пройти через **`CyberPipe.Router`** (попытка решить задачу бесплатно в Tier 0/1) и через **`TokenBudgetGovernor`** (контроль лимитов и стоп-крана).
5. **Четкое разделение `Local` и `RemoteCloud` через `TierModelDescriptor`:** Приложение, CLI и UI всегда знают `ModelHostingType (Local vs RemoteCloud)` выбранной пользователем модели на каждом Tier, а сервис `IModelCompatibilityAdvisor` получает параметры ПК строго через контракт `IHardwareCapabilitiesProbe`.
6. **Неубиваемость `CyberPipe.exe` и изоляция `CyberPipe.Brainstem.exe`:** Все фоновые службы в `CyberPipe.exe` обязаны наследоваться от `IsolatedWorkerBase` (с авто-карантином после 3 сбоев), все каналы — только `BoundedChannel`, а опасные нативные C++ задачи выполняются в отдельном стерильном `worker/CyberPipe.Brainstem.exe` внутри `Windows Job Object` (`Low Integrity`).
