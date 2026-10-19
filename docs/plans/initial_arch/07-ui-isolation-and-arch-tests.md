# 07. Изоляция UI, Панель Контроля Расходов и Архитектурные Тесты (ArchUnitNET)

Десктопный клиент **`CyberPipe.UI.Desktop.exe`** на базе **Avalonia UI 11+** — это легкий и отзывчивый центр управления умным помощником. Он не нагружает ПК (в отличие от Electron) и общается с фоновой службой **`CyberPipe.exe`** (работающей в режиме демона) исключительно по быстрому локальному `gRPC (Named Pipes)`.

---

### 1. Ключевые функции UI для прозрачности и контроля

Помимо отображения задач и графиков CPU/RAM, в интерфейс вынесены главные рычаги безопасности и экономии:
1. **Панель Бюджета и Экономии Токенов (`Token & Cost Dashboard`):**
   * Живой счетчик: сколько запросов решено **бесплатно** через `Tier 0 / Tier 1` (в % и сэкономленных $$$) и сколько потрачено облачных токенов сегодня.
   * Ползунки настройки лимитов на лету: дневной лимит ($), порог авто-паузы задачи (токены) и переключатель *«Только локальный режим (0$ Cloud Freeze)»*.
2. **Экстренный Стоп и Управление Паузами (`Kill-Switch & Pause Queue`):**
   * Большая кнопка **`[🛑 ОСТАНОВИТЬ ВСЁ]`**, дублирующая команду `/stop` в Telegram (мгновенный `CancellationToken.Cancel()`).
   * Список задач, поставленных на паузу из-за бюджета или ожидания подтверждения безопасности, с кнопками `[▶️ Разрешить]` / `[✖ Отклонить]`.
3. **Настройки Защиты Хоста и Медиа (`Host & Media Safety Panel`):**
   * Ползунок **Максимального потолка громкости** для команд из Telegram (по умолчанию `60%` днем / `25%` ночью).
   * Список разрешенных `TelegramUserId` (Владелец / Гости) и лог заблокированных попыток (например, срабатываний фильтра 18+ или стоп-слов).
4. **Хаб Продуктивности, Dev-Радар и Ассистент Встреч (`Personal, Dev & Business Hub`):**
   * **Быстрые рабочие сцены (1 клик):** `[💻 Кодинг]`, `[📞 Созвон (Запись + Выжимка)]`, `[🎯 Фокус 50м]`, `[🌙 Отдых]`.
   * **Git & Infra Радар:** живой статус локальных репозиториев в `source\repos` (незакоммиченные файлы, статус сборки) и индикаторы здоровья рабочих серверов/SSL.
   * **«Второй мозг» и CRM:** быстрый поиск по заметкам, карточкам клиентов, напоминаниям и ближайшим списаниям подписок.
5. **Конфигуратор Моделей по Tiers и Помощник Совместимости с ПК (`Tier Model Selector & Compatibility Wizard`):**
   * Для каждого уровня (`Tier 2` — легкие задачи, `Tier 3` — сложные задачи) пользователь сам выбирает модель с четким разделением **`[🏠 Локальная 0$]`** vs **`[☁️ Удаленная платная $]`**.
   * Встроенный помощник (`IModelCompatibilityAdvisor`) получает параметры ПК от `IHardwareCapabilitiesProbe` и подсвечивает каждую из **15 Curated локальных моделей** индикатором совместимости: `🟢 Оптимально для вашего ПК`, `🟡 Совместимо (нагрузка на CPU/RAM)`, `🔴 Мало памяти (риск фризов)`.
   * Для облачного режима доступен **Curated-список платных моделей** (отдельно быстрые/дешевые для `Tier 2` и флагманские для `Tier 3`).

6. **Управление Шиной Сообщений MassTransit и Монитор Инфраструктуры (`Message Bus & Infrastructure Monitor`):**
   * **Селектор провайдера транспорта:** интерактивный переключатель активного бэкенда шины сообщений:
     * `🔘 InMemory (По умолчанию)` — автономный легковесный режим без внешних брокеров и персистентной памяти.
     * `⚪ SQLite WAL (masstransit.db)` — персистентные очереди в отдельной зашифрованной паролем базе данных, используемой только MassTransit.
     * `⚪ RabbitMQ` — серверный AMQP-брокер с автозапуском через Центр и автоматической генерацией `cyberpipe_admin`.
   * **Живой статус-бейдж:** отображает состояние текущего транспорта (`🟢 Подключен`, `🟡 Запуск через Центр...`, `⚪ InMemory`, `🔴 Ошибка`), пинг в миллисекундах и счетчики сообщений. Данные поступают реактивно через `GlobalStateHub`.
   * **Кнопка `[🔄 Проверить подключение]`:** отправляет gRPC-команду к демону для немедленной проверки текущего транспорта и валидации паролей в `SecretsVault`.
   * **Кнопка `[🚀 Запустить через Центр]`:** при выборе RabbitMQ и отключенном брокере запрашивает старт через платформенный центр супервизии (`ICenterInfrastructureSupervisor`).
   * **Диагностический лог ошибок:** подробное описание сбоя из таблицы `InfrastructureHealthRecords` в `data/subsystems/health.db`.

7. **Планировщик Пользовательских Задач и Автоматизаций (`Task Scheduler & Automations Panel`):**
   * **Список пользовательских задач и расписаний:** таблица активных и отложенных задач (`UserScheduledTasks`) с указанием расписания (Cron / Интервал / Точное время), типа действия, даты следующего запуска и тумблером мгновенного включения/отключения (`IsEnabled`).
   * **Визуальный конструктор задач:** форма быстрого создания без знания Cron (выбор дней недели, времени или интервала) либо экспертный ввод Cron-выражения (`CronExpression`).
   * **Выбор типа действия:**
     * Текстовое напоминание (с опцией подтверждения `[✅ Выполнено]`).
     * Локальный C#-скилл без расхода токенов (`/repos`, замер параметров, проверка сайтов).
     * ИИ-исследование / Агентный промпт (с отображением расчетной стоимости и контролем `TokenBudgetGovernor`).
     * Фоновая dev-задача (запуск тестов в репозитории через `IDevToolProcessRunner`).
   * **Кнопки управления:** `[▶️ Запустить сейчас]` (для проверки), `[⏸ Пауза]`, `[✏️ Редактировать]`, `[🗑 Удалить]`.
   * **Кнопка `[🌐 Открыть Quartz.NET Dashboard]`:** безопасный запуск локальной панели `127.0.0.1:5080` через генерацию одноразового криптографического токена (OTT).

8. **Отдельный Ассистент Netrunner: цепочка Конструктор Каскадов и Хаб Пакетов (`Netrunner Workflow & Package Hub`, см. разд. 16):**
   * **Выделенный раздел приложения:** отдельное рабочее пространство ассистента Netrunner в боковом меню Avalonia UI.
   * **Визуальный конструктор каскадов (`цепочка Trigger → Skill → Script → Delivery Canvas`):** построение цепочек и зависимых задач из узлов `TriggerNode` ➔ `SkillNode` (сбор данных) ➔ `NetrunnerScriptNode` (детерминированный ETL) ➔ `ConditionNode` (ветвление) ➔ `LlmProcessNode` (ИИ-обработка над сжатыми данными) ➔ `HitlApprovalNode` (подтверждение человеком) ➔ `DeliveryNode` с автоматической проверкой совместимости `schema.output.json` и `schema.input.json` на стыках узлов.
   * **Инспектор стандартизированных пакетов (`Package Manifest Inspector`):** просмотр всех 6 обязательных файлов пакета (`netrunner.manifest.json`, `SKILL.md`, `schema.input.json`, `schema.output.json`, `skill-hack.*`, `tests/sample.fixture.json`), входных/выходных файловых контрактов и статуса SHA-256 верификации.
   * **Очередь подтверждений `Human-in-the-Loop (HITL)`:** список приостановленных каскадов со статусом `WaitingForHumanApproval`, превью промежуточных JSON-данных и сгенерированных файлов с кнопками `[✅ Одобрить и продолжить]`, `[✏️ Изменить данные]`, `[🛑 Отклонить]`.
   * **Дашборд экономии токенов:** визуальный счетчик сохраненных средств и токенов (например, *«Сэкономлено: 480 000 токенов ($4.80) на 160 автоматических ETL-запусках»*).
   * **Кнопка `[⚡ Синтезировать пакет и каскад через ИИ]`:** диалог с агентом `Tier 3`, который генерирует все 6 файлов пакета, прогоняет самотест `sample.fixture.json` в песочнице и сохраняет готовый пакет и цепочка в `data/subsystems/netrunner.db`.

---

### 2. Архитектурный контроль чистоты и безопасности (ArchUnitNET)

В проекте `CyberPipe.Tests.Architecture` автоматические тесты при каждой компиляции проверяют, что ни один разработчик (или ИИ-помощник) случайно не нарушил правила безопасности:

```csharp
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.Fluent;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace CyberPipe.Tests.Architecture;

public class ArchitectureIntegrityTests
{
    private static readonly Architecture Architecture =
        new ArchLoader()
            .LoadAssemblies(
                typeof(CyberPipe.Contracts.Models.SecurityInspectionReport).Assembly,
                typeof(CyberPipe.Security.Telemetry.Resilience.IsolatedWorkerBase).Assembly,
                typeof(CyberPipe.Security.Guards.HostAndMediaSafetyGuard).Assembly,
                typeof(CyberPipe.Security.Budget.TokenBudgetGovernor).Assembly,
                typeof(CyberPipe.Security.Sanitization.CentralDataSanitizer).Assembly,
                typeof(CyberPipe.Storage.Sqlite.SqliteConnectionFactory).Assembly,
                typeof(CyberPipe.Router.TieredIntentRouter).Assembly,
                typeof(CyberPipe.Skills.Host.MediaControlSkill).Assembly,
                typeof(CyberPipe.Core.Orchestration.OrchestratorEngine).Assembly,
                typeof(CyberPipe.Core.Agents.CoderAgent).Assembly,
                typeof(CyberPipe.Execution.Abstractions.ISandboxProvider).Assembly,
                typeof(CyberPipe.Execution.Providers.BrainstemProcessSupervisor).Assembly,
                typeof(CyberPipe.Brainstem.Program).Assembly,
                typeof(CyberPipe.Gateway.Telegram.TelegramBotService).Assembly,
                typeof(CyberPipe.Gateway.Api.GrpcControlService).Assembly,
                typeof(CyberPipe.UI.App).Assembly
            )
            .Build();

    [Fact]
    public void UI_Must_Depend_Only_On_Contracts()
    {
        IObjectProvider<IType> uiTypes = Types().That().ResideInAssembly(typeof(CyberPipe.UI.App).Assembly);
        Assert.NotEmpty(uiTypes.GetObjects(Architecture));

        IObjectProvider<IType> forbiddenTypesForUi = Types()
            .That().ResideInNamespace("CyberPipe.Core.*")
            .Or().ResideInNamespace("CyberPipe.Execution.*")
            .Or().ResideInNamespace("CyberPipe.Gateway.*")
            .Or().ResideInNamespace("CyberPipe.Storage.*")
            .Or().ResideInNamespace("CyberPipe.Skills.*")
            .Or().ResideInNamespace("CyberPipe.Security.*")
            .Or().ResideInNamespace("CyberPipe.Router.*");

        Types()
            .That().Are(uiTypes)
            .Should().NotDependOnAny(forbiddenTypesForUi)
            .Because("UI должен оставаться легким и общаться с демоном только через CyberPipe.Contracts!")
            .Check(Architecture);
    }

    [Fact]
    public void HostSkills_Must_Never_Invoke_Arbitrary_Processes_Or_Shell()
    {
        // Скиллы в CyberPipe.Skills.Host не могут напрямую вызывать Process или CliWrap;
        // для фиксированных утилит (git/dotnet) они используют контракт IDevToolProcessRunner из CyberPipe.Contracts
        IObjectProvider<IType> skillTypes = Types().That().ResideInAssembly(typeof(CyberPipe.Skills.Host.MediaControlSkill).Assembly);
        Assert.NotEmpty(skillTypes.GetObjects(Architecture));

        IObjectProvider<IType> forbiddenForHostSkills = Types()
            .That().ResideInNamespace("CyberPipe.Execution.*")
            .Or().HaveFullNameContaining("System.Diagnostics.Process")
            .Or().HaveFullNameContaining("CliWrap");

        Types()
            .That().Are(skillTypes)
            .Should().NotDependOnAny(forbiddenForHostSkills)
            .Because("Скиллы хоста не имеют права напрямую запускать процессы или шелл в обход IDevToolProcessRunner!")
            .Check(Architecture);
    }

    [Fact]
    public void TelegramGateway_Must_Not_Call_Agents_Or_Skills_Directly_Bypassing_Router_And_Guards()
    {
        IObjectProvider<IType> gatewayTypes = Types().That().ResideInAssembly(typeof(CyberPipe.Gateway.Telegram.TelegramBotService).Assembly);
        Assert.NotEmpty(gatewayTypes.GetObjects(Architecture));

        IObjectProvider<IType> forbiddenDirectCalls = Types()
            .That().ResideInNamespace("CyberPipe.Core.Agents.*")
            .Or().ResideInNamespace("CyberPipe.Execution.*");

        Types()
            .That().Are(gatewayTypes)
            .Should().NotDependOnAny(forbiddenDirectCalls)
            .Because("Telegram-шлюз обязан проводить все запросы через AuthGuard, IntentRouter и SafetyGuard!")
            .Check(Architecture);
    }

    [Fact]
    public void ModelCompatibilityAdvisor_Must_Query_Hardware_Only_Via_HardwareCapabilitiesProbe()
    {
        IObjectProvider<IType> advisorTypes = Types().That().HaveNameContaining("ModelCompatibilityAdvisor");
        Assert.NotEmpty(advisorTypes.GetObjects(Architecture));

        Types()
            .That().Are(advisorTypes)
            .Should().DependOnAny(Types().That().HaveFullName("CyberPipe.Contracts.Hardware.IHardwareCapabilitiesProbe"))
            .Because("Помощник подбора моделей обязан получать характеристики ПК от выделенного контракта IHardwareCapabilitiesProbe!")
            .Check(Architecture);
    }

    [Fact]
    public void All_Background_Workers_Must_Inherit_From_IsolatedWorkerBase()
    {
        IObjectProvider<Class> workerClasses = Classes()
            .That().HaveNameEndingWith("Worker").Or().HaveNameEndingWith("Watchdog");
        Assert.NotEmpty(workerClasses.GetObjects(Architecture));

        Classes()
            .That().Are(workerClasses)
            .Should().BeAssignableTo("CyberPipe.Security.Telemetry.Resilience.IsolatedWorkerBase")
            .Because("Все фоновые воркеры должны наследовать IsolatedWorkerBase из базового слоя CyberPipe.Security.Telemetry без циклических ссылок (см. 14-fault-isolation-and-resilience.md)")
            .Check(Architecture);
    }

    [Fact]
    public void No_Unbounded_Channels_Allowed()
    {
        // Запрет создания неограниченных очередей Channel.CreateUnbounded<T>() во всех сборках решения
        Types()
            .That().ResideInNamespace("CyberPipe.*")
            .Should().NotCallAny(
                MethodMembers().That().HaveNameContaining("CreateUnbounded"))
            .Because("Все внутрипроцессные каналы обязаны использовать только Channel.CreateBounded<T>() для защиты от OOM (см. 14-fault-isolation-and-resilience.md)")
            .Check(Architecture);
    }

    [Fact]
    public void Brainstem_Worker_Must_Never_Reference_Daemon_Assemblies()
    {
        IObjectProvider<IType> brainstemTypes = Types().That().ResideInAssembly(typeof(CyberPipe.Brainstem.Program).Assembly);
        Assert.NotEmpty(brainstemTypes.GetObjects(Architecture));

        IObjectProvider<IType> forbiddenDaemonTypes = Types()
            .That().ResideInNamespace("CyberPipe.Core.*")
            .Or().ResideInNamespace("CyberPipe.Gateway.*")
            .Or().ResideInNamespace("CyberPipe.Storage.*")
            .Or().ResideInNamespace("CyberPipe.Skills.*")
            .Or().ResideInNamespace("CyberPipe.Security.*")
            .Or().ResideInNamespace("CyberPipe.Router.*");

        Types()
            .That().Are(brainstemTypes)
            .Should().NotDependOnAny(forbiddenDaemonTypes)
            .Because("Проект CyberPipe.Brainstem не должен ссылаться на сборки демона (см. 15-cli-and-execution-modes.md, разд. 9 и 11)")
            .Check(Architecture);
    }
}
```

> **Примечание:** Статических тестов `ArchUnitNET` недостаточно для проверки фактической изоляции на уровне ОС. Проверки состава папки публикации (`Publish Directory Layout Gate`), изоляции `AppContainer` / `Restricted Token` с `SidsToRestrict` + DACL `Deny` (Windows) / `bwrap` (Linux) / `Seatbelt` (macOS), `Job Object` и IPC вынесены в интеграционный набор `CyberPipe.Tests.Isolation` (см. [`15-cli-and-execution-modes.md`, раздел 9](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md)).


