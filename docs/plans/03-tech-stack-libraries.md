# 03. Технологический стек и библиотеки (.NET 10)

В стек включены библиотеки, гарантирующие сквозное шифрование, очистку секретов, подключаемые среды исполнения, защиту от утечек памяти и высочайшую дисковую надежность в .NET 10.

---

### Сводная таблица библиотек и их роли

| Область | Рекомендуемая библиотека | Обоснование и решаемая задача |
| :--- | :--- | :--- |
| **Кроссплатформенный UI** | `Avalonia` (11.x+) | Полноценный GUI для Windows, macOS, Linux и браузера (WebAssembly). Единая XAML база без Electron. |
| **UI MVVM Framework** | `CommunityToolkit.Mvvm` | Быстрый реактивный MVVM на Source Generators (высокая производительность в Native AOT и WebAssembly). |
| **Транспорт UI ↔ Gateway** | `Grpc.Net.Client`, `Grpc.Net.Client.Web` | Бинарная сериализация Protobuf, потоковая передача (gRPC Streaming), mTLS, поддержка WebAssembly. |
| **Шина сообщений и саги** | `MassTransit` + `MassTransit.RabbitMQ` | Персистентные очереди, Publisher Confirms, саги с конечными автоматами (`MassTransit.Automatonymous`), Outbox, DLQ. |
| **Шифрование шины** | `MassTransit` Encryption Extensions | Message-level шифрование полезной нагрузки сообщений в RabbitMQ (AES-256-GCM / Envelope). |
| **Криптография хранилищ** | `System.Security.Cryptography` | Аппаратное ускорение AES-GCM, ChaCha20-Poly1305, деривация ключей HKDF для поколоночного шифрования в БД. |
| **Управление памятью (Zero-Alloc)** | `System.Buffers` (`ArrayPool<T>`, `MemoryPool<T>`) | Исключение фрагментации кучи больших объектов (LOH) и предотвращение утечек памяти при разборе больших JSON/дампов. |
| **Ограниченное кэширование** | `Microsoft.Extensions.Caching.Memory` | Жесткий потолок `SizeLimit` и компактификация (вытеснение 25% старых записей при исчерпании лимита памяти). |
| **AI Абстракции** | `Microsoft.Extensions.AI` | Официальный стандарт Microsoft в .NET для чат-моделей, эмбеддингов, конвейеров middleware и инкрементального кэширования промптов. |
| **Агентные шаблоны** | `Microsoft.SemanticKernel` / Custom ReAct | Реализация циклов рассуждений агентов, динамического контекста и генерации предложений (Proposals). |
| **Поддержка инструментов** | `ModelContextProtocol.NET` | Протокол MCP от Anthropic для стандартизированного подключения любых внешних тулов. |
| **Абстракция песочниц** | `ISandboxProvider` (Кастомная архитектура) | Абстрактная фабрика сред исполнения. Поддерживает Docker, Wasm, Podman, локальные процессы. |
| **Бэкенды песочниц** | `Docker.DotNet`, `Wasmtime`, `CliWrap` | • `Docker.DotNet` — контейнеры Docker/Podman.<br>• `Wasmtime` — изолированный WebAssembly рантайм (микросекундный старт).<br>• `CliWrap` — ограниченные процессы ОС. |
| **Дисковое кэширование (CAS)** | Собственный CAS-движок на базе SHA-256 | Content-Addressable Storage: единый кэш плагинов и бинарников, исключающий повторную распаковку и износ SSD. |
| **AST-анализ PowerShell** | `System.Management.Automation` | Официальный парсер Microsoft для глубокого анализа абстрактного синтаксического дерева скриптов. |
| **Очистка данных (Sanitizer)** | Кастомный движок + Regex Heuristics | Детектор высокой энтропии (Shannon entropy), сигнатуры токенов (AWS, GitHub, JWT, RSA/SSH ключи, пароли). |
| **Базы данных и Outbox** | `Npgsql.EntityFrameworkCore.PostgreSQL` / `Microsoft.Data.Sqlite` | • Асинхронный Write-Behind через `Channels`.<br>• Секционирование БД по воркспейсам.<br>• Фоновый воркер обслуживания WAL журнала (`PRAGMA wal_checkpoint`). |
| **Векторная память** | `pgvector` / `QdrantClient` | Долговременная семантическая память, профили агентов и RAG-поиск по артефактам с политиками очистки устаревших данных (TTL). |
| **Авторизация State Hub** | `Microsoft.AspNetCore.Authorization` | Policy-based и Resource-based авторизация доступа к срезам состояния приложения (Need-to-Know). |
| **Конфигурация (Zero Hardcode)** | `Microsoft.Extensions.Options.ConfigurationExtensions` | Строго типизированные опции (`IOptionsSnapshot<T>` для изолированного Hot-Reload в активных задачах). |
| **Аудит и трассировка** | `Serilog` + `OpenTelemetry` | Структурированное логирование (после санитизации!) и распределенная трассировка всех решений безопасности. |
