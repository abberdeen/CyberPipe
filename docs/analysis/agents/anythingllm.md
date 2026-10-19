# AnythingLLM (anythingllm)

> Дата проверки: 2026-10-19. Источники: только реально проверенные через web_search/web_fetch URL. Где доказательств недостаточно — статус `UNKNOWN`. Цитаты и URL не выдумываются.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | AnythingLLM |
| Официальный сайт | [anythingllm.com](https://anythingllm.com) |
| Репозиторий | [github.com/Mintplex-Labs/anything-llm](https://github.com/Mintplex-Labs/anything-llm) |
| Документация | [docs.anythingllm.com](https://docs.anythingllm.com) |
| Категория | Local-first all-in-one AI-приложение: чат по документам (RAG) + AI-агенты |
| Назначение | «Chat with your docs, use AI Agents, hyper-configurable, multi-user, & no frustrating setup required» (README); «The all-in-one AI app» |
| Аудитория | Конечные пользователи и команды, которым нужен локальный RAG/агент без сложной сборки; multi-user режим для команд |
| Статус на 2026-10-19 | Активно развивается, релизы частые (v1.16.x–v1.18.0 в проверенных сниппетах/releases); точный последний тег на дату проверки — `UNKNOWN` без фиксации releases-страницы |
| Лицензия | MIT (файл LICENSE, Copyright Mintplex Labs Inc.; проверен raw LICENSE) |
| Развёртывание | Desktop (Mac/Windows/Linux), Docker/self-hosted, hosted instance; Mobile (Android), браузерное расширение, embed-виджет |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | PARTIAL | AI Agents + Scheduled Jobs (cron); полной автономии без настройки нет |
| Планирование | PARTIAL | Agent flows (no-code builder); формального planner/DAG вне flows не показано |
| Долговременная память | PARTIAL | Memories & Personalization (docs); глубина/гарантии — `UNKNOWN` |
| Контекст сессии | SUPPORTED | Чаты/треды, chat logs (docs: All Features) |
| Персистентность | SUPPORTED | Self-hosted хранение, workspaces, документы |
| Инструменты / function-calling | SUPPORTED | Встроенные скиллы агента, MCP Tools (только Tools; без Resources/Prompts/Sampling по вторичному разбору docs), Intelligent Skill Selection |
| Навыки / плагины | SUPPORTED | Agent skills, imported plugins, Community Hub |
| Мультиагентность | UNSUPPORTED | В проверенных источниках мультиагентной оркестрации не заявлено |
| Workflow / DAG | PARTIAL | Agent Flows — no-code цепочки; полноценного DAG уровня Mini-n8n с типизированными портами не показано |
| Ветвления / циклы | PARTIAL | Внутри agent flows; явные ConditionNode-семантики — `UNKNOWN` |
| Обработка ошибок / повторы / восстановление | PARTIAL | Scheduled Jobs: повторный триггер при `queued/running` отбрасывается (docs issue); retry-контроль ограничен (см. раздел 5) |
| Расписание / триггеры | SUPPORTED | Scheduled Jobs с v1.13.0, cron + Run Now; недоступно в Desktop, нужен Docker/self-hosted |
| Интеграции | SUPPORTED | Data connectors, API, embed, Telegram-бот (фичи в releases/commits) |
| Файлы | SUPPORTED | Загрузка документов, RAG по файлам |
| Терминал / исполнение кода | PARTIAL | Через MCP/агентные скиллы; нативного sandbox-терминала как в Open Interpreter не заявлено |
| Браузер / GUI | PARTIAL | Private Browser Tool (docs) |
| Мессенджеры | PARTIAL | Telegram-бот присутствует (commits про Telegram approvals/links); широкого набора мессенджеров не показано |
| Локальные + облачные LLM | SUPPORTED | Ollama, LM Studio, LocalAI, KoboldCPP + OpenAI, Anthropic, Azure, Bedrock, Gemini, Groq, Mistral, OpenRouter и др. (docs LLM Setup) |
| RAG | SUPPORTED | Ядро продукта: документы, эмбеддеры, векторные БД |
| История / логирование | SUPPORTED | Chat Logs, Event Logs (docs) |
| Трейсинг | UNKNOWN | OpenTelemetry-подобный трейсинг в проверенных источниках не подтверждён |
| HITL | PARTIAL | Tool-call approvals (в т.ч. Telegram, таймаут `TOOL_CALL_APPROVAL_TIMEOUT_MS` в commits); полноценного HitlApprovalNode нет |
| Песочница / безопасность | PARTIAL | Multi-user, Security & Access (docs); MCP запрещён в hosted cloud из соображений безопасности; sandbox-исполнения кода не заявлено |
| Аутентификация | SUPPORTED | Multi-user mode, API-ключи |
| API / расширяемость | SUPPORTED | Developer API, MCP-клиент, embed API |
| UI | SUPPORTED | Веб-UI, Desktop, Mobile |
| Импорт / экспорт | PARTIAL | Импорт документов; экспорт чатов/агентов — `UNKNOWN` |
| Тестирование | UNKNOWN | Тесты агентов как пользовательская фича не подтверждены |
| Production-готовность | PARTIAL | Battle-tested позиционирование; hosted cloud с ограничениями (нет MCP) |
| Офлайн | PARTIAL | Локальные модели + локальный эмбеддер возможны; телеметрия отключается (`DISABLE_TELEMETRY`), но остаются CDN/github загрузки |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Local-first: десктоп + Docker, данные у пользователя, телеметрия только события без контента и отключаема | [README (raw)](https://raw.githubusercontent.com/Mintplex-Labs/anything-llm/master/README.md) | Официальный README |
| RAG + агенты + no-code agent builder + MCP в одном приложении | [README (raw)](https://raw.githubusercontent.com/Mintplex-Labs/anything-llm/master/README.md), [All Features](https://docs.anythingllm.com/features/all-features) | Официальные README/docs |
| Scheduled Jobs с полным набором агентных инструментов и Run Now | [Scheduled Jobs overview (docs repo)](https://github.com/mintplex-labs/anythingllm-docs/blob/HEAD/pages/scheduled-jobs/overview.mdx) | Официальные docs (репозиторий документации) |
| Широкий выбор локальных и облачных LLM/эмбеддеров | [docs.anythingllm.com](https://docs.anythingllm.com) (разделы LLM/Embedder Setup) | Официальные docs |
| MIT-лицензия без ограничений hosted-конкурента | [LICENSE (raw)](https://raw.githubusercontent.com/Mintplex-Labs/anything-llm/master/LICENSE) | Первичный текст лицензии |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| MCP недоступен в hosted cloud из-за безопасности произвольного кода; нужен Desktop/self-hosted | Средняя | Подтверждено документацией | [Cloud limitations (docs repo)](https://github.com/mintplex-labs/anythingllm-docs/blob/HEAD/pages/cloud/limitations.mdx) |
| Scheduled Jobs недоступны в Desktop-приложении | Средняя | Подтверждено документацией | [Scheduled Jobs overview](https://docs.anythingllm.com/scheduled-jobs/overview) (через поиск по docs repo) |
| Триггер расписания отбрасывается, если прошлый запуск ещё `queued/running` — пропуск без очереди | Средняя | Заявлено в docs issue | [anythingllm-docs issue #238](https://github.com/mintplex-labs/anythingllm-docs/issues/238) |
| Поведение повторов агента нельзя полностью контролировать системным промптом (наблюдение интегратора Revit MCP) | Низкая | Вторичное наблюдение, не официальный баг | [Axotopia/revitmcp](https://github.com/Axotopia/revitmcp) |
| MCP поддерживает только Tools (без Resources/Prompts/Sampling) | Низкая | Вторичный разбор docs, требует перепроверки по актуальным docs | См. раздел источников в docs: [docs.anythingllm.com](https://docs.anythingllm.com) |

## 5. Нереализованные потребности пользователей

| Потребность | Источник (конкретный URL) |
|---|---|
| Auto-режим всегда вызывает агента без классификатора нужности инструментов — нужен лёгкий роутер intent | [Issue #5520](https://github.com/Mintplex-Labs/anything-llm/issues/5520) |
| Per-workspace параметр `effort` и Extended Thinking для Anthropic | [Issue #5884](https://github.com/Mintplex-Labs/anything-llm/issues/5884) |
| Workspace-assigned Custom Skills (скиллы на уровне воркспейса, а не глобально) | [Issue #2970](https://github.com/Mintplex-Labs/anything-llm/issues/2970) |
| Интеграция NovaNodes MCP Gateway | [Issue #6050](https://github.com/Mintplex-Labs/anything-llm/issues/6050) |
| Нехватка полноценных гайдов как барьер внедрения («the barrier») | [Issue #5267](https://github.com/Mintplex-Labs/anything-llm/issues/5267) |
| Reddit-обсуждения local RAG-чатбота (выбор AnythingLLM vs Open WebUI) — косвенно: запрос на простые локальные сценарии | [Reddit r/LocalLLaMA (через поиск)](https://www.reddit.com/r/LocalLLaMA/comments/1i77a2o/need_advice_building_a_local_ragbased_chatbot_for/) — сам тред в этом прогоне не открывался, статус потребности `UNKNOWN` по содержанию |

## 6. Технические ограничения

- **Модель исполнения:** чат + агентный цикл внутри одного процесса приложения; Scheduled Jobs — серверный cron только в Docker/self-hosted.
- **Детерминизм:** зависит от LLM; встроенного детерминированного DAG-исполнения нет, Agent Flows — LLM-управляемые.
- **Параллелизм:** `UNKNOWN` по документации; для Scheduled Jobs конкурентный запуск того же джоба подавляется (drop, а не очередь).
- **Отмена/таймауты:** таймаут апрува тулов настраивается (`TOOL_CALL_APPROVAL_TIMEOUT_MS`); общий timeout/cancel графа — `UNKNOWN`.
- **Идемпотентность:** не заявлена; повторный триггер отбрасывается, но идемпотентность шагов не гарантируется.
- **Восстановление:** перезапуск чата есть; resume графа после падения — `UNKNOWN`.
- **Версионирование:** версионирование flows/агентов в проверенных источниках не показано — `UNKNOWN`.

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** самый близкий аналог CyberPipe по «личный локальный ассистент + RAG + агенты», но архитектурно это монолитное приложение, а не DAG-движок.

Для CyberPipe полезно заимствовать: модель Memories, Intelligent Skill Selection (экономия токенов — родственно TokenBudgetGovernor), паттерн Scheduled Jobs с Run Now и правилом drop при активном запуске.

Не заменяет: 4-tier routing (Tier0 regex / Tier1 ONNX) — у AnythingLLM аналогичного дешёвого классификатора нет, что прямо подтверждает issue #5520; Mini-n8n DAG с ConditionNode/HitlApprovalNode/DeliveryNode; Brainstem sandbox; Telegram как основной канал (у AnythingLLM Telegram — вторичный бот).

**Применимость:** референс по UX RAG/памяти/расписаний, не как движок исполнения.

---
*Проверено: 2026-10-19. Неподтверждённое помечено `UNKNOWN`.*
