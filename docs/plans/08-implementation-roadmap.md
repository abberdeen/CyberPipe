# 08. План поэтапной реализации CyberPipe

План разработки разбит на 5 логических спринтов (фаз), минимизирующих риски и обеспечивающих работоспособный MVP уже на втором этапе.

---

### Фаза 1: Фундамент, Контракты и Шина сообщений (Спринт 1)
- [ ] Инициализация структуры Solution (`CyberPipe.slnx`) с таргетом на `net10.0`.
- [ ] Создание проекта `CyberPipe.Contracts`:
  - Описание `gateway.proto` (gRPC методы авторизации, создания задач, стриминга логов).
  - Определение базовых C# DTO и записей MassTransit (`ProposeToolExecution`, `SecurityInspectionReport`, `TaskCreatedEvent`).
- [ ] Развертывание `CyberPipe.Infrastructure.Bus`:
  - Интеграция MassTransit с RabbitMQ.
  - Настройка персистентных очередей, Retry Policy и Dead Letter Exchange (DLQ).
  - Подключение Transactional Outbox с базой данных (PostgreSQL / SQLite).

---

### Фаза 2: Gateway и SecurityAgent (Спринт 2)
- [ ] Разработка `CyberPipe.Gateway.Api`:
  - Реализация gRPC-сервера с защитой по токенам (JWT / API Key).
  - Маппинг входящих gRPC-вызовов в шину MassTransit через Outbox.
- [ ] Разработка `CyberPipe.Agents.Security`:
  - Консьюмер очереди `security.inspections`.
  - Модуль AST-анализа PowerShell на базе `System.Management.Automation`.
  - Модуль детекта SSRF (локальные IP, приватные подсети, cloud metadata).
  - Регулярные выражения и блок-листы для ссылок (URL).
  - Формирование отчета `SecurityInspectionReport` с вычислением `RiskScore` и записью в аудит-лог.

---

### Фаза 3: CEO Orchestrator и Zero-Trust Workers (Спринт 3)
- [ ] Разработка `CyberPipe.Agents.Ceo`:
  - Логика декомпозиции задач (Prompt engineering, ReAct supervisor).
  - Управление сессиями и контекстными директориями агентов.
  - Принятие решений на основе отчетов безопасности (Policy Engine).
- [ ] Разработка `CyberPipe.Agents.Workers`:
  - Coder и Researcher агенты на базе `Microsoft.Extensions.AI`.
  - Запрет прямого вызова тулов: генерация только сообщений `ProposeToolExecution`.
- [ ] Реализация `CyberPipe.Execution.Sandbox`:
  - Исполнитель `CliWrap` для локальных изолированных команд.
  - Исполнитель `Docker.DotNet` для контейнеризированных сред.

---

### Фаза 4: Avalonia UI Multiplatform (Спринт 4)
- [ ] Создание проектов `CyberPipe.UI`, `CyberPipe.UI.Desktop`, `CyberPipe.UI.Browser`:
  - Настройка MVVM через `CommunityToolkit.Mvvm`.
  - Подключение `Grpc.Net.Client` и `Grpc.Net.Client.Web`.
- [ ] Главный экран:
  - Панель сессий и активных задач.
  - Окно стриминга рассуждений агентов в реальном времени.
  - Модальное окно **Human-in-the-Loop Approval**: отображение вердикта `SecurityAgent` (Risk Score, подсвеченные опасные фрагменты кода/ссылок) с кнопками «Разрешить» / «Отклонить».

---

### Фаза 5: Тестирование и Hardening (Спринт 5)
- [ ] Написание тестов архитектурной изоляции в `CyberPipe.Tests.Architecture` (ArchUnitNET).
- [ ] Написание тестов на детект угроз в `CyberPipe.Tests.Security` (проверка парсера на обфусцированных скриптах, реверс-шеллах, форк-бомбах).
- [ ] Нагрузочное тестирование устойчивости брокера: имитация аварийного отключения сервера во время исполнения команд.
