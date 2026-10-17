# 08. План поэтапной реализации CyberPipe (От быстрого ядра к полному функционалу)

План перестроен по принципу **«Сначала быстрый, безопасный и рабочий помощник без лишних зависимостей»**. Уже по итогам 1-го и 2-го спринтов система умеет мгновенно выполнять команды из Telegram (музыка, громкость, статус) за 0 токенов, защищена от вайпа и 18+ контента и жестко контролирует расходы.

---

### Фаза 1: Ядро Хоста, Межпроцессная Изоляция, Периметр Telegram и Скиллы Хоста (Спринт 1)
*Цель: Запустить ядро `CyberPipe.exe`, изолированный воркер `CyberPipe.Brainstem.exe`, базовый CLI и безопасное управление медиа/звуком из Telegram за 0 токенов.*

- [ ] Инициализация Solution (`CyberPipe.slnx`) на .NET 10 с тремя точками входа (`CyberPipe.exe`, `CyberPipe.UI.Desktop.exe`, `./worker/CyberPipe.Brainstem.exe`, см. [`15-cli-and-execution-modes.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md)).
- [ ] Реализация диспетчера запуска в `CyberPipe.exe` (`DaemonHostMode` + `CliClientMode`) и защищенного транспорта Named Pipes (верификация `PID`, `SessionNonce`, `PipeSecurity`).
- [ ] Реализация внутрипроцессной и межпроцессной устойчивости (см. [`14-fault-isolation-and-resilience.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/14-fault-isolation-and-resilience.md) и [`15-cli-and-execution-modes.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md)):
  - Настройка `BackgroundServiceExceptionBehavior.Ignore`, `SafeModuleRegistration` и `IsolatedWorkerBase` (с авто-карантином после 3 ошибок).
  - Разделение ресурсов на **Bulkhead-отсеки**, использование только `BoundedChannel`, потоковая квота `QuotaBoundedStream (16 МБ)` и журнал `InFlightOperations` против «отравленных» сообщений.
  - Разработка `BrainstemProcessSupervisor`: запуск `CyberPipe.Brainstem.exe` с `CreateRestrictedToken` (`Low Integrity` / `Restricted SID`), `SACL NO_READ_UP`, `Windows Job Object` (`ActiveProcessLimit = 1`, лимит памяти и CPU, `KillOnJobClose`), изолированным `TEMP` и проверкой SHA-256 бинарника.
- [ ] Разработка системного **CLI и TUI (`CyberPipe <command>`)** с разделением на `Autonomous`, `Daemon-Required` и `Hybrid` команды и поддержкой строгого `--json` контракта.
- [ ] Внутрипроцессная шина на `System.Threading.Channels` + приоритетный канал управления **`PriorityKillSwitch` (`/stop`, `/pause`)**.
- [ ] Разработка `CyberPipe.Gateway.Telegram`:
  - Строгий фильтр `TelegramAuthGuard` (Whitelist `UserId`, разделение `Owner` и `Guest`).
- [ ] Разработка `CyberPipe.Security.Guards` (`HostAndMediaSafetyGuard`):
  - **Anti-Wipe блок:** полный запрет Raw Shell из чата и блокировка деструктивных путей/команд.
  - **Media & Audio Guard:** ограничитель громкости (`Volume Clamp`: макс. 60% днем / 25% ночью, защита от скачков > +20%) + локальный фильтр 18+ / NSFW + Whitelist музыкальных сервисов.
- [ ] Разработка **`Tier 0` Deterministic Router** (Regex / шаблоны / слэш-команды) + базовые `CyberPipe.Skills.Host`:
  - `MediaControlSkill`, `AudioVolumeSkill`, `SystemStatusSkill`.
  - `WorkspaceProfileSkill` (`/mode coding`, `/mode call`, `/focus`, `/lock`, `/sleep`).
  - Быстрый учет личных расходов и заметок за 0 токенов.

---

### Фаза 2: Радикальная экономия токенов (Tier 1/2), Локальный Голос, Память «Второй мозг» и Контроллер Бюджета (Спринт 2)
*Цель: Снизить расход токенов в 10–50 раз на естественном языке, добавить голосовой ввод за 0 токенов, векторную память и авто-паузу при росте расходов.*

- [ ] Разработка `CyberPipe.Storage.Sqlite` (режим WAL + `FTS5` + асинхронный Write-Behind):
  - Таблицы учета токенов, чекпоинтов пауз и локального семантического кэша.
  - **`KnowledgeVault` («Второй мозг»), `PersonalCrm` и `FinanceLedger`** (заметки, карточки клиентов, подписки, база решенных багов `/remember-fix`).
- [ ] Интеграция локального голосового конвейера **`Whisper.net` (ONNX)** внутри `CyberPipe.Brainstem.exe` для расшифровки голосовых сообщений из Telegram за **0 облачных токенов**.
- [ ] Разработка **`Tier 1` Local Semantic Router**:
  - Подключение локальной ONNX-модели эмбеддингов (`multilingual-e5-small`) и SIMD-поиска (`TensorPrimitives.CosineSimilarity`).
  - Реализация **`SemanticResponseCache`** и гибридного поиска по `KnowledgeVault` за 0 токенов.
- [ ] Разработка **`TokenBudgetGovernor` (`CyberPipe.Security.Budget`)**:
  - Точный подсчет токенов до вызова API (`Microsoft.ML.Tokenizers`).
  - Раздельный учет: `$0.00` для моделей `ModelHostingType.Local` (контроль только `MaxTurns`) и строгий долларовый лимит для `ModelHostingType.RemoteCloud`.
  - **Механизм Авто-Паузы (`TaskPausedByBudget`):** заморозка задачи при достижении 70% лимита с отправкой в Telegram инлайн-кнопок `[▶️ Продолжить (+5k)]`, `[⚡ Выдать итог]`, `[🛑 Стоп]`.
- [ ] Разработка связки сервисов подбора моделей (**`IHardwareCapabilitiesProbe` + `IModelCompatibilityAdvisor`**):
  - Сбор характеристик ПК (Total/Free RAM, CPU AVX2, GPU VRAM) в `IHardwareCapabilitiesProbe`.
  - Расчет совместимости в `IModelCompatibilityAdvisor` по **Curated-каталогу из 15 локальных моделей** и **Curated-списку платных моделей** (для легких `Tier 2` и сложных `Tier 3` задач).
- [ ] Разработка **`Tier 2` Classifier (`Local` или `RemoteCloud` по выбору пользователя)** + **Dynamic Tool Pruning** (подстановка только 2–3 релевантных тулов и топ-3 фактов из памяти).
- [ ] Создание `CyberPipe.Security.Sanitization` (вырезание ключей/паролей `[REDACTED]` перед любым вызовом облака).

---

### Фаза 3: Инженерные и Бизнес-Скиллы, Проактивный Планировщик, Песочницы и SecurityGuard (Спринт 3)
*Цель: Автоматизация задач программирования (Git, сборки, мониторинг), бизнеса (брифинги, звонки, договоры) и безопасное выполнение кода в изоляции.*

- [ ] Разработка **`ProactiveScheduler`** (`Cronos` + событийные триггеры):
  - Утренний (09:00) и вечерний (20:00) исполнительные брифинги, умные напоминания с кнопкой `[✅ Выполнено]`, предупреждения о списании подписок за 3 дня.
- [ ] Разработка инженерных скиллов (`CyberPipe.Skills.Host.DevAndInfra`):
  - **`GitRepoRadarSkill` (`/repos`, `/standup`):** сканирование `source\repos`, сводка коммитов за день за 0 токенов, генератор Conventional Commits и пре-ревью диффов.
  - **`InfraHealthWatchdog`:** фоновый мониторинг рабочих серверов, контейнеров Docker и сроков жизни SSL-сертификатов.
  - **`BuildAndTestWatchdog`:** запуск `dotnet build/test` с обрезкой вывода через `ToolOutputTruncator` и кнопкой `[🛠 Предложить фикс]`.
- [ ] Разработка бизнес-скиллов (`CyberPipe.Skills.Host.Business`):
  - **`MeetingAssistantSkill`:** запись системного звука и микрофона на созвонах (`NAudio`) + локальная расшифровка (`Whisper.net` в `Brainstem`) + выжимка резюме и **Action Items**.
  - **`PersonalCrmSkill` & `ContractRedFlagScanner`:** контроль «подвисших» ответов клиентов (Follow-up), извлечение текста PDF/DOCX договоров через `Brainstem` и проверка на риски.
- [ ] Разработка `CyberPipe.Security.Guards` (расширение для Tier 3) и песочниц (`ISandboxProvider`: `CliWrap`, `Wasmtime`, `Docker`):
  - Локальный AST-анализатор PowerShell/Bash (`System.Management.Automation`), пассивный Stealth OSINT и маркировка доверия `MemoryTrustSource`.
  - Разработка `ISystemResourceMonitor` и потокового препроцессора телеметрии (`1 ГБ логов ➔ 30 КБ дайджест`).

---

### Фаза 4: Кроссплатформенный Avalonia UI (Спринт 4)
*Цель: Удобная визуальная панель управления моделями по Tiers, бюджетом, безопасностью, разработкой и личной продуктивностью на рабочем столе.*

- [ ] Разработка `CyberPipe.UI` и `CyberPipe.UI.Desktop` (gRPC клиент поверх Named Pipes к локальному демону).
- [ ] Экраны управления:
  - **Мастер выбора моделей по Tiers (`Tier Model Selector`):** выбор пользователем модели (`🏠 Local 0$` vs `☁️ RemoteCloud $`) отдельно для `Tier 2` и `Tier 3`, таблица 15 локальных моделей с вердиктами совместимости под ПК (`🟢/🟡/🔴` от `IModelCompatibilityAdvisor`) и витрина платных моделей.
  - **Дашборд экономии и бюджета:** графики сэкономленных токенов (`Tier 0/1/Local` vs `RemoteCloud`), настройка лимитов в $ и токенах, кнопка *«Cloud Freeze (Только локально)»*.
  - **Хаб Помощника, Dev-Радар и Запись Встреч:** переключатели режимов (`/mode coding`, `/mode call`), статус локальных Git-репозиториев и серверов, управление записью созвона, быстрый поиск по «Второму мозгу» и CRM.
  - **Панель безопасности хоста:** настройка потолка громкости (день/ночь), управление белым списком Telegram ID, журнал заблокированных угроз.
  - **Живой монитор задач:** кнопка экстренного стопа `[🛑 STOP ALL]`, разблокировка задач на паузе и графики нагрузки CPU/RAM.

---

### Фаза 5: Верификация изоляции в CI, Архитектурные тесты и Бенчмарки SLO (Спринт 5)
*Цель: Инструментальная проверка границ безопасности ОС, IPC, устойчивости к сбоям и целевых показателей скорости.*

- [ ] Статические тесты `CyberPipe.Tests.Architecture` (`ArchUnitNET`) + CI-скрипт **`Publish Directory Layout Gate`** (проверка отсутствия сборок демона в `publish/worker/`).
- [ ] Интеграционный набор проверки изоляции ОС **`CyberPipe.Tests.Isolation`** (см. [`15-cli-and-execution-modes.md`, раздел 9](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md#L211-L234)):
  - Проверка фактического `IntegrityLevel == Low` / `Restricted SID` и отсутствия привилегий у запущенного `CyberPipe.Brainstem.exe`.
  - Негативные тесты попыток обхода (`OpenProcess` к демону $\rightarrow$ `ACCESS_DENIED`, чтение `../data/cyberpipe.db` $\rightarrow$ `UnauthorizedAccessException`, попытка запуска дочернего `cmd.exe` при `ActiveProcessLimit = 1`).
  - Тесты восстановления после искусственного `AccessViolation` и превышения лимита памяти `Job Object` в `Brainstem`.
- [ ] Пентест-набор `CyberPipe.Tests.SafetyAndBudget`:
  - Попытки промпт-инъекций из Telegram и внешних документов (PDF/Web) на удаление файлов (`rm -rf`, `Format-Volume`), установку громкости 100% и поиск 18+ контента.
  - Проверка срабатывания авто-паузы при искусственном зацикливании агента и замер времени срабатывания `/stop`.
- [ ] Бенчмарки `BenchmarkDotNet` (`CyberPipe.Tests.Router`): проверка соответствия целевым SLO для `Tier 0`, `Tier 1 ONNX`, поиска в `KnowledgeVault` и задержки IPC.
