# 08. План поэтапной реализации CyberPipe

План разработки разбит на 5 логических спринтов (фаз), обеспечивающих надежный фундамент безопасности, сквозного шифрования и внеполосного сбора телеметрии.

---

### Фаза 1: Защищенный фундамент, E2EE и Санитизация (Спринт 1)
- [ ] Инициализация структуры Solution (`CyberPipe.slnx`) на .NET 10.
- [ ] Создание проекта `CyberPipe.Security.Cryptography`:
  - Реализация AES-256-GCM Envelope Encryption для сообщений шины.
  - Поколоночные шифровальщики данных для EF Core.
- [ ] Создание проекта `CyberPipe.Security.Sanitization`:
  - Детектор высокой энтропии и база регулярных выражений для токенов/ключей/паролей.
  - Механизм токенизации секретов с заменой на `[REDACTED:ID]`.
- [ ] Развертывание `CyberPipe.Infrastructure.Bus`:
  - Настройка MassTransit с RabbitMQ и шифрованием сообщений.
  - Настройка персистентных очередей, Retry Policy, DLQ и Transactional Outbox.
- [ ] Проект `CyberPipe.Contracts`: gRPC-протоколы и DTO сообщений.

---

### Фаза 2: State Hub, Realtime Монитор ресурсов и Скрытный SecurityAgent (Спринт 2)
- [ ] Создание `CyberPipe.State.Hub`:
  - Реализация scoped-проекций состояния по принципу **Need-to-Know**.
  - Разработка **Realtime Singleton `SystemResourceMonitor`**: сбор CPU/RAM/Диска, проверка емкости хоста для CEO, стриминг в UI, алерты для Security.
- [ ] Разработка `CyberPipe.Gateway.Api`:
  - gRPC / SignalR эндпоинты с TLS 1.3 и JWT авторизацией.
  - Стриминг системных метрик в UI.
- [ ] Разработка `CyberPipe.Agents.Security`:
  - Асинхронный потребитель сообщений инспекции (неблокирующий поток).
  - Шлюз Stealth OSINT (интеграция с VirusTotal, URLhaus, AbuseIPDB, DoH — без прямых запросов к цели).
  - Модуль проверки Email (disposable check, passive MX/SPF/DKIM, breach API).
  - AST-парсер PowerShell (`System.Management.Automation`) и эвристики Bash.

---

### Фаза 3: Подключаемые песочницы, Внеполосная телеметрия и Zero-Trust воркеры (Спринт 3)
- [ ] Разработка `CyberPipe.Execution.Abstractions`:
  - Интерфейсы `ISandboxProvider`, `ISandboxInstance`.
- [ ] Разработка `CyberPipe.Execution.Telemetry`:
  - Внеполосный сбор сетевых вызовов (`veth` tap) и файловых дельт.
  - Хостовый потоковый препроцессор: сжатие гигабайтных логов в 30 КБ `ExecutionTelemetryDigest` (фильтры Блума, агрегация, SHA256 хеши).
  - Запись сырых дампов в `QuarantineStore` с TTL.
- [ ] Реализация бэкендов песочниц:
  - `CyberPipe.Execution.Docker` (контейнеры Docker/Podman).
  - `CyberPipe.Execution.Wasm` (рантайм Wasmtime).
  - `CyberPipe.Execution.Process` (процессы CliWrap).
- [ ] Разработка `CyberPipe.Agents.Ceo`:
  - Управление сагами жизненного цикла задач (`TaskExecutionSaga`).
  - Проверка доступных ресурсов хоста через `ISystemResourceMonitor` перед спавном песочницы.
- [ ] Разработка воркеров `CyberPipe.Agents.Workers` (чистые генераторы `ProposeToolExecution`).

---

### Фаза 4: Avalonia UI Multiplatform (Спринт 4)
- [ ] Разработка `CyberPipe.UI`, `CyberPipe.UI.Desktop`, `CyberPipe.UI.Browser`:
  - Настройка MVVM (`CommunityToolkit.Mvvm`).
  - Подключение `Grpc.Net.Client` / `Grpc.Net.Client.Web`.
- [ ] Основные компоненты интерфейса:
  - Панель задач, сессий и защищенных артефактов.
  - Живые графики утилизации CPU/RAM/Диска из `SystemResourceMonitor`.
  - Стриминг рассуждений агентов в реальном времени (с очищенными логами).
  - Окно подтверждения (**Human-in-the-Loop**): вердикт SecurityAgent, подсвеченные аномалии телеметрии и выбор действия.

---

### Фаза 5: Стресс-тестирование, Hardening и Telemetry Benchmark (Спринт 5)
- [ ] Написание тестов архитектурной изоляции в `CyberPipe.Tests.Architecture` (ArchUnitNET).
- [ ] Тестирование сжатия телеметрии в `CyberPipe.Tests.Telemetry`:
  - Подача синтетического 1 ГБ сетевого/файлового лога в `TelemetryPreProcessor`.
  - Верификация формирования `ExecutionTelemetryDigest` размером < 50 КБ без всплесков памяти.
- [ ] Тесты санитизации `CyberPipe.Tests.Sanitization`: прогон синтетических утечек через Sanitizer.
- [ ] Стресс-тесты брокера сообщений: отключение RabbitMQ под нагрузкой и проверка восстановления саг через Outbox.
