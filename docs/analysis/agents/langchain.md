# LangChain (+ LangGraph) — анализ агентной платформы

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | LangChain; оркестрационный рантайм — LangGraph; наблюдаемость/деплой — LangSmith |
| Официальный сайт / документация | [docs.langchain.com/oss/python/langchain/overview](https://docs.langchain.com/oss/python/langchain/overview), [docs.langchain.com/oss/python/langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) |
| Репозиторий | [github.com/langchain-ai/langchain](https://github.com/langchain-ai/langchain) |
| Категория | Платформа разработки агентов: высокоуровневый агентный фреймворк (create_agent) + низкоуровневый графовый рантайм (LangGraph) + коммерческая платформа LangSmith |
| Назначение | Сборка LLM-приложений и агентов любой сложности: от простого tool-calling цикла до долгоживущих stateful-агентов с durable execution и HITL |
| Целевая аудитория | Python- и JS/TS-разработчики; команды от прототипов до enterprise-production (в доках названы Klarna, Uber, J.P. Morgan как пользователи LangGraph) |
| Статус на 2026-10-19 | Активно развивается. По вторичным сводкам LangChain и LangGraph достигли 1.0 GA в октябре 2025 ([dreaming.press](https://dreaming.press/posts/ai-agent-frameworks-github-ranked-by-stars-2026.html)); официальные доки на дату проверки описывают зрелую линейку LangChain / LangGraph / Deep Agents / LangSmith |
| Лицензия | MIT — поле `license = "MIT"` в метаданных пакета ([commit в langchain-ai/langchain](https://github.com/langchain-ai/langchain/commit/5fc67ca2c760546f27e57ee518f25104dff6dd21)), подтверждение в issue-выводе `pip show` ([Issue #15370](https://github.com/langchain-ai/langchain/issues/15370)). LangSmith — коммерческий сервис поверх открытого ядра |
| Развёртывание | Локально как Python/JS пакеты (self-hosted); управляемый деплой и трейсинг — LangSmith (облако/гибрид) |

Официальная навигация по продуктам из доков: Deep Agents (batteries-included harness) → LangChain agents (create_agent) → LangGraph (низкоуровневая оркестрация) → LangSmith (трейсинг, eval, деплой) — [docs.langchain.com/oss/python/langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview).

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE. Приведены только применимые строки. Где возможность относится именно к LangGraph, это указано.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность агентов | SUPPORTED | create_agent: модель + инструменты + промпт + middleware — [langchain/overview](https://docs.langchain.com/oss/python/langchain/overview) |
| Планирование | PARTIAL | Планирование — в Deep Agents (harness поверх LangGraph); в базовом create_agent отдельного планировщика в проверенных страницах нет |
| Долговременная память | SUPPORTED | LangGraph: «long-term memory» заявлена среди ключевых возможностей рантайма (вторично подтверждено сводкой репозитория [kevinsai2025/langchain](https://github.com/kevinsai2025/langchain)); детали — в persistence-доках LangGraph |
| Сессионный контекст | SUPPORTED | Состояние графа (StateGraph, MessagesState) — [langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) |
| Персистентность | SUPPORTED | LangGraph: durable execution и checkpointer-ы (в т.ч. SQLite/Postgres по вторичным туториалам); HITL требует checkpointer — [whaler404/langchain-docs-skill](https://github.com/whaler404/langchain-docs-skill/blob/HEAD/oss_python_docs/deepagents/human-in-the-loop.md) |
| Инструменты / function-calling | SUPPORTED | Инструменты как обычные функции, OpenAPI/function-calling адаптеры — [langchain/overview](https://docs.langchain.com/oss/python/langchain/overview) |
| Навыки / плагины | PARTIAL | Skills-концепция развивается вокруг Deep Agents/LangGraph.JS (вторично); в проверенных Python-доках «skills» как единицы CyberPipe не выделены |
| Мультиагентность | SUPPORTED | LangGraph: structured graphs для мультиагентных систем; supervisor/handoff паттерны — вторично [dev.to](http://dev.to/ipsphil_digital/building-ai-agents-with-langgraph-from-fundamentals-to-production-1cbl) |
| Workflow / DAG | SUPPORTED | Ядро LangGraph: StateGraph с узлами и рёбрами, смешение детерминированных и агентных шагов — [langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) |
| Ветвления / циклы | SUPPORTED | Условные рёбра, циклы графа, reflection-паттерны — вторично, следует из графовой модели |
| Обработка ошибок / повторы / восстановление | SUPPORTED | Durable execution LangGraph: состояние переживает сбои и возобновляется с checkpoint — [langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) |
| Планирование по расписанию / триггеры | PARTIAL | Стриминг и внешние триггеры через приложения; встроенного cron в проверенных страницах не показано |
| Интеграции | SUPPORTED | Крупнейшая экосистема интеграций моделей и инструментов (OpenAI, Anthropic, Google и др.) — [langchain/overview](https://docs.langchain.com/oss/python/langchain/overview) |
| Работа с файлами | SUPPORTED | Deep Agents: virtual filesystem и автоматическая компрессия контекста — [langchain/overview](https://docs.langchain.com/oss/python/langchain/overview) |
| Выполнение кода / терминал | PARTIAL | Через инструменты; выделенной песочницы в проверенных страницах LangChain не описано |
| Браузер / GUI | PARTIAL | Через инструменты/интеграции; нативного GUI-агента в проверенных страницах нет |
| Мессенджеры | UNKNOWN | В проверенных официальных источниках интеграция с Telegram/мессенджерами не подтверждена |
| Локальные + облачные LLM | SUPPORTED | Провайдеры OpenAI/Anthropic/Google и др.; локальные — через интеграции (Ollama и т.п., вторично) |
| RAG | SUPPORTED | Исторически сильнейшая сторона LangChain: retrievers, vector stores, document loaders (экосистема интеграций) |
| История / логирование | SUPPORTED | LangSmith: трейсинг, отладка, оценка агентов на любом фреймворке — [langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) |
| Трейсинг / наблюдаемость | SUPPORTED | LangSmith + LangSmith Engine (детект проблем в трейсах и предлагаемые фиксы) — [langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) |
| HITL (человек в цикле) | SUPPORTED | LangGraph interrupts: пауза, сохранение состояния в checkpointer, resume с `Command(resume=...)` — вторично [dev.to](https://dev.to/rmatam/langgraph-orchestration-hoh), первичный механизм заявлен в [langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) |
| Песочница / безопасность | PARTIAL | Известна уязвимость сериализатора checkpoint (msgpack) по вторичному отчёту; общей sandbox-модели в проверенных страницах нет — [rsoreq/langchain#52](https://github.com/rsoreq/langchain/issues/52) |
| Аутентификация | PARTIAL | Актуально для LangSmith Platform; в локальном OSS-ядре пользовательской auth нет |
| API / расширяемость | SUPPORTED | Middleware в create_agent, кастомные узлы/рёбра LangGraph, LCEL-композиция |
| UI | PARTIAL | LangSmith (наблюдаемость) и Fleet (no-code builder, вторично из доков); UI конечного пользователя строится приложением |
| Импорт / экспорт | PARTIAL | Сериализация состояния графов; переносимых форматов воркфлоу в проверенных страницах не показано |
| Тестирование | SUPPORTED | LangSmith evaluation: оценка агентов по трейсам и датасетам — [langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) |
| Production-готовность | SUPPORTED | LangGraph доверяют в production крупные компании (заявление доков); durable execution, streaming, HITL из коробки рантайма |
| Оффлайн-работа | PARTIAL | OSS-ядро и локальные модели работают оффлайн; LangSmith-трейсинг требует облака/ключа |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| LangGraph смешивает детерминированные и LLM-шаги в одном графе — контроль ровно там, где нужна предсказуемость | [langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) | Официальная документация (primary) |
| Durable execution + persistence + HITL как свойства рантайма, а не библиотеки поверх | [langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) | Официальная документация (primary) |
| Самая большая экосистема интеграций моделей, инструментов и векторных хранилищ | [langchain/overview](https://docs.langchain.com/oss/python/langchain/overview) | Официальная документация (primary) |
| Чёткое разделение уровней: Deep Agents → LangChain agents → LangGraph → LangSmith | [langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) | Официальная документация (primary) |
| Production-адопшн: Klarna, Uber, J.P. Morgan названы пользователями LangGraph | [langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) | Заявление вендора (primary, маркетинг) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Избыточная сложность и абстракции ради простых задач; «худшая библиотека» в оценках части Reddit/HN-сообщества | Средняя | Устойчивая пользовательская критика (вторично) | [news.ycombinator.com/item?id=36645575](https://news.ycombinator.com/item?id=36645575), [checkthat.ai](https://checkthat.ai/brands/langchain) |
| Крутая кривая обучения и путаница границ LangChain / LangGraph / LangSmith / Platform | Средняя | Вторичные исследовательские сводки | [razpetel/research-catalogue](https://github.com/razpetel/research-catalogue/blob/HEAD/research/catalogue/2026-02-23-langgraph.md) |
| Сложность схем состояния LangGraph при росте графов (over-complicated state schemas) | Средняя | Миграционные гайды сообщества | [agenticallysh/agentic-framework-migration-guides](https://github.com/agenticallysh/agentic-framework-migration-guides/blob/HEAD/migrations/langchain-to-langgraph/README.md) |
| Уязвимость сериализации checkpoint (msgpack в jsonplus.py, severity 7.4 во вторичном отчёте) | Высокая | Вторичный security-отчёт, требует перепроверки по официальному advisory — статус PARTIAL | [rsoreq/langchain#52](https://github.com/rsoreq/langchain/issues/52) |
| Исторический опыт: прямые вызовы API проще, чем цепочки абстракций, для значительной части задач | Средняя | Пользовательский опыт production-систем | [news.ycombinator.com/item?id=35824777](https://news.ycombinator.com/item?id=35824777) |

## 5. Неудовлетворённые потребности пользователей

- Меньше абстракций и прозрачнее отладка: многолетняя линия критики на HN/Reddit — [news.ycombinator.com/item?id=36645575](https://news.ycombinator.com/item?id=36645575), [news.ycombinator.com/item?id=35824777](https://news.ycombinator.com/item?id=35824777).
- Понятные границы продуктов и меньшая фрагментация (LangChain vs LangGraph vs LangSmith vs Deep Agents): [razpetel/research-catalogue](https://github.com/razpetel/research-catalogue/blob/HEAD/research/catalogue/2026-02-23-langgraph.md).
- Управляемая сложность состояния в больших графах: [agenticallysh migration guide](https://github.com/agenticallysh/agentic-framework-migration-guides/blob/HEAD/migrations/langchain-to-langgraph/README.md).
- Конкретных открытых Issues репозитория langchain-ai/langchain, открытых и верифицированных в ходе этой проверки сверх перечисленных вторичных следов, недостаточно — отдельные номера не выдумываем (UNKNOWN по полноте списка).

## 6. Технические ограничения

- **Модель исполнения**: LangChain-агент — цикл поверх LangGraph-рантайма; LangGraph — граф состояний с супершагами, checkpoint после шагов.
- **Детерминизм**: графовый каркас детерминирован, содержимое узлов с LLM — нет; reflection/циклы усиливают вариативность и стоимость.
- **Параллелизм**: параллельные ветки графа поддерживаются моделью; фактическая конкурентность зависит от исполнителя и провайдеров.
- **Отмена / таймауты**: interrupts дают управляемую паузу для HITL; жёсткой семантики отмены LLM-вызова в проверенных страницах не зафиксировано — UNKNOWN.
- **Идемпотентность**: checkpoint-resume может повторно выполнять побочные эффекты узла при восстановлении, если узел не идемпотентен — ответственность разработчика (вывод из модели durable execution).
- **Восстановление после сбоя**: сильное место — checkpointer (SQLite/Postgres и др.); без checkpointer HITL и resume невозможны.
- **Версионирование**: история частых breaking changes и миграций (LCEL, 0.x → 1.0) — главный эксплуатационный налог экосистемы.

## 7. Вердикт и применимость к CyberPipe

**Вердикт**: самая полная и зрелая платформа подборки. LangChain даёт интеграции и агентный цикл, LangGraph — лучший в классе графовый рантайм с durable execution, persistence и HITL. Цена — сложность, фрагментация продуктов и зависимость от LangSmith для полного опыта.

**Применимость к CyberPipe** (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-уровневый роутинг Tier0 regex / Tier1 ONNX / Tier2-3 LLM, TokenBudgetGovernor, Netrunner Mini-n8n DAG, SQLite WAL, Brainstem-песочница, HITL):

- **Как зависимость — нет** (Python/JS платформа в .NET 10 продукте; встраивание означало бы второй рантайм ради уже спроектированного Netrunner).
- **Главный концептуальный донор для Netrunner**: LangGraph — прямой аналог Mini-n8n DAG. Заимствовать: (1) checkpoint после каждого узла в SQLite WAL (у CyberPipe это естественно ложится на WAL-режим); (2) interrupt/resume как реализацию HitlApprovalNode: пауза графа, сохранение состояния, продолжение по команде из Telegram/Avalonia; (3) смешение детерминированных узлов (ConditionNode, NetrunnerScriptNode) и агентных (LlmProcessNode) в одном графе — точное соответствие архитектуре CyberPipe; (4) тайм-тревел/инспекцию состояния графа для отладки через Avalonia UI.
- **Предупреждения**: не повторять фрагментацию (у CyberPipe один DAG-движок, а не четыре продукта); не допускать раздувания схем состояния узлов; сериализацию checkpoint делать безопасной (не msgpack-подобный небезопасный десериализатор — использовать явные JSON-схемы).
- **Итог**: LangGraph — эталон семантики Netrunner (граф + checkpoint + interrupt), LangChain — источник интеграционных паттернов; кодовой зависимостью не брать.
