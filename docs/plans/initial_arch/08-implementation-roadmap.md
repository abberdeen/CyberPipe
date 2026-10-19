> **Решения владельца 2026-10-19 ([DECISIONS.md](../DECISIONS.md), [17-adr-mvp.md](17-adr-mvp.md)):** хранение - одна PostgreSQL + pgvector (PostgreSQL/шифрование на уровне PostgreSQL/диска убраны); цепочки только Trigger -> Skill -> Script -> Delivery (термины цепочка/цепочка/workflow/граф удалены); Telegram - первый канал, не единственный; Service и Desktop - разные приложения. При расхождении верны DECISIONS.md и 17-adr-mvp.md.

# 08. План поэтапной реализации CyberPipe (От быстрого ядра к полному функционалу)

План перестроен по принципу **«Сначала быстрый, безопасный и рабочий помощник без лишних зависимостей»**. Уже по итогам 1-го и 2-го спринтов система умеет мгновенно выполнять команды из Telegram (музыка, громкость, статус) за 0 токенов, защищена от вайпа и 18+ контента и жестко контролирует расходы.

---

### Фаза 0: ADR, реестр БД и проверочные PoC (предусловие всего, AUD-11, раздел K.1)
*Цель: закрыть фундаментные решения до написания кода спринтов 1–5. Старт Фазы 1 только после приемки Фазы 0.*

- [ ] **ADR-001 — шина (`AUD-02`, `AUD-10`):** канонична `собственная абстракция сообщений`; MVP — `транспорт (реализация не фиксируется)` по умолчанию, `PostgreSQL`
  (`data/subsystems/собственная абстракция сообщений` (PostgreSQL)) опционально, `MessageBroker` — enterprise-опция за отдельным ADR. Идемпотентность
  потребителей (`MessageDeduplicationId`). Проверка: одно имя шины во всех документах + интеграционный тест доставки и повторов.
- [ ] **ADR-002 — реестр БД (`AUD-03`):** канонично `data/единая база PostgreSQL` (только настройки и `SubsystemDatabaseRegistry`)
  + `таблицы единой PostgreSQL`. Проверка: тест создает все БД по реестру на чистой машине.
- [ ] **PoC шифрования PostgreSQL (`AUD-04`):** шифрование на уровне PostgreSQL/диска-провайдер; открытие верным ключом успешно, неверным — падает
  с ошибкой. До PoC гарантия шифрования считается недоказанной.
- [ ] **PoC изоляции Brainstem на Windows (`AUD-07`, `AUD-13`):** негативные тесты раздела G (чтение чужой БД,
  `OpenProcess` к демону, запуск запрещенного инструмента) завершаются отказом в доступе.
- [ ] **Ключ кэша (`AUD-05`):** `(QueryHash, UserRole, DataVersion, ExpiresAtUtc)`; тест двух разных вопросов одной роли
  подтверждает отсутствие коллизий.
- [ ] **Методика SLO (`AUD-06`):** локальные бюджеты vs сквозной best-effort путь через Telegram; эталонное железо и `p95/p99`.
- **Критерий приемки (DoD Фазы 0):** ADR приняты, реестр и PoC-тесты зеленые. Без этого Фазы 1–5 не стартуют.

### Фаза 1 (старт только после закрытия Фазы 0): Ядро Хоста, Базовый PostgreSQL, Межпроцессная Изоляция, Периметр Telegram и Скиллы Хоста (Спринт 1)
*Цель: Запустить ядро `CyberPipe`, базовый слой `CyberPipe.Storage.Postgres`, изолированный воркер `CyberPipe.Brainstem`, верификацию изоляции ОС и безопасное управление медиа/звуком из Telegram за 0 токенов.*

- [ ] Инициализация Solution (`CyberPipe.slnx`) на .NET 10 с тремя точками входа (`CyberPipe`, `CyberPipe.UI.Desktop`, `./worker/CyberPipe.Brainstem`, см. [`15-cli-and-execution-modes.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md)) и строгим ациклическим графом проектов (цепочка).
- [ ] Разработка базового слоя **`CyberPipe.Storage.Postgres`** (режим WAL + последовательный раннер миграций `PostgreSQLMigrationRunner` на базе `PRAGMA user_version` с авто-бэкапом `PostgreSQLBackupService` + асинхронный `Write-Behind`):
  - Базовые таблицы Спринта 1: `InFlightOperations`, `FinanceLedger`, `Notes` и персистентный `SecretsVault` (`ISecretVaultPlatform`: `DPAPI` / `LibSecret` / `Keychain` + `AES-256-GCM` + `ZeroMemory`).
- [ ] Реализация диспетчера запуска в `CyberPipe` (`DaemonHostMode` + `CliClientMode`) и защищенного транспорта Named Pipes / Unix Domain Sockets (верификация `PID`, `SessionNonce`, `PipeSecurity`).
- [ ] Реализация внутрипроцессной и межпроцессной устойчивости (см. [`14-fault-isolation-and-resilience.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/14-fault-isolation-and-resilience.md) и [`15-cli-and-execution-modes.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md)):
  - Настройка `BackgroundServiceExceptionBehavior.Ignore`, `SafeModuleRegistration` и `IsolatedWorkerBase` в базовом слое `CyberPipe.Security.Telemetry` (с авто-карантином после 3 ошибок).
  - Разделение ресурсов на **Bulkhead-отсеки**, использование только `BoundedChannel`, потоковая квота `QuotaBoundedStream (16 МБ)` и журнал `InFlightOperations` для тяжелых/внешних входов с очисткой при `OperationCanceledException` (`/stop`).
  - Разработка `BrainstemProcessSupervisor` и `IBrainstemIsolationPlatform`: запуск `CyberPipe.Brainstem` с изоляцией файловой системы (`AppContainer` или `CreateRestrictedToken` с `SidsToRestrict` + явный `ACCESS_DENIED_ACE` в DACL папки `data\` в Windows; `bwrap` в Linux; `Seatbelt` в macOS), `Process SACL NO_READ_UP`, `Job Object` (`ActiveProcessLimit = 1`, лимит памяти и CPU, `KillOnJobClose`), изолированным `TEMP` и проверкой SHA-256 по `worker.manifest.json`.
- [ ] Разработка базовых команд системного **CLI (`CyberPipe <command>`)** для ядра Спринта 1 (`daemon`, `status`, `doctor`, `config`, `secrets`, `tasks stop-all`, `setup`) с поддержкой строгого `--json` контракта и автозапуска в интерактивной сессии пользователя (`Session >= 1`).
- [ ] Интеграция универсальной шины **`собственная абстракция сообщений`** (транспорты `транспорт (реализация не фиксируется)` [по умолчанию], `PostgreSQL` [`data/subsystems/собственная абстракция сообщений` (PostgreSQL), пароль в Vault], `MessageBroker`), разработка **`MessageBrokerSupervisionService`** и **`GlobalStateHub`**:
  - Реализация фабрики провайдеров транспорта с возможностью переключения из UI;
  - Проверка доступности портов 5672 (AMQP) и 15672 (Management UI) при выборе MessageBroker;
  - Автоматический запуск MessageBroker через Центр Инфраструктуры (`ICenterInfrastructureSupervisor`) при отключенном брокере;
  - Проверка наличия логина/пароля в `SecretsVault`. При отсутствии — автогенерация админа `cyberpipe_admin`, установка прав только для себя (`MessageBrokerctl set_permissions`) и сохранение в `secrets.vault`;
  - Запись истории проверок в таблицу `InfrastructureHealthRecords` (`таблицы единой PostgreSQL`) и реактивная доставка статуса через `GlobalStateHub`.
- [ ] Разработка `CyberPipe.Gateway.Telegram` (`TelegramAuthGuard`: Whitelist `UserId`, разделение `Owner`).
- [ ] Разработка `CyberPipe.Security.Guards` (`HostAndMediaSafetyGuard`):
  - **Anti-Wipe блок:** полный запрет Raw Shell из чата и блокировка деструктивных путей/команд.
  - **Media & Audio Guard:** ограничитель громкости (`Volume Clamp`: макс. 60% днем / 25% ночью, защита от скачков > +20%, принудительная проверка `EnsureSafeVolumeBeforePlayback()` перед `Play`) + многослойный фильтр 18+ / NSFW + Whitelist музыкальных сервисов.
- [ ] Разработка **`Tier 0` Deterministic Router** (Regex / шаблоны / слэш-команды) + базовые `CyberPipe.Skills.Host`:
  - `MediaControlSkill`, `AudioVolumeSkill`, `SystemStatusSkill`.
  - `WorkspaceProfileSkill` (`/mode coding`, `/mode call`, `/focus`, `/lock`, `/sleep`).
  - Быстрый учет личных расходов и заметок за 0 токенов.
- [ ] Запуск обязательных гейтов **`CyberPipe.Tests.Architecture`** (`ArchUnitNET`) и **`CyberPipe.Tests.Isolation`** (PoC проверки `UnauthorizedAccessException` при попытке чтения `data/единая база PostgreSQL` из `Brainstem`, `ACCESS_DENIED` при `OpenProcess` к демону и сохранности `SecretsVault` после перезапуска процесса).
- **Критерий приемки (DoD Спринта 1):** Все сборки компилируются без циклических ссылок; проходят тесты `Tests.Architecture` и `Tests.Isolation`; локальная обработка команд `Tier 0` выполняется за `< 1 мс` (0 токенов, без учета сети Telegram; методика — см. 15, разд. 2); `SecretsVault` успешно расшифровывает секреты после перезапуска `CyberPipe`.

---

### Фаза 2: Радикальная экономия токенов (Tier 1/2), Локальный Голос, Память «Второй мозг» и Контроллер Бюджета (Спринт 2)
*Цель: Снизить расход токенов в 10–50 раз на естественном языке, добавить голосовой ввод за 0 токенов, векторную память и авто-паузу при росте расходов.*

- [ ] Расширение `CyberPipe.Storage.Postgres` (индекс `FTS5` + in-memory буфер векторов `Memory<float>` SIMD):
  - Таблицы `BudgetLedger`, `TaskCheckpoints`, `SemanticIntentCache` и ролевой `SemanticResponseCache` (с ключом `(QueryHash, UserRole, DataVersion, ExpiresAtUtc)` и `TTL = 24ч`, ключ включает `SHA-256` нормализованного запроса).
  - **`KnowledgeVault` («Второй мозг» с обязательным столбцом `TrustSource`) и `PersonalCrm`** (заметки, карточки клиентов, подписки, база решенных багов `/remember-fix`).
- [ ] Интеграция локального голосового конвейера **`Whisper.net` (ONNX)** в выделенном прогретом пуле `CyberPipe.Brainstem` (`Stateful Voice STT Pool`) для расшифровки голосовых сообщений из Telegram за **0 облачных токенов**.
- [ ] Разработка **`Tier 1` Local Semantic Router**:
  - Подключение локальной ONNX-модели эмбеддингов (`multilingual-e5-small`) внутри `CyberPipe` и SIMD-поиска (`TensorPrimitives.CosineSimilarity`).
  - Реализация `SemanticIntentCache`, ролевого `SemanticResponseCache` и гибридного поиска по `KnowledgeVault` за 0 токенов.
- [ ] Разработка **`TokenBudgetGovernor` (`CyberPipe.Security.Budget`)**:
  - Предварительная оценка токенов (`Microsoft.ML.Tokenizers` с коэффициентом `1.2x` для кириллицы в не-OpenAI моделях) и точное списание по `UsageDetails` (`InputCostPer1M` / `OutputCostPer1M`), плюс принудительная установка `ChatOptions.MaxOutputTokens` против перерасхода reasoning-моделей.
  - Раздельный учет: `$0.00` для моделей `ModelHostingType.Local` (контроль только `MaxLlmTurnsPerTask = 3`) и строгий долларовый лимит для `ModelHostingType.RemoteCloud`.
  - **Механизм Авто-Паузы (`TaskLifecycleState.PausedByBudget`):** заморозка задачи при достижении `SoftTaskTokenLimit (4 000 токенов)` или `3 шагов` с отправкой в Telegram инлайн-кнопок `[▶️ Разрешить еще +5 000 токенов]` (добавляет `+5 000 токенов` и `+3 шага` вплоть до `HardTaskTokenLimit = 12 000`), `[⚡ Выдать итог]`, `[🛑 Стоп]`.
- [ ] Разработка связки сервисов подбора моделей (**`IHardwareCapabilitiesProbe` + `IModelCompatibilityAdvisor`**):
  - Сбор характеристик ПК (Total/Free RAM, CPU AVX2, GPU VRAM) в `IHardwareCapabilitiesProbe` (`CyberPipe.Contracts.Hardware`).
  - Расчет совместимости в `IModelCompatibilityAdvisor` по **Curated-каталогу из 15 локальных моделей** (работающих через локальный HTTP-сервер `Ollama / LM Studio`, с учетом рабочего контекста `8K–16K` и `KV-Cache`) и **Curated-списку платных моделей**.
- [ ] Разработка **`Tier 2` Classifier (`Local` через `Ollama / LM Studio` или `RemoteCloud`)** + **Dynamic Tool Pruning** (подстановка только 2–3 релевантных тулов и топ-3 доверенных фактов из памяти с `TrustSource <= ToolOutput`).
- [ ] Создание выходного шлюза `CyberPipe.Security.Sanitization` (`CentralDataSanitizer` перед отправкой в `RemoteCloud`, `Serilog` и `Telegram`, без слепой обратной подстановки `[REDACTED:ID]` в команды LLM).
- [ ] Добавление доменных подкоманд CLI: `CyberPipe models` (`scan`, `list`, `set`) и `CyberPipe memory` (`search`, `add`, `status`), `CyberPipe infer`.
- **Критерий приемки (DoD Спринта 2):** Голосовое сообщение распознается локально через `Brainstem` за `< 1 с` (без учета сети Telegram); поиск по 10 000 векторов в памяти выполняется за `< 3 мс`; проходят тесты `CyberPipe.Tests.Router`, `CyberPipe.Tests.Sanitization` и тесты авто-паузы бюджета.

---

### Фаза 3: Все 16+ Прикладных Скиллов (Личное, Dev/DevOps, Бизнес), Планировщик, Песочницы и SecurityGuard (Спринт 3)
*Цель: Реализовать полный каталог из 16+ прикладных скиллов в едином скоупе Релиза 1.0 (сначала один сквозной сценарий `Telegram → роутер → песочница → ответ`, расширение каталога — после рабочего ядра, см. K.3/K.4), проактивный планировщик и изолированные песочницы `Tier 3`.*

- [ ] Разработка **`ProactiveScheduler`** на базе **`Quartz.NET`** (с отдельной зашифрованной паролем базой `Quartz.NET` (PostgreSQL), Read-Only Dashboard на `127.0.0.1` с одноразовым крипто-токеном и HttpOnly cookie + системные триггеры) и личных скиллов (`SecondBrainInboxSkill`, `PersistentReminderSkill`, `FinanceAndSubscriptionsSkill`, `FocusAndHealthSkill`, `RemoteHostCompanionSkill`, `Clipboard & Screen OCR Skill`):
  - Утренний (09:00) и вечерний (20:00) исполнительные брифинги, умные напоминания с кнопкой `[✅ Выполнено]`, предупреждения о списании подписок за 3 дня, визуализация очередей в защищенном Quartz.NET Dashboard.
- [ ] Разработка инженерных скиллов (`CyberPipe.Skills.Host.DevAndInfra`) и сервиса `DevToolProcessRunner` (`CyberPipe.Execution.Providers`):
  - **`GitRepoRadarSkill` (`/repos`, `/standup`) & `DiffReviewSkill`:** сканирование `%USERPROFILE%\source\repos` через `LibGit2Sharp` / `IDevToolProcessRunner`, сводка коммитов за 0 токенов, генератор Conventional Commits и пре-ревью диффов.
  - **`InfraHealthWatchdog` & `DevMemorySkill`:** фоновый мониторинг рабочих серверов, контейнеров Docker, сроков жизни SSL-сертификатов и база фиксов `/remember-fix`.
  - **`BuildAndTestWatchdog`:** запуск `dotnet build/test` через контракт `IDevToolProcessRunner` (реализованный в `CyberPipe.Execution.Providers` со строгим белым списком бинарников и папок) с обрезкой вывода через `ToolOutputTruncator` и кнопкой `[🛠 Предложить фикс]`.
- [ ] Разработка бизнес-скиллов (`CyberPipe.Skills.Host.Business`):
  - **`MeetingAssistantSkill`:** запись системного звука и микрофона на созвонах (`IAudioDeviceController` / `NAudio`) + локальная расшифровка (`Whisper.net` в `Brainstem`) + выжимка резюме и **Action Items**.
  - **`PersonalCrmSkill`, `ContractRedFlagScanner`, `BusinessTemplateSkill` & `MarketOsintDigestSkill`:** контроль «подвисших» ответов клиентов (Follow-up), извлечение текста PDF/DOCX договоров в одноразовом процессе `Brainstem` (`SingleUse Document Parser Pool`), проверка на риски, генерация КП и пассивный OSINT-дайджест.
- [ ] Разработка `CyberPipe.Security.Guards` (расширение для Tier 3) и реальных песочниц (`ISandboxProvider`: `OsRestrictedSandboxProvider` [`AppContainer`/`bwrap`/`Seatbelt`], `Wasmtime`, `Docker`):
  - Физическое удаление хостовых тулов из `ChatOptions.Tools` при наличии `MemoryTrustSource.UntrustedExternal` (`Hard Tool Stripping`), обязательное подтверждение владельцем (`RequiresApproval`) для нативных процессов, локальный AST-анализатор (Defense-in-Depth) и пассивный Stealth OSINT в режиме `Fail-Closed`.
  - Разработка `ISystemResourceMonitor` и потокового препроцессора телеметрии (`1 ГБ логов ➔ 30 КБ дайджест`).
- [ ] Добавление подкоманд CLI: `CyberPipe cron`, `approvals`, `sandbox`, `skills`, `backup`, `health`/`triage`, `security audit`.
- **Критерий приемки (DoD Спринта 3):** Все 16+ скиллов проходят модульные и интеграционные тесты; `Skills.Host` проходит проверку `HostSkills_Must_Never_Invoke_Arbitrary_Processes_Or_Shell`; разбор 50 МБ PDF в `Pool 2` не блокирует распознавание голоса в `Pool 1`; `UntrustedExternal` PDF физически лишается доступа к тулам управления ПК в `ChatOptions.Tools`.

---

### Фаза 4: Кроссплатформенный Avalonia UI и Терминальный TUI (Спринт 4)
*Цель: Удобная визуальная панель управления моделями по Tiers, бюджетом, безопасностью, разработкой и личной продуктивностью на рабочем столе и в терминале.*

- [ ] Разработка `CyberPipe.UI` и `CyberPipe.UI.Desktop` (gRPC клиент поверх Named Pipes / Unix Domain Sockets к локальному демону), а также интерактивного режима `CyberPipe tui`.
- [ ] Экраны управления:
  - **Мастер выбора моделей по Tiers (`Tier Model Selector`):** выбор пользователем модели (`🏠 Local 0$` через `Ollama / LM Studio` vs `☁️ RemoteCloud $`) отдельно для `Tier 2` и `Tier 3`, таблица 15 локальных моделей с вердиктами совместимости под ПК (`🟢/🟡/🔴` от `IModelCompatibilityAdvisor`) и витрина платных моделей.
  - **Дашборд экономии и бюджета:** графики сэкономленных токенов (`Tier 0/1/Local` vs `RemoteCloud`), настройка лимитов в $ и токенах, кнопка *«Cloud Freeze (Только локально)»*.
  - **Хаб Помощника, Dev-Радар и Запись Встреч:** переключатели режимов (`/mode coding`, `/mode call`), статус локальных Git-репозиториев и серверов, управление записью созвона, быстрый поиск по «Второму мозгу» и CRM.
  - **Панель безопасности хоста:** настройка потолка громкости (день/ночь), управление белым списком Telegram ID, журнал заблокированных угроз.
  - **Панель инфраструктуры и шины собственная абстракция сообщений:** селектор транспорта (`транспорт (реализация не фиксируется)` / `PostgreSQL собственная абстракция сообщений` (PostgreSQL) / `MessageBroker`), статус подключения (`🟢/🟡/🔴`), пинг, кнопка `[🔄 Проверить подключение]`, кнопка `[🚀 Запустить через Центр]` (для MessageBroker) и вывод диагностических ошибок запуска из `GlobalStateHub`.
  - **Живой монитор задач:** кнопка экстренного стопа `[🛑 STOP ALL]`, разблокировка задач на паузе и графики нагрузки CPU/RAM.
- **Критерий приемки (DoD Спринта 4):** `CyberPipe.UI` зависит исключительно от `CyberPipe.Contracts`; закрытие или перезапуск GUI не влияет на работу демона `CyberPipe`; все экраны работают по IPC.

---

### Фаза 5: Полная Верификация Безопасности, Пентесты, Стресс-тесты Обновлений и Бенчмарки SLO (Спринт 5)
*Цель: Финальная инструментальная аттестация границ безопасности ОС, устойчивости к сбоям, миграций БД и целевых показателей скорости.*

- [ ] Полный прогон статических тестов `CyberPipe.Tests.Architecture` (`ArchUnitNET`) + CI-скрипт **`Publish Directory Layout Gate`** (проверка отсутствия сборок демона в `publish/worker/` и генерация/проверка `worker.manifest.json` для всех `.exe` и `.dll`).
- [ ] Полный регрессионный прогон **`CyberPipe.Tests.Isolation`** на целевых ОС (см. [`15-cli-and-execution-modes.md`, раздел 9](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md)):
  - Проверка изоляции `AppContainer` / `Restricted Token` с `SidsToRestrict` + DACL `Deny` (Windows), `bwrap` (Linux), `Seatbelt` (macOS).
  - Негативные тесты попыток обхода (`OpenProcess` к демону $\rightarrow$ `ACCESS_DENIED`, чтение `../data/единая база PostgreSQL` и `secrets.vault` $\rightarrow$ `UnauthorizedAccessException`, попытка запуска дочернего процесса при `ActiveProcessLimit = 1`).
  - Тесты восстановления после искусственного `AccessViolation` и превышения лимита памяти `Job Object` в `Brainstem`, а также проверка атомарного обновления и отката (`Side-by-Side Update` + авто-восстановление бэкапа PostgreSQL `cyberpipe.v{N}.bak`).
- [ ] Пентест-набор `CyberPipe.Tests.SafetyAndBudget`:
  - Попытки промпт-инъекций из Telegram, `KnowledgeVault` и внешних документов (PDF/Web) на удаление файлов (`[System.IO.File]::Delete`, `rm -rf`, `Format-Volume`), эксфильтрацию секретов `[REDACTED:ID]`, установку громкости 100% и поиск 18+ контента (включая гомоглифы).
  - Проверка принудительного ограничения `ChatOptions.MaxOutputTokens`, срабатывания авто-паузы при зацикливании агента и замер времени локального срабатывания `/stop` (`< 50 мс`, сеть Telegram исключена).
- [ ] Бенчмарки `BenchmarkDotNet` (`CyberPipe.Tests.Router`): проверка соответствия целевым SLO для `Tier 0 (< 1 мс)`, `Tier 1 ONNX (5–15 мс)`, in-memory SIMD поиска в `KnowledgeVault (< 1.5 мс)` и задержки IPC (`p95 < 25 мс`).
- **Критерий приемки (DoD Спринта 5 / Релиз 1.0):** 100% прохождение всех 5 тестовых проектов (`Architecture`, `Isolation`, `SafetyAndBudget`, `Router`, `Sanitization`) и соответствие всем целевым SLO.






