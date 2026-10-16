# 09. Защита данных, санитизация и Хаб состояний (State Hub)

В архитектуре **CyberPipe** действуют принципы глубокой эшелонированной защиты (Defense-in-Depth). Данные защищаются как в хранилищах и очередях, так и при внутренней циркуляции между программными модулями.

---

### 1. Единый Хаб состояний приложения (State Hub) с принципом Need-to-Know

В системе реализован централизованный хаб состояний (`CyberPipe.State.Hub`), предоставляющий единую точку истины о текущих сессиях, задачах, статусах агентов и ресурсах.

> [!IMPORTANT]
> **Авторизация даже для внутреннего кода (Need-to-Know):**
> Ни один агент или внутренний сервис не имеет права запрашивать «сырое» глобальное состояние системы. Состояние отдается исключительно в виде **авторизованных проекций (Scoped State Projections)** на основании уровня допуска (Clearance Level).

```mermaid
flowchart LR
    Caller["Внутренний компонент (напр. SecurityAgent)"]
    StateHub["Unified State Hub"]
    Policy["Policy / ABAC Authorization"]
    RawState[("Global Raw Application State")]
    ScopedView["Filtered Scoped Projection (PII & Secrets Stripped)"]

    Caller -->|GetTaskStateAsync(TaskId)| StateHub
    StateHub --> Policy
    Policy -->|Check Clearance| RawState
    RawState -->|Filter fields| ScopedView
    ScopedView -->|Return to Caller| Caller
```

#### Пример разграничения доступа:
* Если **CEO Agent** запрашивает состояние задачи: возвращаются метаданные, контекст исполнения и история вызовов.
* Если **SecurityAgent** запрашивает состояние той же задачи: возвращаются только целевые артефакты, аргументы команд и идентификатор сессии. **Личные данные пользователя (Email, ФИО, локальные пути пользователя, персональные токены) принудительно удаляются из проекции**, так как агенту безопасности они не требуются для анализа угрозы.

#### Модель авторизованного запроса:
```csharp
namespace CyberPipe.State.Hub.Abstractions;

public interface IStateHub
{
    Task<TProjection> GetScopedStateAsync<TProjection>(
        Guid stateId,
        AgentSecurityContext callerContext,
        CancellationToken ct = default
    ) where TProjection : class;
}

public record AgentSecurityContext(
    Guid AgentId,
    AgentRole Role,
    AgentClearanceLevel ClearanceLevel // Public, Internal, Confidential, Restricted
);
```

---

### 2. Централизованный сервис очистки данных (Central Data & Log Sanitizer)

Все данные перед передачей наружу или сохранением проходят через `CyberPipe.Security.Sanitization`. Ни один лог, промпт для нейросети или сообщение шины не может обойти этот сервис.

```
 [Любой источник данных] ──► [Central Data Sanitizer] ──► [Куда направляются данные]
 (Команды, Артефакты,          - Детектор энтропии         • Контекст LLM (Промпты)
  Ошибки, Трассировки)         - Сигнатурный regex-анализ  • Файлы логов (Serilog)
                               - Замена секретов на ID     • Сообщения шины (RabbitMQ)
                                                           • Avalonia UI (Стриминг)
```

#### Что выявляет и маскирует Sanitizer:
1. **API Ключи и токены:** OpenAI, Anthropic, AWS Access/Secret Keys, GitHub PAT, Google API keys, JWT токены.
2. **Учетные данные:** Строки подключения к БД (`Server=...;Password=...`), логины/пароли в URI (`https://user:pass@host`).
3. **Криптографические ключи:** Приватные ключи RSA, SSH, PGP (`-----BEGIN RSA PRIVATE KEY-----`).
4. **Персональные данные (PII):** Номера кредитных карт, паспортные данные, персональные токены сессий.

#### Стратегия подмены (Tokenization / Redaction):
Вместо удаления секрет маскируется с сохранением контекстного маркера:
```text
Исходный текст:  git clone https://ghp_928374829374923@github.com/repo/private.git
После очистки:   git clone https://[REDACTED:GITHUB_TOKEN:id=e8a1]@github.com/repo/private.git
```
При необходимости исполнения команды CEO Agent восстанавливает реальное значение секрета исключительно внутри защищенной песочницы непосредственно в момент запуска.

---

### 3. Сквозное шифрование (E2EE in RabbitMQ & Storage)

#### А. Шифрование в RabbitMQ (Untrusted Broker)
Шина сообщений рассматривается как потенциально уязвимый промежуточный транспорт. Полезная нагрузка каждого сообщения шифруется с использованием **Envelope Encryption**:
1. Генерируется одноразовый симметричный ключ данных (**DEK — Data Encryption Key**).
2. Полезная нагрузка сообщения шифруется алгоритмом **AES-256-GCM** (аутентифицированное шифрование).
3. DEK шифруется мастер-ключом узла (**KEK — Key Encryption Key**) и упаковывается в заголовок сообщения.
4. Даже имея доступ к административной панели RabbitMQ или перехватив трафик очередей, прочитать промпты, команды или код невозможно.

#### Б. Поколоночное шифрование в БД (pgvector & SQLite)
* Столбцы с историей диалогов, векторами эмбеддингов, кодом скриптов и системными промптами шифруются через EF Core Value Converters с использованием аппаратных инструкций `AES-NI`.
* Векторные индексы для RAG генерируются на основе безопасных эмбеддингов в защищенном контуре.

---

### 4. Нулевой хардкод (Zero Hardcoding Architecture)

В проекте строго соблюдается правило: **ни одной жестко закодированной константы, пути, ключа или URL в исходном коде**.

* **Конфигурация через Strongly-Typed Options:** Все параметры оформляются в классы настроек, внедряемые через `IOptions<T>` или `IOptionsMonitor<T>` (с поддержкой горячего обновления без перезапуска).
* **Внешние секреты:** Поддержка провайдеров Secret Manager (Azure Key Vault, HashiCorp Vault, зашифрованные файлы `.env.encrypted`).
* **Динамические песочницы:** Выбор бэкенда песочницы (Docker, Wasm, Process) и конфигурация образов/лимитов настраиваются через конфигурационные манифесты:
  ```json
  {
    "CyberPipe": {
      "Execution": {
        "DefaultSandboxProvider": "Docker",
        "Providers": {
          "Docker": {
            "DefaultImage": "mcr.microsoft.com/dotnet/runtime:10.0-alpine",
            "CpuLimit": 1.5,
            "MemoryLimitMb": 1024,
            "NetworkDisabled": true
          },
          "Wasm": {
            "Engine": "Wasmtime",
            "MaxMemoryPages": 256
          }
        }
      }
    }
  }
  ```
