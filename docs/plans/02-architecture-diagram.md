# 02. Диаграмма взаимодействия и защищенных потоков данных

Архитектура **CyberPipe** построена вокруг **скорости отклика (< 1–15 мс для бытовых задач)**, **жесткой экономии токенов** и **непробиваемой защиты хоста** при управлении через Telegram и Avalonia UI. Вместо тяжеловесных внешних брокеров внутри демона используется сверхбыстрая шина на базе `System.Threading.Channels` и персистентный журнал состояний в `SQLite (WAL)`.

---

### Архитектурная схема: Fast-Path, Защита Хоста и Контроль Токенов

```mermaid
flowchart TD
    subgraph Clients ["Клиенты и Внешние Каналы"]
        TG["Telegram Bot Client<br/>(Команды, Голос/Текст, Кнопки Паузы/Стоп)"]
        UI["Avalonia UI (Desktop / Browser)<br/>(gRPC Stream, Панель управления, Графики)"]
    end

    subgraph Perimeter ["1. Периметр, Авторизация и Стоп-Кран (< 0.5 ms)"]
        AuthGuard["Telegram & API Auth Guard<br/>• Whitelist UserId / ChatId (Owner vs Guest)<br/>• Отсев чужих за 0.1 мс (0 токенов)"]
        KillSwitch["Priority Kill-Switch (/stop, /pause)<br/>• Мгновенный CancellationToken.Cancel()<br/>• Остановка стрима LLM и процессов"]
        Sanitizer["Central Data Sanitizer<br/>• Маскирование ключей, паролей, PII"]
    end

    subgraph FastRouter ["2. Каскадный Маршрутизатор (4-Tier Intent Router)"]
        Tier0["Tier 0: Deterministic Fast-Path (0 токенов, < 1 мс)<br/>• Слэш-команды, Regex, Грамматики ('включи музыку', 'громкость 30')"]
        Tier1["Tier 1: Local ONNX Semantic Router + Cache (0 токенов, ~10 мс)<br/>• Векторный поиск интента (SIMD) + Semantic Response Cache"]
        Tier2["Tier 2: Local Micro-LLM / Flash-Lite Classifier (~150 мс)<br/>• Извлечение JSON-параметров + Dynamic Tool Pruning (2-3 тула)"]
        Tier3["Tier 3: Cloud LLM Agent Loop (Только сложные задачи)<br/>• Кодинг, аналитика, многошаговые задачи"]
    end

    subgraph BudgetAndSafety ["3. Детерминированные Предохранители (C# Код, 0 токенов)"]
        BudgetGov["TokenBudgetGovernor & Circuit Breaker<br/>• Лимит на задачу / час / день<br/>• Авто-Пауза с кнопкой [Продолжить] в Telegram"]
        HostGuard["Host & Media Safety Guard (Anti-Wipe / Anti-Abuse)<br/>• Запрет Raw Shell из чата<br/>• Volume Clamp (Max 60% / 25% ночью)<br/>• NSFW / 18+ фильтр и Whitelist медиа-источников"]
        SecGuard["SecurityGuard (AST + Passive OSINT)<br/>• Локальный AST-анализ скриптов<br/>• Stealth проверка внешних URL/IP"]
    end

    subgraph ExecutionLayer ["4. Исполнение: Безопасные Скиллы Хоста vs Песочницы"]
        HostSkills["Typed Host Skills (Без ИИ и песочниц)<br/>• MediaControlSkill (Play/Pause/Next)<br/>• AudioVolumeSkill (Safe Clamped)<br/>• Timer & SystemStatusSkill"]
        SandboxEngine["Pluggable Sandbox Engine (ISandboxProvider)<br/>• CliWrap Restricted / Wasmtime / Docker<br/>• Out-of-Band Telemetry Pre-Processor (1GB -> 30KB)"]
    end

    subgraph StorageAndState ["5. Локальное Хранилище и Мониторинг"]
        SQLite[("SQLite WAL + Secret Vault (AES-GCM / DPAPI)<br/>• Чекпоинты пауз, Кэш ответов, Бюджеты")]
        ResMonitor["ISystemResourceMonitor (Singleton)<br/>• CPU, RAM, Активные задачи и контейнеры"]
    end

    %% Потоки от клиентов
    TG -->|Webhook / Polling| AuthGuard
    UI -->|gRPC / TLS| AuthGuard

    AuthGuard -->|/stop или /pause| KillSwitch
    KillSwitch -.->|Abort HTTP & Kill Process| Tier3
    KillSwitch -.->|Abort| SandboxEngine

    AuthGuard -->|Обычный запрос| Sanitizer
    Sanitizer --> Tier0

    %% Каскад маршрутизации
    Tier0 -->|Совпало (Fast-Path!)| HostGuard
    Tier0 -->|Не совпало| Tier1
    Tier1 -->|Высокий скор / Кэш!| HostGuard
    Tier1 -->|Неизвестная фраза| BudgetGov
    BudgetGov -->|Бюджет ОК| Tier2
    BudgetGov -.->|Превышен порог -> ПАУЗА| TG
    Tier2 -->|Простой скилл| HostGuard
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
```

---

### Как работают два главных пути запроса:

#### Путь А: Быстрая команда из Telegram (например, *«Включи бодрую музыку на 40%»* или *«Сделай потише»*)
1. **`TelegramAuthGuard` (< 0.1 мс):** Проверяет, что `UserId` в белом списке владельца/семьи.
2. **`Tier 0 / Tier 1 Router` (1–10 мс, 0 токенов):** Мгновенно распознает интент `Media.Play` (или `Audio.SetVolume`) локально без обращения к облачному ИИ.
3. **`Host & Media Safety Guard` (< 1 мс):**
   * Проверяет запрос по локальному **NSFW/18+ фильтру** (отсекает порно/шок-контент и неизвестные сайты, разрешая только доверенные плееры: Spotify / Яндекс.Музыка / локальный плейлист).
   * Применяет **`VolumeClamp`**: если пользователь или гость попросил *«громкость 100%»*, ограничитель жестко срезает значение до безопасного потолка (например, `60%` днем или `25%` ночью).
4. **`Typed Host Skill` (5–20 мс):** Вызывает нативный API плеера/аудиомикшера ОС.
5. **Итог:** Музыка играет через **~20–30 миллисекунд**, потрачено **0 токенов (0.00$)**, риск вайпа системы или запуска 18+ контента — **нулевой**.

---

#### Путь Б: Сложная агентная задача (например, *«Напиши скрипт анализа логов и построй отчет»*)
1. Не попав в `Tier 0` и `Tier 1`, запрос проходит через **`TokenBudgetGovernor`**, который проверяет остаток дневного/часового лимита и выделяет квоту на задачу (например, `6 000 токенов`).
2. **`Tier 2 / Tier 3`** вызывает LLM, передавая только сжатый контекст и 2–3 нужных инструмента (**Dynamic Tool Pruning**, экономия до 80% входных токенов).
3. Если задача затягивается и съедает 70% выделенного бюджета задачи, **`TokenBudgetGovernor` автоматически ставит задачу на паузу**, сохраняет чекпоинт в SQLite и присылает в Telegram кнопки: `[▶️ Продолжить]` / `[🛑 Стоп]`.
4. Любой предложенный скрипт перед запуском в изолированной песочнице проходит через локальный AST-анализатор **`SecurityGuard`**.
