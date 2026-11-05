# TASK-ENH-01 — Контракты провайдеров/рантаймов и расширение движка Ollama

> **Родительский план:** [models-ui-simplification-and-providers.md](../models-ui-simplification-and-providers.md)  
> **Зависимости:** Нет (первая задача цепочки)  
> **Статус:** `Todo`

---

## 1. Цель задачи

Подготовить контрактный и сервисный фундамент в `CyberPipe.Contracts` и `CyberPipe.Models` для двух будущих окон управления и упрощённого главного экрана:
1. Расширить возможности локального движка Ollama (`OllamaLocalModelInstaller`), чтобы он работал как полноценный враппер над Ollama API: возвращал полные метаданные всех установленных моделей (`GET /api/tags`), показывал загруженные в оперативную/видеопамять модели (`GET /api/ps`), умел выгружать модель из памяти (`keep_alive: 0`) и скачивать любую модель из реестра Ollama по произвольному тегу (`PullByTagAsync`).
2. Заложить контракт локального провайдера рантайма (`LocalRuntimeKind` / `LocalRuntimeDescriptor`), чтобы кроме Ollama в будущем можно было подключать отдельные окна для **LM Studio**, **llama.cpp** и других локальных серверов.
3. Добавить контракты динамических удалённых (облачных) провайдеров (`RemoteProviderProtocol`, `RemoteModelEntryDto`, `RemoteProviderProfileDto`) и формирование ключей секретов `SecretKeys.ForModelProvider(providerId)` для безопасного хранения API-ключей в `ISecretStore`.

---

## 2. Затрагиваемые файлы

1. `src/CyberPipe.Contracts/LocalModels.cs` — расширение DTO и интерфейса `ILocalModelInstaller`.
2. `src/CyberPipe.Contracts/Models.cs` — добавление DTO для динамических облачных провайдеров и дескриптора локального рантайма.
3. `src/CyberPipe.Contracts/Secrets.cs` — добавление метода `SecretKeys.ForModelProvider(string providerId)`.
4. `src/CyberPipe.Models/ModelInstaller.cs` — реализация новых методов API Ollama в `OllamaLocalModelInstaller`.
5. `tests/CyberPipe.Tests.Unit/LocalModelInstallerTests.cs` — модульные тесты новых операций Ollama (`ListRunningAsync`, `UnloadAsync`, `PullByTagAsync`, расширенный `ListInstalledAsync`).

---

## 3. Детальные требования к реализации

### 3.1. Расширение `src/CyberPipe.Contracts/LocalModels.cs`

1. **Дополнить `InstalledLocalModel` необязательными метаданными из `/api/tags`** (с сохранением обратной совместимости существующих свойств `ModelId` и `SizeBytes`):
   ```csharp
   public sealed record InstalledLocalModel
   {
       public required string ModelId { get; init; }
       public long? SizeBytes { get; init; }
       public string? Family { get; init; }
       public string? ParameterSize { get; init; }
       public string? QuantizationLevel { get; init; }
       public DateTimeOffset? ModifiedAtUtc { get; init; }
   }
   ```
2. **Добавить DTO `OllamaRunningModelDto` (модель, загруженная в RAM/VRAM по данным `GET /api/ps`):**
   ```csharp
   public sealed record OllamaRunningModelDto
   {
       public required string ModelId { get; init; }
       public long SizeBytes { get; init; }
       public long SizeVramBytes { get; init; }
       public DateTimeOffset? ExpiresAtUtc { get; init; }
   }
   ```
3. **Добавить в `ILocalModelInstaller` методы управления рантаймом Ollama** (с реализацией по умолчанию через default interface methods или явной реализацией во всех классах/фейках в тестах, чтобы не сломать существующие фейки):
   * `Task<IReadOnlyList<OllamaRunningModelDto>> ListRunningAsync(CancellationToken cancellationToken = default)` — список моделей, которые сейчас находятся в памяти рантайма (`/api/ps`).
   * `Task<Result> UnloadAsync(string modelId, CancellationToken cancellationToken = default)` — выгрузка модели из оперативной/видеопамяти (`POST /api/generate` с `{ model = modelId, keep_alive = 0 }`).
   * `Task<Result<ModelProbeResult>> PullByTagAsync(string modelTag, IProgress<ModelInstallProgress>? progress = null, CancellationToken cancellationToken = default)` — скачивание и проверка произвольной модели из библиотеки Ollama по строковому тегу (например, `"qwen2.5-coder:7b"`), даже если её нет в статическом JSON-каталоге.

> **Важно:** Чтобы не ломать существующие тестовые фейки `ILocalModelInstaller` в `InstallerViewModelTests.cs` и других тестовых файлах, либо снабдите новые методы интерфейса `ILocalModelInstaller` дефолтной реализацией в интерфейсе (возвращающей пустой список / делегирующей в `InstallAsync`), либо обновите тестовые фейки.

### 3.2. Добавление контрактов провайдеров и локальных рантаймов в `src/CyberPipe.Contracts/Models.cs`

1. **Тип локального рантайма (задел под отдельные окна Ollama, LM Studio и др.):**
   ```csharp
   public enum LocalRuntimeKind
   {
       Ollama = 0,
       LmStudio = 1,
       LlamaCpp = 2,
       CustomOpenAiLocal = 3,
   }

   public sealed record LocalRuntimeDescriptorDto
   {
       public required string Id { get; init; }
       public required string Name { get; init; }
       public required LocalRuntimeKind Kind { get; init; }
       public required string ApiBaseUrl { get; init; }
       public bool SupportsModelPull { get; init; }
       public bool SupportsMemoryUnload { get; init; }
       public bool IsImplemented { get; init; } = true;
   }
   ```
2. **Контракты динамических облачных провайдеров (уход от хардкода):**
   ```csharp
   public enum RemoteProviderProtocol
   {
       OpenAiCompatible = 0,
       Anthropic = 1,
       GoogleGemini = 2,
   }

   public sealed record RemoteModelEntryDto
   {
       public required string ModelId { get; init; }
       public required string DisplayName { get; init; }
       public ModelTaskCategory RecommendedFor { get; init; } = ModelTaskCategory.HeavyReasoningTier3;
       public bool IsEnabled { get; init; } = true;
       public int ContextWindowTokens { get; init; } = 128000;
       public decimal? InputPricePer1mUsd { get; init; }
       public decimal? OutputPricePer1mUsd { get; init; }
       public string? Description { get; init; }
   }

   public sealed record RemoteProviderProfileDto
   {
       public required string Id { get; init; }
       public required string Name { get; init; }
       public required RemoteProviderProtocol Protocol { get; init; }
       public required string BaseUrl { get; init; }
       public required string ApiKeySecretName { get; init; }
       public bool IsEnabled { get; init; } = true;
       public IReadOnlyList<RemoteModelEntryDto> Models { get; init; } = [];
   }
   ```

### 3.3. Ключи секретов для динамических провайдеров в `src/CyberPipe.Contracts/Secrets.cs`

В класс `SecretKeys` добавить:
* Константу префикса `public const string ModelProviderPrefix = "provider.";`
* Статический метод:
  ```csharp
  public static string ForModelProvider(string providerId)
  {
      ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
      return $"provider.{providerId.Trim().ToLowerInvariant()}.apiKey";
  }
  ```

### 3.4. Реализация новых методов Ollama API в `src/CyberPipe.Models/ModelInstaller.cs`

В классе `OllamaLocalModelInstaller`:
1. **`ListInstalledAsync`:**
   * При разборе ответа `GET /api/tags` помимо `name` и `size` дополнительно парсить `modified_at` и вложенный объект `details` (`family`, `parameter_size`, `quantization_level`), если они присутствуют в JSON.
2. **`ListRunningAsync`:**
   * Выполнять `GET /api/ps`.
   * Из массива `models` извлекать `name`, `size`, `size_vram`, `expires_at` в список `OllamaRunningModelDto`. При ошибке соединения или 404 возвращать пустой список или пробрасывать управляемо согласно конвенции класса.
3. **`UnloadAsync(string modelId)`:**
   * Проверять `modelId` на пустоту -> `Result.Failure("Model.NoRuntimeId", ...)`.
   * Отправлять `POST /api/generate` с телом `{ model = modelId, prompt = "", keep_alive = 0, stream = false }`.
   * При успешном HTTP-ответе возвращать `Result.Success()`, иначе `Result.Failure("Model.UnloadFailed", ...)`.
4. **`PullByTagAsync(string modelTag, IProgress<ModelInstallProgress>? progress, CancellationToken ct)`:**
   * Проверять `modelTag` на пустоту.
   * Если модель с таким `modelTag` или `id` есть в `_catalog.LocalModels`, использовать её дескриптор; иначе создавать синтетический `ModelDescriptorDto` с `HostingType = ModelHostingType.Local`, `ModelId = modelTag.Trim()`, `Name = modelTag.Trim()`, `DefaultContextTokens = 8192` и вызывать существующий пайплайн `InstallAsync` (или `PullAsync` + `VerifyAsync`), чтобы работали и прогресс-бар, и отмена, и проверка после скачивания.

---

## 4. Критерии приёмки и проверка

1. `ContractsBoundaryTests` и `ProjectGraphTests` проходят без ошибок (в `CyberPipe.Contracts` нет запрещённых слов и внешних зависимостей).
2. В `LocalModelInstallerTests.cs` добавлены и проходят юнит-тесты для:
   * `ListInstalledAsync` с разбором `details.family`, `details.parameter_size`, `details.quantization_level`;
   * `ListRunningAsync` (`GET /api/ps`) с корректным чтением `SizeBytes` и `SizeVramBytes`;
   * `UnloadAsync` с проверкой отправки `keep_alive = 0`;
   * `PullByTagAsync` для произвольного тега модели вне каталога (например, `"qwen2.5-coder:7b"`).
3. Команды проверки:
   ```powershell
   dotnet build CyberPipe.slnx
   dotnet test tests/CyberPipe.Tests.Architecture/CyberPipe.Tests.Architecture.csproj
   dotnet test tests/CyberPipe.Tests.Unit/CyberPipe.Tests.Unit.csproj --filter "FullyQualifiedName~LocalModelInstaller|FullyQualifiedName~ConfigAndSecrets"
   ```
