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
│   │   ├── CyberPipe.Security.Budget/         # TokenBudgetGovernor, Velocity Circuit Breaker, Пауза задач и Kill-Switch реестр
│   │   ├── CyberPipe.Security.Sanitization/   # Центральный сервис очистки секретов ([REDACTED]) и локальный Vault (DPAPI/AES-GCM)
│   │   └── CyberPipe.Security.Telemetry/      # ISystemResourceMonitor (CPU/RAM) и потоковый дайджест песочниц (1ГБ -> 30КБ)
│   │
│   ├── RouterAndSkills/                       # Ядро Умного Помощника (Быстрый путь без сжигания токенов)
│   │   ├── CyberPipe.Router/                  # 4-Tier Intent Router: Tier 0 (Regex), Tier 1 (Local ONNX SIMD + Cache), Tier 2 (Local SLM)
│   │   └── CyberPipe.Skills.Host/             # Строго типизированные безопасные C#-скиллы хоста (Media, Volume, Timers, SystemInfo)
│   │
│   ├── Core/                                  # Оркестрация и Сложные Агенты (Tier 3)
│   │   ├── CyberPipe.Core.Orchestration/      # Детерминированный OrchestratorEngine, State Machine, In-Process Channel Bus
│   │   └── CyberPipe.Core.Agents/             # Воркеры Tier 3 (Coder, Researcher) с Dynamic Tool Pruning и сжатием контекста
│   │
│   ├── Execution/                             # Изолированные песочницы (только для задач Tier 3)
│   │   ├── CyberPipe.Execution.Abstractions/  # Интерфейсы ISandboxProvider, ISandboxInstance
│   │   └── CyberPipe.Execution.Providers/     # Реализации: CliWrap (Restricted Process), Wasmtime, Docker (опционально)
│   │
│   ├── Gateway/                               # Входной периметр и Каналы связи
│   │   ├── CyberPipe.Gateway.Telegram/        # Быстрый Telegram-бот: Whitelist AuthGuard, приоритетный /stop, кнопки Паузы бюджета
│   │   └── CyberPipe.Gateway.Api/             # Локальный gRPC сервер для Avalonia UI
│   │
│   ├── Storage/                               # Легковесное и надежное локальное хранение
│   │   └── CyberPipe.Storage.Sqlite/          # SQLite (WAL mode): Семантический кэш ответов, чекпоинты пауз, учет токенов и логов
│   │
│   ├── UI/                                    # Мультиплатформенный интерфейс управления
│   │   ├── CyberPipe.UI/                      # Разметка XAML, ViewModels, управление лимитами токенов и громкости
│   │   └── CyberPipe.UI.Desktop/              # Десктопный клиент (Windows, macOS, Linux)
│   │
│   └── Host/
│       └── CyberPipe.Daemon/                  # Единый быстрый сервис-хост (запускается за < 1 сек без внешних зависимостей)
│
└── tests/
    ├── CyberPipe.Tests.Architecture/          # ArchUnitNET: проверка изоляции UI, запрета Raw Shell и обязательности BudgetGovernor
    ├── CyberPipe.Tests.SafetyAndBudget/       # Тесты Anti-Wipe, Volume Clamp (60%/25%), NSFW-фильтра, авто-паузы бюджета и /stop
    ├── CyberPipe.Tests.Router/                # Бенчмарки скорости Tier 0 (< 1 мс) и Tier 1 ONNX (< 15 мс) без вызовов LLM
    └── CyberPipe.Tests.Sanitization/          # Тесты вырезания API-ключей, паролей и сжатия 1 ГБ телеметрии в 30 КБ
```

---

### Жесткие архитектурные правила связей (проверяются автоматически):

1. **`CyberPipe.Contracts`** не зависит ни от одного другого проекта.
2. **`CyberPipe.UI`** ссылается **только** на `CyberPipe.Contracts` и пакеты Avalonia.
3. **`CyberPipe.Skills.Host`** (музыка, громкость, таймеры) не имеет права запускать произвольные процессы (`Process.Start`) и вызывается **исключительно после прохождения `HostAndMediaSafetyGuard`**.
4. **`CyberPipe.Core.Agents`** (облачные LLM) не могут вызываться напрямую из `Gateway` — любой запрос обязан пройти через **`CyberPipe.Router`** (попытка решить задачу бесплатно в Tier 0/1/2) и через **`TokenBudgetGovernor`** (контроль лимитов и стоп-крана).
5. **Нулевые внешние зависимости при старте:** Для запуска `CyberPipe.Daemon` не нужны установленные Docker, RabbitMQ или PostgreSQL — база SQLite (WAL) и шина `System.Threading.Channels` поднимаются внутри процесса за доли секунды.
