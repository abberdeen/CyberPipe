# 11. Лог архитектурных изменений (Changelog ревизии плана)
> [!NOTE]
> **Статус записей (AUD-01, AUD-15):** ниже — **спроектированные** решения, а не реализованные. Кода пока нет
> (`CyberPipe.slnx` пуст); ни одна запись не утверждает наличие несуществующего кода.

**Цель ревизии:** Убрать избыточное «энтерпрайз-усложнение», сделать систему **максимально быстрой, прозрачной и надежной**, превратить оркестратор в отзывчивого умного помощника (управление через Telegram и UI за миллисекунды), защитить хост от вайпа и аудио/медиа-саботажа (18+ на полную громкость) и **сократить расход токенов на 1–2 порядка** с жесткими механизмами паузы и остановки.

---

### 1. Удаление лишней сложности (проектная цель; требует подтверждения бенчмарком)
* **Было:** Обязательный внешний брокер `RabbitMQ + MassTransit`, СУБД `PostgreSQL + pgvector` и тяжелое сквозное перешифрование каждого сообщения внутри одного процесса (`Envelope Encryption DEK/KEK`). Из-за этого простой запрос проходил десятки сетевых сериализаций, а без поднятых контейнеров система не могла запуститься.
* **Спроектировано:** Сверхбыстрая внутрипроцессная шина на базе **`MassTransit (транспорт InMemory)`** (задержка `< 0.05 мс`) + локальная база **`SQLite в режиме WAL`** с асинхронной записью. Приложение запускается за `< 1 секунду` без единого внешнего контейнера. Шифрование (`DPAPI` / `AES-256-GCM`) оставлено там, где оно реально нужно: для хранения ключей/секретов на диске и чувствительных столбцов БД.

---

### 2. Детерминированный Оркестратор вместо «LLM-бюрократии»
* **Было:** `CEO Agent` и `SecurityAgent` проектировались как отдельные вызовы LLM, общающиеся с `Worker Agent` через саги. Один запрос пользователя вызывал 3–4 обращения к нейросети подряд.
* **Спроектировано:** **`OrchestratorEngine`** и **`SecurityGuard`** по проекту переводятся на **100% детерминированный C#-код (0 токенов)**. ИИ вызывается только тогда, когда задачу невозможно решить локальными правилами.

---

### 3. Каскадный Маршрутизатор (`4-Tier Intent Router`) — Снижение расхода токенов на порядки
Спроектирован 4-уровневый фильтр на входе (`CyberPipe.Router`), отсекающий платные запросы к ИИ:
1. **`Tier 0` (Deterministic Fast-Path, `< 1 мс`, `0 токенов`):** Слэш-команды (`/play`, `/vol 40`, `/stop`) и регулярные шаблоны (*«включи музыку»*, *«пауза»*, *«потише»*) сразу вызывают компилированные C#-скиллы.
2. **`Tier 1` (Local ONNX Semantic Router + Cache, `5–15 мс`, `0 токенов`):** Локальная модель эмбеддингов в памяти (`multilingual-e5-small` + SIMD `TensorPrimitives`) понимает фразы своими словами и с опечатками, сопоставляя их со скиллами или забирая готовый ответ из локального `SemanticResponseCache`.
3. **`Tier 2` (Выбор пользователя в UI: `🏠 Local [0$]` через Ollama/LM Studio или `☁️ RemoteCloud [$]` + Dynamic Tool Pruning, `~150–300 мс` на GPU/Cloud и `~800–2000 мс` на CPU):** Быстрый парсинг сложных аргументов легкой локальной моделью (из Curated-списка 15 моделей) или дешевой облачной моделью. В промпт передаются только **2–3 подходящих тула** вместо 50 (экономия ~80% входных токенов) и только доверенные записи из памяти (`TrustSource <= ToolOutput`).
4. **`Tier 3` (Выбор пользователя в UI: `🏠 Local [0$]` или `☁️ RemoteCloud [$]` + Sandbox):** Включается только для сложных задач (~5–10% запросов) с жесткой обрезкой вывода тулов (`ToolOutputTruncator` до 1 500 символов), физическим вырезанием хостовых тулов при недоверенном контексте (`Hard Tool Stripping`) и кэшированием системного префикса.

---

### 4. Железная защита ПК при управлении из Telegram (`HostAndMediaSafetyGuard`)
Чтобы через Telegram нельзя было вайпнуть систему или включить порно на полную громкость:
* **Жесткий периметр (`TelegramAuthGuard`):** Белый список числовых `TelegramUserId` с ролями `Owner` (владелец) и `Guest` (гость/семья). Чужие сообщения отбрасываются за `0.1 мс` (0 токенов).
* **Анти-Вайп (`Anti-Wipe & No Raw Shell`):** Команды из чата никогда не попадают в `cmd` / `powershell` / `bash` хоста — они могут вызывать только закрытый список безопасных C#-скиллов (`MediaControlSkill`, `AudioVolumeSkill`). Сгенерированные в `Tier 3` скрипты выполняются только в реальных песочницах (`Wasmtime`, `Docker`, `AppContainer`/`bwrap`/`Seatbelt`), а нативные утилиты вне песочниц требуют подтверждения владельца (`RequiresApproval`).
* **Ограничитель громкости (`Volume Clamp` + `EnsureSafeVolumeBeforePlayback`):** Программный потолок громкости (`Math.Clamp`: максимум `60%` днем и `25%` ночью с `23:00` до `08:00`, запрет резкого скачка `> +20%` и принудительное снижение текущей системной громкости перед стартом `Play`, если ранее она была выкручена вручную выше лимита).
* **Анти-18+ / NSFW фильтр и Белый список плееров:** Музыка включается только через доверенные аудио-источники (`Spotify`, `Яндекс.Музыка`, локальная папка) с флагом семейного режима в API, DNS `1.1.1.3` и нормализацией гомоглифов/стоп-слов 18+.

---

### 5. Механизмы Паузы, Остановки и Контроля Расходов (`TokenBudgetGovernor`)
* **Предварительный подсчет, `MaxOutputTokens` и 3 уровня лимитов:** Токены оцениваются локально (`Microsoft.ML.Tokenizers` с коэффициентом `1.2x` для кириллицы в не-OpenAI моделях) *до* вызова API и точно списываются по `UsageDetails`, а на каждом шаге принудительно выставляется `ChatOptions.MaxOutputTokens` против взрыва стоимости reasoning-моделей. Действуют лимиты: на одну задачу (`SoftTaskTokenLimit = 4 000`, `HardTaskTokenLimit = 12 000`, `MaxLlmTurnsPerTask = 3`), на скорость сжигания в минуту (`MaxTokensPerMinute = 15 000`) и дневной потолок в долларах (`DailyBudgetUsdCap`).
* **Автоматическая Пауза (`TaskLifecycleState.PausedByBudget`):** При достижении `4 000 токенов` или `3 шагов` LLM задача **автоматически ставится на паузу** (состояние сохраняется в SQLite), а в Telegram приходят кнопки: `[▶️ Разрешить еще +5 000 токенов]` (добавляет `+5 000 токенов` и `+3 шага`), `[⚡ Выдать что есть]`, `[🛑 Стоп]`.
* **Мгновенный Стоп-Кран (`Priority Kill-Switch`):** Команды `/stop` и `/pause` в Telegram (и кнопка в UI) идут по приоритетному каналу в обход очереди и за `< 50 мс` вызывают `CancellationTokenSource.Cancel()`, мгновенно обрывая HTTP-стрим LLM и останавливая дочерние процессы.

---

### 6. Расширение до полноценного Личного, Инженерного и Бизнес-Помощника (`12-assistant-capabilities-and-skills.md`)
* **Системные возможности ядра:**
  * Локальное распознавание голоса (`Whisper.net` ONNX в прогретом пуле `Brainstem`) перед роутером — **0 облачных токенов** на голосовые сообщения из Telegram и записи встреч.
  * Долговременная гибридная память **`KnowledgeVault` (`SQLite FTS5 + In-Memory SIMD TensorPrimitives`)** со столбцом `TrustSource` для заметок, контактов, финансов и базы решенных ошибок.
  * Проактивный планировщик **`ProactiveScheduler` (`Cronos`)** для утренних/вечерние брифингов, контроля подписок и мониторинга серверов.
* **Личные дела:** инбокс «Второго мозга» с авто-тегами, умные напоминания с кнопкой подтверждения `[✅ Выполнено]`, учет расходов и радар подписок, рабочие профили (`/mode coding`, `/mode call`, `/focus`).
* **Программирование и DevOps:** локальный радар Git-репозиториев (`/repos`, `/standup` за 0 токенов через `LibGit2Sharp`), фоновый тест-раннер через `IDevToolProcessRunner` с умной выжимкой ошибок (`ToolOutputTruncator`), пре-ревью диффов и мониторинг инфраструктуры/Docker/SSL.
* **Бизнес и коммуникации:** персональная CRM с контролем Follow-up, запись и протоколирование созвонов через `IAudioDeviceController` (`NAudio`) + `Whisper.net` (выжимка **Action Items**), проверка договоров (PDF/DOCX в одноразовом процессе `Brainstem`) на риски и генерация КП по шаблонам.

---

### 7. Ручной выбор моделей по Tiers (`Local` vs `RemoteCloud`) и Помощник Совместимости с ПК (`13-model-advisor-and-tier-configuration.md`)
* **Явное разделение `Local` и `RemoteCloud`:** Пользователь из Avalonia UI сам назначает желаемую модель на каждый Tier (`Tier 2`, `Tier 3`), при этом локальные модели работают через внешний сервер `Ollama / LM Studio` (`0$`, контроль RAM/VRAM с учетом `KV-Cache`), а облачные — под защитой `TokenBudgetGovernor` и выходного `CentralDataSanitizer`.
* **Связка двух сервисов (`IHardwareCapabilitiesProbe` + `IModelCompatibilityAdvisor`):** Контракт `IHardwareCapabilitiesProbe` (`CyberPipe.Contracts.Hardware`, реализован в `CyberPipe.Security.Telemetry`) собирает характеристики ПК (Total/Free RAM, CPU AVX2, GPU VRAM) и передает их в `IModelCompatibilityAdvisor`, который рассчитывает совместимость каждой модели (`🟢 Оптимально`, `🟡 Совместимо`, `🔴 Мало памяти`).
* **Curated-каталоги моделей:** Встроен полный каталог из **15 проверенных локальных моделей** и **Curated-список платных моделей** для легких и сложных задач.

### 8. Внутрипроцессная устойчивость `CyberPipe` к сбоям и контроль буферов (`14-fault-isolation-and-resilience.md`)
* **Контроль старта и воркеров (`SafeBoot` + `IsolatedWorkerBase`):** Включен `BackgroundServiceExceptionBehavior.Ignore`, запрещен блокирующий I/O в конструкторах DI. Все фоновые воркеры наследуются от `CyberPipe.Security.Telemetry.Resilience.IsolatedWorkerBase` (без циклических ссылок между проектами) с автоматическим переводом сбойного модуля в **Карантин** после 3 последовательных ошибок.
* **Отсеки (`Bulkhead Lanes`), квоты потоков и защита от `Poison-Pill`:** Раздельные пулы семафоров и очередей изолируют подсистемы друг от друга. Запрещены `UnboundedChannel`, введены квоты `QuotaBoundedStream (16 МБ)`, лимит кадра gRPC `4 МБ` и журнал `InFlightOperations` в SQLite для тяжелых и внешних задач (с корректной очисткой при `OperationCanceledException`).

---

### 9. Межпроцессная изоляция (`CyberPipe.Brainstem`), Безопасность IPC и Системный CLI / TUI (`15-cli-and-execution-modes.md`)
* **Три точки входа и кроссплатформенная модель изоляции ОС (`IBrainstemIsolationPlatform`):** Основной хост/CLI (`CyberPipe`), десктопный клиент (`CyberPipe.UI.Desktop`) и изолированный воркер (`./worker/CyberPipe.Brainstem`) для `Whisper.net` и парсеров `PdfPig`/`OpenXml` (в раздельных пулах: прогретый `Stateful Voice STT Pool` и одноразовый `SingleUse Document Parser Pool`). На Windows защита файловой системы `data\` от `Brainstem` обеспечивается через **`AppContainer`** (Профиль А) или **`CreateRestrictedToken` с `SidsToRestrict` + явный `ACCESS_DENIED_ACE` в DACL папки `data\`** (Профиль Б) и `Process SACL NO_READ_UP` + `Windows Job Object`; на Linux — через `Bubblewrap (bwrap)` + `cgroups v2`; на macOS — через `Seatbelt (sandbox-exec)`. Спроектирована проверка всех `.exe`/`.dll` по `worker.manifest.json` и тесты `CyberPipe.Tests.Isolation` с первого спринта.
* **Системный CLI и TUI (`System.CommandLine` + `Spectre.Console`):** Матрица команд с разделением на `Autonomous (Offline)`, `Daemon-Required (IPC)` и `Hybrid`, регистрация демона в интерактивной пользовательской сессии (`Session >= 1`), коды возврата (`ExitCode 0..4`) и строгий контракт `--json`.

### 10. Переход на `Hangfire`, отдельная защищенная паролем SQLite БД и безопасный Read-Only Dashboard
* **Замена Cronos на `Hangfire`:** Фоновые и проактивные задачи переведены на промышленный планировщик `Hangfire` (`Hangfire.Core`, `Hangfire.AspNetCore`, `Hangfire.Storage.SQLite`).
* **Отдельная защищенная паролем база данных (`hangfire.db`):** Для изоляции задач от центрального мастера `cyberpipe_master.db` создается отдельный файл `%LOCALAPPDATA%\CyberPipe\data\subsystems\hangfire.db`, защищенный паролем/шифрованием страниц (ключ генерируется и хранится в системном сейфе `SecretsVault` под DPAPI/LibSecret/Keychain) и защищенный от воркера `Brainstem` на уровне файловой системы ОС (`ACCESS_DENIED_ACE` на Windows, `bwrap` unmount на Linux, `Seatbelt` deny на macOS).
* **Безопасная архитектура Hangfire Dashboard:**
  * Веб-панель слушает строго `127.0.0.1` (привязка к `0.0.0.0` запрещена) в режиме **только для чтения (`IsReadOnlyFunc = _ => true`)**.
  * **Handshake с одноразовым токеном:** При нажатии кнопки в Desktop UI генерируется криптографически случайный одноразовый секрет (OTT, 256 бит, TTL 30 сек). На сервере хранится только его SHA-256 хеш.
  * **Однократное сжигание секрета:** При переходе браузера на `/dashboard-login` локальный endpoint проверяет Loopback, валидирует хеш, немедленно удаляет OTT из памяти (сжигание) и устанавливает сессионную cookie `CyberPipe.Dashboard` с флагами `HttpOnly` и `SameSite=Strict`.
  * `DashboardAuthorizationFilter` валидирует сессию в `DashboardSessionManager`, предотвращая несанкционированный доступ извне или повторное использование URL.

### 11. Переход на `MassTransit`, Выбор Провайдера в UI, Отдельная SQLite БД (`masstransit.db`) и RabbitMQ
* **Универсальная шина `MassTransit` вместо Channels:** Для шины межсервисных сообщений спроектирован `MassTransit` (`MassTransit`, `MassTransit.RabbitMQ`, `MassTransit.SqlTransport.Sqlite`), исключающий привязку к конкретному брокеру.
* **Три переключаемых транспорта:**
  1. **`InMemory` (По умолчанию):** внешний брокер отключен, работает без посредников и персистентной памяти (zero-dependency).
  2. **`SQLite WAL` (`data/subsystems/masstransit.db`):** отдельная изолированная база данных в режиме WAL, используемая **только MassTransit и никем другим**, защищенная паролем в `SecretsVault`.
  3. **`RabbitMQ`:** внешний брокер AMQP 5672 с супервизией и автозапуском через Центр, автогенерацией `cyberpipe_admin` (права строго для себя) и сохранением паролей в `SecretsVault`.
* **Управление провайдером через UI:** На панели инфраструктуры Avalonia Desktop UI пользователь выбирает активный транспорт (`InMemory` / `SQLite WAL` / `RabbitMQ`), проверяет подключение кнопкой `[🔄 Проверить подключение]`, запускает ноду через `[🚀 Запустить через Центр]` и видит живой статус из `GlobalStateHub`.
* **Персистентность в SQLite и GlobalStateHub:** Статусы и ошибки всех проверок сохраняются в `InfrastructureHealthRecords` в `data/subsystems/health.db`.

---

### 12. Архитектура баз данных: Центральный Мастер SQLite + Изолированные БД подсистем (`ISubsystemDatabaseManager`)
* **Центральный Мастер SQLite (`cyberpipe_master.db`):**
  - Хранит **исключительно общие базовые настройки приложения** (глобальные конфигурации, базовый профиль, флаг выбранного транспорта шины).
  - Содержит таблицу-каталог `SubsystemDatabaseRegistry` (пары: `SubsystemKey` + `DatabasePath` + `SecretVaultKeyRef` + статус шифрования и миграций).
  - В центральной базе **нет и никогда не сохраняются** бизнес-данные подсистем.
* **Изолированные БД для каждой подсистемы (`data/subsystems/*.db`):**
  - Каждая подсистема/сервис имеет собственный файл SQLite в режиме `WAL`: `hangfire.db` (планировщик), `masstransit.db` (только для MassTransit), `knowledge.db` (память «Второй мозг», FTS5, SIMD векторы), `crm.db` (личная CRM и контакты), `finance.db` (учет финансов), `budget.db` (токены, квоты, чекпоинты задач), `health.db` (аудит инфраструктуры).
* **Раздельные доступы и индивидуальные пароли:**
  - Каждая база данных защищена собственным уникальным криптографическим паролем (`Password=...`).
  - Все пароли генерируются криптографическим CSP-генератором (`RandomNumberGenerator.GetBytes(32)`) и персистентно хранятся в платформенном сейфе `SecretsVault` (Windows DPAPI / Linux LibSecret / macOS Keychain) под уникальными ключами `db:secret:{SubsystemKey}`.
* **Специализированный сервис управления и доступа (`ISubsystemDatabaseManager`):**
  - Выделенный сервис `SubsystemDatabaseManagerService` отвечает за:
    1. Lazy-обнаружение и авто-создание баз данных при первом обращении (`ProvisionSubsystemDatabaseAsync`).
    2. Генерацию и сохранение паролей в `SecretsVault`.
    3. Регистрацию записей в мастер-таблице `SubsystemDatabaseRegistry`.
    4. Предоставление защищенного экземпляра `SqliteConnection` (`GetConnectionAsync(subsystemKey)`).
    5. Автоматическое применение миграций схемы конкретной подсистемы (`MigrateSubsystemDatabaseAsync`).
  - Прямой междоменный доступ (когда сервис лезет напрямую в чужой `.db` файл) категорически запрещен архитектурными контрактами.

---

### 13. Ассистент Netrunner: Движок Каскадных Задач (Mini-n8n DAG), HITL и Стандарт Пакетов (`16-netrunner-etl-and-workflow-engine.md`)
* **Концепция Netrunner:** Выделен отдельный автономный ассистент и раздел приложения для **радикальной экономии токенов и построения каскадных пайплайнов (`Mini-n8n`)**. Вместо ежедневного скармливания нейросети тысяч токенов на рутинные запросы (курсы валют, парсинг страниц, выгрузка API), ИИ привлекается один раз для синтеза детерминированного ETL-пакета (или пользователь пишет его вручную).
* **Стандартизированный формат пакета скрипта (6 обязательных файлов + проектный Skill):**
  - Введен жесткий контракт пакета `data/netrunner-packages/<slug>/<version>/`, проверяемый `INetrunnerPackageValidator` и задокументированный в [`.agents/skills/netrunner-script-package/SKILL.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/.agents/skills/netrunner-script-package/SKILL.md):
    1. `netrunner.manifest.json` (аналог `package.json`: описывает входные/выходные форматы, файловые контракты `inputFiles`/`outputFiles`, права песочницы и SHA-256 хеши всех файлов).
    2. `SKILL.md` (семантический паспорт и фразы-триггеры для роутера `Tier 1`).
    3. `schema.input.json` (JSON Schema Draft 2020-12 для валидации входа до старта).
    4. `schema.output.json` (JSON Schema Draft 2020-12 для валидации выхода и проверки совместимости соседних узлов графа).
    5. `main.csx` / `main.py` / `main.js` (точка входа детерминированного скрипта).
    6. `tests/sample.fixture.json` (обязательный эталонный самотест `Dry-Run` перед сохранением в базу).
* **Каскадный движок `Netrunner DAG Workflow Engine` (Mini-n8n):**
  - Поддержка последовательных, условных и параллельных цепочек из 8 типов узлов (`TriggerNode`, `SkillNode`, `NetrunnerScriptNode`, `ConditionNode` / `SwitchNode`, `ParallelFanOutNode` + `JoinNode`, `LlmProcessNode`, `HitlApprovalNode`, `DeliveryNode`) и **4 типов ветвлений** (`If/Else & Switch`, параллельный `FanOut/Join`, ветки решения человека `approved/rejected/timeout` и ветка перехвата ошибок `onError`).
  - Поддержка умного ожидания обновления параметров и отложенных ретраев без удержания ОЗУ (`DeferRetry` из скрипта и декларативный `retryPolicy.untilExpression` на узле).
* **Глобальное правило поведения при аварийном сбое (`Pipeline:CrashRecoveryPolicy` в `cyberpipe_master.db`):**
  - В общих базовых настройках приложения (`AppGlobalSettings` в центральной БД `cyberpipe_master.db`, управляемой через главный экран Settings в UI) добавлен переключатель поведения пайплайнов после отключения света или падения процесса:
    1. **`ResumeFromCheckpoint` (Продолжать с места падения):** автоматически продолжает пайплайн с прерванного шага, сохраняя все уже пройденные шаги.
    2. **`StopAndMarkCancelledByCrash` (Остановить и оставить как есть):** не перезапускает прерванный пайплайн, оставляет все выполненные шаги и их артефакты нетронутыми и присваивает запуску и прерванному шагу специальный статус **`CancelledByCrash`** (*«Отменен из-за аварийного сбоя»*).
* **Изолированная база `data/subsystems/netrunner.db`:** Хранит таблицы `NetrunnerPackages`, `NetrunnerWorkflowDefinitions`, `NetrunnerWorkflowRuns` и `NetrunnerStepRuns` с пошаговыми чекпоинтами для идемпотентного продолжения после перезагрузки ПК или ожидания `HITL`.

---

### Сводка затронутых файлов документации:
* [`01-principles.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/01-principles.md) — Принципы простоты, кроссплатформенных контрактов ОС, 4-Tier роутера, защиты хоста, Hangfire, мастер-каталога БД и контроллера бюджета.
* [`02-architecture-diagram.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/02-architecture-diagram.md) — Схема с Fast-Path, выходным `CentralDataSanitizer`, CLI/TUI клиентом, `Hangfire`, `Netrunner ETL Engine`, `ISubsystemDatabaseManager` (`cyberpipe_master.db` + `data/subsystems/*.db`).
* [`03-tech-stack-libraries.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md) — Стек с `MassTransit`, `ISubsystemDatabaseManager`, `OllamaSharp` (Local HTTP), `IBrainstemIsolationPlatform`, `QuotaBoundedStream`, `Whisper.net`, `LibGit2Sharp` и `Hangfire` с защищенным Read-Only Dashboard.
* [`04-agent-mesh-proposals.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/04-agent-mesh-proposals.md) — Детальное проектирование 4-Tier роутера, ролевого кэша, `MaxOutputTokens`, авто-паузы `PausedByBudget` и `/stop`.
* [`05-security-agent.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/05-security-agent.md) — Правила защиты от вайпа ПК, `Volume Clamp` (с проверкой перед `Play`), NSFW-фильтр, `Hard Tool Stripping` для `UntrustedExternal` и `Fail-Closed` OSINT.
* [`06-solution-structure.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/06-solution-structure.md) — Ациклический граф проектов (DAG) с базовым слоем `CyberPipe.Contracts`, `CyberPipe.Security.Telemetry` и `CyberPipe.Skills.Netrunner`.
* [`07-ui-isolation-and-arch-tests.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/07-ui-isolation-and-arch-tests.md) — Конфигуратор моделей по Tiers в UI, панель шины MassTransit, планировщик задач, конструктор каскадов Netrunner и тесты `ArchUnitNET`.
* [`08-implementation-roadmap.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md) — Сбалансированный план спринтов с `Storage.Sqlite` и `Tests.Isolation` в Спринте 1 и критериями приемки (DoD).
* [`09-data-protection-and-state-hub.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/09-data-protection-and-state-hub.md) — Выходной `Sanitizer`, персистентный `SecretsVault`, центральный мастер `cyberpipe_master.db` + изолированные БД подсистем (`ISubsystemDatabaseManager`), `GlobalStateHub`.
* [`10-sandbox-telemetry-and-heuristics.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/10-sandbox-telemetry-and-heuristics.md) — Инспекция железа `IHardwareCapabilitiesProbe`, защита ПК от тормозов (`Resource Circuit Breaker`) и обрезка логов (`ToolOutputTruncator`).
* [`12-assistant-capabilities-and-skills.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/12-assistant-capabilities-and-skills.md) — Полный каталог из 16+ системных и прикладных скиллов для личных дел, программирования, бизнеса и автоматизации Netrunner.
* [`13-model-advisor-and-tier-configuration.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/13-model-advisor-and-tier-configuration.md) — Архитектура `IHardwareCapabilitiesProbe` + `IModelCompatibilityAdvisor` (с учетом `KV-Cache`), таблица 15 локальных и 8 облачных моделей.
* [`14-fault-isolation-and-resilience.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/14-fault-isolation-and-resilience.md) — Внутрипроцессные контуры устойчивости `CyberPipe` (`SafeBoot`, `IsolatedWorkerBase`, `Bulkhead Lanes`, `QuotaBoundedStream`, `InFlightOperations`).
* [`15-cli-and-execution-modes.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md) — Единая спецификация исполняемых файлов, кроссплатформенной изоляции `Brainstem`, раздельных пулов воркеров, защиты IPC, `worker.manifest.json`, тестов изоляции в CI и матрицы команд CLI / TUI.
* [`16-netrunner-etl-and-workflow-engine.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/16-netrunner-etl-and-workflow-engine.md) — Спецификация ассистента Netrunner: стандарт пакетов скриптов (`netrunner.manifest.json` + 6 обязательных файлов), каскадный движок `Mini-n8n DAG`, узлы `LlmProcessNode` и `HitlApprovalNode`, схема БД `netrunner.db`.

