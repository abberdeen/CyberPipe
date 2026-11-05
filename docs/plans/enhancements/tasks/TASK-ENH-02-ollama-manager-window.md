# TASK-ENH-02 — Отдельное окно-враппер управления Ollama (`OllamaManagerWindow`)

> **Родительский план:** [models-ui-simplification-and-providers.md](../models-ui-simplification-and-providers.md)  
> **Зависимости:** [TASK-ENH-01](TASK-ENH-01-contracts-and-ollama-runtime-api.md)  
> **Статус:** `Todo`

---

## 1. Цель задачи

Создать специализированное окно управления локальным рантаймом Ollama (`OllamaManagerWindow` + `OllamaManagerViewModel`), которое работает как удобный, наглядный GUI-враппер над Ollama:
* Показывает статус сервера Ollama, характеристики ПК (ОЗУ, свободная память, диск) и **живой список моделей в памяти RAM/VRAM** (`/api/ps`) с кнопкой мгновенной выгрузки;
* Отображает **все реально установленные в Ollama модели** (`/api/tags`), даже если пользователь скачал их вручную вне стандартного каталога из 15 моделей;
* Содержит понятную витрину **«Рекомендованные для вашего ПК»** с простыми человеческими фильтрами (*«Все»*, *«Идеально для моего ПК»*, *«Быстрые (Tier 2)»*, *«Умные и для кода (Tier 3)»*);
* Позволяет **скачать любую модель из реестра Ollama по имени/тегу** (`ollama pull <tag>`) с прогресс-баром и отменой;
* Включает управление модулем семантического поиска `multilingual-e5-small (ONNX)`.

---

## 2. Затрагиваемые файлы

1. `src/CyberPipe.Desktop/ViewModels/OllamaManagerViewModel.cs` — **новый файл** (ViewModel окна-враппера Ollama и карточки установленных/рекомендованных моделей).
2. `src/CyberPipe.Desktop/Views/OllamaManagerWindow.axaml` — **новый файл** (разметка окна с вкладками и панелью состояния рантайма).
3. `src/CyberPipe.Desktop/Views/OllamaManagerWindow.axaml.cs` — **новый файл** (code-behind окна с закрытием по команде).
4. `tests/CyberPipe.Tests.Unit/OllamaManagerViewModelTests.cs` — **новый файл** (модульные тесты логики враппера Ollama).

*(Примечание: существующий `LocalModelInstallerViewModel.cs` в этой задаче не удаляется, чтобы старые тесты продолжали проходить до финальной перестройки главного экрана в `TASK-ENH-04`).*

---

## 3. Детальные требования к реализации

### 3.1. `OllamaManagerViewModel` (`src/CyberPipe.Desktop/ViewModels/OllamaManagerViewModel.cs`)

1. **Конструктор и зависимости:**
   Принимает `IReadOnlyList<ModelDescriptorDto> localCatalog`, `ILocalModelInstaller installer`, `ILocalRuntimeInstaller runtime`, `IModelCompatibilityAdvisor? advisor = null`, `IDesktopNotificationService? notifications = null`.
2. **Блок состояния сервера и железа ПК (Header Bar):**
   * `RuntimeStatusText`, `IsRuntimeReady`, `ApiBaseUrl` (например, `http://localhost:11434`).
   * `HardwareSummaryText` — человекопонятная сводка от `_advisor.GetHardwareProfileAsync()` (например: `💻 ОЗУ: 32.0 ГБ (свободно 18.4 ГБ) • Свободно на диске: 120.5 ГБ`).
   * Команды: `RefreshCommand`, `EnsureRuntimeCommand`.
3. **Монитор активной памяти (`RunningModels` из `GET /api/ps`):**
   * `ObservableCollection<RunningOllamaModelItemViewModel> RunningModels`:
     * `ModelId`, `SizeFormatted` (например, `5.2 ГБ в памяти (VRAM: 4.8 ГБ)`), `UnloadCommand`.
   * `HasRunningModels` (`RunningModels.Count > 0`), `MemoryStatusText` (например: `В памяти сейчас: 1 модель (5.2 ГБ)` или `Память свободна (нет загруженных моделей)`).
   * Метод `UnloadModelAsync(string modelId)` вызывает `_installer.UnloadAsync(modelId)` и обновляет список `RunningModels`.
4. **Вкладка 1: «📦 Установленные в Ollama» (`InstalledModels`):**
   * Формируется из **всех** записей `_installer.ListInstalledAsync()`, а не только из 15 моделей каталога!
   * Если установленная модель совпадает с записью из `localCatalog` (по `ModelId` или каноническому имени без `:latest`), она обогащается дружелюбным именем, описанием `BestFor` и вердиктом совместимости `VerdictText`. Если модель сторонняя (например, скачанная через CLI `qwen2.5-coder:7b`), она отображается со своим тегом, реальным размером `SizeBytes`, `ParameterSize` и `QuantizationLevel` из `/api/tags`.
   * Действия для каждой установленной модели:
     * `AssignToFastAssistantCommand` (назначить на роль «Быстрый помощник» / `Tier 2`);
     * `AssignToSmartExpertCommand` (назначить на роль «Умный эксперт» / `Tier 3`);
     * `TestCommand` (открыть диалог тест-драйва модели);
     * `VerifyCommand` (пробный запрос);
     * `RemoveCommand` (удаление из Ollama с обновлением списка).
5. **Вкладка 2: «🌟 Рекомендованные для вашего ПК» (`RecommendedModels` + Фильтры):**
   * Список из 15 курируемых моделей каталога с оценкой от `IModelCompatibilityAdvisor`.
   * Фильтрация (`SelectedFilter`: `"all"`, `"optimal"`, `"tier2"`, `"tier3"`):
     * `All` — все 15 моделей (отсортированные советником: сначала `🟢 Оптимально`, затем `🟡 Совместимо`, внизу `🔴 Мало памяти`);
     * `Optimal` — только модели с вердиктом `HardwareFitVerdict.Optimal`;
     * `Tier2` — лёгкие и быстрые модели (`LightFastTier2`);
     * `Tier3` — модели для сложных задач и кода (`HeavyReasoningTier3`).
   * `FilteredRecommendedModels` — коллекция для привязки в UI, автоматически обновляющаяся при переключении фильтра или поискового запроса `SearchQuery`.
   * Полная поддержка установки (`InstallCommand`), прогресса (`ProgressPercent`, `ProgressText`), отмены (`CancelCommand`) и подтверждения риска при нехватке ОЗУ (`ConfirmRiskCommand`).
6. **Вкладка 3 / Панель: «🔍 Скачать любую модель по тегу (Ollama Pull)»:**
   * Свойства: `CustomPullTag` (строка ввода, например `qwen2.5-coder:7b`), `IsCustomPulling`, `CustomPullProgressPercent`, `CustomPullProgressText`, `CustomPullStatusMessage`.
   * Быстрые чипсы-подсказки популярных тегов: `qwen2.5-coder:7b`, `llama3.2:3b`, `deepseek-r1:7b`, `nomic-embed-text`.
   * Команды: `PullCustomTagCommand` (вызывает `_installer.PullByTagAsync(CustomPullTag, ...)` и после успеха обновляет `InstalledModels` + уведомляет главное окно через колбэк `OnModelsChanged`), `CancelCustomPullCommand`.
7. **Коллбэки для связи с главным экраном `ModelsViewModel`:**
   * `Action<IntentTier, string>? OnAssignModelToTier` — вызывается при нажатии «Назначить Быстрым помощником (Tier 2)» или «Назначить Экспертом (Tier 3)»;
   * `Action<string, string?>? OnTestModelRequested` — вызывается при нажатии «💬 Тест»;
   * `Action? OnInstalledModelsChanged` — уведомляет главный экран, когда модель была установлена или удалена, чтобы выпадающие списки на главном экране обновились автоматически.

### 3.2. `OllamaManagerWindow.axaml` (`src/CyberPipe.Desktop/Views/OllamaManagerWindow.axaml`)

* Размер окна: `Width="920" Height="680" MinWidth="760" MinHeight="520"`, `WindowStartupLocation="CenterOwner"`, заголовок `"Управление локальными моделями — Ollama"`.
* Верхняя карточка:
  * Статус сервера Ollama + адрес API + паспорт железа ПК + кнопки `[🔄 Обновить]` и `[📥 Установить / Проверить Ollama]`.
  * Блок активной памяти (виден, когда `HasRunningModels == true`): список загруженных в RAM/VRAM моделей с кнопкой `[⏏ Выгрузить из памяти]`.
* `TabControl` на 3 понятные вкладки:
  1. **«📦 Установленные модели»** (счётчик в заголовке, список всех моделей из `/api/tags`, кнопки назначения в 1 клик, теста и удаления; если список пуст — дружелюбная подсказка и кнопка перехода на вкладку «Рекомендованные»).
  2. **«🌟 Рекомендованные для вашего ПК»** (переключатели фильтров + поле быстрого поиска + карточки моделей с индикаторами `🟢 / 🟡 / 🔴`, размером в ГБ и кнопкой скачивания).
  3. **«🔍 Скачать по имени (Pull) & Поиск ONNX»** (блок ручного скачивания любой модели по тегу из библиотеки Ollama + карточка модуля системного поиска `multilingual-e5-small ONNX`).
* Нижняя панель с кнопкой `[Закрыть]`.

---

## 4. Критерии приёмки и проверка

1. `OllamaManagerViewModel` корректно отображает **все** модели, возвращённые `ILocalModelInstaller.ListInstalledAsync()`, включая модели, отсутствующие в `local-models.json`.
2. Фильтры на вкладке «Рекомендованные» (`All`, `Optimal`, `Tier2`, `Tier3`) и поиск по строке мгновенно фильтруют `FilteredRecommendedModels`.
3. Вызов `PullCustomTagCommand` скачивает произвольную модель по тегу с отображением прогресса и после завершения добавляет её в `InstalledModels`.
4. Вызов `UnloadCommand` на активной модели в памяти вызывает `_installer.UnloadAsync` и обновляет `RunningModels`.
5. Команды проверки:
   ```powershell
   dotnet build CyberPipe.slnx
   dotnet test tests/CyberPipe.Tests.Unit/CyberPipe.Tests.Unit.csproj --filter "FullyQualifiedName~OllamaManagerViewModel"
   ```
