# Архитектурная документация CyberPipe

Оркестратор автономных агентов нового поколения на платформе **.NET 10** с кроссплатформенным **Avalonia UI**, шиной гарантированной доставки на базе **RabbitMQ**, сквозным шифрованием (E2EE), скрытным **SecurityAgent**, подключаемыми песочницами и моделью безопасности **Zero-Trust**.

---

## 📚 Разделы документации

1. [**01. Фундаментальные принципы**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/01-principles.md)  
   *Zero-Trust, принцип Need-to-Know, сквозное шифрование (E2EE), скрытный (Stealth) SecurityAgent, подключаемые песочницы, внеполосный сбор телеметрии, защита от OOM/WAL-дедлоков, Content-Addressable Storage (CAS) и нулевой хардкод.*

2. [**02. Диаграмма взаимодействия и защищенных потоков данных**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/02-architecture-diagram.md)  
   *Mermaid-схема слоев: Avalonia UI ↔ Gateway & Sanitizer ↔ Зашифрованный RabbitMQ ↔ CEO / Workers / SecurityAgent ↔ Pluggable Sandboxes & Telemetry Pre-processor.*

3. [**03. Технологический стек и библиотеки (.NET 10)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md)  
   *Обоснование выбора: Avalonia, MassTransit + RabbitMQ (E2EE), System.Security.Cryptography, ArrayPool/MemoryPool (Zero-Alloc), Bounded Caches, Docker.DotNet, Wasmtime, CliWrap, System.Management.Automation.*

4. [**04. Паттерн "Zero-Trust Agent Proposal" и асинхронные саги**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/04-agent-mesh-proposals.md)  
   *Неблокирующая шина сообщений: MassTransit State Machine Saga, сохранение последовательности шагов, защита от A2A-зацикливания (HopCountGuard), дедупликация и ETag, инкрементальный кэш промптов, атомарный Append.*

5. [**05. SecurityAgent: Скрытная инспекция, OSINT, аудит и эвристический анализ телеметрии**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/05-security-agent.md)  
   *Принцип Stealth OPSEC (запрет прямых пингов/запросов к цели), пассивный OSINT (URL/IP), проверка Email (disposable/MX/SPF/breach), AST-анализ, маркировка доверия к памяти (Memory Trust Tagging) и изоляция очередей от пользовательских чатов.*

6. [**06. Структура Solution и проектов (`CyberPipe.slnx`)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/06-solution-structure.md)  
   *Дерево проектов с выделенными модулями Sanitization, Cryptography, State.Hub, Execution.Telemetry, Pluggable Execution, и правила межпроектных зависимостей.*

7. [**07. Изоляция UI и архитектурные тесты (ArchUnitNET)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/07-ui-isolation-and-arch-tests.md)  
   *Тонкий клиент на Avalonia UI, взаимодействие исключительно через CyberPipe.Contracts (gRPC), тесты изоляции воркеров и UI на базе ArchUnitNET.*

8. [**08. План поэтапной реализации**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md)  
   *Пошаговый план разработки на 5 спринтов: от криптографического фундамента и санитизации до UI, мониторинга ресурсов и стресс-тестирования.*

9. [**09. Защита данных, санитизация и Хаб состояний (State Hub)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/09-data-protection-and-state-hub.md)  
   *Единый State Hub с разделением доступа Need-to-Know, Central Secret Redactor, Envelope Encryption, асинхронный Write-Behind через Channels, секционирование БД и безопасный Hot-Reload (`IOptionsSnapshot`).*

10. [**10. Мониторинг ресурсов и эвристический анализ телеметрии песочниц**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/10-sandbox-telemetry-and-heuristics.md)  
    *Realtime Singleton `ISystemResourceMonitor` (авторизованный срез для CEO, UI, Security), внеполосный Out-of-Band перехват трафика/файлов, двухуровневый препроцессор для решения проблемы «1 ГБ логов», CAS-кэш инструментов, `tmpfs` в RAM и RAII-очистка.*
