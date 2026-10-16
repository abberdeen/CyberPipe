# 07. Изоляция UI, Панель Контроля Расходов и Архитектурные Тесты (ArchUnitNET)

Пользовательский интерфейс **CyberPipe** на базе **Avalonia UI 11+** — это легкий и отзывчивый центр управления умным помощником. Он не нагружает ПК (в отличие от Electron) и общается с фоновой службой `CyberPipe.Daemon` исключительно по быстрому локальному `gRPC`.

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
}
```
