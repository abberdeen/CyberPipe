# OpenClaw (openclaw)

> Дата проверки: 2026-10-19. Источники: только реально проверенные через web_search/web_fetch URL. Где доказательств недостаточно — статус `UNKNOWN`.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | OpenClaw |
| Официальный сайт | [openclaw.ai](https://openclaw.ai) |
| Репозиторий | [github.com/openclaw/openclaw](https://github.com/openclaw/openclaw) |
| Документация | [docs.openclaw.ai](https://docs.openclaw.ai) |
| Категория | Self-hosted personal AI assistant / gateway к AI-агентам через мессенджеры |
| Назначение | «Your AI assistant, on your own hardware, in every chat app you already use» — один Gateway, любая модель, любое устройство, без hosted-сервиса посередине (docs) |
| Аудитория | Разработчики, power users и команды, которым нужен доступный из любого чата ассистент без отказа от контроля над данными (docs) |
| Статус на 2026-10-19 | Активно развивается; steward — OpenClaw Foundation, независимая 501(c)(3) ([openclaw.org](https://openclaw.org)); платного тарифа/hosted-сервиса нет (README). Точная последняя версия в ходе проверки: `UNKNOWN` (не фиксировалась из releases) |
| Лицензия | MIT (бейдж License: MIT в README репозитория) |
| Развёртывание | Локально/на своём сервере: macOS, Linux, Windows (PowerShell installer, npm `openclaw`), Docker/Nix упомянуты в install guide; Node 24.16+ или 26.1+ (README). Native apps: macOS, iOS, Android, Windows, Linux |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Gateway как always-available ассистент, heartbeat/cron (docs, вторичные обзоры архитектуры) |
| Планирование | PARTIAL | Агентный цикл + cron/heartbeat; отдельного формального planner/DAG в проверенных источниках не показано |
| Долговременная память | SUPPORTED | Persistent memory/workspace across sessions (FAQ: [openclaw/docs faq](https://github.com/openclaw/docs/blob/HEAD/docs/help/faq.md)) |
| Контекст сессии | SUPPORTED | Session store, compaction; есть запросы на hook после compaction (issue ниже) |
| Персистентность | SUPPORTED | State, memory, credentials — на своём железе (README) |
| Инструменты / function-calling | SUPPORTED | Tool orchestration: browser, files, scheduling, hooks (FAQ) |
| Навыки / плагины | SUPPORTED | Skills, ClawHub marketplace, channel plugins (docs: разделы Tools, ClawHub) |
| Мультиагентность | PARTIAL | Multi-agent routing, sub-agent orchestration; меж-инстансная коллаборация — feature request (issue #53025) |
| Workflow / DAG | UNSUPPORTED | DAG-движка уровня Mini-n8n в проверенных источниках не заявлено; только агентный цикл + cron |
| Ветвления / циклы | PARTIAL | Цикл агента есть; явные ConditionNode-подобные ветвления не документированы |
| Обработка ошибок / повторы / восстановление | PARTIAL | Failover моделей заявлен в docs (Models: failover); идемпотентность шагов не показана |
| Расписание / триггеры | SUPPORTED | Cron jobs & heartbeats, webhooks (docs) |
| Интеграции | SUPPORTED | 20+ каналов, MCP упоминается в экосистеме; точный список MCP — частично `UNKNOWN` |
| Файлы | SUPPORTED | Работа с workspace/файлами (FAQ, README) |
| Терминал / исполнение кода | SUPPORTED | exec-инструмент (issue #7901 анализирует exec как топ расхода токенов) |
| Браузер / GUI | SUPPORTED | Browser automation, Control UI, native apps |
| Мессенджеры | SUPPORTED | Discord, iMessage, Slack, Teams, Telegram, WhatsApp, Signal, Matrix и др. (README, docs) |
| Локальные + облачные LLM | SUPPORTED | Модели/harness (Claude, Codex, local models) — сменные плагины (README); провайдеры и local model services (docs) |
| RAG | PARTIAL | Вторичный обзор упоминает memory index SQLite + vector/BM25 hybrid; из официальных docs на 2026-10-19 прямо не подтверждено — частично `UNKNOWN` |
| История / логирование | SUPPORTED | Session history; usage RPC `usage.cost`, `sessions.usage` (issue #56460) |
| Трейсинг | PARTIAL | Diagnostics/telemetry opt-in; OpenTelemetry-подобный трейсинг не подтверждён (`UNKNOWN`) |
| HITL (подтверждение человеком) | PARTIAL | Pairing flows, access groups; полноценный HitlApprovalNode не заявлен |
| Песочница / безопасность | PARTIAL | Docker sandbox для tool execution (вторичные обзоры), политика «trusted gateway, untrusted execution» (README → Why OpenClaw); при этом серия CVE (раздел 4) |
| Аутентификация | SUPPORTED | Pairing, credentials на своём железе, доступ по каналам |
| API / расширяемость | SUPPORTED | Gateway RPC, CLI reference, schemas, hooks, ClawHub (docs) |
| UI | SUPPORTED | Control UI (браузерный дашборд), TUI/WebChat, native apps |
| Импорт / экспорт | UNKNOWN | В проверенных источниках явно не подтверждено |
| Тестирование | PARTIAL | CI badge в README; собственные тесты ассистента как фича — `UNKNOWN` |
| Production-готовность | PARTIAL | Используется как team deployment (README), но история CVE и сложность конфигурации снижают оценку |
| Офлайн | PARTIAL | Локальные модели возможны; каналы/облачные модели требуют сети |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Данные, память и credentials остаются на своём железе; по умолчанию только daily version check, статистика opt-in | [README](https://raw.githubusercontent.com/openclaw/openclaw/main/README.md) | Официальный README |
| Самый широкий набор каналов (20+) и native apps под все ОС | [README](https://raw.githubusercontent.com/openclaw/openclaw/main/README.md), [docs](https://docs.openclaw.ai) | Официальные сайт/docs |
| Экосистема навыков и плагинов, ClawHub | [docs.openclaw.ai](https://docs.openclaw.ai) | Официальная документация |
| Сменные модели и harness без смены остального стека | [README](https://raw.githubusercontent.com/openclaw/openclaw/main/README.md) | Официальный README |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Шесть новых уязвимостей, включая SSRF в Gateway tool (CVE-2026-26322, CVSS 7.6), missing Telnyx webhook auth (CVE-2026-26319, 7.5), path traversal в browser upload | High | Сообщено, патчи в статье не верифицированы по релизам | [Infosecurity Magazine](https://www.infosecurity-magazine.com/news/researchers-six-new-openclaw/) |
| Три moderate-уязвимости: bypass политики, мутация конфигурации gateway, host override → раскрытие credentials; исправлено в 2026.4.20 | Medium | Исправлено по данным статьи | [CyberSecurityNews](https://cybersecuritynews.com/openclaw-vulnerabilities/) |
| Высокий расход токенов: все tool schemas инжектятся в каждый запрос (~14k токенов на свежую сессию) | Medium | Открытые запросы на оптимизацию | [Issue #14812](https://github.com/openclaw/openclaw/issues/14812), [Issue #7901](https://github.com/openclaw/openclaw/issues/7901) |
| Публично закоммиченные конфиги часто без sandbox/контроля доступа, с hardcoded ключами | High | Сторонний аудит, не официальный | [clawhatch audit](https://github.com/clawhatch/clawhatch/blob/HEAD/docs/openclaw-config-audit-2026.md) |

## 5. Неудовлетворённые потребности пользователей

Только GitHub Issues/Discussions с конкретными URL. По Reddit в ходе проверки релевантных результатов web_search не найдено — `UNKNOWN` (не выдумывать).

- Конфигурируемые лимиты вывода инструментов для снижения расхода токенов (exec/read — 40–60% затрат): [Issue #7901](https://github.com/openclaw/openclaw/issues/7901)
- Разделение статического системного промпта и динамических метаданных ради provider-side caching: [Issue #19936](https://github.com/openclaw/openclaw/issues/19936)
- Динамическая загрузка/выгрузка навыков по контексту (−30–50% токенов): [Issue #6691](https://github.com/openclaw/openclaw/issues/6691)
- Per-tool enable/disable в конфиге: [Issue #14812](https://github.com/openclaw/openclaw/issues/14812)
- Мультиагентная коллаборация: capability profiling, shared blackboard, layered memory, token cost governance: [Issue #35203](https://github.com/openclaw/openclaw/issues/35203)
- Коллаборация между несколькими инстансами OpenClaw с передачей артефактов: [Issue #53025](https://github.com/openclaw/openclaw/issues/53025)
- Дашборд учёта токенов и стоимости по моделям/дням: [Issue #56460](https://github.com/openclaw/openclaw/issues/56460)

## 6. Технические ограничения

- **Модель исполнения:** один долгий Gateway-процесс, агентный цикл поверх LLM; не детерминированный DAG.
- **Детерминизм:** не гарантируется (LLM-цикл), `UNKNOWN` в части воспроизводимости шагов.
- **Параллелизм:** sub-agent orchestration заявлена, лимиты/семантика слияния — `UNKNOWN`.
- **Отмена/тайм-ауты:** в проверенных источниках детально не раскрыты — `UNKNOWN`.
- **Идемпотентность:** не заявлена; повтор cron/heartbeat может дублировать действия (риск следует из архитектуры, прямо не документирован).
- **Восстановление:** session compaction есть, запрос на `session:after_compaction` hook показывает, что восстановление после компакции — ручная обвязка: [Issue #19959](https://github.com/openclaw/openclaw/issues/19959)
- **Версионирование:** частые релизы (схема `2026.x`), богатая поверхность → риск регрессий и CVE.

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** самый зрелый и широкий по каналам/экосистеме персональный ассистент из пяти, но тяжёлый (Node, большой расход токенов) и с самой заметной историей CVE.

**Для CyberPipe (.NET 10, Telegram+Avalonia, 4-tier routing, TokenBudgetGovernor, Mini-n8n DAG, SQLite WAL, Brainstem sandbox, HITL):**
- Заимствовать: модель Gateway + pairing/allowlist каналов, идею ClawHub-подобного каталога навыков, usage-учёт как первоклассную фичу (актуально для TokenBudgetGovernor).
- Не заимствовать: инжект всех tool schemas в каждый запрос — прямо противоположно Tier0/Tier1-роутингу и экономии токенов CyberPipe; отсутствие формального DAG компенсируется в CyberPipe Netrunner.
- Как зависимость не применим (другой стек, Node); применим как референс UX и каталог антипаттернов безопасности.
