# 07. Изоляция UI и архитектурные тесты (ArchUnitNET)

Пользовательский интерфейс **CyberPipe** спроектирован по принципу "Thin Smart Client" (Тонкий интеллектуальный клиент) на базе **Avalonia UI 11+**, а чистота архитектуры контролируется автоматическими тестами.

---

### Архитектура UI-слоя

* **Multiplatform Target:** Единая разметка и логика в `CyberPipe.UI` компилируется под:
  * Desktop (Windows x64/arm64, macOS x64/arm64, Linux x64/arm64);
  * Web (WebAssembly через Avalonia Browser backend).
* **Сетевой стек:**
  * Клиент подключается к `CyberPipe.Gateway.Api` через **gRPC over HTTP/2 с TLS 1.3**.
  * Для WebAssembly используется `Grpc.Net.Client.Web` (трансляция gRPC через HTTP/1.1 и WebSockets).
* **Стриминг в реальном времени:**
  * Отображение мыслительного процесса агентов (ReAct steps), системных логов и прогресса исполнения команд реализуется через двунаправленный серверный стриминг.
  * **Все стриминг-сообщения предварительно очищаются Sanitizer-сервисом**, что исключает случайный показ секретов оператору.

---

### Архитектурный контроль чистоты (ArchUnitNET)

В тестовый проект `CyberPipe.Tests.Architecture` включены обязательные правила, проверяющие изоляцию слоев при каждой сборке:

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
                typeof(CyberPipe.Contracts.Messages.ProposeToolExecution).Assembly,
                typeof(CyberPipe.Agents.Workers.CoderAgent).Assembly,
                typeof(CyberPipe.Agents.Ceo.CeoOrchestrator).Assembly
            )
            .Build();

    [Fact]
    public void UI_Must_Not_Reference_Internal_Modules()
    {
        IObjectProvider<IType> forbiddenTypesForUi = Types()
            .That().ResideInNamespace("CyberPipe.Agents.*")
            .Or().ResideInNamespace("CyberPipe.Execution.*")
            .Or().ResideInNamespace("CyberPipe.Gateway.*")
            .Or().ResideInNamespace("CyberPipe.Infrastructure.*")
            .Or().ResideInNamespace("CyberPipe.Security.Cryptography.*")
            .Or().ResideInNamespace("CyberPipe.Daemon.*");

        IArchRule uiRule = Types()
            .That().ResideInAssembly(typeof(CyberPipe.UI.App).Assembly)
            .Should().NotDependOnAny(forbiddenTypesForUi)
            .Because("UI должен быть строго изолирован и общаться с бэкендом только через Contracts и gRPC!");

        uiRule.Check(Architecture);
    }

    [Fact]
    public void Workers_Must_Not_Reference_Execution_Or_Storage()
    {
        IObjectProvider<IType> forbiddenForWorkers = Types()
            .That().ResideInNamespace("CyberPipe.Execution.*")
            .Or().ResideInNamespace("CyberPipe.Infrastructure.Storage.*");

        IArchRule workerRule = Types()
            .That().ResideInAssembly(typeof(CyberPipe.Agents.Workers.CoderAgent).Assembly)
            .Should().NotDependOnAny(forbiddenForWorkers)
            .Because("Воркеры должны быть слепы и изолированы от прямого вызова песочниц и баз данных (Zero-Trust)!");

        workerRule.Check(Architecture);
    }
}
```

Любое нарушение принципов Zero-Trust или изоляции UI мгновенно блокирует сборку проекта.
