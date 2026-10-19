# ZeroClaw (zeroclaw)

> Дата проверки: 2026-10-19. Источники: только реально проверенные через web_search/web_fetch URL. Где доказательств недостаточно — `UNKNOWN`.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | ZeroClaw |
| Официальный сайт | [zeroclaw.com](https://www.zeroclaw.com) |
| Репозиторий | [github.com/zeroclaw-labs/zeroclaw](https://github.com/zeroclaw-labs/zeroclaw) (ветка по умолчанию `master`) |
| Документация | [docs.zeroclaw.com/master/en/introduction.html](https://docs.zeroclaw.com/master/en/introduction.html) |
| Категория | Agent runtime / personal AI assistant infrastructure, единый Rust-бинарник |
| Назначение | «You own the agent. You own the data. You own the machine» — runtime, который говорит с LLM-провайдерами, 30+ каналами и инструментами на своей машине со своими ключами (README, docs) |
| Аудитория | Владельцы слабого железа и self-hosted пользователи; также hardware/edge-сценарии (GPIO/I2C/SPI по вторичным каталогам) |
| Статус на 2026-10-19 | Активно развивается, релизы на GitHub; точная последняя версия — `UNKNOWN` в рамках проверки |
| Лицензия | В README ветки master: MIT OR Apache 2.0 (бейдж License); при этом коммит 2026-02-17 менял лицензию с Apache-2.0 на MIT — актуальную редакцию сверять по файлам LICENSE/LICENSE-APACHE в репозитории |
| Развёртывание | Единый бинарник: Unix `install.sh` + `zeroclaw quickstart`, Windows prebuilt PowerShell, Docker, NixOS; сервис systemd/launchctl/Windows Service (`zeroclaw service install/start`) |

Источники идентификации: [README (master)](https://raw.githubusercontent.com/zeroclaw-labs/zeroclaw/master/README.md), [docs introduction](https://docs.zeroclaw.com/master/en/introduction.html).

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Always-on service, «fully autonomous» в описании репозитория (GitHub) при supervised-by-default по вторичным каталогам |
| Планирование | PARTIAL | SOP (standard operating procedures) в docs; формального планировщика/DAG-плана нет |
| Долговременная память | SUPPORTED | Memory как сменный trait (README вторичных форков/каталоги), система памяти в архитектуре docs (`UNKNOWN` по глубине семантики) |
| Контекст сессии | SUPPORTED | Сессии агентов `[agents.<alias>]`, история каналов |
| Персистентность | SUPPORTED | Workspace и конфиг на своей машине, сервис-режим |
| Инструменты / function-calling | SUPPORTED | Shell, browser, HTTP, hardware, custom MCP servers (README/docs) |
| Навыки / плагины | PARTIAL | Отдельный репозиторий zeroclaw-skills упомянут в поиске; зрелость каталога `UNKNOWN` |
| Мультиагентность | PARTIAL | Несколько агентов по alias, delegation и ACP console по вторичным каталогам; детали `UNKNOWN` |
| Workflow / DAG | PARTIAL | SOP — процедурные workflow; DAG с ветвлениями уровня Netrunner не подтверждён |
| Ветвления / циклы | PARTIAL | SOP-шаги и агентный цикл; condition-узлы `UNKNOWN` |
| Обработка ошибок / повторы / восстановление | PARTIAL | Баги «incomplete terminal responses reported as successful» показывают незрелость терминации (issue #9421) |
| Расписание / триггеры | PARTIAL | Webhooks и каналы; cron в проверенных выдержках прямо не показан — `UNKNOWN` |
| Интеграции | SUPPORTED | ~20 провайдеров, 30+ каналов (Discord, Telegram, Matrix, email, voice, webhooks, CLI) |
| Файлы | SUPPORTED | Workspace-инструменты |
| Терминал / исполнение кода | SUPPORTED | Shell tool, `zeroclaw agent`, zerocode TUI (docs) |
| Браузер / GUI | PARTIAL | Browser tool, web dashboard по вторичным каталогам; глубина GUI-автоматизации `UNKNOWN` |
| Мессенджеры | SUPPORTED | Telegram, Discord, Matrix и др. |
| Локальные + облачные LLM | SUPPORTED | Anthropic, OpenAI, Ollama и ~20 других (README/docs) |
| RAG | UNKNOWN | Не подтверждён в проверенных источниках |
| История / логирование | SUPPORTED | История сессий; token usage logging — открытый запрос (issue #3565) |
| Трейсинг | PARTIAL | Signed tool receipts по вторичному каталогу agentlist; операционный трейсинг `UNKNOWN` |
| HITL | PARTIAL | Supervised-by-default autonomy, approvals в dashboard по вторичным источникам; формального approval-узла нет |
| Песочница / безопасность | SUPPORTED | OS-level sandboxes и security/autonomy docs (`Security & Autonomy` в docs); при этом есть security-баги (раздел 4) |
| Аутентификация | SUPPORTED | Свои ключи провайдеров, конфиг-доступ; детали RBAC `UNKNOWN` |
| API / расширяемость | SUPPORTED | Traits для providers/channels/tools/memory/tunnels, rustdoc API, CLI/Config reference генерируется из кода (docs) |
| UI | SUPPORTED | Web dashboard, zerocode TUI, CLI (docs) |
| Импорт / экспорт | UNKNOWN | Не подтверждено |
| Тестирование | PARTIAL | CI badge, eval harness по дайджестам; пользовательские тесты сценариев `UNKNOWN` |
| Production-готовность | PARTIAL | Operations раздел в docs; быстрый темп фич и review bottleneck по дайджестам |
| Офлайн | PARTIAL | Ollama/локальные провайдеры; каналы требуют сети |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Единый Rust-бинарник, <5MB RAM по заявлениям экосистемы, запуск на $10 железе | [agentlist catalog](https://github.com/agentlistio/awesome-agent-list), [README](https://raw.githubusercontent.com/zeroclaw-labs/zeroclaw/master/README.md) | Вторичный каталог + официальный README |
| Всё сменное через traits: providers, channels, tools, memory, tunnels | Вторичные форки/каталоги, напр. [hongyuatcufe/zeroclaw README](https://github.com/hongyuatcufe/zeroclaw/blob/HEAD/README.md) | Вторичный источник |
| Документация CLI/Config генерируется из кода (clap + JSON schema) и не расходится с бинарником | [docs introduction](https://docs.zeroclaw.com/master/en/introduction.html) | Официальная документация |
| Явная философия владения данными и машиной | [README](https://raw.githubusercontent.com/zeroclaw-labs/zeroclaw/master/README.md) | Официальный README |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Неполные терминальные ответы могут репортиться как успешные | High | Открыт, связаны PR #9424/#9447 | [Issue #9421](https://github.com/zeroclaw-labs/zeroclaw/issues/9421) |
| Прерванные Telegram-обороты теряют последний контекст при нормализации истории | Medium | По issue подан PR #1057 | [Issue #1056](https://github.com/zeroclaw-labs/zeroclaw/issues/1056) |
| Slack `conversations.history` rate limiting игнорирует Retry-After | Medium | Открыт | [Issue #1839](https://github.com/zeroclaw-labs/zeroclaw/issues/1839) |
| High-entropy detector редактирует легитимные имена файлов, ломая читаемость путей | Low | Открыт | [Issue #8722](https://github.com/zeroclaw-labs/zeroclaw/issues/8722) |
| Учёт стоимости токенов не работает полностью: нужен JSONL-лог, CLI summary и dashboard | Medium | Открыт (tracking issue) | [Issue #3565](https://github.com/zeroclaw-labs/zeroclaw/issues/3565) |

## 5. Неудовлетворённые потребности пользователей

По Reddit релевантных результатов web_search не найдено — `UNKNOWN`.

- Полноценный token usage logging и cost tracking dashboard: [Issue #3565](https://github.com/zeroclaw-labs/zeroclaw/issues/3565)
- Backoff по Retry-After для Slack history: [Issue #1839](https://github.com/zeroclaw-labs/zeroclaw/issues/1839)
- Настраиваемость high-entropy redaction (связанный запрос #4832 упомянут в issue): [Issue #8722](https://github.com/zeroclaw-labs/zeroclaw/issues/8722)
- Корректная семантика неполных ответов провайдеров: [Issue #9421](https://github.com/zeroclaw-labs/zeroclaw/issues/9421)

## 6. Технические ограничения

- **Модель исполнения:** Rust agent runtime, агентный цикл + SOP; не детерминированный DAG-движок.
- **Детерминизм:** не гарантируется; SOP ближе к процедурам, чем к формальному графу с контрактами узлов.
- **Параллелизм:** background delegation по вторичным источникам; лимиты/отмена — частично `UNKNOWN`, в дайджестах упоминался cancel для delegation.
- **Отмена/тайм-ауты:** детали в проверенных источниках ограничены (`UNKNOWN`).
- **Идемпотентность:** signed tool receipts помогают аудиту, но идемпотентность повторных шагов не заявлена.
- **Восстановление:** сервис-режим и персистентный workspace; checkpoint/rollback шагов нет.
- **Версионирование:** быстрые релизы, RFC-культура по дайджестам; риск review bottleneck и регрессий терминации (issue #9421).

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** самый «инфраструктурный» из лёгких клонов: Rust, traits, генерируемые из кода reference-доки и security/autonomy как отдельный раздел. Ближе всех к инженерной дисциплине, но SOP слабее формального DAG, а учёт токенов — открытая потребность.

**Для CyberPipe:**
- Заимствовать: генерацию CLI/config-reference из кода (для .NET — из атрибутов/JSON Schema Netrunner-манифестов), signed tool receipts как аудит для Brainstem sandbox, supervised-by-default как дефолт HitlApprovalNode.
- TokenBudgetGovernor в CyberPipe — прямой ответ на открытый запрос ZeroClaw #3565; здесь CyberPipe концептуально впереди.
- Как зависимость не применим (Rust vs .NET 10); применим как референс edge/сервис-развёртывания и документации.
