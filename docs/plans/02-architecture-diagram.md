# 02. Диаграмма взаимодействия и защищенных потоков данных

Архитектура **CyberPipe** построена вокруг **скорости отклика (< 1–15 мс для бытовых задач)**, **жесткой экономии токенов** и **непробиваемой защиты хоста** при управлении через Telegram и Avalonia UI. Вместо тяжеловесных внешних брокеров внутри демона используется сверхбыстрая шина на базе `System.Threading.Channels` и персистентный журнал состояний в `SQLite (WAL)`.

---

### Архитектурная схема: Fast-Path, Защита Хоста и Контроль Токенов

```mermaid
flowchart TD
    subgraph Clients ["Клиенты, Голос и Проактивные Триггеры"]
        TG["Telegram Bot Client<br/>(Текст, Голосовые, Документы, Кнопки Паузы/Стоп)"]
        UI["Avalonia UI (Desktop / Browser)<br/>(gRPC Stream, Панель управления, Графики)"]
        Cron["Proactive Scheduler & Event Watchdogs<br/>(Утренний брифинг, Git/Infra/SSL пингер, Подписки)"]
    end

    subgraph Perimeter ["1. Периметр, Локальный Голос и Стоп-Кран (< 0.5 ms)"]
        AuthGuard["Telegram & API Auth Guard<br/>• Whitelist UserId / ChatId (Owner vs Guest)<br/>• Отсев чужих за 0.1 мс (0 токенов)"]
        KillSwitch["Priority Kill-Switch (/stop, /pause)<br/>• Мгновенный CancellationToken.Cancel()<br/>• Остановка стрима LLM и процессов"]
        VoiceSTT["Local Voice-to-Text (Whisper.net ONNX)<br/>• Расшифровка голосовых и звонков за 0 токенов"]
        Sanitizer["Central Data Sanitizer<br/>• Маскирование ключей, паролей, PII"]
    end

    subgraph FastRouter ["2. Каскадный Маршрутизатор (4-Tier Intent Router: Local vs RemoteCloud)"]
        Tier0["Tier 0: Deterministic Fast-Path (0 токенов, < 1 мс)<br/>• Слэш-команды, Regex ('/mode coding', '/repos', 'громкость 30', 'кофе 350')"]
        Tier1["Tier 1: Local ONNX Semantic Router + Cache (0 токенов, ~10 мс)<br/>• Векторный поиск интента и памяти (SIMD) + Semantic Response Cache"]
        Tier2["Tier 2: Light/Fast Task Model (Выбор в UI: 🏠 Local 0$ или ☁️ Remote $)<br/>• Извлечение JSON-параметров + Dynamic Tool Pruning (2-3 тула)"]
        Tier3["Tier 3: Heavy Reasoning Model (Выбор в UI: 🏠 Local 0$ или ☁️ Remote $)<br/>• Кодинг, фикс багов, аудит договоров, выжимка встреч"]
    end

    subgraph BudgetAndSafety ["3. Детерминированные Предохранители (Адаптация под Local / RemoteCloud)"]
        BudgetGov["TokenBudgetGovernor & Circuit Breaker<br/>• Для ☁️ Remote: лимит $ / авто-пауза в Telegram<br/>• Для 🏠 Local: $0.00 (контроль только MaxTurns)"]
        HostGuard["Host & Media Safety Guard (Anti-Wipe / Anti-Abuse)<br/>• Запрет Raw Shell из чата<br/>• Volume Clamp (Max 60% / 25% ночью)<br/>• NSFW / 18+ фильтр и Whitelist медиа-источников"]
        SecGuard["SecurityGuard (AST + Passive OSINT)<br/>• Локальный AST-анализ скриптов<br/>• Stealth проверка внешних URL/IP"]
    end

    subgraph ExecutionLayer ["4. Исполнение: Доменные Скиллы Помощника vs Песочницы"]
        HostSkills["Typed Host & Assistant Skills (0 токенов)<br/>• Media/Volume & Workspace Profiles (/mode)<br/>• Personal: Второй мозг, Финансы, Подписки, Напоминания<br/>• Dev: Git Repo Radar, Infra/SSL Health, Dev Memory<br/>• Business: Личная CRM, Запись звонков (NAudio)"]
        SandboxEngine["Pluggable Sandbox Engine (ISandboxProvider)<br/>• CliWrap Restricted / Wasmtime / Docker<br/>• Out-of-Band Telemetry Pre-Processor (1GB -> 30KB)"]
    end

    subgraph StorageAndState ["5. Хранилище, Мониторинг ПК и Помощник Подбора Моделей"]
        SQLite[("SQLite WAL + KnowledgeVault (FTS5 + SIMD)<br/>• Второй мозг, Личная CRM, Финансы, Чекпоинты, Секреты (DPAPI)")]
        ResMonitor["ISystemResourceMonitor & IHardwareCapabilitiesProbe<br/>• CPU, Total/Free RAM, GPU VRAM, Активные контейнеры"]
        ModelAdvisor["IModelCompatibilityAdvisor<br/>• Сверяет железо ПК с 15 Local + Curated Cloud моделями"]
    end

    %% Потоки от клиентов
    TG -->|Webhook / Polling| AuthGuard
    UI -->|gRPC / TLS| AuthGuard
    Cron -->|Расписание / Событие| Sanitizer

    AuthGuard -->|/stop или /pause| KillSwitch
    KillSwitch -.->|Abort HTTP & Kill Process| Tier3
    KillSwitch -.->|Abort| SandboxEngine

    AuthGuard -->|Голосовое сообщение| VoiceSTT
    VoiceSTT -->|Текст (0 токенов)| Sanitizer
    AuthGuard -->|Текстовый запрос| Sanitizer
    Sanitizer --> Tier0

    %% Каскад маршрутизации
    Tier0 -->|Совпало (Fast-Path!)| HostGuard
    Tier0 -->|Не совпало| Tier1
    Tier1 -->|Высокий скор / Память / Кэш!| HostGuard
    Tier1 -->|Неизвестная фраза| BudgetGov
    BudgetGov -->|Бюджет ОК| Tier2
    BudgetGov -.->|Превышен порог -> ПАУЗА| TG
    Tier2 -->|Скилл с параметрами| HostGuard
    Tier2 -->|Сложная задача| Tier3

    %% Вызов сложной LLM и проверка безопасности
    Tier3 <-->|Контроль каждого шага| BudgetGov
    Tier3 -->|Tool Proposal| SecGuard
    SecGuard -->|Безопасно| SandboxEngine
    SecGuard -.->|Риск High -> Запрос подтверждения| TG

    %% Выполнение скиллов хоста
    HostGuard -->|Одобрено и ограничено (Volume/NSFW)| HostSkills
    HostGuard -.->|Заблокировано (18+ / Wipe / Лимит)| TG

    %% Связь с хранилищем и монитором
    ResMonitor -.->|Проверка RAM/CPU| SandboxEngine
    ResMonitor -.->|Метрики| UI
    BudgetGov <--> SQLite
    Tier1 <--> SQLite
    HostSkills <--> SQLite
```

---

### Как работают три главных пути запроса:

#### Путь А: Быстрая команда или Голосовая заметка из Telegram (*«Включи бодрую музыку на 40%»*, *«Кофе 350»*, *«/repos»*)
1. **`TelegramAuthGuard` (< 0.1 мс):** Проверяет, что `UserId` в белом списке владельца/семьи. Если пришло голосовое — `Whisper.net` локально переводит его в текст за **0 токенов**.
2. **`Tier 0 / Tier 1 Router` (1–10 мс, 0 токенов):** Мгновенно распознает интент (`Media.Play`, `Finance.AddExpense`, `Dev.GitRadar` или поиск во «Втором мозге») локально без обращения к облачному ИИ.
3. **`Host & Media Safety Guard` (< 1 мс):**
   * Проверяет запрос по локальному **NSFW/18+ фильтру** и применяет **`VolumeClamp`** (макс. `60%` днем / `25%` ночью).
4. **`Typed Host & Assistant Skill` (5–20 мс):** Вызывает нативный API плеера, записывает расход в `SQLite` или сканирует локальные Git-репозитории.
5. **Итог:** Задача решена за **~10–30 миллисекунд**, потрачено **0 токенов (0.00$)**.

---

#### Путь Б: Сложная инженерная или бизнес-задача (*«Запусти тесты, найди причину падения и предложи фикс»* или *«Проверь PDF договора на риски»*)
1. Не попав в `Tier 0` и `Tier 1`, запрос проходит через **`TokenBudgetGovernor`**, который проверяет остаток дневного/часового лимита и выделяет квоту на задачу (например, `6 000 токенов`).
2. **`Tier 2 / Tier 3`** вызывает LLM, передавая только сжатый через `ToolOutputTruncator` вывод (например, только `StackTrace` упавшего теста), топ-3 факта из `KnowledgeVault` и 2–3 нужных инструмента (**Dynamic Tool Pruning**).
3. Если задача съедает 70% выделенного бюджета, **`TokenBudgetGovernor` автоматически ставит задачу на паузу**, сохраняет чекпоинт в SQLite и присылает в Telegram кнопки: `[▶️ Продолжить]` / `[🛑 Стоп]`.
4. Любой предложенный скрипт перед запуском в изолированной песочнице проходит через локальный AST-анализатор **`SecurityGuard`**.

---

#### Путь В: Проактивная работа и Ассистент созвонов (*Утренний брифинг, Контроль серверов, Запись встречи*)
1. **`ProactiveScheduler`** в 09:00 (или при падении рабочего контейнера / за 3 дня до списания подписки) собирает данные из локальной `SQLite` (задачи, CRM follow-ups, коммиты Git, статус серверов) за **0 токенов** и отправляет структурированную сводку в Telegram.
2. Во время рабочего звонка скилл **`MeetingAssistantSkill`** записывает аудиопоток через `NAudio`, локально расшифровывает через `Whisper.net` (**0$**) и формирует список задач (**Action Items**) с кнопкой `[➕ Добавить в трекер]`.

