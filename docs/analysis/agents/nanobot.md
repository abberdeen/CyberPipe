# nanobot (nanobot)

> Дата проверки: 2026-10-19. Источники: только реально проверенные через web_search/web_fetch URL. Где доказательств недостаточно — `UNKNOWN`.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | nanobot |
| Официальный сайт / документация | [nanobot.wiki](https://nanobot.wiki/docs/latest/getting-started/nanobot-overview) |
| Репозиторий | [github.com/HKUDS/nanobot](https://github.com/HKUDS/nanobot) |
| Документация | [nanobot.wiki — overview](https://nanobot.wiki/docs/latest/getting-started/nanobot-overview), docs в репозитории (`docs/README.md`) |
| Категория | Ultra-lightweight open-source self-hosted personal AI agent framework (Python) |
| Назначение | Работа с файлами, память предпочтений, расписание задач в браузере, терминале или чат-приложении (README) |
| Аудитория | Разработчики и энтузиасты, которым нужен аудируемый компактный ассистент; популярен как учебная платформа и база кастомных ассистентов (вторичный каталог agentlist) |
| Статус на 2026-10-19 | Активно развивается; PyPI-пакет `nanobot-ai`, Python >=3.11; точная последняя версия — `UNKNOWN` в рамках проверки |
| Лицензия | MIT (бейдж License в README) |
| Развёртывание | Self-hosted: `pip install nanobot-ai`, `nanobot onboard`, `nanobot agent`, `nanobot gateway` для WebUI/каналов; облачный деплой описан в `docs/deployment.md` |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Gateway + cron, фоновые субагенты |
| Планирование | PARTIAL | Агентный цикл и subagent delegation; формального planner/DAG нет |
| Долговременная память | SUPPORTED | Long-term memory через Dream, workspace files (README, docs/concepts) |
| Контекст сессии | SUPPORTED | Session history, prompt assembly pipeline (`agent/context.py` по MarkTechPost) |
| Персистентность | SUPPORTED | Сессии и память переживают рестарты (README) |
| Инструменты / function-calling | SUPPORTED | Files, shell, web search/fetch, MCP, cron, image generation, subagents |
| Навыки / плагины | SUPPORTED | Reusable Skills, Agent Plugins, CLI Apps (вторичный обзор + README-разделы docs) |
| Мультиагентность | PARTIAL | Multi-agent delegation/subagents; профили субагентов с разными правами — запрос (issue #1012 по дайджесту, первоисточник не открывался) |
| Workflow / DAG | UNSUPPORTED | DAG-движка нет; только цикл + субагенты + cron |
| Ветвления / циклы | PARTIAL | Цикл агента; condition-узлов нет |
| Обработка ошибок / повторы / восстановление | PARTIAL | Circuit breaker для Responses API fallback (release v0.1.5.post2), retry heartbeats; есть баг маршрутизации `finish_reason='length'` (issue #5133) |
| Расписание / триггеры | SUPPORTED | Cron scheduling (README, MarkTechPost) |
| Интеграции | SUPPORTED | MCP servers, каналы, Langfuse (упомянут в docs-конфигурации), web tools |
| Файлы | SUPPORTED | Чтение PDF, DOCX, XLSX и др. (README) |
| Терминал / исполнение кода | SUPPORTED | Shell tool, CLI `nanobot agent` |
| Браузер / GUI | PARTIAL | WebUI в браузере; браузерная автоматизация как у OpenClaw не заявлена (`UNKNOWN`) |
| Мессенджеры | SUPPORTED | Telegram, Discord, Slack, WeChat, Email, Mattermost, Linear и др. (README) |
| Локальные + облачные LLM | SUPPORTED | Поддерживаемые провайдеры и local model server (README); OpenAI-compatible/Ollama/vLLM по вторичным каталогам |
| RAG | UNKNOWN | Векторный RAG по документам в проверенных источниках не подтверждён |
| История / логирование | SUPPORTED | Session history сохраняется; subagent follow-ups persist in session history (release notes) |
| Трейсинг | PARTIAL | Langfuse упомянута в конфигурации; глубина трейсинга `UNKNOWN` |
| HITL | PARTIAL | `restrictToWorkspace` и security-конфигурация; одобрения шагов как узла нет |
| Песочница / безопасность | PARTIAL | restrictToWorkspace; при этом сообщения о SSRF в image download и утечке API-ключей через global env (дайджесты, раздел 4) |
| Аутентификация | PARTIAL | Токены ботов/каналов, API-ключи провайдеров; изоляция ключей критиковалась |
| API / расширяемость | SUPPORTED | Python SDK, OpenAI-compatible Agent API (`docs/python-sdk.md`, `docs/openai-api.md`) |
| UI | SUPPORTED | WebUI, терминал |
| Импорт / экспорт | UNKNOWN | Явно не подтверждено |
| Тестирование | PARTIAL | CI/Test Suite badge; пользовательское тестирование сценариев `UNKNOWN` |
| Production-готовность | PARTIAL | Gateway как сервис; зрелость ниже OpenClaw/Hermes по числу интеграций безопасности |
| Офлайн | PARTIAL | Локальные модели (Ollama/vLLM) возможны; каналы требуют сети |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Ультра-лёгкость и аудируемость: ~4 000 строк ядра по MarkTechPost | [MarkTechPost guide](https://www.marktechpost.com/2026/03/28/a-coding-guide-to-exploring-nanobots-full-agent-pipeline-from-wiring-up-tools-and-memory-to-skills-subagents-and-cron-scheduling/) | Вторичный технический разбор |
| Полный набор: WebUI, каналы, MCP, память Dream, cron, субагенты в компактном Python-коде | [README](https://raw.githubusercontent.com/HKUDS/nanobot/main/README.md), [GitHub repo](https://github.com/HKUDS/nanobot) | Официальный README |
| OpenAI-compatible API и Python SDK для интеграций | README (раздел Start Here: Python SDK / Agent API) | Официальный README |
| Мультиязычная документация, включая русский | [nanobot.wiki](https://nanobot.wiki/docs/latest/getting-started/nanobot-overview) | Официальные docs |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| При `restrictToWorkspace: true` агент не читает файлы из каналов: медиа лежат в `~/.nanobot/media/` вне workspace | Medium | Открыт, связан с #984/#1862 | [Issue #2660](https://github.com/HKUDS/nanobot/issues/2660) |
| `finish_reason='length'` с tool_calls и пустым контентом уходит в empty-response retry вместо length recovery | Medium | Открыт | [Issue #5133](https://github.com/HKUDS/nanobot/issues/5133) |
| SSRF в image URL download (unvalidated redirects) — по экосистемному дайджесту, PR #5095 | High | Первоисточник PR в ходе проверки не открывался, статус `UNKNOWN` по фиксу | Дайджест: [agents-radar #361](https://github.com/stevenko2002/agents-radar/issues/361) |
| Provider API key isolation: global `os.environ` mutation утекает ключи между провайдерами (#4784 по дайджесту) | High | Первоисточник не открывался | Дайджест: [news-radar #95](https://github.com/datnguyenquy94/news-radar/issues/95) |
| Duplicate tool_use IDs в streamed Anthropic responses отравляли сессии (400 от провайдера) | High | По дайджесту подан fix PR #4443 | Дайджест: [agents-radar #464](https://github.com/kakapez/agents-radar/issues/464) |

## 5. Неудовлетворённые потребности пользователей

По Reddit релевантных результатов web_search не найдено — `UNKNOWN`.

- Доступ к медиафайлам каналов при включённом restrictToWorkspace / auto-nesting медиа в workspace: [Issue #2660](https://github.com/HKUDS/nanobot/issues/2660), связанный запрос #1862 упомянут внутри issue
- Корректная обработка `finish_reason='length'` с tool_calls: [Issue #5133](https://github.com/HKUDS/nanobot/issues/5133)
- Смена модели в конкретной сессии без переконфигурации всего инстанса (#5198 по дайджесту; первоисточник не открывался): [Issue #5198](https://github.com/HKUDS/nanobot/issues/5198)
- Профили субагентов с разными правами инструментов (#1012 по дайджесту; первоисточник не открывался): [Issue #1012](https://github.com/HKUDS/nanobot/issues/1012)

## 6. Технические ограничения

- **Модель исполнения:** Python asyncio-подобный агентный цикл (`agent/loop.py`), gateway-процесс.
- **Детерминизм:** не гарантируется (LLM-цикл).
- **Параллелизм:** фоновые субагенты; лимиты и изоляция контекста детально не верифицированы (`UNKNOWN`).
- **Отмена/тайм-ауты:** circuit breaker и retry есть на уровне провайдеров; пользовательский cancel шагов — `UNKNOWN`.
- **Идемпотентность:** не заявлена; cron может повторять действия.
- **Восстановление:** сессии переживают рестарт, checkpoint шагов workflow нет.
- **Версионирование:** SemVer-подобные теги (v0.1.5.post2, v0.2.0 во вторичных источниках); политика совместимости конфига — `UNKNOWN`.

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** лучший учебный/аудируемый референс персонального ассистента: компактный, понятный, с памятью, MCP и cron, но без DAG, строгой песочницы и зрелой изоляции секретов.

**Для CyberPipe:**
- Заимствовать архитектурные идеи: prompt assembly pipeline, Dream-подобную консолидацию памяти поверх SQLite WAL, OpenAI-compatible Agent API как внешний контракт.
- Модель «skills + MCP + cron» близка Netrunner, но CyberPipe сильнее за счёт формального Mini-n8n DAG (ConditionNode, HitlApprovalNode) и Brainstem sandbox — nanobot здесь слабее и не заменяет Netrunner.
- Как кодовая зависимость не применим (Python vs .NET 10); применим как референс простоты онбординга (`nanobot onboard`) для Telegram+Avalonia UX.
