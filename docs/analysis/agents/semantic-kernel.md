# Semantic Kernel — анализ

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Semantic Kernel (Microsoft Semantic Kernel) |
| Официальный сайт / документация | [learn.microsoft.com/en-us/semantic-kernel/overview](https://learn.microsoft.com/en-us/semantic-kernel/overview/) |
| Репозиторий | [github.com/microsoft/semantic-kernel](https://github.com/microsoft/semantic-kernel) |
| Категория | SDK / middleware для встраивания LLM и AI-агентов в существующий код (C#, Python, Java) |
| Назначение | Интеграция LLM в приложения: плагины из существующего кода, function-calling, оркестрация вызовов моделей |
| Целевая аудитория | .NET/Python/Java-разработчики, в первую очередь enterprise; Fortune 500 упомянуты в официальной вводной странице |
| Статус на 2026-10-19 | PARTIAL-подтверждено вторичными источниками: SK переведён в maintenance, новые инвестиции — в Microsoft Agent Framework (GA 1.0 — апрель 2026). Официальная страница [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) на дату проверки этого прямо не фиксирует. Источники: [medium.com — Migrating from Semantic Kernel Agents to Microsoft Agent Framework](https://medium.com/@bhargavkoya56/migrating-from-semantic-kernel-agents-to-microsoft-agent-framework-in-net-42f8236327dc), [particula.tech — Microsoft Agent Framework 1.0 vs Google ADK vs smolagents](https://particula.tech/blog/microsoft-agent-framework-vs-google-adk-vs-smolagents) |
| Лицензия | MIT — [github.com/microsoft/semantic-kernel/blob/main/LICENSE](https://github.com/microsoft/semantic-kernel/blob/main/LICENSE) (подтверждено бейджем в результатах поиска и [devblogs.microsoft.com/semantic-kernel](https://devblogs.microsoft.com/semantic-kernel/?p=2061)) |
| Развёртывание | Встраиваемая библиотека (NuGet/PyPI/Maven), не самостоятельный сервер; хостинг — внутри приложения пользователя, локально или в облаке |

Официальное определение: «lightweight, open-source development kit that lets you easily build AI agents and integrate the latest AI models into your C#, Python, or Java codebase… efficient middleware» — [learn.microsoft.com/en-us/semantic-kernel/overview](https://learn.microsoft.com/en-us/semantic-kernel/overview/).

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE. Приведены только применимые строки.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность агентов | PARTIAL | Агенты поверх Kernel есть, но центр тяжести SK — middleware и function-calling, а не автономный runtime; развитие агентов ушло в Agent Framework — [medium.com](https://medium.com/@bhargavkoya56/migrating-from-semantic-kernel-agents-to-microsoft-agent-framework-in-net-42f8236327dc) |
| Планирование | UNSUPPORTED | SequentialPlanner удалён в v1.0, StepwisePlanner deprecated, .NET-планировщики сняты с публикации в апреле 2025: [PR #12399 — .Net: Remove planners](https://github.com/microsoft/semantic-kernel/pull/12399), [openlegion-ai/landing — semantic-kernel.md](https://github.com/openlegion-ai/landing/blob/HEAD/src/content/comparison/semantic-kernel.md). Замена — auto function-calling |
| Долговременная память | PARTIAL | Memory-коннекторы (volatile, Redis, Azure AI Search и др.) существуют; есть вопросы безопасности — [Issue #14006](https://github.com/microsoft/semantic-kernel/issues/14006) |
| Сессионный контекст | SUPPORTED | Chat history как часть Kernel-конвейера — [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) |
| Персистентность | PARTIAL | Персистентность памяти — через внешние коннекторы; встроенного durable-хранилища состояния агента в проверенных страницах не заявлено |
| Инструменты / function-calling | SUPPORTED | Ядро продукта: модель вызывает функции, SK транслирует вызов — [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) |
| Навыки / плагины | SUPPORTED | Существующий код оформляется как plugin, OpenAPI-спецификации поддерживаются — [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) |
| Мультиагентность | PARTIAL | Была в SK Agents, но multi-agent orchestration, checkpointing и HITL теперь в первую очередь в Agent Framework — [medium.com](https://medium.com/@bhargavkoya56/migrating-from-semantic-kernel-agents-to-microsoft-agent-framework-in-net-42f8236327dc) |
| Workflow / DAG | PARTIAL | Process Framework упомянут как experimental во вторичных источниках — [openlegion-ai/landing](https://github.com/openlegion-ai/landing/blob/HEAD/src/content/comparison/semantic-kernel.md); зрелость не подтверждена официально в проверенных страницах |
| Ветвления / циклы | PARTIAL | Через код приложения и function-calling, не через визуальный DAG |
| Обработка ошибок / повторы / восстановление | PARTIAL | Hooks and filters заявлены официально — [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/); durable recovery в проверенных страницах не заявлено |
| Планирование по расписанию / триггеры | NOT_APPLICABLE | SK — библиотека, не планировщик; триггеры — ответственность хост-приложения |
| Интеграции | SUPPORTED | Connectors и OpenAPI-плагины — [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) |
| Работа с файлами | PARTIAL | Через плагины пользователя; встроенного файлового агента нет |
| Выполнение кода / терминал | PARTIAL | Только через пользовательские плагины; встроенного code-executor в проверенных страницах не заявлено |
| Браузер / GUI | UNSUPPORTED | В проверенных официальных источниках не заявлено |
| Мессенджеры | UNSUPPORTED | В проверенных официальных источниках интеграций с Telegram/мессенджерами нет |
| Локальные + облачные LLM | SUPPORTED | Model-agnostic middleware, смена моделей без переписывания кода — [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) |
| RAG | SUPPORTED | Memory/RAG-коннекторы; детали миграции неймспейсов — [Issue #3151](https://github.com/microsoft/semantic-kernel/issues/3151) |
| История / логирование | SUPPORTED | Telemetry support заявлена официально — [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) |
| Трейсинг / наблюдаемость | SUPPORTED | «flexible, modular, and observable», telemetry — [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) |
| HITL | PARTIAL | Hooks/filters позволяют вставить подтверждение; первоклассного HitlApproval-примитива в проверенных страницах нет. Вторичные источники относят HITL к Agent Framework |
| Песочница / безопасность | PARTIAL | Security enhancing capabilities, hooks/filters; риск memory poisoning обсуждается в [Issue #14006](https://github.com/microsoft/semantic-kernel/issues/14006) |
| Аутентификация | PARTIAL | Через коннекторы провайдеров и хост-приложение; собственной системы auth в SK нет |
| API / расширяемость | SUPPORTED | Модульность и расширяемость — заявленный принцип — [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) |
| UI | NOT_APPLICABLE | UI нет, это SDK |
| Импорт / экспорт | PARTIAL | OpenAPI как переносимый формат плагинов — [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) |
| Тестирование | UNKNOWN | Выделенного eval/test-фреймворка в проверенных страницах не найдено |
| Production-готовность | PARTIAL | Enterprise-позиционирование и 1.0+ стабильность заявлены, но maintenance-статус (вторичные источники) — стратегический риск |
| Оффлайн-работа | PARTIAL | Возможна с локальными моделями через коннекторы; детали в проверенных страницах ограничены |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Нативный .NET-путь (C# наряду с Python/Java), редкость среди агентных фреймворков | [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) | Официальная документация (primary) |
| Плагины из существующего кода + OpenAPI: переиспользование уже написанных API | [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) | Официальная документация (primary) |
| Model-agnostic: смена моделей без переписывания приложения | [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) | Официальная документация (primary) |
| Наблюдаемость и hooks/filters для ответственного AI на масштабе | [learn.microsoft.com/semantic-kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) | Официальная документация (primary) |
| MIT-лицензия | [devblogs.microsoft.com/semantic-kernel](https://devblogs.microsoft.com/semantic-kernel/?p=2061) | Официальный блог Microsoft (primary) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Maintenance mode, новые фичи — в Microsoft Agent Framework; миграционный долг для существующих пользователей | Высокая | Вторичные источники, официально в проверенном фрагменте не зафиксировано | [medium.com](https://medium.com/@bhargavkoya56/migrating-from-semantic-kernel-agents-to-microsoft-agent-framework-in-net-42f8236327dc), [particula.tech](https://particula.tech/blog/microsoft-agent-framework-vs-google-adk-vs-smolagents) |
| Планировщики удалены: SequentialPlanner (v1.0), .NET planners сняты с публикации в апреле 2025 | Высокая | Подтверждено PR | [PR #12399](https://github.com/microsoft/semantic-kernel/pull/12399) |
| Memory-коннекторы уязвимы к memory poisoning (ASI06) | Средняя | Feature/security-запрос открыт | [Issue #14006](https://github.com/microsoft/semantic-kernel/issues/14006) |
| Частые переломы API вокруг Memory/неймспейсов в RC-периоде | Средняя | Исторические issue | [Issue #3151](https://github.com/microsoft/semantic-kernel/issues/3151), [Issue #4262](https://github.com/microsoft/semantic-kernel/issues/4262) |

## 5. Неудовлетворённые потребности пользователей

- Защита памяти от отравления (OWASP ASI06) для memory-коннекторов: [Issue #14006 — Python: Security: OWASP Agent Memory Guard](https://github.com/microsoft/semantic-kernel/issues/14006).
- Понятная замена удалённым планировщикам внутри SK, а не только миграция в другой фреймворк: контекст — [PR #12399](https://github.com/microsoft/semantic-kernel/pull/12399) и разбор [openlegion-ai/landing](https://github.com/openlegion-ai/landing/blob/HEAD/src/content/comparison/semantic-kernel.md).
- Стабильность неймспейсов и пакетов Memory при апгрейдах: [Issue #3151](https://github.com/microsoft/semantic-kernel/issues/3151), [Issue #4262](https://github.com/microsoft/semantic-kernel/issues/4262).
- Отдельных релевантных тредов Reddit и Discussions с прямыми URL в ходе проверки надёжно верифицировать не удалось — UNKNOWN.

## 6. Технические ограничения

- **Модель исполнения**: синхронно-асинхронный конвейер внутри процесса приложения (Kernel вызывает модель, модель просит функцию, SK исполняет и возвращает результат).
- **Детерминизм**: недетерминирован на уровне LLM; детерминированные шаги — только обычный код плагинов.
- **Параллелизм**: определяется хост-приложением (.NET/Python/Java runtime), встроенной DAG-параллельности в проверенных страницах не заявлено.
- **Отмена / таймауты**: через механизмы хост-языка (CancellationToken и аналоги) — в проверенных официальных страницах детализация ограничена (PARTIAL).
- **Идемпотентность**: встроенных гарантий не заявлено — ответственность плагинов (UNKNOWN в официальных источниках).
- **Восстановление после сбоя**: durable checkpoint/resume в SK в проверенных источниках не заявлен; вторичные источники относят checkpointing к Agent Framework.
- **Версионирование**: 1.0+ заявлена как non-breaking, но удаление планировщиков и смена стратегического вектора на Agent Framework — главный версионный риск.

## 7. Вердикт и применимость к CyberPipe

**Вердикт**: зрелый model-agnostic SDK с лучшим в подборке .NET-путём и плагинной моделью, но стратегически замороженный: планировщики удалены, развитие ушло в Microsoft Agent Framework. Как новая зависимость для проекта с горизонтом в годы — рискованно, как источник паттернов — ценно.

**Применимость к CyberPipe** (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-уровневый роутинг Tier0 regex / Tier1 ONNX / Tier2–3 LLM, TokenBudgetGovernor, Netrunner Mini-n8n DAG, SQLite WAL, Brainstem-песочница, HITL):

- **Прямое использование — нет для нового кода**: встраивать maintenance-SDK в новый .NET 10 продукт нельзя; если понадобится готовый .NET-агентный стек, оценивать следует Microsoft Agent Framework, а не SK.
- **Что заимствовать**: (1) плагинную модель `[KernelFunction]`-подобных функций как контракт для NetrunnerScriptNode/SkillNode — функция с описанием, которую LLM может вызвать; (2) auto function-calling вместо отдельного планировщика — прямое соответствие Tier2–3, где модель сама выбирает инструмент, а TokenBudgetGovernor ограничивает число шагов; (3) hooks/filters как точку вставки HITL и бюджетирования перед исполнением функции; (4) предупреждение из Issue #14006: долговременная память в SQLite WAL нуждается в защите от отравления (валидация записываемых фактов).
- **Что не подходит**: отсутствие DAG, триггеров, мессенджеров и UI — всё это CyberPipe строит сам (Netrunner, Telegram, Avalonia), SK здесь ничего не даёт.
- **Итог**: Semantic Kernel — референс плагинной/function-calling архитектуры для .NET и предупреждение о платформенном риске, а не зависимость CyberPipe.
