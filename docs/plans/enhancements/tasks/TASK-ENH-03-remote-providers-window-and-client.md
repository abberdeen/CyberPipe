# TASK-ENH-03 — Динамическое управление облачными провайдерами (`RemoteProvidersWindow` и клиент API)

> **Родительский план:** [models-ui-simplification-and-providers.md](../models-ui-simplification-and-providers.md)  
> **Зависимости:** [TASK-ENH-01](TASK-ENH-01-contracts-and-ollama-runtime-api.md)  
> **Статус:** `Done`

---

## 1. Цель задачи

Избавиться от хардкода сторонних провайдеров и дать пользователю простое, интуитивное окно **`RemoteProvidersWindow`** для управления облачными и удалёнными ИИ-провайдерами:
1. Реализовать сервис **`IRemoteProviderService`**, который:
   * хранит профили провайдеров (`RemoteProviderProfileDto`) в секции `"RemoteProviders"` файла `ui-overrides.json`;
   * сохраняет и читает API-ключи строго через защищённое хранилище `ISecretStore` (`SecretKeys.ForModelProvider(providerId)`);
   * умеет проверять подключение к провайдеру и **динамически загружать список доступных моделей** (через `GET /v1/models` для OpenAI-совместимых API: OpenAI, DeepSeek, OpenRouter, Groq, Mistral, Custom Endpoint; либо проверять ключ и формировать каталог для Anthropic и Google Gemini).
2. Обновить **`ModelTestService`** ([IModelTestService.cs](../../../src/CyberPipe.Desktop/Services/IModelTestService.cs)), чтобы тестирование облачных моделей (`ModelHostingType.RemoteCloud`) выполняло **реальный HTTP-запрос к API соответствующего облачного провайдера** с ключом из `ISecretStore`, а не стучалось в локальную Ollama.
3. Создать окно **`RemoteProvidersWindow`** и **`RemoteProvidersViewModel`** с пресетами провайдеров в 1 клик, вводом API-ключа, проверкой связи, загрузкой списка моделей и выбором активных моделей для главного экрана.

---

## 2. Затрагиваемые файлы

1. `src/CyberPipe.Desktop/Services/IRemoteProviderService.cs` — **новый файл** (интерфейс и реализация `RemoteProviderService`).
2. `src/CyberPipe.Desktop/Services/IModelTestService.cs` — **обновление** (добавление реального вызова облачных провайдеров в `ModelTestService`).
3. `src/CyberPipe.Desktop/ViewModels/RemoteProvidersViewModel.cs` — **новый файл** (ViewModel окна управления облачными провайдерами).
4. `src/CyberPipe.Desktop/Views/RemoteProvidersWindow.axaml` — **новый файл** (мастер-деталь интерфейс окна провайдеров).
5. `src/CyberPipe.Desktop/Views/RemoteProvidersWindow.axaml.cs` — **новый файл** (code-behind окна).
6. `tests/CyberPipe.Tests.Unit/RemoteProvidersViewModelTests.cs` — **новый файл** (юнит-тесты сервиса провайдеров, ViewModel и облачного теста в `ModelTestService`).

---

## 3. Детальные требования к реализации

### 3.1. Сервис `IRemoteProviderService` (`src/CyberPipe.Desktop/Services/IRemoteProviderService.cs`)

1. **Интерфейс `IRemoteProviderService`:**
   ```csharp
   public sealed record RemoteProviderProbeResult
   {
       public required bool Success { get; init; }
       public string? Message { get; init; }
       public long LatencyMs { get; init; }
       public IReadOnlyList<RemoteModelEntryDto> DiscoveredModels { get; init; } = [];
   }

   public interface IRemoteProviderService
   {
       IReadOnlyList<RemoteProviderProfileDto> LoadProviders();
       void SaveProviders(IEnumerable<RemoteProviderProfileDto> providers);
       Task<bool> HasApiKeyAsync(string providerId, CancellationToken ct = default);
       Task SaveApiKeyAsync(string providerId, string plainApiKey, CancellationToken ct = default);
       Task<SecretValue?> GetApiKeyAsync(string providerId, CancellationToken ct = default);
       Task DeleteApiKeyAsync(string providerId, CancellationToken ct = default);
       Task<RemoteProviderProbeResult> ProbeAndDiscoverModelsAsync(
           RemoteProviderProfileDto provider,
           string? unsavedApiKey = null,
           CancellationToken ct = default);
       IReadOnlyList<RemoteProviderProfileDto> GetBuiltInPresets();
   }
   ```
2. **Встроенные пресеты (`GetBuiltInPresets()`):**
   При первом открытии (если в `ui-overrides.json` ещё нет секции `"RemoteProviders"`) сервис инициализирует стартовые профили на базе курируемого каталога `cloud-models.json` и популярных пресетов:
   * **OpenAI** (`id: "openai"`, `Protocol: OpenAiCompatible`, `BaseUrl: "https://api.openai.com/v1"`, стартовые модели: `gpt-4o-mini`, `gpt-4.1`);
   * **Anthropic** (`id: "anthropic"`, `Protocol: Anthropic`, `BaseUrl: "https://api.anthropic.com/v1"`, стартовые модели: `claude-3-5-haiku`, `claude-3-7-sonnet`);
   * **Google Gemini** (`id: "google"`, `Protocol: GoogleGemini`, `BaseUrl: "https://generativelanguage.googleapis.com/v1beta"`, стартовые модели: `gemini-2.5-flash-lite`, `gemini-2.5-pro`);
   * **DeepSeek** (`id: "deepseek"`, `Protocol: OpenAiCompatible`, `BaseUrl: "https://api.deepseek.com/v1"`, стартовые модели: `deepseek-chat`, `deepseek-reasoner`);
   * **OpenRouter** (`id: "openrouter"`, `Protocol: OpenAiCompatible`, `BaseUrl: "https://openrouter.ai/api/v1"`, стартовые модели: `openai/gpt-4o-mini`, `anthropic/claude-3.7-sonnet`, `deepseek/deepseek-r1`);
   * **Custom OpenAI-Compatible** (шаблон для создания любого собственного провайдера с произвольным `BaseUrl`).
3. **Динамическое обнаружение моделей (`ProbeAndDiscoverModelsAsync`):**
   * Для `OpenAiCompatible` отправляет `GET {BaseUrl}/models` с заголовком `Authorization: Bearer {apiKey}` (если ключ задан). Парсит массив `data[].id` из JSON-ответа, объединяет с уже настроенными моделями провайдера и возвращает `RemoteProviderProbeResult`.
   * Для `GoogleGemini` отправляет `GET {BaseUrl}/models?key={apiKey}`, парсит `models[].name` (убирая префикс `models/`).
   * Для `Anthropic` отправляет `GET {BaseUrl}/models` с заголовками `x-api-key: {apiKey}` и `anthropic-version: 2023-06-01`.
   * При ошибках 401/403 возвращает понятное сообщение на русском языке: `"Ошибка авторизации (HTTP 401/403): проверьте правильность API-ключа."`.
4. **Хранение секретов в Desktop:**
   * Принимает `ISecretStore?` в конструкторе. Если `ISecretStore` не передан извне, использует безопасное локальное хранилище секретов (либо обёртку на базе DPAPI / пользовательского защищённого хранилища в `%LocalAppData%\CyberPipe\secrets`), гарантируя, что в `ui-overrides.json` никогда не попадают открытые значения API-ключей!

### 3.2. Реальное тестирование облачных моделей в `ModelTestService` (`src/CyberPipe.Desktop/Services/IModelTestService.cs`)

Обновить `ModelTestService`:
* Добавить опциональную зависимость `IRemoteProviderService? _remoteProviders`.
* Если `hostingType == ModelHostingType.RemoteCloud`:
  1. Найти подходящий профиль провайдера по `provider` (по `Id` или `Name`) или по `modelId` среди `LoadProviders()`.
  2. Получить API-ключ через `GetApiKeyAsync(profile.Id)`. Если ключ не задан — сразу вернуть понятную подсказку:
     `"Для модели '{modelId}' ({profile.Name}) не настроен API-ключ. Откройте «Управление облачными провайдерами» и укажите ключ доступа."`
  3. Выполнить реальный тестовый запрос в зависимости от `profile.Protocol`:
     * `OpenAiCompatible`: `POST {BaseUrl}/chat/completions` (`{ model = runtimeModelId, messages = [{ role = "user", content = prompt }], max_tokens = 512 }`);
     * `Anthropic`: `POST {BaseUrl}/messages` (`x-api-key`, `anthropic-version: 2023-06-01`, `{ model = runtimeModelId, max_tokens = 512, messages = [...] }`);
     * `GoogleGemini`: `POST {BaseUrl}/models/{runtimeModelId}:generateContent?key={apiKey}` (`{ contents = [{ parts = [{ text = prompt }] }] }`).
  4. Извлечь текст ответа, число входных/выходных токенов и время отклика `LatencyMs`.

### 3.3. `RemoteProvidersViewModel` и `RemoteProvidersWindow.axaml`

1. **Мастер-деталь компоновка окна (`Width="940" Height="660"`):**
   * **Левая панель (Список провайдеров, ширина `260px`):**
     * Список `Providers` (`ObservableCollection<RemoteProviderItemViewModel>`): название, протокол, бейдж состояния (`🟢 Ключ настроен` / `⚪ Нет ключа`, тумблер `IsEnabled`).
     * Кнопка **`[+ Добавить провайдер]`** и быстрые кнопки-шаблоны (`+ OpenRouter`, `+ Groq`, `+ Свой OpenAI URL`).
   * **Правая панель (Редактирование выбранного провайдера):**
     * Поля: `Название` (`Name`), `Тип протокола` (`ComboBox`: OpenAI-совместимый / Anthropic / Google Gemini), `Базовый URL API` (`BaseUrl`), тумблер `Провайдер включён`.
     * Блок API-ключа: поле ввода `ApiKeyInput` (`PasswordChar="•"`), индикатор `HasSavedApiKey` (`"✓ Ключ сохранён в защищённом хранилище"`), кнопки **`[💾 Сохранить ключ и настройки]`** и **`[🗑 Удалить ключ]`**.
     * Кнопка **`[🔄 Проверить связь и загрузить модели]`** (`ProbeProviderCommand`) + строка статуса проверки (`ProbeStatusText`).
     * **Список моделей провайдера (`Models`):**
       * Поиск по моделям провайдера (`ModelFilterText`) — особенно полезно, когда OpenRouter возвращает десятки моделей;
       * Для каждой модели: чекбокс `IsEnabled` (*«Показывать в списке выбора»*), `ModelId`, `DisplayName`, переключатель рекомендации (`Быстрый помощник Tier 2` / `Умный эксперт Tier 3`), кнопки `[⚡ Назначить Быстрым]`, `[🧠 Назначить Экспертом]`, `[💬 Тест]`, `[✖]`;
       * Строка ручного добавления модели: поле `NewModelId` + `NewModelDisplayName` + кнопка **`[+ Добавить модель]`**.
2. **Коллбэки для главного экрана:**
   * `Action? OnProvidersUpdated` — вызывается при сохранении провайдера или изменении списка моделей, чтобы главный экран `ModelsView` сразу обновил выпадающие списки облачных моделей;
   * `Action<IntentTier, string, string>? OnAssignRemoteModelToTier` (`tier`, `modelId`, `providerName`);
   * `Action<string, string, string>? OnTestRemoteModelRequested` (`modelId`, `providerName`, `displayName`).

---

## 4. Критерии приёмки и проверка

1. Пользователь может добавить новый провайдер (из пресета или `Custom OpenAI-Compatible`), сохранить его `BaseUrl` в `ui-overrides.json` и API-ключ в `ISecretStore` (в JSON-файле открытого ключа нет!).
2. Кнопка «Проверить связь и загрузить модели» (`ProbeProviderCommand`) на фейковом `HttpMessageHandler` успешно парсит ответ `GET /v1/models` и пополняет список моделей провайдера.
3. Пользователь может добавить произвольную модель вручную по `ModelId`, включить/выключить модели чекбоксами и назначить модель на `Tier 2` или `Tier 3`.
4. `ModelTestService` при `ModelHostingType.RemoteCloud` вызывает API настроенного облачного провайдера и возвращает ответ и токены.
5. Команды проверки:
   ```powershell
   dotnet build CyberPipe.slnx
   dotnet test tests/CyberPipe.Tests.Unit/CyberPipe.Tests.Unit.csproj --filter "FullyQualifiedName~RemoteProviders|FullyQualifiedName~ModelTest"
   ```
