# 08. План поэтапной реализации CyberPipe

План разработки разбит на 5 логических спринтов (фаз), обеспечивающих надежный фундамент безопасности и сквозного шифрования с первых дней.

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

### Фаза 2: State Hub, Gateway и Скрытный SecurityAgent (Спринт 2)
- [ ] Создание `CyberPipe.State.Hub`:
  - Реализация scoped-проекций состояния по принципу **Need-to-Know**.
  - Исключение персональных данных и секретов из срезов для аналитических модулей.
- [ ] Разработка `CyberPipe.Gateway.Api`:
  - gRPC / SignalR эндпоинты с TLS 1.3 и JWT авторизацией.
  - Интеграция с Central Sanitizer для входящих и исходящих потоков.
- [ ] Разработка `CyberPipe.Agents.Security`:
  - Асинхронный потребитель сообщений инспекции (неблокирующий поток).
  - Шлюз Stealth OSINT (интеграция с VirusTotal, URLhaus, AbuseIPDB, DoH — без прямых запросов к цели).
  - Модуль проверки Email (disposable check, passive MX/SPF/DKIM, breach API).
  - AST-парсер PowerShell (`System.Management.Automation`) и эвристики Bash.

---

### Фаза 3: Подключаемые песочницы и Zero-Trust воркеры (Спринт 3)
- [ ] Разработка `CyberPipe.Execution.Abstractions`:
  - Определение интерфейсов `ISandboxProvider`, `ISandboxInstance`.
- [ ] Реализация бэкендов песочниц:
  - `CyberPipe.Execution.Docker` (управление контейнерами Docker/Podman).
  - `CyberPipe.Execution.Wasm` (рантайм Wasmtime для микросекундных изолированных задач).
  - `CyberPipe.Execution.Process` (локальные процессы через CliWrap).
- [ ] Разработка `CyberPipe.Agents.Ceo`:
  - Управление сагами жизненного цикла задач (`TaskExecutionSaga`).
  - Принятие решений по риск-репортам и отправка команд в песочницы.
- [ ] Разработка воркеров `CyberPipe.Agents.Workers`:
  - Coder и Researcher на базе `Microsoft.Extensions.AI`.
  - Генерация исключительно `ProposeToolExecution` (полная изоляция от ОС).

---

### Фаза 4: Avalonia UI Multiplatform (Спринт 4)
- [ ] Разработка `CyberPipe.UI`, `CyberPipe.UI.Desktop`, `CyberPipe.UI.Browser`:
  - Настройка MVVM (`CommunityToolkit.Mvvm`).
  - Подключение `Grpc.Net.Client` / `Grpc.Net.Client.Web`.
- [ ] Основные компоненты интерфейса:
  - Панель сессий, задач и защищенных артефактов.
  - Стриминг рассуждений агентов в реальном времени (с очищенными логами).
  - Интерактивное окно подтверждения (**Human-in-the-Loop**): детали отчета SecurityAgent, подсвеченные фрагменты кода и выбор действия.

---

### Фаза 5: Hardening, Тестирование и Zero Hardcode аудит (Спринт 5)
- [ ] Написание тестов архитектурной изоляции в `CyberPipe.Tests.Architecture` (ArchUnitNET):
  - Проверка отсутствия ссылок на ядро из UI.
  - Проверка отсутствия ссылок на execution/storage из воркеров.
- [ ] Тесты санитизации `CyberPipe.Tests.Sanitization`: прогон синтетических утечек через Sanitizer.
- [ ] Тестирование скрытности и устойчивости SecurityAgent: верификация отсутствия прямых сетевых вызовов.
- [ ] Стресс-тесты брокера сообщений: отключение RabbitMQ под нагрузкой и проверка восстановления саг через Outbox.
