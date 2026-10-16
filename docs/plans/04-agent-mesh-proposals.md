# 04. Паттерн "Zero-Trust Agent Proposal"

В классических агентных системах (AutoGPT, OpenClaw и др.) каждый агент часто имеет прямой доступ к вызову функций (Function Calling) и немедленному исполнению команд в терминале. В **CyberPipe** это категорически запрещено.

---

### Архитектурный принцип

Воркеры (Coder, Researcher, Tester) функционируют в режиме **чистого генератора решений** (Pure Reasoners). У них физически отсутствуют ссылки на библиотеки выполнения (`CliWrap`, `Docker.DotNet`, `System.IO`).

```mermaid
sequenceDiagram
    autonumber
    actor User as Оператор (UI / Telegram)
    participant CEO as CEO / Orchestrator Agent
    participant Worker as Coder Agent (Zero-Trust)
    participant Bus as RabbitMQ Bus
    participant Sec as SecurityAgent
    participant Exec as Execution Sandbox

    User->>CEO: Создать задачу (написать скрипт бекапа)
    CEO->>Worker: Постановка подзадачи (с изолированным контекстом)
    Worker->>Worker: LLM ReAct цикл: принимает решение вызвать Bash-команду
    Note over Worker: Воркер НЕ может выполнить команду сам!
    Worker->>Bus: Опубликовать ProposeToolExecution
    Bus->>CEO: Доставка предложения к CEO Agent
    CEO->>Bus: Отправить команду на проверку (InspectPayload)
    Bus->>Sec: Доставка в SecurityAgent
    Sec->>Sec: AST-анализ, проверка аргументов, поиск рисков
    Sec-->>Bus: Возврат SecurityInspectionReport (RiskScore, Findings)
    Bus-->>CEO: Доставка отчета безопасности
    alt Риск Low / Safe
        CEO->>Exec: Авторизовать и выполнить команду
        Exec-->>CEO: Результат выполнения (stdout / exit code)
        CEO->>Worker: Передать результат в контекст воркера
    else Риск High / Critical
        CEO->>User: Запрос подтверждения (Human-in-the-Loop) с отчетом Security
    end
```

---

### Модели данных (DTO контрактов)

#### 1. Предложение выполнения инструмента:
```csharp
namespace CyberPipe.Contracts.Messages;

public record ProposeToolExecution(
    Guid ProposalId,
    Guid TaskId,
    Guid AgentId,
    string ToolName,
    IReadOnlyDictionary<string, object> Arguments,
    string Justification,
    DateTimeOffset ProposedAtUtc
);
```

#### 2. Запрос на авторизацию выполнения:
```csharp
namespace CyberPipe.Contracts.Messages;

public record AuthorizeToolExecution(
    Guid ProposalId,
    Guid ApprovedByAgentId, // Всегда CEO Agent
    bool RequiresSandboxIsolation,
    TimeSpan Timeout
);
```
