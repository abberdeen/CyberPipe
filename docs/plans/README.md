# Архитектурная документация CyberPipe

Оркестратор автономных агентов нового поколения на платформе **.NET 10** с кроссплатформенным **Avalonia UI**, шиной гарантированной доставки на базе **RabbitMQ** и моделью безопасности **Zero-Trust**.

---

## 📚 Разделы документации

1. [**01. Фундаментальные принципы**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/01-principles.md)  
   *Концепция Zero-Trust, роль CEO-агента, инспекция безопасности и гарантия сохранности сообщений.*

2. [**02. Диаграмма взаимодействия и потоков данных**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/02-architecture-diagram.md)  
   *Mermaid-схема слоев системы: Avalonia UI ↔ Gateway ↔ RabbitMQ Broker ↔ CEO / Workers / SecurityAgent ↔ Execution Sandbox.*

3. [**03. Технологический стек и библиотеки (.NET 10)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md)  
   *Обоснование выбора Avalonia, Microsoft.Extensions.AI, MassTransit, RabbitMQ, Docker.DotNet, CliWrap, System.Management.Automation.*

4. [**04. Паттерн "Zero-Trust Agent Proposal"**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/04-agent-mesh-proposals.md)  
   *Протокол взаимодействия воркеров с CEO: воркеры не вызывают тулы напрямую, а отправляют предложения (Proposals) на валидацию.*

5. [**05. SecurityAgent: Движок инспекции и аудита**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/05-security-agent.md)  
   *AST-анализ PowerShell/Bash, предотвращение SSRF, проверка ссылок и доменов, структура SecurityInspectionReport и аудит-лог.*

6. [**06. Структура Solution и папок (`CyberPipe.slnx`)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/06-solution-structure.md)  
   *Дерево проектов, организация директорий и правила зависимостей между сборками.*

7. [**07. Изоляция UI и архитектурные тесты (ArchUnitNET)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/07-ui-isolation-and-arch-tests.md)  
   *Тонкий клиент на Avalonia UI, взаимодействие исключительно через CyberPipe.Contracts (gRPC), автоматический тест чистоты зависимостей.*

8. [**08. План поэтапной реализации**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md)  
   *Пошаговый план разработки на 5 спринтов: от фундамента шины и контрактов до UI и нагрузочного тестирования.*
