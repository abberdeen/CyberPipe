# Hermes Agent (hermes-agent)

> Дата проверки: 2026-10-19. Источники: только реально проверенные через web_search/web_fetch URL. Где доказательств недостаточно — `UNKNOWN`.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Hermes Agent |
| Официальный сайт | [hermes-agent.nousresearch.com](https://hermes-agent.nousresearch.com/) |
| Репозиторий | [github.com/NousResearch/hermes-agent](https://github.com/NousResearch/hermes-agent) |
| Документация | [hermes-agent.nousresearch.com/docs](https://hermes-agent.nousresearch.com/docs/) |
| Категория | Self-improving autonomous AI agent framework |
| Назначение | «The self-improving AI agent» — замкнутый learning loop: создаёт навыки из опыта, улучшает их, ищет по своим прошлым разговорам, строит модель пользователя (README) |
| Аудитория | Разработчики и power users; сценарии от $5 VPS до GPU-кластера и serverless (README) |
| Статус на 2026-10-19 | Активно развивается, частые релизы (в поиске виден v0.22.2); точная последняя версия — сверять на [releases](https://github.com/NousResearch/hermes-agent/releases/tag/v0.22.2) |
| Лицензия | MIT (бейдж в README, файл LICENSE в репозитории) |
| Развёртывание | Linux/macOS/WSL2 (`install.sh`), native Windows PowerShell, Hermes Desktop; терминальные бэкенды: local, Docker, SSH, Singularity, Modal, Daytona, Vercel Sandbox (README) |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Autonomous agent, scheduled automations unattended (README) |
| Планирование | PARTIAL | Делегирование субагентам и RPC-скрипты; формального planner/DAG нет |
| Долговременная память | SUPPORTED | Agent-curated memory, Honcho dialectic user modeling, FTS5 session search с LLM-summarization (README) |
| Контекст сессии | SUPPORTED | Cross-session recall, cross-platform conversation continuity |
| Персистентность | SUPPORTED | Serverless persistence Daytona/Modal: среда hibernates и просыпается (README) |
| Инструменты / function-calling | SUPPORTED | Tool Gateway (web search, image generation, TTS, browser) через Nous Portal (docs) |
| Навыки / плагины | SUPPORTED | Автосоздание навыков, agentskills.io open standard, каталоги Skills/Plugins в docs |
| Мультиагентность | PARTIAL | Spawn isolated subagents для параллельных workstreams (README); оркестрация уровня DAG не заявлена |
| Workflow / DAG | UNSUPPORTED | Python-скрипты через RPC сжимают multi-step pipelines, но это не DAG-движок |
| Ветвления / циклы | PARTIAL | Цикл обучения/агента; явных condition-узлов нет |
| Обработка ошибок / повторы / восстановление | PARTIAL | Есть bugfix-поток по compaction/тайм-аутам (issue #99511); идемпотентность не заявлена |
| Расписание / триггеры | SUPPORTED | Built-in cron scheduler с доставкой на любую платформу (README) |
| Интеграции | SUPPORTED | MCP client (вторичные SKILL-описания), Nous Portal, OpenRouter, OpenAI и др. |
| Файлы | SUPPORTED | Работа в терминальных бэкендах, workspace |
| Терминал / исполнение кода | SUPPORTED | Full TUI, streaming tool output, семь terminal backends |
| Браузер / GUI | PARTIAL | Browser как tool; Desktop app есть; полноценный GUI/browser agent — `UNKNOWN` по глубине |
| Мессенджеры | SUPPORTED | Telegram, Discord, Slack, WhatsApp, Signal, CLI из одного gateway (README) |
| Локальные + облачные LLM | SUPPORTED | Nous Portal, OpenRouter, OpenAI, свой endpoint; `hermes model` без смены кода |
| RAG | PARTIAL | FTS5-поиск по сессиям — не классический векторный RAG по документам; векторный RAG `UNKNOWN` |
| История / логирование | SUPPORTED | Conversation history, FTS5 session search |
| Трейсинг | PARTIAL | Trajectory generation/compression для обучения моделей (README, research-ready); операционный трейсинг `UNKNOWN` |
| HITL | PARTIAL | Approval system для деструктивных команд (PR по Windows destructive commands); системного HitlApproval — нет |
| Песочница / безопасность | PARTIAL | Docker/Modal/Daytona/Vercel Sandbox бэкенды; при этом сторонний аудит с HIGH-находками (раздел 4) |
| Аутентификация | SUPPORTED | OAuth через `hermes setup --portal`, пулы credentials (есть баги статуса — issue #134398 из поиска) |
| API / расширяемость | SUPPORTED | Плагины, навыки по открытому стандарту, свой endpoint |
| UI | SUPPORTED | TUI, Desktop, gateway в мессенджерах |
| Импорт / экспорт | UNKNOWN | Явно не подтверждено в проверенных источниках |
| Тестирование | PARTIAL | Batch trajectory generation; тесты как пользовательская фича — `UNKNOWN` |
| Production-готовность | PARTIAL | Позиционируется как production-ready во вторичных источниках; частота багов Windows/compaction снижает оценку |
| Офлайн | PARTIAL | Свой/локальный endpoint возможен; learning loop и порталы требуют сети |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Единственный из пяти с заявленным замкнутым learning loop: автосоздание и самоулучшение навыков | [README](https://raw.githubusercontent.com/NousResearch/hermes-agent/main/README.md) | Официальный README |
| Семь терминальных бэкендов, включая serverless с hibernation | [README](https://raw.githubusercontent.com/NousResearch/hermes-agent/main/README.md) | Официальный README |
| Смена модели одной командой без lock-in | [README](https://raw.githubusercontent.com/NousResearch/hermes-agent/main/README.md), [docs](https://hermes-agent.nousresearch.com/docs/) | Официальные README/docs |
| Голосовые заметки и continuity между платформами | [README](https://raw.githubusercontent.com/NousResearch/hermes-agent/main/README.md) | Официальный README |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Сторонний аудит Oct 2026: 6 HIGH — bypass blocklist через git aliases, OSC-8 terminal escape injection, skill scanner доверяет ignore-файлу бандла, stored XSS, WSL command injection | High | Утверждение блога, официально не подтверждено в ходе проверки | [TerminalBlog](https://terminalblog.com/blog/beware-hermes-agent-oct2026-security-audit-6-high-findings/) |
| Все данные в hardcoded SQLite-файлах — ограничение для security-critical развёртываний, нужен pluggable backend | Medium | Открытый feature request | [Issue #66238](https://github.com/NousResearch/hermes-agent/issues/66238) |
| Telegram adapter: allow_from short-circuits group-scoped authorization | High | Открытый баг | [Issue #68716](https://github.com/NousResearch/hermes-agent/issues/68716) |
| Windows: Smart App Control блокирует локально собранный Desktop как неподписанный | Medium | Открытый баг | [Issue #70544](https://github.com/NousResearch/hermes-agent/issues/70544) |
| Compaction может упираться в 600s ceiling на v0.20.6 | Medium | Открытый баг | [Issue #99511](https://github.com/NousResearch/hermes-agent/issues/99511) |

## 5. Неудовлетворённые потребности пользователей

По Reddit релевантных результатов web_search не найдено — `UNKNOWN`.

- Pluggable database backend вместо hardcoded SQLite для security-critical сценариев: [Issue #66238](https://github.com/NousResearch/hermes-agent/issues/66238)
- Корректная групповая авторизация в Telegram без short-circuit через allow_from: [Issue #68716](https://github.com/NousResearch/hermes-agent/issues/68716)
- Подписанный/проходящий Smart App Control Desktop на Windows 11: [Issue #70544](https://github.com/NousResearch/hermes-agent/issues/70544)
- Устранение зависаний compaction: [Issue #99511](https://github.com/NousResearch/hermes-agent/issues/99511)
- CLI/GUI рассинхронизация конфигурации (упоминание #71298 в экосистемных дайджестах; прямой URL issue в ходе проверки не открывался — статус потребности `UNKNOWN` по первоисточнику)

## 6. Технические ограничения

- **Модель исполнения:** агентный цикл + субагенты + RPC-скрипты; не формальный DAG.
- **Детерминизм:** не гарантируется; learning loop намеренно меняет поведение со временем (навыки самоулучшаются) — воспроизводимость ниже, чем у статичных агентов.
- **Параллелизм:** параллельные субагенты заявлены; семантика конфликтов/слияния — `UNKNOWN`.
- **Отмена/тайм-ауты:** interrupt-and-redirect в TUI заявлен; программный cancel для субагентов — `UNKNOWN`.
- **Идемпотентность:** не заявлена.
- **Восстановление:** serverless persistence окружения, но не checkpoint шагов workflow.
- **Версионирование:** навыки эволюционируют автоматически — аудит версий навыков критичен, встроенного версионирования в проверенных источниках не показано (`UNKNOWN`).

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** сильнейшая идея из пяти — learning loop и самомодель пользователя; при этом самая рискованная для детерминированного локального ассистента из-за недетерминированной эволюции навыков и потока Windows/compaction-багов.

**Для CyberPipe:**
- Заимствовать как идею (не код): периодические «nudges» на сохранение знаний и FTS5-поиск по истории — хорошо ложится на SQLite WAL CyberPipe и дешевле векторного RAG для Tier1/Tier2.
- Автосоздание навыков конфликтует с Netrunner-контрактом CyberPipe (manifest + schema.input/output + тесты): навыки должны оставаться проверяемыми пакетами, а не свободно эволюционирующим кодом; допустим только черновик навыка с HITL-подтверждением.
- Learning loop нельзя пускать мимо TokenBudgetGovernor и HitlApprovalNode.
