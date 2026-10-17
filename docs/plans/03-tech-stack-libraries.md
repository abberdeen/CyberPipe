# 03. Технологический стек и библиотеки (.NET 10)

Стек подобран по критериям **максимальной скорости отклика, минимального потребления памяти, простоты развертывания (Zero-Dependency Startup)** и **радикальной экономии токенов** в .NET 10. Из стека исключены тяжеловесные внешние сервисы (обязательный RabbitMQ, PostgreSQL), которые усложняли запуск и добавляли сетевые задержки.

---

### Сводная таблица библиотек и их роли

| Область | Рекомендуемая библиотека | Обоснование и решаемая задача |
| :--- | :--- | :--- |
| **Внутрипроцессная шина (Fast Bus)** | `System.Threading.Channels` (Встроено в .NET 10) | Ультрабыстрая неблокирующая передача сообщений внутри процесса (< 0.05 мс) с поддержкой Backpressure (`BoundedChannel`) и приоритетного канала отмены (`Kill-Switch`). |
| **Локальная БД, Память («Второй мозг») и CRM** | `Microsoft.Data.Sqlite` (WAL + `FTS5`) + `Dapper` | Надежное локальное хранение без внешних СУБД. Полнотекстовый (`FTS5`) и векторный поиск по заметкам, CRM, финансам, чекпоинтам пауз и семантическому кэшу. |
| **Telegram-интеграция** | `Telegram.Bot` | Быстрый клиент Telegram API (Long Polling / Webhook), поддержка голосовых сообщений, файлов, Inline-кнопок бюджета (`[▶️ Продолжить]`, `[🛑 Стоп]`) и фильтрация по `UserId`. |
| **Локальное распознавание речи (0$ Voice STT)** | `Whisper.net` (ONNX / GGML runtime) | Локальная транскрибация голосовых сообщений из Telegram и записей рабочих созвонов на CPU/GPU за **0 облачных токенов**. |
| **Локальный Семантический Роутер (Tier 1)** | `Microsoft.ML.OnnxRuntime` + `System.Numerics.Tensors` | Запуск крошечной мультиязычной модели эмбеддингов (`multilingual-e5-small` / `bge-micro`) локально на CPU за ~5–10 мс + аппаратное SIMD-вычисление косинусной близости (`TensorPrimitives`). **0 токенов на распознавание команд и поиск в памяти!** |
| **Гибкий AI Пайплайн (`Local` и `RemoteCloud` для каждого Tier)** | `Microsoft.Extensions.AI` (`IChatClient`) + `OllamaSharp` (Local) + `OpenAI` / Cloud SDKs + `Microsoft.ML.Tokenizers` | Пользователь в UI сам назначает для `Tier 2` и `Tier 3` либо **локальную модель (`Local`, 0$)** из Curated-списка 15 моделей, либо **удаленную платную (`RemoteCloud`)**. Приложение четко знает тип хостинга (`ModelHostingType`), адаптируя бюджет (`TokenBudgetGovernor`) и контроль RAM (`ISystemResourceMonitor`). |
| **Оценка железа ПК и Подбор моделей** | `IHardwareCapabilitiesProbe` + `IModelCompatibilityAdvisor` (Встроено в ядро) | Сервис `IHardwareCapabilitiesProbe` считывает RAM/VRAM/CPU хоста и передает в `IModelCompatibilityAdvisor`, который рассчитывает совместимость каждой из 15 локальных моделей с ПК пользователя (`🟢 Оптимально`, `🟡 Совместимо`, `🔴 Мало RAM`). |
| **Аудио Хоста, Медиа и Запись Созвонов** | `NAudio` (WASAPI Loopback & CoreAudio) + System Media | Управление громкостью с жестким потолком (`Volume Clamp`), медиа-команды (`Play/Pause/Next`) и захват системного звука + микрофона для протоколирования встреч. |
| **Проактивный планировщик и Триггеры** | `Cronos` + `System.Threading.PeriodicTimer` | Встроенный планировщик утренних/вечерних брифингов, умных напоминаний, контроля подписок и мониторинга серверов/SSL. |
| **Интеграция с Git и Репозиториями** | `LibGit2Sharp` / `CliWrap (git)` | Мгновенный локальный радар репозиториев (`/repos`, `/standup`), проверка незакоммиченных изменений и чтение `git diff` за 0 токенов. |
| **Чтение документов, договоров и OCR** | `UglyToad.PdfPig` + `DocumentFormat.OpenXml` + `Windows.Media.Ocr` | Локальное извлечение текста из PDF-договоров, счетов, DOCX-шаблонов и скриншотов буфера обмена без отправки сырых бинарников в облако. |
| **Кроссплатформенный UI (`CyberPipe.UI.Desktop.exe`)** | `Avalonia` (11.x+) + `CommunityToolkit.Mvvm` | Нативный быстрый GUI (Desktop/Трей/Попап `Alt+Space`) без прожорливого Electron. Быстрый MVVM на Source Generators. |
| **Системный CLI и TUI (Режим №2 в `CyberPipe.exe`)** | `System.CommandLine` + `Spectre.Console` | Встроенный в `CyberPipe.exe` консольный интерфейс (`doctor`, `status`, `models scan`, `infer`, `tui`, строгий `--json`), подключающийся к демону за `< 15 мс`. |
| **Транспорт UI / CLI ↔ `CyberPipe.exe`** | `Grpc.Net.Client` / `Grpc.AspNetCore` (Named Pipes) | Быстрый бинарный стриминг метрик, логов, расходов токенов и команд управления между `CyberPipe.UI.Desktop.exe`, CLI-режимом и фоновым демоном `CyberPipe.exe` (лимит кадра `4 МБ`). |
| **Бэкенды песочниц и `CyberPipe.Brainstem.exe`** | `Windows Job Objects` (`./worker/CyberPipe.Brainstem.exe`), `CliWrap`, `Wasmtime`, `Docker.DotNet` | • `CyberPipe.Brainstem.exe` в `Windows Job Object` + `Low Integrity` — стерильный воркер без `.dll` демона для C++ вызовов (`Whisper`/`LLama`/`PDF`).<br>• `CliWrap` / `Wasmtime` / `Docker.DotNet` — песочницы для задач `Tier 3`. |
| **AST-анализ и защита от вайпа** | `System.Management.Automation` + Кастомный `HostSafetyGuard` | Синтаксический разбор скриптов PowerShell/Bash + жесткий блокировщик деструктивных команд (`rm -rf`, `Format-Volume`, реестр) и фильтр 18+ / NSFW контента. |
| **Защита секретов и Очистка данных** | `System.Security.Cryptography` (`DPAPI` / `AES-256-GCM`) + `Sanitizer` | Локальное аппаратное шифрование ключей API и секретов на диске; автоматическое вырезание секретов (`[REDACTED]`) перед отправкой в облачные LLM и логи. |
| **Защита Буферов и Памяти (Anti-OOM)** | `System.Buffers` (`ArrayPool<T>`) + `QuotaBoundedStream` + `MemoryCache` | Запрет `UnboundedChannel`, обрезка потоков `> 16 МБ` на лету, нулевые лишние аллокации в LOH и строгий потолок потребления ОЗУ кэшем. |
| **Аудит и трассировка** | `Serilog` + `OpenTelemetry` | Быстрое асинхронное логирование (после очистки секретов) с учетом стоимости каждого запроса в токенах и долларах. |
