# NanoClaw

> Дата проверки: 2026-10-19. Источники — только реально открытые через web_search/web_fetch страницы. Недостаток доказательств — `UNKNOWN`.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное имя | NanoClaw |
| Сайт | https://nanoclaw.dev |
| Репозиторий | https://github.com/nanocoai/nanoclaw (ранее `qwibitai/nanoclaw` — переименование видно в стороннем аудите: https://github.com/beforelanding/sift/blob/HEAD/CLAUDE.md ) |
| Документация | https://docs.nanoclaw.dev |
| Категория | Лёгкая безопасная альтернатива OpenClaw: персональный ассистент, агенты в отдельных контейнерах |
| Назначение | «An AI assistant that runs agents securely in their own containers. Lightweight, built to be easily understood and completely customized» (README) |
| Аудитория | Индивидуальные пользователи, которым нужен понятный аудируемый self-hosted ассистент |
| Статус на 2026-10-19 | Активен; идёт линейка v2 (миграция `migrate-v2.sh`, `docs/v1-to-v2-changes.md` в README) |
| Лицензия | MIT (README экосистемы и зеркала: https://github.com/onegate-bot/nanogate — «MIT, same as upstream»; также https://medevel.com/nanoclaw/ ) |
| Развёртывание | macOS или Linux; Windows — только через WSL (docs: «Windows users first set up WSL»); нужен Docker |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Запланированные задачи и постоянная работа на своей машине (docs introduction) |
| Планирование | UNKNOWN | Отдельный planner в источниках не выделен |
| Долговременная память | SUPPORTED | «Each agent has its own memory» (docs); память на SQLite упоминается во внешнем разборе https://www.youtube.com/watch?v=Fmq3vlSZn84 — официальный приоритет у docs |
| Контекст сессии | SUPPORTED | Диалоговые агенты с собственной рабочей папкой (docs) |
| Персистентность | SUPPORTED | Состояние v1→v2 миграции включает БД, конфиги, scheduled tasks (README) |
| Инструменты / function-calling | SUPPORTED | Claude agents с web search и page-reading tools (docs) |
| Навыки / плагины | SUPPORTED | Skills как способ расширения: репозитории каналов прямо указывают «Everything else … should be contributed as skills» (https://github.com/nanocoai/nanoclaw-telegram) |
| Мультиагентность | SUPPORTED | «a team of assistants» / multi-agent swarm guide (docs); Slack: отдельный агент — отдельное приложение, контейнер и память (README) |
| Workflow / DAG | UNKNOWN | DAG-модель не показана |
| Ветвления / циклы | UNKNOWN | Не подтверждено |
| Обработка ошибок / retry / recovery | PARTIAL | При сбое установки Claude Code автоматически диагностирует и продолжает (README Quick Start); общая retry-семантика задач — UNKNOWN |
| Расписание / триггеры | SUPPORTED | Scheduled tasks (docs, README миграции) |
| Интеграции | SUPPORTED | WhatsApp, Telegram, Slack, Discord, Gmail «and other messaging apps» (описание репозитория) |
| Файлы | SUPPORTED | Рабочие файлы агента в отдельной workspace-папке (docs) |
| Терминал / выполнение кода | PARTIAL | Агенты выполняют задачи в контейнерах (README); отдельный code-exec инструмент при проверке не детализирован |
| Браузер / GUI | PARTIAL | Web search/page-reading у Claude-агентов (docs); полноценный браузерный GUI-агент — UNKNOWN |
| Мессенджеры | SUPPORTED | Telegram, WhatsApp, Discord, Slack, iMessage, CLI при первичной привязке канала (README) |
| Локальные + облачные LLM-провайдеры | PARTIAL | По умолчанию Claude, при установке предлагается Codex, есть provider guide (docs); локальные модели при проверке не подтверждены |
| RAG | UNKNOWN | Не подтверждено |
| История / логирование | UNKNOWN | В проверенных источниках не детализировано |
| Трейсинг | UNKNOWN | Не подтверждено из официальных источников |
| HITL | PARTIAL | Pairing каналов при установке (README); approval-flow для опасных действий при проверке не показан |
| Песочница / безопасность | SUPPORTED | Каждый агент — свой Linux-контейнер с filesystem isolation, «not merely behind permission checks» (README) |
| Аутентификация | SUPPORTED | Credential gateway при установке, регистрация Anthropic credential (README) |
| API / расширяемость | SUPPORTED | Кастомизация «completely customized for your needs», skills и channel adapters (README/docs) |
| UI | PARTIAL | Терминал и мессенджеры; отдельного богатого UI в проверенных источниках нет |
| Импорт / экспорт | PARTIAL | Миграция состояния v1→v2 (`migrate-v2.sh`) — это импорт собственного формата, не общий экспорт |
| Тестирование | UNKNOWN | Тестовый набор при проверке не верифицирован |
| Production-готовность | PARTIAL | Есть v2 и enterprise-интерес по VentureBeat, но обновление v2 имеет транзакционные риски (раздел 4) |
| Офлайн-режим | UNSUPPORTED | Docs прямо требуют интернет и AI-аккаунт: «You'll need a supported Mac or Linux computer, an internet connection, and an AI account» |

## 3. Сильные стороны

- Настоящая OS-изоляция: агент в своём контейнере, а не только allowlist в одном процессе — официальный README, «Why I Built NanoClaw».
- Маленькая аудируемая кодовая база как принцип («small enough to understand: one process and a handful of files») — README.
- Память, расписание и мультиагентные «команды» из коробки — docs introduction.
- Автодиагностика сбоев установки через Claude Code с продолжением с места сбоя — README Quick Start.
- MIT-лицензия — источники раздела 1.

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| При исчерпании бюджета LLM ход молча теряется, пользователь не получает ответа | Средняя | Issue открыта, заголовок прямо фиксирует поведение | https://github.com/nanocoai/nanoclaw/issues/2751 |
| Windows + VS 2026: сборка better-sqlite3 падает, блокирует Windows-разработку | Высокая для Windows | По стороннему дайджесту; нативный Windows в любом случае не поддерживается (только WSL) | https://github.com/nanocoai/nanoclaw/issues/3017 |
| `/update-nanoclaw` нетранзакционна: может пометить обновление успешным до валидации, SQLite/конфиги не защищены от отката | Высокая | По стороннему дайджесту | https://github.com/nanocoai/nanoclaw/issues/3194 |
| Нет self-healing для OneCLI gateway: нездоровый контейнер не перезапускается, нет алертинга | Средняя | По стороннему дайджесту | https://github.com/nanocoai/nanoclaw/issues/2657 |

Примечание: пункты кроме #2751 при проверке подтверждены только вторичными дайджестами agents-radar, а не прямым чтением issue — это помечено как более слабый тип доказательств.

## 5. Незакрытые потребности пользователей

- Видимый ответ при исчерпании токен/бюджет-лимита вместо тишины — https://github.com/nanocoai/nanoclaw/issues/2751 . Особенно релевантно на фоне внедрения учёта стоимости из-за «surprise bills» (дайджест: https://github.com/yinwm/agents-radar/issues/18 ).
- Нормальная Windows-поддержка без WSL-обходных путей — https://github.com/nanocoai/nanoclaw/issues/3017 .
- Безопасные атомарные обновления с защитой БД — https://github.com/nanocoai/nanoclaw/issues/3194 .
- Reddit при проверке не открывался — `UNKNOWN`.

## 6. Технические ограничения

- Модель выполнения: один хост-процесс + контейнер на агента (README). Детерминизм не заявлен.
- Жёсткая платформенная граница: macOS/Linux, Windows только WSL (docs).
- Облачная зависимость: без интернета и аккаунта провайдера не работает (docs) — офлайн `UNSUPPORTED`.
- Параллелизм контейнеров, отмена/таймауты, идемпотентность scheduled jobs, версионирование: не специфицированы в проверенных источниках — `UNKNOWN`.
- Обновления между мажорными версиями требуют отдельной миграции состояния (README).

## 7. Вердикт и применимость к CyberPipe

NanoClaw — самый близкий к CyberPipe по сценарию «личный ассистент в Telegram с памятью и расписанием», но противоположный по платформе: TypeScript/Docker/WSL против .NET 10/Avalonia/Windows-first. Для CyberPipe ценность — в паттернах: контейнер/процесс на агента (аналог Brainstem sandbox process), отдельная память и workspace на агента, scheduled tasks, pairing каналов. Критика #2751 прямо валидирует TokenBudgetGovernor в CyberPipe: исчерпание бюджета должно давать видимый ответ, а не тишину. Как зависимость не применим (стек и Windows-ограничение). Итог: референс по продуктовым паттернам и антипаттерну «silent budget failure», не интеграция.
