# Open Interpreter (open-interpreter)

> Дата проверки: 2026-10-19. Источники: только реально проверенные через web_search/web_fetch URL. Где доказательств недостаточно — статус `UNKNOWN`. Цитаты и URL не выдумываются.

> Важно о состоянии проекта: актуальный канонический репозиторий на дату проверки — [openinterpreter/open-interpreter](https://github.com/openinterpreter/open-interpreter) (новая Rust-версия, fork OpenAI Codex). Оригинальный Python-проект [OpenInterpreter/open-interpreter](https://github.com/OpenInterpreter/open-interpreter) продолжается как community-maintained fork [endolith/open-interpreter](https://github.com/endolith/open-interpreter). Ниже — про актуальную Rust-линейку, если не указано иное.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Open Interpreter |
| Официальный сайт | [openinterpreter.com](https://www.openinterpreter.com) |
| Репозиторий | [github.com/openinterpreter/open-interpreter](https://github.com/openinterpreter/open-interpreter) |
| Документация | [openinterpreter.com/docs/terminal](https://www.openinterpreter.com/docs/terminal) |
| Категория | Терминальный coding-агент для дешёвых/открытых моделей; computer-use через скиллы |
| Назначение | «A coding agent optimized for low-cost models» (README); эмуляция harness-ов (Claude Code, Kimi Code, Qwen Code и др.) для максимальной отдачи слабых моделей |
| Аудитория | Разработчики в терминале и редакторах (ACP-клиенты), пользователи открытых моделей (Kimi, GLM, DeepSeek) |
| Статус на 2026-10-19 | Активно развивается (Rust-переписывание на базе Codex); Python-оригинал заморожен в пользу community fork — риск фрагментации экосистемы |
| Лицензия | Apache-2.0 (файл LICENSE, проверен raw) |
| Развёртывание | Локально: `curl -fsSL https://www.openinterpreter.com/install \| sh` (macOS/Linux), PowerShell installer (Windows); команды `i` / `interpreter`; ACP-режим `interpreter acp`, daemon, app-server |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | PARTIAL | Агентный цикл в терминале; non-interactive `exec`; полной фоновой автономии уровня сервиса нет |
| Планирование | PARTIAL | Планирование внутри агентного цикла LLM; отдельного planner/DAG нет |
| Долговременная память | PARTIAL | `AGENTS.md`, скиллы, локальное состояние в `~/.openinterpreter`; долговременной семантической памяти уровня RAG не заявлено |
| Контекст сессии | SUPPORTED | Sessions (docs: CLI → Sessions) |
| Персистентность | SUPPORTED | Конфиг и состояние локально, данные пользователя — читаемые файлы (Portable by default) |
| Инструменты / function-calling | SUPPORTED | exec, MCP, skills, hooks, permissions (README Features) |
| Навыки / плагины | SUPPORTED | `.agents/skills` (общий стандарт), legacy-директории читаются |
| Мультиагентность | UNSUPPORTED | В проверенных источниках мультиагентной оркестрации нет; harness-эмуляция — не мультиагентность |
| Workflow / DAG | UNSUPPORTED | DAG-движка нет; есть Workflows-гайд как сценарии промптов (docs), не граф исполнения |
| Ветвления / циклы | PARTIAL | Только цикл агента |
| Обработка ошибок / повторы / восстановление | PARTIAL | Auto-Review (docs); идемпотентность/восстановление графа не применимы |
| Расписание / триггеры | PARTIAL | GitHub Action, daemon; встроенного cron-планировщика уровня продукта — `UNKNOWN` |
| Интеграции | SUPPORTED | MCP, ACP, Codex SDK (exec protocol), редакторы |
| Файлы | SUPPORTED | Работа с репозиторием/файлами через exec и инструменты |
| Терминал / исполнение кода | SUPPORTED | Ядро продукта: команды в нативной песочнице macOS/Linux/Windows (README Features) |
| Браузер / GUI | PARTIAL | QA-скилл: web через agent-browser, native apps через trycua; не встроенный браузерный агент уровня Browser Use |
| Мессенджеры | UNSUPPORTED | Мессенджерных каналов в проверенных источниках нет |
| Локальные + облачные LLM | SUPPORTED | Любые OpenAI-compatible провайдеры (Chat Completions), `/model` в TUI, гайды Kimi/DeepSeek/Z.AI GLM |
| RAG | UNSUPPORTED | RAG-пайплайна по документам не заявлено |
| История / логирование | PARTIAL | История сессий; централизованного аудит-лога уровня платформы нет |
| Трейсинг | UNKNOWN | В проверенных источниках не подтверждён |
| HITL | SUPPORTED | Sandbox & Approvals: подтверждение исполнения (docs); в классической Python-версии — подтверждение перед запуском кода, обход `-y`/`auto_run` |
| Песочница / безопасность | SUPPORTED | Два контроля: sandbox mode + approvals (docs Sandbox & Approvals); Execution policy, Permissions, Cyber Safety |
| Аутентификация | SUPPORTED | Authentication (docs Configuration) |
| API / расширяемость | SUPPORTED | OpenAI SDK-совместимость, App Server, MCP Server, ACP |
| UI | PARTIAL | TUI в терминале; браузерный UI — только гайд «tiny streaming web chat», десктоп — отдельный Interpreter Workstation |
| Импорт / экспорт | PARTIAL | Портируемость: `AGENTS.md`, `.agents/skills`, MCP/ACP/Codex protocol; экспорт сессий — `UNKNOWN` |
| Тестирование | PARTIAL | QA-скилл для тестирования интерфейсов; `scripts/test-codex-sdk-compat.sh` |
| Production-готовность | PARTIAL | CLI/daemon/app-server; как личный серверный runtime не позиционируется |
| Офлайн | PARTIAL | Локальные/OpenAI-compatible эндпоинты возможны; офлайн без модели бессмысленен |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Harness-эмуляция (`/harness`: native, claude-code, kimi-code, qwen-code, deepseek-tui, swe-agent и др.) выжимает максимум из дешёвых моделей | [README (raw)](https://raw.githubusercontent.com/OpenInterpreter/open-interpreter/main/README.md) | Официальный README |
| Нативная песочница на macOS/Linux/Windows + approvals как два отдельных контроля | [README (raw)](https://raw.githubusercontent.com/OpenInterpreter/open-interpreter/main/README.md), [Sandbox & Approvals](https://www.openinterpreter.com/docs/terminal/sandbox) | Официальные README/docs |
| Портируемость: `AGENTS.md`, `.agents/skills`, MCP, ACP, Codex exec protocol вместо проприетарного формата | [README (raw)](https://raw.githubusercontent.com/OpenInterpreter/open-interpreter/main/README.md) | Официальный README |
| Совместимость с Codex SDK одной строкой (`codexPathOverride`) и ACP-редакторами | [README (raw)](https://raw.githubusercontent.com/OpenInterpreter/open-interpreter/main/README.md) | Официальный README |
| Computer Use через QA-скилл: браузер (agent-browser) и native apps (trycua) | [README (raw)](https://raw.githubusercontent.com/OpenInterpreter/open-interpreter/main/README.md) | Официальный README |
| Apache-2.0 | [LICENSE (raw)](https://raw.githubusercontent.com/OpenInterpreter/open-interpreter/main/LICENSE) | Первичный текст лицензии |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| В классической Python-версии исполнение кода шло прямо на хосте — риск для машины пользователя | Высокая (исторически) | Частично закрыто песочницей в Rust-версии; запрос на Wasm-изоляцию открыт | [Issue #1717](https://github.com/openinterpreter/open-interpreter/issues/1717) |
| Нет встроенных guardrails/лимитов на исполнение команд (запрос фичи) | Средняя | Открытый запрос сообщества | [Issue #1162](https://github.com/openinterpreter/open-interpreter/issues/1162) |
| Фрагментация: Rust-переписывание + community fork Python-версии; старые гайды/интеграции могут не совпадать | Средняя | Наблюдение по README (указание на fork) | [README (raw)](https://raw.githubusercontent.com/OpenInterpreter/open-interpreter/main/README.md) |
| Обход подтверждения (`-y`/`auto_run`) снимает HITL-защиту полностью | Средняя | Задокументированное поведение классической версии | Результаты поиска по классическому README (см. раздел 5 источников в git-истории проекта) — в актуальной Rust-документации эквивалент: [Sandbox & Approvals](https://www.openinterpreter.com/docs/terminal/sandbox) |

## 5. Нереализованные потребности пользователей

| Потребность | Источник (конкретный URL) |
|---|---|
| Wasm-песочница для безопасного запуска недоверенного кода | [Issue #1717](https://github.com/openinterpreter/open-interpreter/issues/1717) |
| MCP-серверы внутри Open Interpreter (конфиг `interpreter.mcp_servers`) | [Issue #1670](https://github.com/openinterpreter/open-interpreter/issues/1670) |
| Guardrails и лимиты на исполнение команд | [Issue #1162](https://github.com/openinterpreter/open-interpreter/issues/1162) |
| Локальный STT FunASR/SenseVoice для голосового режима | [Issue #1774](https://github.com/openinterpreter/open-interpreter/issues/1774), [Issue #1775](https://github.com/openinterpreter/open-interpreter/issues/1775) |
| Настраиваемый порт LM Studio вместо жёсткого 1234 (классическая версия) | [Issue #769](https://github.com/openinterpreter/open-interpreter/issues/769) |
| Reddit/Discussions в этом прогоне отдельно не открывались — потребности оттуда `UNKNOWN` | — |

## 6. Технические ограничения

- **Модель исполнения:** синхронный агентный цикл LLM ↔ exec в песочнице ОС; Rust-ядро (Codex fork).
- **Детерминизм:** отсутствует; результат зависит от модели и harness-эмуляции.
- **Параллелизм:** `UNKNOWN` по docs; продукт однокористувательский терминальный.
- **Отмена/таймауты:** прерывание пользователем в терминале; программные timeout/cancel графа неприменимы (графа нет).
- **Идемпотентность:** не гарантируется; повторный запуск команд может повторять побочные эффекты.
- **Восстановление:** сессии сохраняются; автоматического recovery исполнения после падения — `UNKNOWN`.
- **Версионирование:** скиллы/конфиги — файлы в git-совместимых директориях; версионирования прогонов нет.

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** эталон терминального code-exec агента с сильной моделью безопасности «песочница + апрувы», но без мессенджеров, RAG и DAG.

Для CyberPipe полезно: двухконтурная модель Sandbox & Approvals (прямая аналогия Brainstem sandbox + HitlApprovalNode), идея harness-эмуляции под дешёвые модели (родственно Tier2/Tier3 с TokenBudgetGovernor), стандарт `.agents/skills` и `AGENTS.md` для Netrunner-скиллов.

Не применимо: Telegram/Avalonia UI, SQLite WAL персистентность, Mini-n8n DAG — всё это CyberPipe строит сам; Open Interpreter может быть только внешним exec-бэкендом через ACP/MCP, а не ядром.

**Применимость:** референс по sandbox/approvals и harness-подходу; кандидат на внешний инструмент, не на замену Netrunner.

---
*Проверено: 2026-10-19. Неподтверждённое помечено `UNKNOWN`.*
