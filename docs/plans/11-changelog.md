# 11. Лог архитектурных изменений (Changelog ревизии плана)

**Цель ревизии:** Убрать избыточное «энтерпрайз-усложнение», сделать систему **максимально быстрой, прозрачной и надежной**, превратить оркестратор в отзывчивого умного помощника (управление через Telegram и UI за миллисекунды), защитить хост от вайпа и аудио/медиа-саботажа (18+ на полную громкость) и **сократить расход токенов на 1–2 порядка** с жесткими механизмами паузы и остановки.

---

### 1. Удаление лишней сложности (Ускорение в ~100 раз и рост надежности)
* **Было:** Обязательный внешний брокер `RabbitMQ + MassTransit`, СУБД `PostgreSQL + pgvector` и тяжелое сквозное перешифрование каждого сообщения внутри одного процесса (`Envelope Encryption DEK/KEK`). Из-за этого простой запрос проходил десятки сетевых сериализаций, а без поднятых контейнеров система не могла запуститься.
* **Стало:** Сверхбыстрая внутрипроцессная шина на базе **`System.Threading.Channels`** (задержка `< 0.05 мс`) + локальная база **`SQLite в режиме WAL`** с асинхронной записью. Приложение запускается за `< 1 секунду` без единого внешнего контейнера. Шифрование (`DPAPI` / `AES-256-GCM`) оставлено там, где оно реально нужно: для хранения ключей/секретов на диске и чувствительных столбцов БД.

---

### 2. Детерминированный Оркестратор вместо «LLM-бюрократии»
* **Было:** `CEO Agent` и `SecurityAgent` проектировались как отдельные вызовы LLM, общающиеся с `Worker Agent` через саги. Один запрос пользователя вызывал 3–4 обращения к нейросети подряд.
* **Стало:** **`OrchestratorEngine`** и **`SecurityGuard`** переведены на **100% детерминированный C#-код (0 токенов)**. ИИ вызывается только тогда, когда задачу невозможно решить локальными правилами.

---

### 3. Каскадный Маршрутизатор (`4-Tier Intent Router`) — Снижение расхода токенов на порядки
Внедрен 4-уровневый фильтр на входе (`CyberPipe.Router`), отсекающий платные запросы к ИИ:
1. **`Tier 0` (Deterministic Fast-Path, `< 1 мс`, `0 токенов`):** Слэш-команды (`/play`, `/vol 40`, `/stop`) и регулярные шаблоны (*«включи музыку»*, *«пауза»*, *«потише»*) сразу вызывают компилированные C#-скиллы.
2. **`Tier 1` (Local ONNX Semantic Router + Cache, `5–15 мс`, `0 токенов`):** Локальная модель эмбеддингов в памяти (`multilingual-e5-small` + SIMD `TensorPrimitives`) понимает фразы своими словами и с опечатками, сопоставляя их со скиллами или забирая готовый ответ из локального `SemanticResponseCache`.
3. **`Tier 2` (Выбор пользователя в UI: `🏠 Local [0$]` или `☁️ RemoteCloud [$]` + Dynamic Tool Pruning, `~150 мс`):** Быстрый парсинг сложных аргументов легкой локальной моделью (из Curated-списка 15 моделей) или дешевой облачной моделью. В промпт передаются только **2–3 подходящих тула** вместо 50 (экономия ~80% входных токенов).
4. **`Tier 3` (Выбор пользователя в UI: `🏠 Local [0$]` или `☁️ RemoteCloud [$]` + Sandbox):** Включается только для сложных задач (~5–10% запросов) с жесткой обрезкой вывода тулов (`ToolOutputTruncator` до 1 500 символов) и кэшированием системного префикса.

---

### 4. Железная защита ПК при управлении из Telegram (`HostAndMediaSafetyGuard`)
Чтобы через Telegram нельзя было вайпнуть систему или включить порно на полную громкость:
* **Жесткий периметр (`TelegramAuthGuard`):** Белый список числовых `TelegramUserId` с ролями `Owner` (владелец) и `Guest` (гость/семья). Чужие сообщения отбрасываются за `0.1 мс` (0 токенов).
* **Анти-Вайп (`Anti-Wipe & No Raw Shell`):** Команды из чата никогда не попадают в `cmd` / `powershell` хоста — они могут вызывать только закрытый список безопасных C#-скиллов (`MediaControlSkill`, `AudioVolumeSkill`). На уровне AST-фильтра жестко заблокированы любые команды удаления/форматирования (`rm -rf`, `Format-Volume`, `del /s`, реестр).
* **Ограничитель громкости (`Volume Clamp`):** Программный потолок громкости (`Math.Clamp`: например, максимум `60%` днем и `25%` ночью с `23:00` до `08:00`, плюс запрет резкого скачка `> +20%`). Даже если написать *«громкость 100%»*, выше безопасного лимита звук не поднимется.
* **Анти-18+ / NSFW фильтр и Белый список плееров:** Музыка включается только через доверенные источники (`Spotify`, `Яндекс.Музыка`, локальная папка). Поисковый запрос за `< 0.5 мс` проверяется локальным словарем стоп-слов 18+/порно/шок-контента, а произвольные внешние ссылки в браузере со звуком не открываются.

---

### 5. Механизмы Паузы, Остановки и Контроля Расходов (`TokenBudgetGovernor`)
* **Предварительный подсчет и 3 уровня лимитов:** Токены считаются локально (`Microsoft.ML.Tokenizers`) *до* вызова API. Действуют лимиты: на одну задачу (`Soft`/`Hard`), на скорость сжигания в минуту (`Velocity Circuit Breaker`) и дневной потолок в долларах (`DailyBudgetUsdCap`).
* **Автоматическая Пауза (`TaskPausedByBudget`):** Если сложная задача съела 70% своего бюджета или сделала > 3 шагов LLM, она **автоматически ставится на паузу** (состояние сохраняется в SQLite), а в Telegram приходят кнопки: `[▶️ Продолжить (+5k токенов)]`, `[⚡ Выдать что есть]`, `[🛑 Стоп]`.
* **Мгновенный Стоп-Кран (`Priority Kill-Switch`):** Команды `/stop` и `/pause` в Telegram (и кнопка в UI) идут по приоритетному каналу в обход очереди и за `< 50 мс` вызывают `CancellationTokenSource.Cancel()`, мгновенно обрывая HTTP-стрим LLM и убивая дочерние процессы.

---

### 6. Расширение до полноценного Личного, Инженерного и Бизнес-Помощника (`12-assistant-capabilities-and-skills.md`)
* **Системные возможности ядра:**
  * Локальное распознавание голоса (`Whisper.net` ONNX) перед роутером — **0 облачных токенов** на голосовые сообщения из Telegram и записи встреч.
  * Долговременная гибридная память **`KnowledgeVault` (`SQLite FTS5 + SIMD TensorPrimitives`)** для заметок, контактов, финансов и базы решенных ошибок.
  * Проактивный планировщик **`ProactiveScheduler` (`Cronos`)** для утренних/вечерних брифингов, контроля подписок и мониторинга серверов.
* **Личные дела:** инбокс «Второго мозга» с авто-тегами, умные напоминания с кнопкой подтверждения `[✅ Выполнено]`, учет расходов и радар подписок, рабочие профили (`/mode coding`, `/mode call`, `/focus`).
* **Программирование и DevOps:** локальный радар Git-репозиториев (`/repos`, `/standup` за 0 токенов), фоновый тест-раннер с умной выжимкой ошибок (`ToolOutputTruncator`), пре-ревью диффов и мониторинг инфраструктуры/Docker/SSL.
* **Бизнес и коммуникации:** персональная CRM с контролем Follow-up, запись и протоколирование созвонов через `NAudio` + `Whisper.net` (выжимка **Action Items**), проверка договоров (PDF/DOCX) на риски и генерация КП по шаблонам.

---

### 7. Ручной выбор моделей по Tiers (`Local` vs `RemoteCloud`) и Помощник Совместимости с ПК (`13-model-advisor-and-tier-configuration.md`)
* **Явное разделение `Local` и `RemoteCloud`:** Пользователь из Avalonia UI сам назначает желаемую модель на каждый Tier (`Tier 2`, `Tier 3`), при этом и приложение, и пользователь четко видят тип хостинга (`ModelHostingType.Local` — 0$, контроль RAM/VRAM; `ModelHostingType.RemoteCloud` — учет $ в `TokenBudgetGovernor`).
* **Связка двух сервисов (`IHardwareCapabilitiesProbe` + `IModelCompatibilityAdvisor`):** Сервис `IHardwareCapabilitiesProbe` собирает характеристики ПК (Total/Free RAM, CPU AVX2, GPU VRAM) и передает их в класс-помощник `IModelCompatibilityAdvisor`, который рассчитывает совместимость каждой модели (`🟢 Оптимально`, `🟡 Совместимо`, `🔴 Мало памяти`).
* **Curated-каталоги моделей:** Встроен полный каталог из **15 проверенных локальных моделей** (`Qwen3-8B`, `Gemma 3n E2B`, `Llama 3.1 8B`, `Phi-4 Mini`, `Mistral 7B`, `SmolLM3-3B`, `Qwen3-4B`, `DeepSeek-R1 Distill`, `Gemma 3 4B`, `GLM-4-9B`, `Qwen3-0.6B`, `TinyLlama 1.1B`, `Phi-4 Mini Reasoning`, `StableLM Zephyr 3B`, `Qwen3-1.7B`) и **Curated-список платных моделей** для легких и сложных задач.

### 8. 5-Уровневая бронезащита ядра `CyberPipe.exe` от падений и взаимного влияния (`14-fault-isolation-and-resilience.md`)
* **Защита старта и воркеров (`SafeBoot` + `IsolatedWorkerBase`):** Включен `BackgroundServiceExceptionBehavior.Ignore`, запрещена тяжелая логика в конструкторах DI. Все фоновые воркеры обязаны наследоваться от `IsolatedWorkerBase` с автоматическим переводом сбойного модуля в **Карантин** после 3 ошибок подряд без остановки `CyberPipe.exe`.
* **Отсеки живучести (`Bulkhead Lanes`) и защита памяти:** Отдельные пулы семафоров и очередей гарантируют, что зависший фоновый сосед не задержит `Fast-Path` или `/stop`. Полностью запрещены `UnboundedChannel`, введены квоты `QuotaBoundedStream (16 МБ)`, лимит кадра gRPC `4 МБ` и черный ящик `InFlightOperations` в SQLite против «отравленных» сообщений (`Poison-Pill`).
* **Изоляция нативного C++ кода в отдельном стерильном `./worker/CyberPipe.Brainstem.exe`:** Рискованные нативные вызовы (`Whisper`, `LLamaSharp`, сложные PDF) выполняются в отдельном исполняемом файле `CyberPipe.Brainstem.exe` (внутри `Windows Job Object` + `Low Integrity`), который физически не содержит `.dll` и классов демона (защита от `System.Reflection` и `AccessViolationException`).

---

### 9. Архитектура 3-х `.exe` файлов, Режимы `CyberPipe.exe` и Системный CLI / TUI (`15-cli-and-execution-modes.md`)
* **Четкая компоновка 3-х `.exe` по уровням привилегий:**
  1. **`CyberPipe.exe`** — главное ядро (2 режима: Фоновый Демон и тонкий CLI/TUI клиент за `< 15 мс`);
  2. **`CyberPipe.UI.Desktop.exe`** — графический клиент Avalonia;
  3. **`./worker/CyberPipe.Brainstem.exe`** — стерильный изолированный субпроцесс без кода демона.
* **Полноценный системный CLI и TUI (`System.CommandLine` + `Spectre.Console`):** Поддержка команд `doctor`, `setup`, `status`, `triage`, `models scan|list|set`, `infer --explain`, `secrets`, `tasks`, `sandbox`, `memory search`, `cron`, `skills`, интерактивного `tui` и строгого контракта `--json` для автоматизации и CI/CD.

---

### Сводка затронутых файлов документации:
* [`01-principles.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/01-principles.md) — Принципы простоты, 4-Tier роутера (`Local` vs `RemoteCloud`), `IModelCompatibilityAdvisor`, защиты хоста и контроллера бюджета.
* [`02-architecture-diagram.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/02-architecture-diagram.md) — Схема с Fast-Path (< 15 мс), CLI/TUI клиентом, `IHardwareCapabilitiesProbe` ➔ `IModelCompatibilityAdvisor`, `Whisper.net`, `KnowledgeVault` и `TokenBudgetGovernor`.
* [`03-tech-stack-libraries.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md) — Стек с `System.CommandLine` + `Spectre.Console`, `Windows Job Objects` (`CyberPipe.Brainstem.exe`), `QuotaBoundedStream`, `Whisper.net`, `SQLite FTS5`, `LibGit2Sharp` и `Cronos`.
* [`04-agent-mesh-proposals.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/04-agent-mesh-proposals.md) — Детальное проектирование 4-Tier роутера с выбором `Local`/`RemoteCloud` для каждого Tier, авто-паузой бюджета и `/stop`.
* [`05-security-agent.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/05-security-agent.md) — Правила защиты от вайпа ПК, `Volume Clamp` (60%/25%), NSFW/18+ фильтр для Telegram и детерминированный `SecurityGuard`.
* [`06-solution-structure.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/06-solution-structure.md) — Дерево проектов с `CyberPipe.exe` (Демон + CLI), `CyberPipe.UI.Desktop.exe` и стерильным `CyberPipe.Brainstem.exe`.
* [`07-ui-isolation-and-arch-tests.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/07-ui-isolation-and-arch-tests.md) — Конфигуратор моделей по Tiers в UI, дашборд экономии токенов и тесты `ArchUnitNET` (включая проверку `IsolatedWorkerBase` и стерильности `Brainstem`).
* [`08-implementation-roadmap.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md) — План спринтов с запуском `CyberPipe.exe`, `CyberPipe.Brainstem.exe`, 5-уровневой изоляции, CLI, помощника выбора моделей и скиллов.
* [`09-data-protection-and-state-hub.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/09-data-protection-and-state-hub.md) — Локальный `SemanticResponseCache`, `KnowledgeVault`, `PersonalCrm` в SQLite WAL и настройка `TierModels` (`Local`/`RemoteCloud`).
* [`10-sandbox-telemetry-and-heuristics.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/10-sandbox-telemetry-and-heuristics.md) — Инспекция железа `IHardwareCapabilitiesProbe`, защита ПК от тормозов (`Resource Circuit Breaker`) и обрезка логов (`ToolOutputTruncator`).
* [`12-assistant-capabilities-and-skills.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/12-assistant-capabilities-and-skills.md) — Полный каталог системных возможностей и скиллов для личных дел, программирования и бизнеса.
* [`13-model-advisor-and-tier-configuration.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/13-model-advisor-and-tier-configuration.md) — Архитектура `IHardwareCapabilitiesProbe` + `IModelCompatibilityAdvisor`, таблица 15 локальных моделей и Curated-список платных моделей.
* [`14-fault-isolation-and-resilience.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/14-fault-isolation-and-resilience.md) — 5-уровневая защита `CyberPipe.exe` от падений, переполнения буферов, «шумных соседей», отравленных сообщений и вынос натива в `CyberPipe.Brainstem.exe`.
* [`15-cli-and-execution-modes.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md) — Компоновка 3-х `.exe` файлов (`CyberPipe.exe`, `CyberPipe.UI.Desktop.exe`, `worker/CyberPipe.Brainstem.exe`), режимы запуска и полный справочник команд CLI / TUI.
