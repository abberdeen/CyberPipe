# LangGraph — анализ агентной платформы

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN. Парный обзор экосистемы LangChain в целом — в [langchain.md](/docs/analysis/agents/langchain.md); здесь фокус только на LangGraph как рантайме.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | LangGraph |
| Официальный сайт | [langchain.com/langgraph](https://www.langchain.com/langgraph) |
| Репозиторий | [github.com/langchain-ai/langgraph](https://github.com/langchain-ai/langgraph); JS-версия — langgraphjs |
| Документация | [docs.langchain.com/oss/python/langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) |
| Категория | Низкоуровневый оркестрационный фреймворк и рантайм для stateful-агентов |
| Назначение | «low-level orchestration framework for building, managing, and deploying long-running, stateful agents» — README; смешение детерминированных и LLM-шагов в одном графе — доки |
| Целевая аудитория | Разработчики (Python/JS), которым нужен контроль над агентной оркестрацией; production-команды (в доках названы Klarna, Uber, J.P. Morgan) |
| Статус на 2026-10-19 | Активно развивается; экосистема Deep Agents / LangChain / LangGraph / LangSmith описана в актуальных доках |
| Лицензия | MIT — бейдж PyPI license в README [github.com/langchain-ai/langgraph](https://github.com/langchain-ai/langgraph); LangSmith/Deployments — коммерческий слой |
| Развёртывание | `pip install -U langgraph` локально; managed deployment — LangSmith Deployments (платно по вторичным источникам) |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Агентные циклы на StateGraph; Deep Agents поверх — планирование/субагенты |
| Планирование | PARTIAL | В самом LangGraph планировщика нет — он в Deep Agents (harness); граф задаёт разработчик |
| Долговременная память | SUPPORTED | «Comprehensive memory: short-term working + long-term persistent across sessions» — README |
| Сессионный контекст | SUPPORTED | Threads, MessagesState, состояние графа |
| Персистентность | SUPPORTED | Checkpointer-ы (в т.ч. SQLite/Postgres по экосистеме); «if you use a checkpointer, durable execution is enabled» — durable-execution доки из поиска |
| Инструменты / function-calling | SUPPORTED | Tools через LangChain-компоненты или свои функции; LangGraph работает и без LangChain — доки |
| Навыки / плагины | PARTIAL | Skills-парадигма развивается в Deep Agents; в ядре LangGraph skills как единицы нет |
| Мультиагентность | SUPPORTED | Subgraphs, supervisor/handoff паттерны экосистемы |
| Workflow / DAG | SUPPORTED | StateGraph: узлы, рёбра, START/END — hello-world из доков |
| Ветвления / циклы | SUPPORTED | Условные рёбра и циклы — суть графовой модели |
| Обработка ошибок / повторы / восстановление | SUPPORTED | Durable execution: возобновление «from exactly where they left off» — README |
| Планирование по расписанию / триггеры | PARTIAL | Внешние триггеры через приложение/LangSmith; встроенного cron в проверенных страницах ядра нет |
| Интеграции | SUPPORTED | Вся экосистема LangChain интеграций моделей/инструментов |
| Работа с файлами | PARTIAL | Через инструменты; filesystem — в Deep Agents |
| Выполнение кода / терминал | PARTIAL | Через инструменты; собственной песочницы в ядре нет |
| Браузер / GUI | PARTIAL | Через инструменты; нативного browser-агента и UI нет |
| Мессенджеры | UNKNOWN | Telegram в проверенных официальных источниках не подтверждён |
| Локальные + облачные LLM | SUPPORTED | Любые модели через интеграции, включая локальные (Ollama вторично) |
| RAG | PARTIAL | Строится из LangChain retrievers/vector stores; готового RAG-пайплайна в LangGraph нет |
| История / логирование | SUPPORTED | Состояние/чекпоинты + LangSmith трейсинг |
| Трейсинг | SUPPORTED | LangSmith: visualization, state transitions, runtime metrics — README |
| HITL | SUPPORTED | `interrupt()`: пауза, состояние в checkpointer, resume `Command(resume=...)` — доки/демо из поиска [dev.to](https://dev.to/rmatam/langgraph-orchestration-hoh) и обзор interrupts |
| Песочница / безопасность | PARTIAL | Sandbox-модели в ядре нет; безопасность — ответственность приложения |
| Аутентификация | PARTIAL | Актуально для LangSmith Platform; в OSS-ядре пользовательской auth нет |
| API / расширяемость | SUPPORTED | Полностью программный API графов, subgraphs, functional API |
| UI | UNSUPPORTED | UI нет by design; визуализация — LangSmith/Studio вторично |
| Импорт / экспорт | PARTIAL | Сериализация состояния; переносимого формата воркфлоу между системами нет |
| Тестирование | PARTIAL | LangSmith evaluation поверх трейсов; юнит-тесты графов — своими средствами |
| Production-готовность | SUPPORTED | Длительные stateful агенты в production у названных компаний (заявление вендора) |
| Оффлайн-работа | PARTIAL | OSS-ядро + локальные модели — да; LangSmith требует облака |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Смешение детерминированных и LLM-шагов в одном графе — аудируемость там, где нужна предсказуемость | [docs.langchain.com/oss/python/langgraph/overview](https://docs.langchain.com/oss/python/langgraph/overview) | Официальная документация (primary) |
| Durable execution: переживает сбои и длинные паузы, resume с checkpoint | [github.com/langchain-ai/langgraph](https://github.com/langchain-ai/langgraph) | README (primary) |
| HITL через interrupt как примитив рантайма, а не костыль | Доки overview + interrupts-материалы из поиска | Primary + вторичное подтверждение |
| Двухуровневая память: short-term working + long-term persistent | README | Primary |
| Низкий уровень без навязанной магии: можно без LangChain | Доки overview | Primary |
| Отладка через LangSmith: пути исполнения, переходы состояния, метрики | README | Primary |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Overkill и крутая кривая обучения для простых задач | Средняя | Устойчивая вторичная критика | [dev.to обзор 2026](https://dev.to/guang_diyuzhi_74b0fec2c09/langgraph-review-building-reliable-ai-agents-in-2026-3nc2) |
| Сложность схем состояния при росте графов | Средняя | Миграционные гайды сообщества (см. также langchain.md) | Вторичное |
| Конфликт кастомного checkpointer с API server runtime: кастомный игнорируется на платформе | Средняя | Подтверждён ответом в issue | [issues/6559](https://github.com/langchain-ai/langgraph/issues/6559) |
| Для локальных/desktop сценариев в langgraph-api нет SQLite-бэкенда, только in-memory/JSON или Postgres | Средняя для CyberPipe-подобных кейсов | Открытый feature request JS-трека | [langgraphjs/issues/1970](https://github.com/langchain-ai/langgraphjs/issues/1970) |
| Детерминизм и идемпотентность — обязанность разработчика, а не гарантия рантайма | Средняя | Прямо следует из durable-execution доков («workflow should be deterministic and idempotent») | Доки из поиска (restful3 mirror) |
| Платный слой: трейсинг/деплой LangSmith — per-seat + usage по вторичным источникам | Низкая для OSS-использования | Вторичные прайс-разборы | rentierdigital из поиска |

## 5. Неудовлетворённые потребности пользователей

- SQLite-бэкенд для langgraph-api в локальных и desktop-развёртываниях вместо in-memory/JSON или тяжёлого Postgres — [langgraphjs/issues/1970](https://github.com/langchain-ai/langgraphjs/issues/1970).
- Понятная семантика кастомных checkpointer-ов на managed runtime (сейчас молча игнорируются) — [issues/6559](https://github.com/langchain-ai/langgraph/issues/6559).
- Меньше boilerplate и проще state-схемы для типовых агентов — вторичная линия критики [dev.to](https://dev.to/guang_diyuzhi_74b0fec2c09/langgraph-review-building-reliable-ai-agents-in-2026-3nc2).
- Конкретные Reddit-запросы в этой проверке не извлекались — UNKNOWN; не выдумывать.

## 6. Технические ограничения

- Модель исполнения: графовый рантайм StateGraph с чекпоинтами на переходах; super-step семантика (детали вне проверенных страниц — PARTIAL).
- Детерминизм: рантайм требует детерминированных, идемпотентных узлов для корректного replay; сам по себе LLM-узел недетерминирован.
- Параллелизм: параллельные ветки графа поддержаны моделью; лимитов параллелизма и бюджетной политики в ядре нет — это уровень приложения (аналог TokenBudgetGovernor придётся делать самому).
- Отмена/таймауты: interrupt — для HITL-пауз; общих per-node timeout/cancel политик в проверенных источниках не показано — PARTIAL.
- Идемпотентность: требование к коду узлов, а не гарантия; сайд-эффекты при replay могут повториться.
- Восстановление: сильнейшая сторона — resume с последнего чекпоинта, включая ожидание человека неограниченно долго.
- Версионирование: графы — код, версионируются Git-ом; версионирования состояния/миграций чекпоинтов как продукта нет.

## 7. Вердикт и применимость к CyberPipe

**Вердикт: главный архитектурный референс для Netrunner, при нулевом заимствовании кода.** LangGraph — единственная из пяти систем, где durable execution, checkpoint, interrupt/HITL и двухуровневая память являются примитивами рантайма, а не надстройками. Это ровно контракт, который CyberPipe должен реализовать в Mini-n8n DAG на .NET: чекпоинт после каждого узла в SQLite WAL, HitlApprovalNode как interrupt с resume, thread_id как идентификатор сессии Telegram-диалога.

Применимо точечно:
- Модель чекпоинтов: состояние после каждого шага, resume «exactly where left off» — образец для персистентности Netrunner-прогонов.
- Interrupt-семантика: пауза → сохранение → внешнее решение → `Command(resume=...)`; в CyberPipe resume приходит из Telegram/Avalonia HITL.
- Требование детерминированности/идемпотентности узлов для replay — внести как правило для NetrunnerScriptNode и DeliveryNode (идемпотентная доставка в Telegram!).
- Урок issues/6559 и langgraphjs/1970: не усложнять хранилище — SQLite WAL достаточен локальному ассистенту, Postgres не нужен.
- Неприменимо: Python/JS стек вместо .NET 10, отсутствие UI/мессенджеров, платный LangSmith вместо локального трейсинга, отсутствие 4-tier routing и TokenBudgetGovernor — бюджетная политика остаётся уникальной частью CyberPipe поверх графовой модели.

Итог: REFERENCE_ONLY высшего приоритета для семантики исполнения Netrunner (checkpoints + interrupts + durable resume); стек и код не заимствовать.
