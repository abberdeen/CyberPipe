# 02. Диаграмма взаимодействия и защищенных потоков данных

Архитектура **CyberPipe** построена вокруг **скорости отклика (< 1–15 мс для бытовых задач)**, **жесткой экономии токенов** и **непробиваемой защиты хоста** при управлении через Telegram и Avalonia UI. Вместо тяжеловесных внешних брокеров внутри демона используется сверхбыстрая шина на базе `MassTransit (транспорт InMemory)` и персистентный журнал состояний в `SQLite (WAL)`.

---

### Архитектурная схема: Fast-Path, Защита Хоста и Контроль Токенов

```mermaid
flowchart TD
    subgraph Clients ["Клиенты, CLI/TUI, Голос и Проактивные Триггеры"]
        TG["Telegram Bot Client<br/>(Текст, Голосовые, Документы, Кнопки Паузы/Стоп)"]
        UI["CyberPipe.UI.Desktop.exe (Avalonia UI)<br/>(gRPC Named Pipes, Панель управления, Графики)"]
        CLI["CyberPipe.exe CLI & TUI<br/>(status, doctor, models scan, infer, tui, --json)"]
        Cron["Proactive Scheduler (Quartz.NET + Watchdogs)<br/>(Утренний брифинг, Git/Infra/SSL пингер, Подписки)<br/>+ Read-Only Dashboard на 127.0.0.1"]
    end

    subgraph Perimeter ["1. Периметр, Локальный Голос и Стоп-Кран (< 0.5 ms)"]
        AuthGuard["Telegram & IPC Auth Guard<br/>• Whitelist UserId / ChatId (Owner)<br/>• Верификация PID/Nonce для IPC<br/>• Отсев чужих за 0.1 мс (0 токенов)"]
        KillSwitch["Priority Kill-Switch (/stop, /pause)<br/>• Мгновенный CancellationToken.Cancel()<br/>• Остановка стрима LLM и процессов"]
        VoiceSTT["Local Voice-to-Text (Whisper.net в Brainstem)<br/>• Расшифровка голосовых и звонков за 0 токенов"]
    end

    subgraph FastRouter ["2. Каскадный Маршрутизатор (4-Tier Intent Router: Local vs RemoteCloud)"]
        Tier0["Tier 0: Deterministic Fast-Path (0 токенов, < 1 мс)<br/>• Слэш-команды, Regex ('/mode coding', '/repos', 'громкость 30', 'кофе 350')"]
        Tier1["Tier 1: Local ONNX Semantic Router + Cache (0 токенов, ~10 мс)<br/>• Векторный поиск интента и памяти (SIMD) + Role-Scoped Semantic Cache"]
        Tier2["Tier 2: Light/Fast Task Model (Выбор в UI: 🏠 Local 0$ или ☁️ Remote $)<br/>• Извлечение JSON-параметров + Dynamic Tool Pruning (2-3 тула)"]
        Tier3["Tier 3: Heavy Reasoning Model (Выбор в UI: 🏠 Local 0$ или ☁️ Remote $)<br/>• Кодинг, фикс багов, аудит договоров, выжимка встреч"]
    end

    subgraph BudgetAndSafety ["3. Детерминированные Предохранители и Выходной Шлюз Очистки"]
        Sanitizer["Central Data Sanitizer (Egress Gateway)<br/>• Маскирование ключей, паролей, PII ([REDACTED])<br/>• Перед RemoteCloud LLM, логами Serilog и Telegram"]
        BudgetGov["TokenBudgetGovernor & Circuit Breaker<br/>• Для ☁️ Remote: Soft 4k / Hard 12k, MaxOutputTokens, авто-пауза<br/>• Для 🏠 Local: $0.00 (контроль только MaxTurns = 3)"]
        HostGuard["Host & Media Safety Guard (Anti-Wipe / Anti-Abuse)<br/>• Запрет Raw Shell из чата<br/>• Volume Clamp (Max 60% / 25% ночью + проверка перед Play)<br/>• NSFW / 18+ фильтр и Whitelist медиа-источников"]
        SecGuard["SecurityGuard (AST + Passive OSINT + Hard Tool Stripping)<br/>• Физическое удаление хост-тулов при UntrustedExternal<br/>• Локальный AST-анализ + Stealth проверка URL/IP (Fail-Closed)"]
    end

    subgraph ExecutionLayer ["4. Исполнение: Скиллы, Netrunner и Песочницы"]
        HostSkills["Typed Host & Assistant Skills (0 токенов)<br/>• Media/Volume & Workspace Profiles (/mode)<br/>• Personal: Второй мозг, Финансы, Подписки, Напоминания<br/>• Dev: Git Repo Radar, Infra/SSL Health, Dev Memory<br/>• Business: Личная CRM, Запись звонков (NAudio)"]
        Netrunner["Netrunner ETL Engine (0 токенов)<br/>• Исполнение сохраненных скриптов (ИИ/ручные)<br/>• Детерминированный парсинг и экономия токенов"]
        SandboxEngine["Pluggable Sandbox Engine (ISandboxProvider)<br/>• OS Sandbox (AppContainer / bwrap / Seatbelt) / Wasmtime / Docker<br/>• Out-of-Band Telemetry Pre-Processor (1GB -> 30KB)"]
    end

    subgraph StorageAndState ["5. Хранилища, Шина MassTransit и GlobalStateHub"]
        MasterDB[("Центральный Мастер SQLite (cyberpipe_master.db)<br/>• Базовые настройки приложения<br/>• Реестр SubsystemDatabaseRegistry")]
        DbManager["ISubsystemDatabaseManager<br/>• Разрешение БД + Provisioning<br/>• Пароли в SecretsVault (DPAPI/Keychain)<br/>• Изолированный доступ к подсистемам"]
        SubsystemDBs[("Изолированные БД подсистем (data/subsystems/*.db):<br/>• Quartz.NET.db: Планировщик Quartz.NET<br/>• masstransit.db: Только для MassTransit<br/>• knowledge.db: База знаний (FTS5 + SIMD)<br/>• crm.db: Личная CRM и контакты<br/>• finance.db: Финансы и транзакции<br/>• budget.db: Токены и чекпоинты задач<br/>• health.db: Аудит здоровья сервисов<br/>• netrunner.db: ETL-скрипты Netrunner")]
        MassTransitBus["MassTransit Bus (Выбор транспорта в UI)<br/>• 1. InMemory (По умолчанию, 0$ и 0 зависимостей)<br/>• 2. SQLite WAL (masstransit.db, только для шины)<br/>• 3. RabbitMQ (AMQP 5672, старт через Центр, cyberpipe_admin)"]
        StateHub["GlobalStateHub (Реактивный Синглтон)<br/>• Статус шины MassTransit (InMemory/SQLite/RabbitMQ)<br/>• Запись в health.db ➔ gRPC Стрим в Desktop UI"]
        ResMonitor["ISystemResourceMonitor & IHardwareCapabilitiesProbe<br/>• CPU, Total/Free RAM, GPU VRAM, Активные контейнеры"]
        ModelAdvisor["IModelCompatibilityAdvisor<br/>• Сверяет железо ПК с 15 Local + Curated Cloud моделями"]
    end

    %% Потоки от клиентов
    TG -->|Webhook / Polling| AuthGuard
    UI -->|Named Pipe IPC (gRPC)| AuthGuard
    CLI -->|Named Pipe IPC / Offline Mode| AuthGuard
    Cron -->|Расписание / Событие| Tier0

    AuthGuard -->|/stop или /pause| KillSwitch
    KillSwitch -.->|Abort HTTP & Kill Process| Tier3
    KillSwitch -.->|Abort| SandboxEngine

    AuthGuard -->|Голосовое сообщение| VoiceSTT
    VoiceSTT -->|Текст (0 токенов)| Tier0
    AuthGuard -->|Текстовый запрос| Tier0

    %% Каскад маршрутизации
    Tier0 -->|Совпало (Fast-Path!)| HostGuard
    Tier0 -->|Netrunner Action| Netrunner
    Tier0 -->|Не совпало| Tier1
    Tier1 -->|Высокий скор / Память / Кэш!| HostGuard
    Tier1 -->|Неизвестная фраза| BudgetGov
    BudgetGov -->|Бюджет ОК| Sanitizer
    Sanitizer -->|Очищенный промпт (для RemoteCloud)| Tier2
    BudgetGov -.->|Превышен порог -> ПАУЗА| TG
    Tier2 -->|Скилл с параметрами| HostGuard
    Tier2 -->|Сложная задача| Tier3

    %% Вызов сложной LLM и проверка безопасности
    Tier3 <-->|Контроль каждого шага + Sanitizer| BudgetGov
    Tier3 -->|Tool Proposal| SecGuard
    SecGuard -->|Безопасно (Wasmtime / Docker / OS Sandbox)| SandboxEngine
    SecGuard -.->|Риск High / Нативный процесс -> Запрос подтверждения| TG

    %% Выполнение скиллов хоста и Netrunner
    HostGuard -->|Одобрено и ограничено (Volume/NSFW)| HostSkills
    HostGuard -.->|Заблокировано (18+ / Wipe / Лимит)| TG
    HostSkills -.->|Сырые данные (Fetch)| Netrunner

    %% Связь с хранилищем и монитором
    ResMonitor -.->|Проверка RAM/CPU| SandboxEngine
    ResMonitor -.->|Метрики| UI
    BudgetGov <--> DbManager
    Tier1 <--> DbManager
    HostSkills <--> DbManager
    Netrunner <--> DbManager
    DbManager <--> MasterDB
    DbManager <--> SubsystemDBs

    %% MassTransit и GlobalStateHub
    MassTransitBus <-->|Health Check & State| StateHub
    StateHub <-->|Срез состояния и аудит| DbManager
    StateHub -.->|gRPC State Stream| UI
```

---

### Как работают три главных пути запроса:

#### Путь А: Быстрая команда или Голосовая заметка из Telegram (*«Включи бодрую музыку на 40%»*, *«Кофе 350»*, *«/repos»*)
1. **`TelegramAuthGuard` (< 0.1 мс):** Проверяет, что `UserId` в белом списке владельца/семьи. Если пришло голосовое — `Whisper.net` в выделенном прогретом пуле `Brainstem` переводит его в текст за **0 токенов**.
2. **`Tier 0 / Tier 1 Router` (1–10 мс, 0 токенов):** Мгновенно распознает интент (`Media.Play`, `Finance.AddExpense`, `Dev.GitRadar` или поиск во «Втором мозге») локально без обращения к облачному ИИ и без искажения локальных заметок санитайзером.
3. **`Host & Media Safety Guard` (< 1 мс):**
   * Проверяет запрос по локальному **NSFW/18+ фильтру** и применяет **`VolumeClamp`** (макс. `60%` днем / `25%` ночью, включая принудительную проверку текущей системной громкости перед `Play`).
4. **`Typed Host & Assistant Skill` (5–20 мс):** Вызывает нативный API плеера, записывает расход в `SQLite` или сканирует локальные Git-репозитории.
5. **Итог:** Задача решена за **~10–30 миллисекунд**, потрачено **0 токенов (0.00$)**.

---

#### Путь Б: Сложная инженерная или бизнес-задача (*«Запусти тесты, найди причину падения и предложи фикс»* или *«Проверь PDF договора на риски»*)
1. Не попав в `Tier 0` и `Tier 1`, запрос проходит через **`TokenBudgetGovernor`**, который проверяет остаток дневного/минутного лимита, выставляет `ChatOptions.MaxOutputTokens` и выделяет квоту на задачу (`SoftTaskTokenLimit = 4 000 токенов`, `HardTaskTokenLimit = 12 000 токенов`, `MaxLlmTurnsPerTask = 3`).
2. Перед отправкой в `RemoteCloud` промпт очищается выходным шлюзом **`CentralDataSanitizer`**. Если контекст содержит внешние данные (`UntrustedExternal`), **`SecurityGuard`** физически удаляет схемы хостовых скиллов из `ChatOptions.Tools` (`Hard Tool Stripping`). **`Tier 2 / Tier 3`** вызывает LLM, передавая только сжатый через `ToolOutputTruncator` вывод, топ-3 доверенных факта из `KnowledgeVault` и 2–3 нужных инструмента (**Dynamic Tool Pruning**).
3. При достижении порога `SoftTaskTokenLimit (4 000 токенов)` или `3 шагов`, **`TokenBudgetGovernor` автоматически переводит задачу в состояние `TaskLifecycleState.PausedByBudget`**, сохраняет чекпоинт в SQLite и присылает в Telegram кнопки: `[▶️ Разрешить еще +5 000 токенов]` (добавляет `+5 000 токенов` и `+3 шага`), `[⚡ Выдать итог]` / `[🛑 Стоп]`.
4. Любой сгенерированный агентом скрипт выполняется только в реальной песочнице (`Wasmtime`, `Docker` или ОС-песочнице `AppContainer`/`bwrap`/`Seatbelt` с записью строго в `EphemeralWorkspace`) после проверки локальным AST-анализатором **`SecurityGuard`** (а запуск нативных утилит вне песочницы требует явного подтверждения владельца в Telegram/UI).

---

#### Путь В: Проактивная работа и Ассистент созвонов (*Утренний брифинг, Контроль серверов, Запись встречи*)
1. **`ProactiveScheduler` (Quartz.NET)** в 09:00 (или при падении рабочего контейнера / за 3 дня до списания подписки) собирает данные из локальной `SQLite` (задачи, CRM follow-ups, коммиты Git, статус серверов) за **0 токенов** и отправляет структурированную сводку в Telegram (а состояние очереди доступно в Read-Only Dashboard).
2. Во время рабочего звонка скилл **`MeetingAssistantSkill`** записывает аудиопоток через `NAudio`, локально расшифровывает через `Whisper.net` (**0$**) и формирует список задач (**Action Items**) с кнопкой `[➕ Добавить в трекер]`.

---

### Методика измерения задержек (AUD-06)

Все миллисекундные бюджеты (`Tier 0 < 1 мс`, `Tier 1 5–15 мс`, Fast-Path `< 20 мс`, `/stop < 50 мс`, IPC `p95 < 25 мс`) —
это **локальные бюджеты обработки** на эталонном железе (4+ ядер CPU, SSD NVMe, прогретый процесс; см. `15`, разд. 2).
Сетевая задержка Telegram (путь до серверов Telegram и обратно) в них **не входит** и нормируется отдельно как best-effort.
Отчетность — процентилями `p95/p99` в `BenchmarkDotNet`, а не разовыми замерами.


