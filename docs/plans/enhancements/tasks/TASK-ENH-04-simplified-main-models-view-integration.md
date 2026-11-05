# TASK-ENH-04 — Радикальное упрощение главного экрана `ModelsView` и связка всех окон

> **Родительский план:** [models-ui-simplification-and-providers.md](../models-ui-simplification-and-providers.md)  
> **Зависимости:** [TASK-ENH-02](TASK-ENH-02-ollama-manager-window.md), [TASK-ENH-03](TASK-ENH-03-remote-providers-window-and-client.md)  
> **Статус:** `Done`

---

## 1. Цель задачи

Завершить трансформацию раздела «Models» в `CyberPipe.Desktop`:
1. Заменить перегруженную «простыню» в [ModelsView.axaml](../../../src/CyberPipe.Desktop/Views/ModelsView.axaml) на **простой, чистый и интуитивно понятный каждому пользователю экран**, состоящий всего из 3 наглядных блоков:
   * **Карточки источников моделей и статус ПК** (с кнопками открытия отдельных окон `OllamaManagerWindow` и `RemoteProvidersWindow`, а также списком локальных движков с архитектурным заделом под LM Studio);
   * **Две понятные карточки ролей ИИ:** **«⚡ Быстрый помощник»** (`Tier 2`) и **«🧠 Умный эксперт»** (`Tier 3`) с переключателем `[🏠 На моём ПК (Бесплатно)]` ↔ `[☁️ В облаке (По API)]`, выпадающим списком реально доступных моделей, автосохранением и кнопкой `[💬 Проверить]`;
   * **Компактная строка статуса системного поиска** (`Tier 1 ONNX multilingual-e5-small`), без отображения технического `Tier 0 (Regex)` в качестве редактируемой модели.
2. Убрать из [ModelsViewModel.cs](../../../src/CyberPipe.Desktop/ViewModels/ModelsViewModel.cs) захардкоженные массивы моделей и подключить динамическое формирование списков кандидатов из `OllamaManagerViewModel` (реально установленные локальные модели) и `IRemoteProviderService` (включённые модели активных облачных провайдеров).
3. Связать открытие окон `OllamaManagerWindow`, `RemoteProvidersWindow` и `ModelTestWindow` в [MainWindow.axaml.cs](../../../src/CyberPipe.Desktop/MainWindow.axaml.cs) и [DesktopModelScreen.cs](../../../src/CyberPipe.Desktop/Services/DesktopModelScreen.cs).

---

## 2. Затрагиваемые файлы

1. `src/CyberPipe.Desktop/ViewModels/ModelsViewModel.cs` — рефакторинг и интеграция с `OllamaManagerViewModel` и `RemoteProvidersViewModel` / `IRemoteProviderService`.
2. `src/CyberPipe.Desktop/Views/ModelsView.axaml` — полный редизайн разметки главного экрана в сторону минимализма и понятности.
3. `src/CyberPipe.Desktop/Services/DesktopModelScreen.cs` — сборка `ModelsViewModel` вместе с `OllamaManagerViewModel` и `IRemoteProviderService`.
4. `src/CyberPipe.Desktop/MainWindow.axaml.cs` — подписка на события открытия `OllamaManagerWindow` и `RemoteProvidersWindow`.
5. `tests/CyberPipe.Tests.Unit/DesktopUiTests.cs` — обновление и дополнение модульных тестов главного экрана моделей.

---

## 3. Детальные требования к реализации

### 3.1. Обновление `ModelsViewModel` (`src/CyberPipe.Desktop/ViewModels/ModelsViewModel.cs`)

1. **Понятные человеку названия и описания ролей (`TierModelAssignmentViewModel`):**
   * Добавить вычисляемое свойство `IsVisibleInSimpleUi => Tier == IntentTier.Tier2 || Tier == IntentTier.Tier3;` (при этом в коллекции `Assignments` можно оставить все 4 яруса для обратной совместимости существующих тестов, а в UI фильтровать или привязывать карточки `FastAssistantAssignment` (`Tier 2`) и `SmartExpertAssignment` (`Tier 3`) напрямую!).
   * Для `Tier 2`:
     * `RoleTitle = "⚡ Быстрый помощник (Tier 2)"`
     * `RoleSubtitle = "Мгновенно отвечает на повседневные вопросы, разбирает команды, ставит таймеры и заметки"`
   * Для `Tier 3`:
     * `RoleTitle = "🧠 Умный эксперт (Tier 3)"`
     * `RoleSubtitle = "Решает сложные задачи: пишет код, анализирует документы, строит цепочки и глубоко рассуждает"`
2. **Умное формирование списка доступных моделей (`GetCandidatesForHosting`) + Автосохранение:**
   * При выборе `ModelHostingType.Local`:
     * Первым приоритетом в `AvailableModels` добавляются **реально установленные в Ollama модели** (из `OllamaManager.InstalledModels` / `Installer.Items.Where(i => i.IsInstalled)`).
     * Если в Ollama ещё не установлено ни одной модели, добавляются рекомендованные модели из локального каталога и показывается подсказка `HasNoInstalledLocalModels = true` с кнопкой `[📥 Открыть менеджер Ollama и скачать модель]`.
     * Никаких захардкоженных массивов `["qwen2.5:7b", "llama3.1:8b", ...]` в коде больше нет — данные берутся только из рантайма и каталога.
   * При выборе `ModelHostingType.RemoteCloud`:
     * Список формируется из моделей включённых облачных провайдеров (`IRemoteProviderService.LoadProviders().Where(p => p.IsEnabled)` -> `p.Models.Where(m => m.IsEnabled)`), дополняясь моделями из `cloud-models.json`.
   * **Автосохранение:** при изменении `SelectedModel` пользователем (после инициализации ViewModel) автоматически вызывается `SaveTierAssignments()`, чтобы пользователю не приходилось каждый раз искать кнопку «Сохранить назначения».
3. **Карточки источников и задел под будущие локальные рантаймы (LM Studio и др.):**
   * Свойство `ObservableCollection<LocalRuntimeDescriptorDto> LocalRuntimes`:
     * По умолчанию содержит:
       1. `Ollama` (`Kind = LocalRuntimeKind.Ollama`, `IsImplemented = true`, статус синхронизирован с `OllamaManagerViewModel`);
       2. `LM Studio (Скоро)` (`Kind = LocalRuntimeKind.LmStudio`, `ApiBaseUrl = "http://localhost:1234"`, `IsImplemented = false`).
   * Сводка статуса локального источника: `OllamaSummaryText` (например: `"🟢 Ollama готова • Установлено моделей: 3"` или `"⚪ Ollama не запущена"`).
   * Сводка статуса облачных провайдеров: `RemoteProvidersSummaryText` (например: `"🟢 Активно провайдеров: 2 (доступно 8 моделей)"`).
   * Сводка паспорта ПК: `HardwareSummaryText` (например: `"💻 Ваш ПК: 32 ГБ ОЗУ (свободно 18.4 ГБ)"`).
4. **Команды открытия окон управления:**
   * `OpenOllamaManagerCommand` -> вызывает `Func<OllamaManagerViewModel, Task>? OpenOllamaManagerRequested`;
   * `OpenRemoteProvidersCommand` -> вызывает `Func<RemoteProvidersViewModel, Task>? OpenRemoteProvidersRequested`.
   * При закрытии или изменении данных в `OllamaManagerViewModel` / `RemoteProvidersViewModel` главный экран автоматически вызывает `RefreshAvailableModels()` и обновляет статус-бейджи.

### 3.2. Новый лаконичный интерфейс `ModelsView.axaml` (`src/CyberPipe.Desktop/Views/ModelsView.axaml`)

Экран полностью очищается от старых простыней установок и дублирующегося каталога. Структура:

1. **Блок 1: Заголовок и 2 карточки источников (Grid из 2 колонок):**
   * Вверху: заголовок `"Настройка ИИ-моделей"` + строка паспорта ПК (`HardwareSummaryText`).
   * **Левая карточка — «🏠 Локальные модели (На вашем ПК • 0$)»:**
     * Показывает активный локальный движок **Ollama** и его статус (`OllamaSummaryText`).
     * Кнопка **`[🦙 Управление моделями Ollama]`** (`Command="{Binding OpenOllamaManagerCommand}"`, `Classes="accent"`).
     * Небольшая плашка снизу: `"Движки: [✓ Ollama]  [⏳ LM Studio — в будущих версиях]"`.
   * **Правая карточка — «☁️ Облачные провайдеры (По API-ключу)»:**
     * Показывает статус подключённых провайдеров (`RemoteProvidersSummaryText`: OpenAI, DeepSeek, Gemini, OpenRouter и др.).
     * Кнопка **`[🔑 Настроить провайдеры и API-ключи]`** (`Command="{Binding OpenRemoteProvidersCommand}"`).

2. **Блок 2: Выбор моделей для работы ассистента (2 понятные карточки ролей):**
   * Заголовок блока: `"Какие модели сейчас отвечают на ваши запросы"` + справа аккуратная кнопка `[Сбросить по умолчанию]`.
   * **Карточка «⚡ Быстрый помощник» (`Tier 2`)** и **Карточка «🧠 Умный эксперт» (`Tier 3`)**:
     * Слева: понятное название роли (`RoleTitle`) и простое описание человеческим языком (`RoleSubtitle`).
     * Посередине:
       * Переключатель источника: `ToggleSwitch` (`OffContent="🏠 На моём ПК (Бесплатно)"`, `OnContent="☁️ В облаке (API)"`);
       * Чистый `ComboBox` выбора модели (`MinWidth="280"`) без пугающего ручного `TextBox` рядом!
       * Подпись под списком: провайдер/рантайм выбранной модели и подсказка (или ссылка `[📥 Скачать модели в менеджере Ollama]`, если локальная модель ещё не установлена).
     * Справа: кнопка **`[💬 Проверить]`** (`TestCommand`), открывающая окно живого тест-чата с выбранной моделью.

3. **Блок 3: Компактная карточка «🔍 Системный модуль умного поиска и памяти (Tier 1 ONNX)»:**
   * Занимает одну компактную строку внизу:
     * Объяснение: `"Отвечает за мгновенный поиск по вашим заметкам и понимание смысла команд без расхода токенов (multilingual-e5-small)."`.
     * Статус (`Installer.EmbeddingStateText`) + прогресс-бар (только во время скачивания) + одна контекстная кнопка (`[📥 Скачать (470 МБ)]` если не установлен, или `[Удалить с диска]` если установлен).

### 3.3. Связка окон в `MainWindow.axaml.cs` и `DesktopModelScreen.cs`

1. В `DesktopModelScreen.Create(...)`:
   * Создавать `IRemoteProviderService` и `OllamaManagerViewModel` и передавать их в `ModelsViewModel`.
2. В `MainWindow.axaml.cs` (там, где уже подписан `OpenModelTestRequested`):
   * Подписаться на `vm.Models.OpenOllamaManagerRequested`: открывать `OllamaManagerWindow` (с привязкой `OpenModelTestRequested` внутри него, чтобы кнопка `💬 Тест` работала и из окна Ollama!).
   * Подписаться на `vm.Models.OpenRemoteProvidersRequested`: открывать `RemoteProvidersWindow` (также с поддержкой открытия `ModelTestWindow` по кнопке `💬 Тест`).

---

## 4. Критерии приёмки и проверка

1. Главный экран `ModelsView.axaml` больше не содержит дублирующихся списков `Локальные ИИ модели` и `Model Catalog`, не показывает `Tier 0 (Regex)` как редактируемую модель и не требует ручного ввода `ID модели` в текстовое поле.
2. Переключение между `🏠 На моём ПК` и `☁️ В облаке` в карточках «⚡ Быстрый помощник» и «🧠 Умный эксперт» подгружает актуальные модели из Ollama и из `IRemoteProviderService`, а выбор модели автоматически сохраняется в `ui-overrides.json`.
3. Кнопки `[🦙 Управление моделями Ollama]` и `[🔑 Настроить провайдеры и API-ключи]` открывают соответствующие окна, а изменения в них сразу отражаются в выпадающих списках на главном экране.
4. Все архитектурные и модульные тесты проходят:
   ```powershell
   dotnet build CyberPipe.slnx
   dotnet test tests/CyberPipe.Tests.Architecture/CyberPipe.Tests.Architecture.csproj
   dotnet test tests/CyberPipe.Tests.Unit/CyberPipe.Tests.Unit.csproj
   ```
