# PydanticAI — анализ агентного фреймворка

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Pydantic AI |
| Официальный сайт / документация | [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) (ранее ai.pydantic.dev, редирект подтверждён при проверке) |
| Репозиторий | [github.com/pydantic/pydantic-ai](https://github.com/pydantic/pydantic-ai) |
| Категория | Типизированный Python AI SDK: агентный цикл, модели, инструменты, структурированные выходы; рядом — Pydantic Graph, Pydantic Evals, Logfire |
| Назначение | Production GenAI-приложения с типобезопасностью «как FastAPI для агентов»: валидируемые выходы, dependency injection, наблюдаемость |
| Целевая аудитория | Python-разработчики production-систем, команды, которым важны типы, тесты и наблюдаемость |
| Статус на 2026-10-19 | Активно развивается; документация описывает зрелую платформу с Harness, durable execution и Gateway. По вторичным данным v1 GA — сентябрь 2025 ([agentscamp](https://github.com/imtiazrayhan/agentscamp/blob/HEAD/src/content/tools/pydantic-ai.md)); точную версию на дату проверки независимо перепроверить не удалось — UNKNOWN |
| Лицензия | MIT — бейдж лицензии в репозитории [github.com/pydantic/pydantic-ai](https://github.com/pydantic/pydantic-ai), подтверждение во вторичных сводках ([aitoolsvault.site](https://aitoolsvault.site/blog/ai-agent-frameworks-compared)) |
| Развёртывание | Локально как Python-пакет; запуск «везде»: веб-фронтенд, терминал, голосовые вызовы, durable background queue, GitHub Actions — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |

Ключевой тезис официальной страницы: «typed, extensible agent loop with every model a string swap away», плюс Harness-возможности (memory, guardrails, sub-agents, planning, context management, persistence, coding agent) и отдельная библиотека графов `pydantic-graph` ([pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/), [pydantic.dev/docs/ai/graph/graph](https://pydantic.dev/docs/ai/graph/graph/)).

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE. Приведены только применимые строки.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность агентов | SUPPORTED | Агентный цикл `run()` с инструментами и итерациями — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| Планирование | SUPPORTED | Planning в составе Harness capabilities — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| Долговременная память | SUPPORTED | Memory в составе Harness — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| Сессионный контекст | SUPPORTED | История сообщений и persistence в core/Harness — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| Персистентность | SUPPORTED | Persistence заявлена в Harness; детали бэкендов в проверенном фрагменте не раскрыты (PARTIAL по глубине, SUPPORTED по наличию) |
| Инструменты / function-calling | SUPPORTED | Типизированные инструменты, RunContext, dependency injection — вторично [github.com/srilaxmi1993/generative-ai-projects](https://github.com/srilaxmi1993/generative-ai-projects/blob/HEAD/agents/Simple%20Data%20Analysis%20Agent%20(PydanticAI)/TUTORIAL.md), первично — обзорная страница |
| Навыки / плагины | SUPPORTED | Capabilities как подключаемые модули Harness (guardrails, sub-agents, compaction и др.) — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| Мультиагентность | SUPPORTED | Multi-agent applications и sub-agents; графы — для сложных случаев — [pydantic.dev/docs/ai/graph/graph](https://pydantic.dev/docs/ai/graph/graph/) |
| Workflow / DAG | SUPPORTED | `pydantic-graph`: типизированные узлы/рёбра, FSM, визуализация — [pydantic.dev/docs/ai/graph/graph](https://pydantic.dev/docs/ai/graph/graph/) |
| Ветвления / циклы | SUPPORTED | Явные состояния, ветвления и возобновляемые переходы в pydantic-graph — [pydantic.dev/docs/ai/graph/graph](https://pydantic.dev/docs/ai/graph/graph/) |
| Обработка ошибок / повторы / восстановление | SUPPORTED | Валидация выходов Pydantic + durable execution (Temporal/DBOS/Prefect по вторичным источникам) — [bixtech.ai](https://bixtech.ai/pydanticai-vs-langchain-which-framework-to-use-for-ai-agents-with-data/) |
| Планирование по расписанию / триггеры | PARTIAL | Durable background queue заявлена в сценариях запуска; встроенного cron в проверенных страницах нет — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| Интеграции | SUPPORTED | MCP/A2A по вторичным сводкам; UI/CLI/voice интерфейсы — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| Работа с файлами | PARTIAL | Image generation и embeddings «в той же коробке»; файлового менеджера в проверенном фрагменте нет |
| Выполнение кода / терминал | PARTIAL | Coding agent в Harness; песочница исполнения в проверенных страницах не детализирована — UNKNOWN в части sandbox |
| Браузер / GUI | PARTIAL | Веб-фронтенд как интерфейс агента; браузерного GUI-агента в проверенных страницах нет |
| Мессенджеры | UNKNOWN | В проверенных официальных источниках интеграция с Telegram/мессенджерами не подтверждена |
| Локальные + облачные LLM | SUPPORTED | «Every model, every interface, typed end to end»; смена модели строкой — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| RAG | PARTIAL | Embeddings встроены; RAG-конвейер собирается пользователем |
| История / логирование | SUPPORTED | Logfire: наблюдаемость всего приложения, не только LLM-вызовов — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| Трейсинг / наблюдаемость | SUPPORTED | Pydantic Logfire (OpenTelemetry) + Gateway с мониторингом стоимости и бюджетным контролем — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| HITL (человек в цикле) | UNKNOWN | В проверенных официальных фрагментах отдельный approval-примитив не подтверждён (deferred tools упоминаются во внешних материалах, но не верифицированы открытием) |
| Песочница / безопасность | PARTIAL | Guardrails в Harness; известен SSRF-инцидент в загрузке URL (см. раздел 4) |
| Аутентификация | PARTIAL | Gateway с единым ключом моделей; пользовательской auth-модели в проверенных страницах нет |
| API / расширяемость | SUPPORTED | Extensible agent loop, кастомные capabilities — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| UI | SUPPORTED | Web frontend, terminal/CLI, voice — интерфейсы запуска — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| Импорт / экспорт | UNKNOWN | В проверенных источниках не подтверждено |
| Тестирование | SUPPORTED | Pydantic Evals: тестирование поведения агентов «как pytest тестирует код» — [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |
| Production-готовность | SUPPORTED | Durable execution, observability, Gateway с бюджетами; позиционирование production-grade подтверждается независимым сравнением [bixtech.ai](https://bixtech.ai/pydanticai-vs-langchain-which-framework-to-use-for-ai-agents-with-data/) |
| Оффлайн-работа | PARTIAL | Локальные модели поддерживаются концептуально («every model»), но Logfire/Gateway — облачные сервисы; полностью оффлайн-сценарий в проверенных страницах не показан |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Типобезопасность end-to-end: структурированные выходы валидируются Pydantic, ошибки уходят из runtime в write-time | [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/), вторично [agentscamp](https://github.com/imtiazrayhan/agentscamp/blob/HEAD/src/content/tools/pydantic-ai.md) | Официальная документация (primary) + вторичная сводка |
| Durable execution через Temporal/DBOS/Prefect: длинные агенты переживают рестарты процессов | [bixtech.ai](https://bixtech.ai/pydanticai-vs-langchain-which-framework-to-use-for-ai-agents-with-data/) | Независимое сравнение (secondary) |
| Честная позиция по графам: «не используйте гвоздомёт, если не нужен гвоздомёт», графы — только когда control flow — сложная часть | [pydantic.dev/docs/ai/graph/graph](https://pydantic.dev/docs/ai/graph/graph/) | Официальная документация (primary) |
| Наблюдаемость всего приложения через Logfire (OpenTelemetry), а не только LLM-вызовов | [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) | Официальная документация (primary) |
| Gateway: один ключ на все модели, real-time cost monitoring и budget control | [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) | Официальная документация (primary, вендор) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| SSRF в обработке загрузки URL (CVE-2026-25580, GHSA-2jrp-274c-jhv3) | Высокая | Опубликованный advisory | [github.com/advisories/GHSA-2jrp-274c-jhv3](https://github.com/advisories/GHSA-2jrp-274c-jhv3) |
| Инструментация Logfire может «замолкать» при пересоздании глобального OTel tracer provider другим кодом | Средняя | Зафиксировано интеграторами (вторично) | [github.com/fastaifoundry/fastaiagent-sdk](https://github.com/fastaifoundry/fastaiagent-sdk/blob/HEAD/docs/integrations/pydanticai.md) |
| Async/sync mixing — частый gotcha при комбинировании синхронных и асинхронных вызовов | Низкая | Пользовательские гайды (вторично) | [github.com/arustydev/agents](https://github.com/arustydev/agents/blob/HEAD/content/.context/context-engineering-intro/use-cases/pydantic-ai/CLAUDE.md) |
| Быстрая эволюция API: `history_processors` deprecated в пользу capabilities, удаление обещано в v2 | Низкая | Задокументировано в скиллах пакета (вторично) | [github.com/liquiditylab/pydantic-ai](https://github.com/liquiditylab/pydantic-ai/blob/HEAD/pydantic_ai_slim/pydantic_ai/.agents/skills/building-pydantic-ai-agents/SKILL.md) |
| HITL/approval как первоклассный примитив в проверенных официальных страницах не показан | Средняя | Отсутствие в проверенных источниках — UNKNOWN, а не доказанный минус | [pydantic.dev/docs/ai/overview](https://pydantic.dev/docs/ai/overview/) |

## 5. Неудовлетворённые потребности пользователей

- Устойчивая телеметрия при сосуществовании с другим OpenTelemetry-кодом: интеграторы вынуждены проверять wrapper-спаны вместо PydanticAI-спанов — [fastaiagent-sdk docs](https://github.com/fastaifoundry/fastaiagent-sdk/blob/HEAD/docs/integrations/pydanticai.md).
- Безопасная загрузка внешних URL без SSRF: [GHSA-2jrp-274c-jhv3](https://github.com/advisories/GHSA-2jrp-274c-jhv3).
- Стабильность API между мажорными версиями (миграция history_processors → capabilities): [liquiditylab/pydantic-ai skill](https://github.com/liquiditylab/pydantic-ai/blob/HEAD/pydantic_ai_slim/pydantic_ai/.agents/skills/building-pydantic-ai-agents/SKILL.md).
- Конкретных открытых GitHub Issues репозитория pydantic/pydantic-ai и Reddit-тредов, открытых и верифицированных в ходе этой проверки, недостаточно для отдельного списка номеров — UNKNOWN. Номера не выдумываем.

## 6. Технические ограничения

- **Модель исполнения**: асинхронный агентный цикл; графы — async FSM на `pydantic-graph`; durable-режим делегируется внешнему движку (Temporal и аналоги).
- **Детерминизм**: агентный цикл недетерминирован (LLM), выходы детерминированно валидируются по схемам; графы делают control flow детерминированным.
- **Параллелизм**: async-конкурентность внутри Python-процесса; распределённость — через durable-движки, а не собственный кластер.
- **Отмена / таймауты**: семантика зависит от модели/провайдера и durable-движка; в проверенных страницах единой политики cancel не зафиксировано — UNKNOWN.
- **Идемпотентность**: в проверенных источниках гарантий нет; durable-движки дают at-least-once семантику активностей, что требует идемпотентных инструментов от пользователя (вывод, помечен как вывод).
- **Восстановление после сбоя**: сильное место при подключении durable execution; без него — в пределах процесса.
- **Версионирование**: быстрый темп, deprecations с удалением в v2; типизация смягчает миграции (ошибки видны статически).

## 7. Вердикт и применимость к CyberPipe

**Вердикт**: самый «инженерный» фреймворк подборки: типы, валидация, evals, observability и durable execution. Оптимален, когда агент — часть production Python-системы с требованиями к надёжности. Мультиагентность и графы присутствуют, но сознательно не навязываются.

**Применимость к CyberPipe** (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-уровневый роутинг Tier0 regex / Tier1 ONNX / Tier2-3 LLM, TokenBudgetGovernor, Netrunner Mini-n8n DAG, SQLite WAL, Brainstem-песочница, HITL):

- **Как зависимость — нет** (Python SDK в .NET-продукте), **как архитектурный референс — да, наиболее близкий по духу**.
- **Что заимствовать**: (1) типизированные входы/выходы узлов Netrunner: каждый SkillNode/NetrunnerScriptNode/LlmProcessNode должен иметь схему входа/выхода уровня Pydantic-моделей (в .NET — record + валидация), чтобы ошибки ловились на границах узлов, а не в рантайме DAG; (2) идею Evals: наборы сценариев «как pytest» для Tier0/Tier1 роутинга и промптов Tier2/Tier3; (3) Gateway-паттерн budget control — прямое соответствие TokenBudgetGovernor: единый учёт стоимости всех моделей в реальном времени; (4) принцип «графы только когда control flow сложен» — оправдание того, что Netrunner остаётся простым DAG без лишней агентной магии; (5) урок SSRF: загрузка URL в инструментах CyberPipe должна идти через egress-allowlist в Brainstem.
- **Чего не хватает относительно CyberPipe**: HITL в проверенных страницах не показан как примитив — HitlApprovalNode придётся проектировать самим (здесь референс скорее LangGraph/AutoGen); мессенджеры не подтверждены.
- **Итог**: PydanticAI — главный референс по контрактам узлов, тестированию и бюджетному контролю; не источник DAG-движка или HITL.
