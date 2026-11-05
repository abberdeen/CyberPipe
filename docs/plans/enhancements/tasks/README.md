# Атомарные задачи: Упрощение экрана моделей, окно-враппер Ollama и динамические облачные провайдеры

> **Базовый архитектурный план:** [models-ui-simplification-and-providers.md](../models-ui-simplification-and-providers.md)  
> **Назначение разбивки:** Каждая задача спроектирована как **самодостаточный инкремент (Vertical Slice)** оптимального размера (4–6 файлов за задачу).  
> Если лимит токенов или контекста закончится между задачами, проект гарантированно остаётся в **рабочем, компилируемом состоянии с зелёными тестами**, а следующую сессию можно начать просто со следующего файла `TASK-ENH-0X`.

---

## Реестр и порядок выполнения задач

| ID | Файл задачи | Фокус и результат инкремента | Зависимости | Статус |
| :--- | :--- | :--- | :--- | :--- |
| **TASK-ENH-01** | [TASK-ENH-01-contracts-and-ollama-runtime-api.md](TASK-ENH-01-contracts-and-ollama-runtime-api.md) | **Контракты и бэкенд Ollama:** DTO динамических облачных провайдеров, ключи секретов провайдеров в `SecretKeys`, абстракция локальных рантаймов (задел под LM Studio) и расширение `OllamaLocalModelInstaller` (`/api/ps` память, выгрузка из RAM/VRAM, `PullByTagAsync` для любой модели, полные метаданные `/api/tags`). | — | `Done` |
| **TASK-ENH-02** | [TASK-ENH-02-ollama-manager-window.md](TASK-ENH-02-ollama-manager-window.md) | **Окно-враппер Ollama (`OllamaManagerWindow`):** `OllamaManagerViewModel` + `OllamaManagerWindow.axaml`. Управление демоном, монитор памяти VRAM/RAM с кнопкой «Выгрузить», все реально установленные модели в Ollama, витрина с фильтрами под ПК и скачивание любой модели по тегу. | `TASK-ENH-01` | `Done` |
| **TASK-ENH-03** | [TASK-ENH-03-remote-providers-window-and-client.md](TASK-ENH-03-remote-providers-window-and-client.md) | **Динамические облачные провайдеры (`RemoteProvidersWindow`):** сервис `RemoteProviderService` (хранение профилей в `ui-overrides.json`, API-ключей в `ISecretStore`, автозагрузка `GET /v1/models`), окно настройки провайдеров с пресетами (OpenAI, Anthropic, Gemini, DeepSeek, OpenRouter, Custom URL) и реальный тест облачных моделей в `ModelTestService`. | `TASK-ENH-01` | `Done` |
| **TASK-ENH-04** | [TASK-ENH-04-simplified-main-models-view-integration.md](TASK-ENH-04-simplified-main-models-view-integration.md) | **Упрощение главного экрана `ModelsView` и интеграция окон:** редизайн `ModelsView.axaml` и `ModelsViewModel.cs` (2 понятные карточки «⚡ Быстрый помощник» и «🧠 Умный эксперт», автосохранение, уход от хардкода списков, компактный статус ONNX, кнопки открытия `OllamaManagerWindow` и `RemoteProvidersWindow` в `MainWindow`). | `TASK-ENH-02`, `TASK-ENH-03` | `Done` |

---

## Правила выполнения для ИИ-агента (Контроль контекста и качества)

1. **Одна задача — один завершённый цикл (`Code -> Build -> Test`):**
   Перед переходом к следующему файлу задачи обязательно должны проходить:
   ```powershell
   dotnet build CyberPipe.slnx
   dotnet test tests/CyberPipe.Tests.Architecture/CyberPipe.Tests.Architecture.csproj
   dotnet test tests/CyberPipe.Tests.Unit/CyberPipe.Tests.Unit.csproj --filter "FullyQualifiedName~Desktop|FullyQualifiedName~Model|FullyQualifiedName~Installer"
   ```
2. **Обратная совместимость на промежуточных шагах:**
   * Задачи `TASK-ENH-01`, `TASK-ENH-02` и `TASK-ENH-03` добавляют новые возможности и окна, **не ломая** существующие тесты `DesktopUiTests` и `InstallerViewModelTests`.
   * Переключение главного экрана `ModelsView` на новую упрощённую структуру выполняется финальным шагом в `TASK-ENH-04`.
3. **Соблюдение архитектурных границ:**
   * В `CyberPipe.Contracts` не должно быть внешних NuGet-пакетов и запрещённых идентификаторов (проверяется `ContractsBoundaryTests`).
   * `CyberPipe.Desktop` не ссылается на `CyberPipe.Data` или `CyberPipe.Security` напрямую — работа с секретами в UI идёт через интерфейс `ISecretStore` из `CyberPipe.Contracts`.
   * После завершения задачи обновите её статус в этом файле и в самом файле задачи с `Todo` на `Done`.
