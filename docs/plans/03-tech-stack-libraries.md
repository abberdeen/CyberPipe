# 03. Технологический стек и библиотеки (.NET 10)

В стек включены библиотеки, гарантирующие сквозное шифрование, очистку секретов, подключаемые среды исполнения и полное отсутствие жестко закодированных значений.

---

### Сводная таблица библиотек и их роли

| Область | Рекомендуемая библиотека | Обоснование и решаемая задача |
| :--- | :--- | :--- |
| **Кроссплатформенный UI** | `Avalonia` (11.x+) | Полноценный GUI для Windows, macOS, Linux и браузера (WebAssembly). Единая XAML база без Electron. |
| **UI MVVM Framework** | `CommunityToolkit.Mvvm` | Быстрый реактивный MVVM на Source Generators (высокая производительность в Native AOT и WebAssembly). |
| **Транспорт UI ↔ Gateway** | `Grpc.Net.Client`, `Grpc.Net.Client.Web` | Бинарная сериализация Protobuf, потоковая передача (gRPC Streaming), mTLS, поддержка WebAssembly. |
| **Шина сообщений и саги** | `MassTransit` + `MassTransit.RabbitMQ` | Персистентные очереди, Publisher Confirms, саги с конечными автоматами (`MassTransit.Automatonymous`), Outbox. |
| **Шифрование шины** | `MassTransit` Encryption Extensions | Message-level шифрование полезной нагрузки сообщений в RabbitMQ (AES-256-GCM / Envelope). |
| **Криптография хранилищ** | `System.Security.Cryptography` | Аппаратное ускорение AES-GCM, ChaCha20-Poly1305, деривация ключей HKDF для поколоночного шифрования в БД. |
| **AI Абстракции** | `Microsoft.Extensions.AI` | Официальный стандарт Microsoft в .NET для чат-моделей, эмбеддингов, конвейеров middleware (логирование, кэширование, rate limiting). |
| **Агентные шаблоны** | `Microsoft.SemanticKernel` / Custom ReAct | Реализация циклов рассуждений агентов, динамического контекста и генерации предложений (Proposals). |
| **Поддержка инструментов** | `ModelContextProtocol.NET` | Протокол MCP от Anthropic для стандартизированного подключения любых внешних тулов. |
| **Абстракция песочниц** | `ISandboxProvider` (Кастомная архитектура) | Абстрактная фабрика сред исполнения. Поддерживает Docker, Wasm, Podman, локальные процессы. |
| **Бэкенды песочниц** | `Docker.DotNet`, `Wasmtime`, `CliWrap` | • `Docker.DotNet` — контейнеры Docker/Podman.<br>• `Wasmtime` — изолированный WebAssembly рантайм (микросекундный старт).<br>• `CliWrap` — ограниченные процессы ОС. |
| **AST-анализ PowerShell** | `System.Management.Automation` | Официальный парсер Microsoft для глубокого анализа абстрактного синтаксического дерева скриптов. |
| **Очистка данных (Sanitizer)** | Кастомный движок + Regex Heuristics | Детектор высокой энтропии (Shannon entropy), сигнатуры токенов (AWS, GitHub, JWT, RSA/SSH ключи, пароли). |
| **Базы данных и Outbox** | `Npgsql.EntityFrameworkCore.PostgreSQL` / `Microsoft.Data.Sqlite` | Хранение сессий, состояний агентов, неизменяемых аудит-логов и таблиц Transactional Outbox. |
| **Векторная память** | `pgvector` / `QdrantClient` | Долговременная семантическая память, профили агентов и RAG-поиск по артефактам. |
| **Авторизация State Hub** | `Microsoft.AspNetCore.Authorization` | Policy-based и Resource-based авторизация доступа к срезам состояния приложения (Need-to-Know). |
| **Конфигурация (Zero Hardcode)** | `Microsoft.Extensions.Options.ConfigurationExtensions` | Строго типизированные опции (`IOptions<T>`), поддержка горячей перезагрузки (`IOptionsMonitor<T>`) и внешних хранилищ секретов. |
| **Аудит и трассировка** | `Serilog` + `OpenTelemetry` | Структурированное логирование (после санитизации!) и распределенная трассировка всех решений безопасности. |
