# Dify — анализ агентной платформы

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Dify (Do It For You — по докам) |
| Официальный сайт | [dify.ai](https://dify.ai) |
| Репозиторий | [github.com/langgenius/dify](https://github.com/langgenius/dify) |
| Документация | [docs.dify.ai](https://docs.dify.ai) |
| Категория | Open-source платформа разработки LLM-приложений: agentic workflow + RAG pipeline + agent framework + model management + observability |
| Назначение | «Create agents, agentic workflows, and chatbots that draw on your own data, then publish them as web apps or integrate them through APIs» — [docs.dify.ai/en/home](https://docs.dify.ai/en/introduction) |
| Целевая аудитория | Команды от прототипа до production; non-dev через визуальный canvas + разработчики через API/плагины |
| Статус на 2026-10-19 | Активно развивается: доки на Mintlify, версии 1.x (в issues упоминается v1.15.0), Marketplace, CLI difyctl |
| Лицензия | Модифицированный Apache 2.0: multi-tenant SaaS без письменного разрешения запрещён, логотип/копирайт во фронтенде удалять нельзя — вторичные разборы лицензионного текста, напр. [getdynamiq.ai](https://www.getdynamiq.ai/post/best-dify-alternatives-for-ai-workflow-automation); точный текст LICENSE в этой проверке построчно не извлекался |
| Развёртывание | Dify Cloud (Sandbox free), self-host Docker Compose, VPC/Enterprise |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | PARTIAL | Agent Node: model + strategy (ReAct, Plan-and-Execute) + tools — формулировка из [issues/35940](https://github.com/langgenius/dify/issues/35940); автономность ограничена предопределёнными инструментами |
| Планирование | PARTIAL | Plan-and-Execute стратегия агента (там же); визуальный workflow — основная модель |
| Долговременная память | PARTIAL | Переменные workflow, knowledge; долговременной памяти уровня Open WebUI в проверенных источниках не выделено |
| Сессионный контекст | SUPPORTED | Chatflow, conversation variables |
| Персистентность | SUPPORTED | Self-hosted БД, история приложений и прогонов |
| Инструменты / function-calling | SUPPORTED | Tools, плагины, Marketplace [marketplace.dify.ai](https://marketplace.dify.ai/) |
| Навыки / плагины | SUPPORTED | Develop Plugins: tools/models/integrations — [docs.dify.ai](https://docs.dify.ai) |
| Мультиагентность | PARTIAL | Несколько agent-узлов в workflow; supervisor-паттернов уровня LangGraph в проверенных источниках не показано |
| Workflow / DAG | SUPPORTED | Визуальный canvas, workflow/chatflow — ядро продукта |
| Ветвления / циклы | SUPPORTED | iteration (is_parallel, parallel_nums), loop, if-else — вторичные DSL-справочники по нодам Dify; первичная нодовая документация в этой проверке не открывалась, поэтому детали — PARTIAL по глубине |
| Обработка ошибок / повторы / восстановление | PARTIAL | Workflow pause/resumption упоминается во вторичных сводках; детальной retry-семантики в проверенных первичных источниках нет |
| Планирование по расписанию / триггеры | SUPPORTED | trigger-schedule (cron), trigger-webhook, trigger-plugin — глоссарий [langgenius/dify-docs glossary](https://github.com/langgenius/dify-docs/blob/HEAD/writing-guides/glossary.md) |
| Интеграции | SUPPORTED | Marketplace, сотни провайдеров моделей [docs.dify.ai](https://docs.dify.ai) |
| Работа с файлами | PARTIAL | File upload в workflow; ограничение: агенты не могут напрямую передавать загруженные файлы workflow-инструментам — [issues/30713](https://github.com/langgenius/dify/issues/30713) по поиску |
| Выполнение кода / терминал | PARTIAL | Code-узлы в workflow (вторично); терминала/Brainstem-песочницы нет |
| Браузер / GUI | PARTIAL | Web apps публикация; browser-агента нет |
| Мессенджеры | UNKNOWN | Telegram как канал доставки в проверенных источниках не подтверждён |
| Локальные + облачные LLM | SUPPORTED | Model providers list — [docs.dify.ai](https://docs.dify.ai/getting-started/readme/model-providers) (ссылка из README) |
| RAG | SUPPORTED | Knowledge/RAG pipeline, modular RAG в 1.9.0 — [discussions/26138](https://github.com/langgenius/dify/discussions/26138) |
| История / логирование | SUPPORTED | Observability: Opik, Langfuse, Arize Phoenix — README репозитория |
| Трейсинг | SUPPORTED | Интеграции observability выше |
| HITL | SUPPORTED | human-input node: «Pauses workflow execution to request human review» — глоссарий dify-docs |
| Песочница / безопасность | PARTIAL | Workspace/тенантность; sandbox исполнения кода в проверенных источниках не детализирована |
| Аутентификация | SUPPORTED | Workspace, API keys; запрос workflow-level credential override — [issues/28815](https://github.com/langgenius/dify/issues/28815) |
| API / расширяемость | PARTIAL | REST API приложений есть, но программного создания/удаления workflow через API нет — [issues/30099](https://github.com/langgenius/dify/issues/30099) |
| UI | SUPPORTED | Визуальный canvas — ядро продукта |
| Импорт / экспорт | PARTIAL | DSL workflow (вторично); канонической схемы агента нет — критика в §4 |
| Тестирование | PARTIAL | Workflow test/run в canvas; eval-фреймворка уровня LangSmith в проверенных источниках не показано |
| Production-готовность | SUPPORTED | Позиционирование «prototype to production», Cloud/VPC/self-host |
| Оффлайн-работа | PARTIAL | Self-host возможен; полностью оффлайн с локальными моделями зависит от конфигурации, в проверенных источниках не гарантирована |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Единый canvas: workflow + RAG + agents + модели + observability | [github.com/langgenius/dify](https://github.com/langgenius/dify) | README (primary) |
| Триггеры schedule/webhook/plugin как стартовые узлы | [dify-docs glossary](https://github.com/langgenius/dify-docs/blob/HEAD/writing-guides/glossary.md) | Официальные доки (primary) |
| Human-input узел с паузой workflow | Там же | Primary |
| Модульный RAG с настраиваемым пайплайном обработки документов | [discussions/26138](https://github.com/langgenius/dify/discussions/26138) | Официальное обсуждение релиза |
| Marketplace плагинов и шаблонов | [docs.dify.ai](https://docs.dify.ai) | Primary |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Нет канонической схемы агента: приложение = визуальная конфигурация, а не структурно определённый агент | Средняя | Внешний анализ 2026 | PDF-анализ из поиска (qu3ry.net) — вторичный, требует осторожности |
| Public API не умеет программно создавать/удалять/менять структуру workflow | Высокая для DevOps/GitOps | Открытый запрос | [issues/30099](https://github.com/langgenius/dify/issues/30099) |
| Agent Node ограничен предопределёнными инструментами, нет интеграции внешних агентных рантаймов (Claude Code/Codex) | Средняя | Открытый feature request | [issues/35940](https://github.com/langgenius/dify/issues/35940) |
| Metadata filter в Knowledge Retrieval нельзя привязать к переменным workflow | Средняя | Открытый issue v1.15.0 | [issues/38508](https://github.com/langgenius/dify/issues/38508) |
| Start node не видит заголовки/query/body входящего HTTP-запроса | Средняя | Открытый issue | [issues/28270](https://github.com/langgenius/dify/issues/28270) |
| Tool streaming в chatflow не поддержан для agent nodes | Низкая | Известное ограничение по issue | [issues/25206](https://github.com/langgenius/dify/issues/25206) |
| Лицензионные ограничения multi-tenancy и брендинга | Средняя при коммерческом SaaS; низкая для персонального CyberPipe | Подтверждена вторичными разборами | [getdynamiq.ai](https://www.getdynamiq.ai/post/best-dify-alternatives-for-ai-workflow-automation) |

## 5. Неудовлетворённые потребности пользователей

- Программное управление workflow через API (create/delete/structure) для CI/CD — [issues/30099](https://github.com/langgenius/dify/issues/30099).
- Внешний агентный узел (Claude Code/Codex) вместо закрытого Agent Node — [issues/35940](https://github.com/langgenius/dify/issues/35940).
- Workflow-level переопределение кредов провайдеров для учёта затрат и разделения окружений — [issues/28815](https://github.com/langgenius/dify/issues/28815).
- Доступ к HTTP-заголовкам/телу в Start node для мультитенантных интеграций — [issues/28270](https://github.com/langgenius/dify/issues/28270).
- Передача загруженных файлов от агента в workflow-инструменты — поисковая сводка [issues/30713](https://github.com/langgenius/dify/issues/30713).

## 6. Технические ограничения

- Модель исполнения: серверный workflow-движок (Python backend), визуальный DAG с контейнерными узлами iteration/loop.
- Детерминизм: структура графа детерминирована, содержимое LLM-узлов — нет; Tier0/Tier1 детерминированной маршрутизации без LLM нет.
- Параллелизм: iteration is_parallel/parallel_nums (вторично); семантика отмены параллельных веток UNKNOWN.
- Отмена/таймауты: human-input пауза есть; общих таймаутов узлов в проверенных источниках не показано.
- Идемпотентность: не специфицирована; повторный webhook-триггер может дублировать эффекты.
- Восстановление: pause/resume заявлен во вторичных сводках; durable checkpoint каждого узла уровня LangGraph не подтверждён.
- Версионирование: версии приложений есть как продуктовая функция (детали UNKNOWN в этой проверке); GitOps-версионирования workflow как кода нет — следствие issues/30099.

## 7. Вердикт и применимость к CyberPipe

**Вердикт: сильнейший референс по набору узлов и триггеров для Netrunner, но не заимствуемая платформа.** Dify ближе всего к CyberPipe Mini-n8n по духу: визуальный DAG, trigger-schedule/webhook, human-input, iteration/loop, RAG, observability. Именно этот словарь узлов стоит сверять с Netrunner: SkillNode/NetrunnerScriptNode/ConditionNode/LlmProcessNode/HitlApprovalNode/DeliveryNode против start/trigger-*/human-input/iteration/loop/if-else у Dify.

Применимо точечно:
- Триггерная модель (schedule/webhook/plugin) — как чек-лист для Netrunner-триггеров сверх Telegram-сообщений.
- Human-input как узел графа с паузой — подтверждение дизайна HitlApprovalNode.
- Параллельная iteration с явным parallel_nums — образец управления параллелизмом и бюджетом токенов.
- Уроки провалов: CyberPipe должен иметь программный API создания workflow (то, чего не хватает Dify, issues/30099) и передачу файлов между узлами без костылей.
- Неприменимо: Python/веб-стек, серверная мультитенантность, лицензионные ограничения, отсутствие локального .NET, Telegram/Avalonia, TokenBudgetGovernor и Brainstem-песочницы; RAG Dify тяжелее нужного персональному ассистенту.

Итог: REFERENCE_ONLY — эталонная нодовая модель и триггеры; реализацию делать свою в Netrunner.
