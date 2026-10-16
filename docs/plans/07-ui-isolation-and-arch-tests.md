# 07. Изоляция UI и архитектурные тесты

Пользовательский интерфейс **CyberPipe** спроектирован по принципу "Thin Smart Client" (Тонкий интеллектуальный клиент) на базе **Avalonia UI 11+**.

---

### Архитектура UI-слоя

* **Multiplatform Target:** Единая разметка и логика в `CyberPipe.UI` компилируется под:
  * Desktop (Windows x64/arm64, macOS x64/arm64, Linux x64/arm64);
  * Web (WebAssembly через Avalonia Browser backend).
* **Сетевой стек:**
  * Клиент подключается к `CyberPipe.Gateway.Api` через **gRPC over HTTP/2 с TLS**.
  * Для WebAssembly используется `Grpc.Net.Client.Web` (трансляция gRPC через HTTP/1.1 и WebSockets).
* **Стриминг в реальном времени:**
  * Отображение мыслительного процесса агентов (ReAct steps), системных логов и прогресса исполнения команд реализуется через двунаправленный серверный стриминг:
    ```csharp
    rpc StreamTaskEvents(TaskSubscriptionRequest) returns (stream TaskEventMessage);
    ```

---

### Архитектурный контроль изоляции (ArchUnitNET)

Чтобы предотвратить случайное добавление прямой ссылки из UI во внутренние сборки системы (Core, Agents, Execution, Infrastructure), в тестовый проект `CyberPipe.Tests.Architecture` включается автоматический юнит-тест на базе **`ArchUnitNET`**:

```csharp
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.Fluent;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace CyberPipe.Tests.Architecture;

public class UiIsolationTests
{
    private static readonly Architecture Architecture =
        new ArchLoader()
            .LoadAssemblies(
                typeof(CyberPipe.UI.App).Assembly,
                typeof(CyberPipe.Contracts.Messages.ProposeToolExecution).Assembly
            )
            .Build();

    [Fact]
    public void UI_Must_Not_Reference_Internal_Modules()
    {
        // Запрещенные пространства имен внутри UI
        IObjectProvider<IType> forbiddenTypes = Types()
            .That().ResideInNamespace("CyberPipe.Agents.*")
            .Or().ResideInNamespace("CyberPipe.Execution.*")
            .Or().ResideInNamespace("CyberPipe.Gateway.*")
            .Or().ResideInNamespace("CyberPipe.Infrastructure.*")
            .Or().ResideInNamespace("CyberPipe.Daemon.*");

        // Правило: типы из сборки UI не должны иметь зависимостей от запрещенных сборок
        IArchRule rule = Types()
            .That().ResideInAssembly(typeof(CyberPipe.UI.App).Assembly)
            .Should().NotDependOnAny(forbiddenTypes)
            .Because("UI должен быть полностью изолирован и общаться с бэкендом только через Contracts и gRPC!");

        rule.Check(Architecture);
    }
}
```

При попытке разработчика подключить проект, например, `CyberPipe.Agents.Ceo` в `CyberPipe.UI`, сборка тестов на CI/CD немедленно упадет с ошибкой.
