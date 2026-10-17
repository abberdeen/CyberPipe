# 09. Защита данных, Санитизация, Локальный Кэш и Хранение (SQLite WAL)

В **CyberPipe** защита данных построена прагматично: мы защищаем реальные векторы утечек (отправка промптов в облачные LLM, логи, база данных на диске и вывод в Telegram), не замедляя работу приложения избыточным перешифрованием памяти внутри одного процесса.

---

### 1. Выходной шлюз очистки секретов (`Central Data Sanitizer` на границе Egress)

`CyberPipe.Security.Sanitization` работает исключительно на **выходных границах доверенного контура (`Egress Middleware`)** — перед отправкой промпта во внешнюю облачную LLM (`SanitizingChatClient` для `RemoteCloud`), перед записью в файлы логов (`Serilog` Enricher/Sink) и перед отправкой сообщений/отчетов в `Telegram` (`TelegramOutputFormatter`). Локальные команды `Tier 0 / Tier 1` и заметки, сохраняемые в локальную зашифрованную базу `KnowledgeVault`, **не искажаются** на входе.

```
 [Внутренний контекст / Вывод тулов] ──► [Central Data Sanitizer (Egress)] ──► [Куда уходят данные]
                                          • Regex + Энтропия Шеннона           • Облачные LLM (RemoteCloud)
                                          • Замена на [REDACTED:ID]            • Telegram чат
                                          • < 0.5 мс на проверку               • Локальные файлы логов (Serilog)
```

#### Что автоматически вырезается и маскируется на выходе:
1. **API-ключи и токены:** OpenAI (`sk-...`), Anthropic, Telegram Bot Token, GitHub PAT (`ghp_...`), AWS, JWT.
2. **Пароли и строки подключения:** `Password=...`, `https://user:pass@host`, локальные куки и сессии.
3. **Приватные ключи:** SSH, RSA, PGP, `.pem` / `.pfx` содержимое.
4. **Чувствительные пути и PII:** Номера банковских карт и системные переменные окружения.

#### Токенизация и категорический запрет слепой обратной подстановки (`No Blind Rehydration`):
```text
До очистки:     git clone https://ghp_928374829374923@github.com/my/repo.git
После очистки:  git clone https://[REDACTED:GITHUB_TOKEN:e8a1]@github.com/my/repo.git
```
Облачная нейросеть видит структуру команды, но **не получает сам секрет**.
> [!IMPORTANT]
> **Защита от Confused Deputy при работе с секретами:**
> Система **никогда не выполняет автоматическую строковую замену** плейсхолдера `[REDACTED:ID]` обратно на реальный секрет в тексте команд или URL, сгенерированных LLM (так как промпт-инъекция могла бы подставить `[REDACTED:GITHUB_TOKEN:e8a1]` в URL атакующего хоста `attacker.com` или записать его в файл). Если доверенному скиллу (например, `git` в `DevToolProcessRunner`) требуется аутентификация, учетные данные передаются исключительно через штатный `Git Credential Helper` или изолированные переменные окружения процесса со строгой привязкой к белому списку доменов (`github.com`, `gitlab.com`).

---

### 2. Локальный Сейф Секретов (`Local Secret Vault`) и Шифрование на Диске

Вместо сложной инфраструктуры внешних крипто-брокеров используется надежное персистентное платформенное шифрование (`ISecretVaultPlatform`):
* **Персистентность между перезапусками и работа в CLI:** Все ключи (API Keys, Bot Tokens) в файле `data/secrets.vault` шифруются алгоритмом **AES-256-GCM**, где мастер-ключ защищен нативным персистентным хранилищем ОС (**Windows DPAPI** с детерминированной привязкой к профилю установки и опциональным мастер-паролем пользователя / **Linux LibSecret** / **macOS Keychain**). Благодаря этому демон `CyberPipe` и `Hybrid`-команды CLI (`CyberPipe setup`, `CyberPipe secrets set|list`) надежно расшифровывают сейф после перезапуска процесса или перезагрузки ПК.
* **Файловая изоляция от `Brainstem` и зануление буферов в ОЗУ:** Защита от чтения `secrets.vault` дочерним процессом `CyberPipe.Brainstem` и песочницами обеспечивается **на уровне файловой системы ОС** (явный `ACCESS_DENIED_ACE` для `RestrictedWorkerCustomSid` в DACL папки `data\` и отсутствие прав у `AppContainer` в Windows; несмонтированный каталог в `bwrap` на Linux и запрет в профиле `Seatbelt` на macOS), поэтому изолированный воркер физически не может прочитать блоб `secrets.vault` с диска. Внутри `CyberPipe` расшифрованные буферы ключей немедленно очищаются в памяти через `CryptographicOperations.ZeroMemory`.
* **Чувствительные поля в SQLite:** Чекпоинты задач, приватные заметки и сохраненные переменные окружения шифруются прозрачно при записи на диск с аппаратным ускорением `AES-NI`.

---

### 3. Архитектура баз данных: Центральный Мастер SQLite (`cyberpipe_master.db`) + Изолированные БД подсистем (`data/subsystems/*.db`)

Вместо монолитной общей базы данных или обязательного внешнего сервера PostgreSQL, в **CyberPipe** реализована строгая децентрализованная архитектура локального хранения на базе **SQLite в режиме `WAL` (Write-Ahead Logging)** с центральным реестром и раздельными паролями:

```
┌─────────────────────────────────────────────────────────────────────────────────────────────┐
│                       Центральный Мастер SQLite (cyberpipe_master.db)                       │
│  • Хранит ТОЛЬКО общие базовые настройки приложения (Global App Settings)                  │
│  • Таблица-каталог SubsystemDatabaseRegistry (Key-Value: SubsystemKey + Путь к файлу БД)   │
└──────────────────────────────────────────────┬──────────────────────────────────────────────┘
                                               │
                                               ▼
                         ┌───────────────────────────────────────────┐
                         │         ISubsystemDatabaseManager         │
                         │    (Специализированный сервис доступа)    │
                         │ • Разрешение путей по SubsystemKey        │
                         │ • Автоматическое создание / lazy init БД  │
                         │ • Получение/генерация пароля из Сейфа     │
                         │ • Накатывание миграций подсистемы         │
                         └───────┬───────────────────────────┬───────┘
                                 │                           │
          Чтение паролей         │                           │ Предоставление защищенного
        (AES-256-GCM / DPAPI)    │                           │ изолированного SqliteConnection
                                 ▼                           ▼
                    ┌─────────────────────────┐   ┌───────────────────────────────────────────┐
                    │   Local SecretsVault    │   │      Изолированные БД подсистем           │
                    │   (data/secrets.vault)  │   │          (data/subsystems/*.db)           │
                    ├─────────────────────────┤   ├───────────────────────────────────────────┤
                    │ db:secret:hangfire      ├──►│ hangfire.db     (Фоновый планировщик)     │
                    │ db:secret:masstransit   ├──►│ masstransit.db  (MassTransit очереди/Outbox)│
                    │ db:secret:knowledge     ├──►│ knowledge.db    (База знаний / FTS5 / SIMD) │
                    │ db:secret:crm           ├──►│ crm.db          (Личная CRM и контакты)     │
                    │ db:secret:finance       ├──►│ finance.db      (Финансы и транзакции)      │
                    │ db:secret:budget        ├──►│ budget.db       (Бюджет токенов, чекпоинты) │
                    │ db:secret:health        ├──►│ health.db       (История здоровья сервисов) │
                    │ db:secret:netrunner     ├──►│ netrunner.db    (База ETL-скриптов и схем)  │
                    └─────────────────────────┘   └───────────────────────────────────────────┘
```

#### 1. Центральный Мастер SQLite (`cyberpipe_master.db`), Общие Настройки (`AppGlobalSettings`) и Таблица-Каталог
Центральная база содержит **только общие базовые настройки приложения** (`AppGlobalSettings`: системные флаги, выбранный шинный провайдер, глобальное правило поведения пайплайнов при аварийном сбое/отключении света) и специальную таблицу каталога баз данных (`SubsystemDatabaseRegistry`):
```sql
-- Таблица общих базовых настроек приложения (управляется через главный раздел Settings в Avalonia UI)
CREATE TABLE IF NOT EXISTS AppGlobalSettings (
    SettingKey TEXT PRIMARY KEY,         -- Например: 'Messaging:ActiveTransport', 'Pipeline:CrashRecoveryPolicy'
    SettingValue TEXT NOT NULL,          -- Например: 'SqliteWal' | 'RabbitMq', 'ResumeFromCheckpoint' | 'StopAndMarkCancelledByCrash'
    ValueType TEXT NOT NULL,             -- 'String', 'Enum', 'Boolean', 'Integer'
    Description TEXT,                    -- Описание настройки для UI
    UpdatedAtUtc TEXT NOT NULL           -- Время последнего изменения через UI
);

-- Начальные значения общих настроек по умолчанию:
-- ('Pipeline:CrashRecoveryPolicy', 'ResumeFromCheckpoint', 'Enum', 'Поведение пайплайнов после отключения света или падения: ResumeFromCheckpoint (продолжать с места падения) или StopAndMarkCancelledByCrash (остановить как есть со спец-статусом CancelledByCrash)')

-- Реестр изолированных баз данных подсистем
CREATE TABLE IF NOT EXISTS SubsystemDatabaseRegistry (
    SubsystemKey TEXT PRIMARY KEY,       -- 'Hangfire', 'MassTransit', 'KnowledgeVault', 'PersonalCrm', 'FinanceLedger', 'BudgetGov', 'InfraHealth', 'Netrunner'
    DatabasePath TEXT NOT NULL,          -- Относительный/абсолютный путь: 'data/subsystems/knowledge.db'
    SecretVaultKeyRef TEXT NOT NULL,     -- Ключ пароля в SecretsVault: 'db:secret:knowledge'
    IsEncrypted INTEGER NOT NULL,        -- 1 / 0 (флаг криптозащиты)
    CreatedAtUtc TEXT NOT NULL,          -- Дата первой инициализации базы
    LastMigratedAtUtc TEXT,              -- Дата последнего успешного наката миграций
    MetadataJson TEXT                    -- Дополнительные метаданные (версия схемы, размер кэша)
);
```
> [!IMPORTANT]
> **Принцип изоляции:** В `cyberpipe_master.db` **нет и никогда не записываются** бизнес-данные подсистем (контакты, транзакции, задачи, очереди, токены, скрипты). Мастер-база выступает исключительно хранилищем глобальных базовых настроек (`AppGlobalSettings`) и системным справочником размещения хранилищ (`SubsystemDatabaseRegistry`).

#### 2. Изолированные базы данных подсистем (`data/subsystems/*.db`)
Каждая подсистема и сервис имеют свой собственный файл SQLite в режиме `WAL`:
1. **`data/subsystems/hangfire.db`** — планировщик периодических задач и расписаний Hangfire.
2. **`data/subsystems/masstransit.db`** — очереди сообщений, Saga state и Outbox/Inbox (используется **только** MassTransit и никто другой).
3. **`data/subsystems/knowledge.db`** — долгосрочная память «Второй мозг», семантические индексы FTS5 и буфер векторов SIMD (`Memory<float>`).
4. **`data/subsystems/crm.db`** — личная CRM, карточки контактов, каналы связи и социальные связи.
5. **`data/subsystems/finance.db`** — финансовый учет, категории расходов, балансы и выписки.
6. **`data/subsystems/budget.db`** — учет токенов LLM, дневные квоты, жесткие лимиты и чекпоинты задач (`TaskCheckpoints`).
7. **`data/subsystems/health.db`** — аудит доступности инфраструктуры (`InfrastructureHealthRecords`: RabbitMQ, Brainstem, Hangfire).
8. **`data/subsystems/netrunner.db`** — специализированная база детерминированных ETL-скриптов и JSON-схем ассистента Netrunner.

#### 3. Раздельные доступы, криптографические пароли и сейф секретов
* **Уникальный пароль для каждой БД:** Каждая база данных защищена собственным уникальным 256-битным ключом/паролем шифрования (`Password=...`). Общий мастер-пароль на все базы **запрещен**.
* **Хранение паролей в `SecretsVault`:** Все пароли подсистем шифруются алгоритмом **AES-256-GCM** и хранятся в `data/secrets.vault` под защитой платформенного механизма (**Windows DPAPI**, **Linux LibSecret**, **macOS Keychain**).
* **Зануление в оперативной памяти:** При получении пароля из сейфа и формировании строки подключения, промежуточные байтовые буферы очищаются через `CryptographicOperations.ZeroMemory`.
* **Защита на уровне ОС:** Каталог `data/subsystems/` аппаратно и логически изолирован от дочернего процесса `CyberPipe.Brainstem` и песочниц (`ACCESS_DENIED_ACE` на Windows, `bwrap` unmount на Linux, `Seatbelt` deny на macOS).

#### 4. Специализированный сервис управления и доступа (`ISubsystemDatabaseManager`)
За обнаружение, создание, регистрацию, миграции и выдачу защищенных подключений отвечает выделенный сервис `ISubsystemDatabaseManager`:

```csharp
public interface ISubsystemDatabaseManager
{
    /// <summary>
    /// Получить защищенное соединение SQLite к базе конкретной подсистемы.
    /// Если база еще не создана, производит ее авто-создание, настройку WAL и регистрацию в реестре.
    /// </summary>
    Task<SqliteConnection> GetConnectionAsync(string subsystemKey, CancellationToken ct = default);

    /// <summary>
    /// Инициализировать/зарегистрировать базу подсистемы с генерацией нового пароля в SecretsVault.
    /// </summary>
    Task<SubsystemDatabaseDescriptor> ProvisionSubsystemDatabaseAsync(
        string subsystemKey, 
        string relativePath, 
        CancellationToken ct = default);

    /// <summary>
    /// Применить версионные миграции для конкретной подсистемы.
    /// </summary>
    Task MigrateSubsystemDatabaseAsync(string subsystemKey, CancellationToken ct = default);

    /// <summary>
    /// Получить список всех зарегистрированных баз данных из центрального каталога.
    /// </summary>
    Task<IReadOnlyList<SubsystemDatabaseDescriptor>> GetAllDatabasesAsync(CancellationToken ct = default);
}
```

##### Реализация алгоритма доступа (`SubsystemDatabaseManagerService`):
1. **Проверка в реестре:** Сервис читает `SubsystemDatabaseRegistry` из `cyberpipe_master.db`.
2. **Provisioning (если база отсутствует):**
   * Создает директорию `data/subsystems/` (если не существует).
   * Генерирует криптографически стойкий случайный пароль (`RandomNumberGenerator.GetBytes(32)`).
   * Сохраняет пароль в `SecretsVault` под ключом `db:secret:{subsystemKey}`.
   * Выполняет SQL `CREATE TABLE SubsystemDatabaseRegistry ...` в мастер-базе с фиксацией пути и ссылки на ключ сейфа.
   * Настраивает в новой базе `PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;`.
3. **Получение пароля:** Запрашивает расшифрованный пароль из `SecretsVault` по ссылке `SecretVaultKeyRef`.
4. **Открытие подключения:** Конструирует защищенную строку подключения `Data Source={fullPath};Password={secretPassword};Mode=ReadWriteCreate;Cache=Shared;`.
5. **Предотвращение Confused Deputy:** Сервисы инжектируют только свои клиенты (например, `IKnowledgeStore` получает доступ исключительно к ключу `"KnowledgeVault"` и не имеет права запрашивать соединение с `"FinanceLedger"` или `"Hangfire"`).

---

### 4. Глобальный Хаб Состояний (`GlobalStateHub`) и Персистентность Здоровья Инфраструктуры

Для обеспечения прозрачности между демоном `CyberPipe` и клиентскими интерфейсами (Avalonia Desktop UI, CLI, Telegram) в ядре функционирует реактивный синглтон **`GlobalStateHub`**:

```mermaid
flowchart LR
    RMQ["RabbitMQ Health Check<br/>(Автозапуск через Центр)"] -->|Статус / Latency / Ошибки| DB[("data/subsystems/health.db<br/>(Таблица InfrastructureHealthRecords)")]
    DB -->|Загрузка актуального среза| Hub["GlobalStateHub (In-Memory Singleton)<br/>• RabbitMqStatus: Connected, Latency, Error<br/>• BudgetStatus: Spent, HardLimit<br/>• WorkersStatus: Brainstem, Hangfire"]
    Hub -->|gRPC Server Streaming / Named Pipes| UI["Avalonia Desktop UI<br/>(Живой индикатор + Кнопка 'Проверить')"]
```

#### 1. Таблица персистентности в SQLite (`health.db` -> `InfrastructureHealthRecords`):
Каждая проверка инфраструктурного сервиса (автоматическая при старте, периодическая или по кнопке из UI) сохраняется в изолированную базу здоровья `data/subsystems/health.db`:
```sql
CREATE TABLE IF NOT EXISTS InfrastructureHealthRecords (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ServiceName TEXT NOT NULL,         -- 'RabbitMQ', 'Hangfire', 'Brainstem', 'MassTransit'
    Status TEXT NOT NULL,              -- 'Connected', 'Disconnected', 'Starting', 'Degraded', 'Error'
    IsHealthy INTEGER NOT NULL,        -- 1 / 0
    LatencyMs REAL,                    -- Пинг в миллисекундах
    ErrorMessage TEXT,                 -- Детали ошибки запуска или подключения
    CheckedAtUtc TEXT NOT NULL,        -- Время проверки в UTC
    MetadataJson TEXT                  -- Доп. параметры (нода, очереди, порты)
);
CREATE INDEX IF NOT EXISTS idx_infra_health_service ON InfrastructureHealthRecords(ServiceName, CheckedAtUtc DESC);
```

#### 2. Реактивный стейт `GlobalStateHub` в оперативной памяти:
```csharp
public sealed class GlobalStateHub : IGlobalStateHub
{
    private readonly BehaviorSubject<SystemGlobalStateSnapshot> _stateSubject;
    private readonly ISubsystemDatabaseManager _dbManager;

    public SystemGlobalStateSnapshot Current => _stateSubject.Value;
    public IObservable<SystemGlobalStateSnapshot> StateChanges => _stateSubject.AsObservable();

    public async Task UpdateRabbitMqHealthAsync(RabbitMqHealthStatus status, CancellationToken ct = default)
    {
        // 1. Атомарно персистим в изолированную базу здоровья data/subsystems/health.db
        await using var conn = await _dbManager.GetConnectionAsync("InfraHealth", ct);
        await conn.ExecuteAsync(@"
            INSERT INTO InfrastructureHealthRecords (ServiceName, Status, IsHealthy, LatencyMs, ErrorMessage, CheckedAtUtc)
            VALUES (@ServiceName, @Status, @IsHealthy, @LatencyMs, @ErrorMessage, @CheckedAtUtc);",
            new {
                ServiceName = "RabbitMQ",
                Status = status.State.ToString(),
                IsHealthy = status.IsConnected ? 1 : 0,
                LatencyMs = status.PingLatencyMs,
                ErrorMessage = status.ErrorMessage,
                CheckedAtUtc = DateTimeOffset.UtcNow.ToString("O")
            });

        // 2. Мутируем in-memory срез и оповещаем подписчиков (UI через gRPC)
        var updatedSnapshot = Current with { RabbitMq = status, LastUpdatedUtc = DateTimeOffset.UtcNow };
        _stateSubject.OnNext(updatedSnapshot);
    }
}
```

#### 3. Безопасность паролей RabbitMQ в `SecretsVault`:
* Учетные данные RabbitMQ (`rabbitmq:username` и `rabbitmq:password`) шифруются алгоритмом **AES-256-GCM** и хранятся исключительно в `data/secrets.vault` под защитой **Windows DPAPI** / **Linux LibSecret** / **macOS Keychain**.
* В базу `health.db` или мастер-базу пароли **никогда не записываются** (пишутся только статус `Connected/Error`, пинг и имя ноды).
* При первом старте, если учетные данные отсутствуют:
  1. `RabbitMqSupervisionService` генерирует 256-битный случайный пароль.
  2. Создает администратора `cyberpipe_admin` с тегом `administrator`.
  3. Ограничивает права vhost только для созданного пользователя (`rabbitmqctl set_permissions -p / cyberpipe_admin ".*" ".*" ".*"`).
  4. Сохраняет пару в `SecretsVault` и завершает подключение.
  1. `RabbitMqSupervisionService` генерирует 256-битный случайный пароль.
  2. Создает администратора `cyberpipe_admin` с тегом `administrator`.
  3. Ограничивает права vhost только для созданного пользователя (`rabbitmqctl set_permissions -p / cyberpipe_admin ".*" ".*" ".*"`).
  4. Сохраняет пару в `SecretsVault` и завершает подключение.

---

### 5. Разграничение доступа к состоянию (`Need-to-Know Scoping`)

Даже внутри процесса действует принцип минимальных привилегий:
* **Скиллы Хоста (`Skills.Host`):** Имеют доступ только к параметрам своей узкой задачи (например, `MediaControlSkill` видит название трека и безопасный лимит громкости, но не видит историю чатов или ключи API).
* **Воркеры `Tier 3`:** Получают только очищенный выходным санитайзером текст конкретной подзадачи и строго урезанный набор из 2–3 инструментов (`Dynamic Tool Pruning` + `Hard Tool Stripping` при `UntrustedExternal`).

---

### 6. Конфигурация без хардкода (`IOptionsMonitor<T>`)

Все лимиты безопасности и экономии настраиваются в едином прозрачном файле `appsettings.json` (с раскрытием платформенных переменных окружения и поддержкой горячей перезагрузки):

```json
{
  "CyberPipe": {
    "Telegram": {
      "OwnerUserIds": [ 123456789 ],
      "GuestUserIds": [ 987654321 ]
    },
    "Safety": {
      "MaxDaytimeVolumePercent": 60,
      "MaxNightVolumePercent": 25,
      "NightHoursStart": "23:00",
      "NightHoursEnd": "08:00",
      "EnableNsfwFilter": true,
      "AllowedMediaProviders": [ "Spotify", "YandexMusic", "LocalLibrary" ],
      "AllowedShareDirectories": [ "%USERPROFILE%/Work/Exports" ]
    },
    "Budget": {
      "SoftTaskTokenLimit": 4000,
      "HardTaskTokenLimit": 12000,
      "MaxStepOutputTokens": 2048,
      "MaxLlmTurnsPerTask": 3,
      "MaxTokensPerMinute": 15000,
      "DailyBudgetUsdCap": 0.50
    },
    "Router": {
      "Tier1SimilarityThreshold": 0.86,
      "EmbeddingOnnxModelPath": "models/multilingual-e5-small.onnx"
    },
    "Brainstem": {
      "WhisperOnnxModelPath": "models/whisper-small-onnx",
      "VoiceWorkerIdleTimeoutMinutes": 45,
      "VoiceWorkerMaxTasks": 500,
      "DocumentParserSingleUse": true
    },
    "TierModels": {
      "Tier2LightTask": {
        "HostingType": "Local",
        "CuratedModelId": 7,
        "ModelName": "Qwen3-4B",
        "Endpoint": "http://localhost:11434",
        "DefaultContextTokens": 8192,
        "MinRamGb": 4.0
      },
      "Tier3HeavyTask": {
        "HostingType": "RemoteCloud",
        "ModelName": "claude-3-7-sonnet",
        "Endpoint": "https://api.anthropic.com/v1"
      }
    },
    "Assistant": {
      "GitRepositoriesRoot": "%USERPROFILE%/source/repos",
      "MorningBriefingCron": "0 9 * * *",
      "EveningReviewCron": "0 20 * * *",
      "SubscriptionWarningDaysBefore": 3
    },
    "Netrunner": {
      "MaxNodesPerWorkflow": 64,
      "MaxSequentialDepth": 32,
      "MaxParallelBranches": 8,
      "MaxSubWorkflowCallDepth": 3,
      "SoftLlmNodesPerWorkflowRun": 5,
      "MaxRunWorkspaceMb": 128,
      "DefaultHitlTimeoutMinutes": 1440
    }
  }
}
```

