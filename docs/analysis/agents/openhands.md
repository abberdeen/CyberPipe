# OpenHands

> Дата проверки: 2026-10-19. Источники — только реально открытые через web_search/web_fetch страницы. Где доказательств недостаточно — UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | OpenHands (продукт/фронтенд — Agent Canvas; ранее OpenDevin) |
| Официальный сайт | [openhands.dev](https://openhands.dev/) (ссылка из официальных материалов), документация — [docs.openhands.dev](https://docs.openhands.dev/llms.txt) |
| Репозиторий | [OpenHands/OpenHands](https://github.com/OpenHands/OpenHands) (Agent Canvas), [OpenHands/software-agent-sdk](https://github.com/OpenHands/software-agent-sdk), [OpenHands/automation](https://github.com/OpenHands/automation) |
| Документация | [docs.openhands.dev](https://docs.openhands.dev/llms.txt), [Key Features](https://docs.openhands.dev/openhands/usage/key-features.md) |
| Категория | Платформа software-development агентов / self-hosted developer control center для кодинг-агентов и автоматизаций |
| Назначение | «Run OpenHands, Claude Code, Codex, Gemini, Pi, OpenCode, or any ACP-compatible agent across local, remote, and cloud backends» (README) |
| Аудитория | Разработчики и команды: dependency upgrades, unit tests, merge conflicts, vulnerability sweeps, рефакторинги, автоматизация GitHub Issues → задачи |
| Статус на 2026-10-19 | Beta (бейдж `status-beta` в README), активно развивается; есть OpenHands Cloud и Enterprise как коммерческие варианты |
| Лицензия | MIT (Copyright © 2025 OpenHands contributors) — проверено по LICENSE |
| Развёртывание | `npm install -g @openhands/agent-canvas`, Docker sandbox (`ghcr.io/openhands/agent-canvas`), multi-sandbox через `OH_CONVERSATION_RUNTIME=docker`, from source, VMs, OpenHands Cloud |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Агенты выполняют задачи разработки end-to-end (код, команды, веб) |
| Планирование | PARTIAL | Декомпозиция GitHub issues в задачи в Agent Canvas; детерминированного планировщика в источниках нет |
| Долговременная память | PARTIAL | Hosted memory упомянут в parity-issue #18632 как догоняющая функция; зрелость — PARTIAL |
| Контекст сессии | SUPPORTED | Conversations, persisted state переживает замену контейнера (README Option 3) |
| Персистентность | SUPPORTED | `~/.openhands`, workspace mounts, conversation history сохраняется |
| Инструменты / function-calling | SUPPORTED | SDK tools, MCP tools, agent-server REST API |
| Навыки / плагины | SUPPORTED | Skills overview, keyword/path-triggered skills, extensions registry (по llms.txt) |
| Мультиагентность | SUPPORTED | Несколько бэкендов и агентов (OpenHands, Claude Code, Codex, Gemini, Pi, OpenCode, ACP), parallel campaigns |
| Workflow / DAG | PARTIAL | Automations (schedule/events) + Automation Server; полноценного DAG-редактора с типами узлов в источниках нет |
| Ветвления / циклы | UNKNOWN | Не подтверждено |
| Обработка ошибок / retry / recovery | PARTIAL | Hooks (block dangerous commands, quality checks), но parity/bug-issues показывают незрелость отдельных контуров |
| Планировщик / триггеры | SUPPORTED | Automation Server: «run on a schedule or in response to events», Slack/GitHub/Linear интеграции автоматизаций |
| Интеграции | SUPPORTED | GitHub, Slack, Linear, MCP, ACP-агенты |
| Файлы | SUPPORTED | Changes Tab, встроенный VS Code, upload/download |
| Терминал / исполнение кода | SUPPORTED | Terminal Tab, sandbox runtime |
| Браузер / GUI | PARTIAL | Browser Tab есть, но «browser is non-interactive» (Key Features) |
| Мессенджеры | PARTIAL | Slack-интеграции автоматизаций; Telegram не заявлен |
| Локальные + облачные LLM | SUPPORTED | Model-agnostic, «can work with any LLM», локальные/remote/cloud бэкенды |
| RAG | UNKNOWN | Не заявлен как продуктовая функция |
| История / логирование | SUPPORTED | Run history автоматизаций, conversation history |
| Трейсинг | UNKNOWN | Отдельного tracing в просмотренных источниках не видно |
| HITL | PARTIAL | Hooks могут блокировать опасные команды; полноценного approval UX уровня HitlApprovalNode не показано |
| Песочница / безопасность | SUPPORTED | Docker sandbox с изолированными FS/network; без sandbox — честное предупреждение README о полном доступе к ФС |
| Аутентификация | PARTIAL | Session key / API key, `LOCAL_BACKEND_API_KEY`; enterprise SSO в открытых источниках не детализирован |
| API / расширяемость | SUPPORTED | Agent Server REST API, TypeScript client, SDK |
| UI | SUPPORTED | Agent Canvas: Chat, Changes, VS Code, Terminal, App, Browser tabs |
| Импорт / экспорт | PARTIAL | Git workspaces; экспорт агентов/конфигов — UNKNOWN |
| Тестирование | PARTIAL | Automated QA Testing use-case (агент валидирует PR запуском софта); собственный eval-контур — PARTIAL |
| Production | PARTIAL | Enterprise/Cloud и «scale from one to thousands of agents» заявлены вендором; OSS-часть помечена beta |
| Offline | PARTIAL | Локальный запуск возможен, но LLM чаще внешний; полностью offline не заявлен |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Мульти-бэкенд: local, Docker, VM, cloud; переключение без потери фокуса | [README](https://github.com/OpenHands/OpenHands) | Официальная документация |
| Чужие агенты как бэкенды: Claude Code, Codex, Gemini CLI, Pi, OpenCode, ACP | [README](https://github.com/OpenHands/OpenHands) | Официальная документация |
| Docker sandbox на разговор: свой Agent Server и инструменты, состояние переживает пересоздание контейнера | [README](https://github.com/OpenHands/OpenHands) | Официальная документация |
| Автоматизации по расписанию и событиям (Slack, GitHub, Linear) через отдельный Automation Server | [README](https://github.com/OpenHands/OpenHands), [docs](https://docs.openhands.dev/llms.txt) | Официальная документация |
| Богатый UI: Chat/Changes/VS Code/Terminal/App/Browser | [Key Features](https://docs.openhands.dev/openhands/usage/key-features.md) | Официальная документация |
| Коммерческая зрелость: Series A и enterprise-позиционирование | [Business Wire, 2025-11-18](https://www.businesswire.com/news/home/20251118768131/en/OpenHands-Raises-%2418.8M-Series-A-to-Bring-Open-Source-Cloud-Coding-Agents-to-Enterprises) | Пресс-релиз (заявление компании, не независимый аудит) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| OSS/Enterprise parity: hosted memory, extension controls, secret/MCP ownership, routing — серия незакрытых parity-issues | Средняя | Открыты | [#18632](https://github.com/OpenHands/OpenHands/issues/18632), [#18631](https://github.com/OpenHands/OpenHands/issues/18631), [#18630](https://github.com/OpenHands/OpenHands/issues/18630) |
| `POST /api/conversations` с secrets/initial_message возвращает 502/500 | Высокая | Открыт | [#18585](https://github.com/OpenHands/OpenHands/issues/18585) |
| `docker_runtime` хардкодит `--user <host uid:gid>`, ломается на rootless Docker | Средняя | Открыт | [#18584](https://github.com/OpenHands/OpenHands/issues/18584) |
| Нет встроенной очереди задач и контроля конкурентности для bulk issue resolution — пользователи просят как фичу | Средняя | Открыт (feature) | [#18593](https://github.com/OpenHands/OpenHands/issues/18593) |
| Запуск внешнего ACP-агента падает generic-ошибкой при отсутствии/неавторизованности CLI, нет preflight readiness check | Средняя | Открыт | [#18581](https://github.com/OpenHands/OpenHands/issues/18581) |
| Токен-жадность и дороговизна на сложных/плохо специфицированных задачах; проблемы с неоднозначными задачами и хрупкими окружениями | Средняя | Внешняя оценка (survey), не официальный источник | [Open-Source Coding Agents: A Survey](https://airesponsibly.substack.com/p/open-source-ai-coding-agents-a-survey) |

## 5. Незакрытые потребности пользователей (Issues/Discussions/Reddit)

Reddit-треды в поиске надёжно не подтвердились — по Reddit UNKNOWN. Ниже только GitHub Issues с конкретными URL.

| Потребность / боль | Источник |
|---|---|
| Указание agent-server image при создании scheduled automation | [#18633](https://github.com/OpenHands/OpenHands/issues/18633) |
| Task Queue & Concurrency Control для bulk-разрешения issues | [#18593](https://github.com/OpenHands/OpenHands/issues/18593) |
| Счётчик queued (PENDING) прогонов автоматизаций на дашборде | [#18576](https://github.com/OpenHands/OpenHands/issues/18576), [#18577](https://github.com/OpenHands/OpenHands/issues/18577) |
| Поиск моделей и LLM-профилей в chat composer | [#18594](https://github.com/OpenHands/OpenHands/issues/18594) |
| DigitalOcean provider setup при первом запуске | [#18600](https://github.com/OpenHands/OpenHands/issues/18600) |
| Reload-durable доставка held-сообщений (parity) | [#18629](https://github.com/OpenHands/OpenHands/issues/18629) |
| Локальный plugin launch молча теряет отредактированные параметры | [#18614](https://github.com/OpenHands/OpenHands/issues/18614) |

## 6. Технические ограничения

| Аспект | Оценка |
|---|---|
| Модель исполнения | Agent Server (REST) + SDK-агенты в sandbox; Canvas — контрольный центр поверх нескольких бэкендов |
| Детерминированность | LLM-driven; в источниках отмечается стабильность за счёт жёстких настроек типа temperature 0 (внешняя оценка) |
| Параллелизм | Несколько контейнеров/разговоров; но встроенной очереди задач нет (#18593) |
| Отмена / таймауты | UNKNOWN по открытым источникам |
| Идемпотентность | Не заявлена; автоматизации по событиям без идемпотентных ключей в источниках не описаны |
| Восстановление | Workspace и история переживают замену контейнера; held-message delivery при reload — открытый вопрос (#18629) |
| Версионирование | x-release-please версии образа; версионирование автоматизаций/агентов — UNKNOWN |

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** OpenHands — самый зрелый «контрольный центр кодинг-агентов» с sandbox-per-conversation и автоматизациями, но это платформа для software-engineering команд, а не персональный ассистент. Для CyberPipe ценны архитектурные паттерны, а не продукт целиком (.NET-стеку чужды Python SDK и Node Canvas).

Применимость к CyberPipe (.NET 10, Telegram+Avalonia, Tier0–Tier3 routing, TokenBudgetGovernor, Netrunner Mini-n8n DAG, SQLite WAL, Brainstem sandbox, HITL):

- **Заимствовать:** sandbox-per-conversation → аналог Brainstem-процесса на Netrunner-прогон; Automation Server (schedule/events, run history) → триггеры для DAG; Hooks (block dangerous commands, quality gate перед остановкой) → политики для NetrunnerScriptNode и HitlApprovalNode; честное разделение «без sandbox = полный доступ к ФС» в UX.
- **Не заимствовать:** ставку на LLM-агента как единственного исполнителя и «токен-жадность» — CyberPipe закрывает это Tier0/Tier1 и TokenBudgetGovernor; bulk-параллелизм без очереди (#18593) показывает, что конкурентность нужно проектировать сразу (лимиты, очередь, идемпотентность в SQLite WAL).
- **Интеграции:** GitHub/Slack/Linear автоматизации — референс для DeliveryNode; Telegram у OpenHands не заявлен, CyberPipe здесь оригинален.
- **HITL:** у OpenHands только hooks-уровень; CyberPipe с HitlApprovalNode идёт дальше — это конкурентное преимущество, а не пробел.
