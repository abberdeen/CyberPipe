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
                typeof(CyberPipe.UI.App).Assembly,
                typeof(CyberPipe.Contracts.Models.SecurityInspectionReport).Assembly,
                typeof(CyberPipe.Skills.Host.MediaControlSkill).Assembly,
                typeof(CyberPipe.Core.Agents.CoderAgent).Assembly,
                typeof(CyberPipe.Gateway.Telegram.TelegramBotService).Assembly
            )
            .Build();

    [Fact]
    public void UI_Must_Depend_Only_On_Contracts()
    {
        IObjectProvider<IType> forbiddenTypesForUi = Types()
            .That().ResideInNamespace("CyberPipe.Core.*")
            .Or().ResideInNamespace("CyberPipe.Execution.*")
            .Or().ResideInNamespace("CyberPipe.Gateway.*")
            .Or().ResideInNamespace("CyberPipe.Storage.*")
            .Or().ResideInNamespace("CyberPipe.Skills.*")
            .Or().ResideInNamespace("CyberPipe.Security.*");

        Types()
            .That().ResideInAssembly(typeof(CyberPipe.UI.App).Assembly)
            .Should().NotDependOnAny(forbiddenTypesForUi)
            .Because("UI должен оставаться легким и общаться с демоном только через CyberPipe.Contracts!")
            .Check(Architecture);
    }

    [Fact]
    public void HostSkills_Must_Never_Invoke_Arbitrary_Processes_Or_Shell()
    {
        // Гарантия, что скиллы умного помощника (музыка, звук) не могут вызвать cmd/powershell
        IObjectProvider<IType> forbiddenForHostSkills = Types()
            .That().ResideInNamespace("CyberPipe.Execution.*")
            .Or().HaveFullNameContaining("System.Diagnostics.Process");

        Types()
            .That().ResideInAssembly(typeof(CyberPipe.Skills.Host.MediaControlSkill).Assembly)
            .Should().NotDependOnAny(forbiddenForHostSkills)
            .Because("Скиллы хоста (Media/Volume) не имеют права запускать произвольные процессы или шелл!")
            .Check(Architecture);
    }

    [Fact]
    public void TelegramGateway_Must_Not_Call_Agents_Or_Skills_Directly_Bypassing_Router_And_Guards()
    {
        IObjectProvider<IType> forbiddenDirectCalls = Types()
            .That().ResideInNamespace("CyberPipe.Core.Agents.*")
            .Or().ResideInNamespace("CyberPipe.Execution.*");

        Types()
            .That().ResideInAssembly(typeof(CyberPipe.Gateway.Telegram.TelegramBotService).Assembly)
            .Should().NotDependOnAny(forbiddenDirectCalls)
            .Because("Telegram-шлюз обязан проводить все запросы через AuthGuard, IntentRouter и SafetyGuard!")
            .Check(Architecture);
    }

    [Fact]
    public void ModelCompatibilityAdvisor_Must_Query_Hardware_Only_Via_HardwareCapabilitiesProbe()
    {
        // Сервис-помощник выбора моделей получает данные о ПК исключительно через сервис IHardwareCapabilitiesProbe
        Types()
            .That().HaveNameContaining("ModelCompatibilityAdvisor")
            .Should().DependOnAny(Types().That().HaveNameContaining("IHardwareCapabilitiesProbe"))
            .Because("Помощник подбора моделей обязан получать характеристики ПК от выделенного сервиса IHardwareCapabilitiesProbe!")
            .Check(Architecture);
    }

    [Fact]
    public void All_Background_Workers_Must_Inherit_From_IsolatedWorkerBase()
    {
        // Гарантия неубиваемости CyberPipe.exe: запрет наследования напрямую от BackgroundService в обход авто-карантина
        Classes()
            .That().HaveNameEndingWith("Worker").Or().HaveNameEndingWith("Watchdog")
            .Should().BeAssignableTo("CyberPipe.Core.Orchestration.Resilience.IsolatedWorkerBase")
            .Because("Любой фоновый воркер обязан работать внутри IsolatedWorkerBase с Circuit Breaker и таймаутом, чтобы не уронить CyberPipe.exe!")
            .Check(Architecture);
    }

    [Fact]
    public void Brainstem_Worker_Must_Never_Reference_Daemon_Assemblies()
    {
        // Защита от рефлексии: в CyberPipe.Brainstem.exe физически не должно быть классов демона
        IObjectProvider<IType> forbiddenDaemonTypes = Types()
            .That().ResideInNamespace("CyberPipe.Core.*")
            .Or().ResideInNamespace("CyberPipe.Gateway.*")
            .Or().ResideInNamespace("CyberPipe.Storage.*")
            .Or().ResideInNamespace("CyberPipe.Skills.*")
            .Or().ResideInNamespace("CyberPipe.Security.*");

        Types()
            .That().ResideInNamespace("CyberPipe.Brainstem.*")
            .Should().NotDependOnAny(forbiddenDaemonTypes)
            .Because("CyberPipe.Brainstem.exe — это стерильный процесс, в котором не должно быть кода демона даже через рефлексию!")
            .Check(Architecture);
    }
}
```
