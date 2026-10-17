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

### 3. Сверхбыстрое локальное хранилище (`SQLite WAL`, In-Memory SIMD и Миграции)

Отказ от обязательного PostgreSQL и RabbitMQ в пользу правильно настроенного **SQLite в режиме `WAL` (Write-Ahead Logging)** дает идеальный баланс надежности и скорости:

1. **Раздельный Семантический Кэш (`SemanticIntentCache` + ролевой `SemanticResponseCache`):**
   * **`SemanticIntentCache`:** хранит сопоставление `(EmbeddingVector, IntentName, SkillName, CreatedAtUtc)` для мгновенного распознавания команд.
   * **`SemanticResponseCache`:** хранит статические ответы со строгой ролевой изоляцией **`(EmbeddingVector, UserRole, Answer, CreatedAtUtc, ExpiresAtUtc, HitCount)` и `TTL = 24 часа`**. Ответы, сгенерированные для `Owner`, физически никогда не возвращаются пользователям с ролью `Guest`, а динамические запросы (*«какие сегодня встречи?»*) не кэшируются как статический текст.
2. **Память «Второй мозг», Личная CRM и In-Memory SIMD Буфер (`KnowledgeVault`, `PersonalCrm`, `FinanceLedger`):**
   * Каждая запись в `KnowledgeVault` и `PersonalCrm` обязательно хранит столбец **`TrustSource` (`MemoryTrustSource`)** для защиты от отложенных промпт-инъекций.
   * Для мгновенного векторного поиска без чтения всех `BLOB` с диска в оперативной памяти поддерживается компактный плоский буфер нормализованных векторов **`Memory<float>`** (для 10 000 записей по 384 `float` это всего `~15 МБ` RAM!), обеспечивающий полное SIMD-сканирование `TensorPrimitives.CosineSimilarity` за **`< 1.5 мс`** в связке с полнотекстовым индексом **`SQLite FTS5`**.
3. **Журнал Бюджета и Чекпоинты Пауз (`BudgetLedger` & `TaskCheckpoints`):**
   * Точный учет каждого потраченного токена по задачам и часам.
   * При срабатывании авто-паузы (`TaskLifecycleState.PausedByBudget`) полное состояние задачи сохраняется одной атомарной записью. Даже если ПК перезагрузится, после нажатия кнопки `[▶️ Разрешить еще +5 000 токенов]` в Telegram задача продолжит работу с того же шага!
4. **Асинхронная запись (`Write-Behind Channel`) и синхронный `InFlightOperations`:**
   * Запись логов, аудита и статистики токенов идет через неблокирующий `Channel<PersistItem>` батчами раз в 200 мс, а синхронный `COMMIT` в `InFlightOperations` применяется только для тяжелых/внешних входов (`Brainstem`, файлы, `Tier 2/3`).
   * Фоновый воркер `WalCheckpointWorker` следит за размером журнала (`PRAGMA wal_checkpoint(TRUNCATE)`).
5. **Версионные миграции схемы БД и авто-бэкап (`SqliteMigrationRunner` & `SqliteBackupService`):**
   * При старте `CyberPipe.Storage.Sqlite` проверяет `PRAGMA user_version` и последовательно накатывает встроенные SQL-миграции внутри транзакции.
   * Перед применением новых миграций при обновлении версии автоматически создается горячий бэкап `cyberpipe.v{N}.bak` (хранятся последние 3 снимка) для безопасного отката в режиме `Side-by-Side Update`.

---

### 4. Разграничение доступа к состоянию (`Need-to-Know Scoping`)

Даже внутри процесса действует принцип минимальных привилегий:
* **Скиллы Хоста (`Skills.Host`):** Имеют доступ только к параметрам своей узкой задачи (например, `MediaControlSkill` видит название трека и безопасный лимит громкости, но не видит историю чатов или ключи API).
* **Воркеры `Tier 3`:** Получают только очищенный выходным санитайзером текст конкретной подзадачи и строго урезанный набор из 2–3 инструментов (`Dynamic Tool Pruning` + `Hard Tool Stripping` при `UntrustedExternal`).

---

### 5. Конфигурация без хардкода (`IOptionsMonitor<T>`)

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
    }
  }
}
```
