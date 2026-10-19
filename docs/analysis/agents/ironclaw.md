# IronClaw

> Дата проверки: 2026-10-19. Источники — только реально открытые через web_search/web_fetch страницы (указаны ссылками). Если доказательств недостаточно — статус `UNKNOWN`, а не догадка.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное имя | IronClaw |
| Сайт / сообщество | Telegram: https://t.me/ironclawAI ; Reddit: https://www.reddit.com/r/ironclawAI/ (ссылки из README репозитория) |
| Репозиторий | https://github.com/nearai/ironclaw |
| Документация | README и разделы внутри репозитория https://github.com/nearai/ironclaw (отдельный docs-сайт при проверке не подтверждён — `UNKNOWN`) |
| Категория | Agent OS / безопасный персональный AI-ассистент (самоописание репозитория: «Agent OS focused on privacy, security and extensibility», «Your secure personal AI assistant») |
| Назначение | Локальный персональный ассистент с акцентом на изоляцию инструментов, защиту секретов и расширяемость |
| Аудитория | Индивидуальные пользователи и разработчики, которым нужен self-hosted ассистент с жёсткой моделью безопасности |
| Статус на 2026-10-19 | Активная разработка; в поиске виден релиз `ironclaw-v1.4.1` от 2026-09-29: https://github.com/nearai/ironclaw/releases/tag/ironclaw-v1.4.1 |
| Лицензия | MIT OR Apache-2.0 (бейдж в README: `license-MIT OR Apache 2.0`) |
| Развёртывание | macOS, Linux, Windows/WSL (установка скриптом релиза, `ironclaw onboard`, `ironclaw serve` на Windows, фоновый сервис на macOS/Linux — по README) |

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | README: фоновый сервис, routines, heartbeat; позиционирование «always on» |
| Планирование | UNKNOWN | Явный планировщик задач в открытых источниках не показан |
| Долговременная память | PARTIAL | В стороннем changelog-зеркале упоминались memory connections/spaces/profiles, но в официальном README при проверке это не развёрнуто — недостаточно доказательств для SUPPORTED |
| Контекст сессии | SUPPORTED | Диалоговый ассистент, WebUI-чат (README) |
| Персистентность | SUPPORTED | «All information is stored locally, encrypted» (README, Philosophy) |
| Инструменты / function-calling | SUPPORTED | WASM-инструменты, self-expanding tools (README) |
| Навыки / плагины | PARTIAL | Самогенерация skill-определений из NL описана во внешнем issue https://github.com/bug-ops/zeph/issues/2418 как pattern IronClaw; официальный реестр навыков при проверке не открыт |
| Мультиагентность | UNKNOWN | Достоверных официальных данных не получено |
| Workflow / DAG | UNKNOWN | Routines есть, но DAG-модель в источниках не показана |
| Ветвления / циклы | UNKNOWN | Не подтверждено |
| Обработка ошибок / retry / recovery | UNKNOWN | Не подтверждено в открытых при проверке источниках |
| Расписание / триггеры | SUPPORTED | README: «Routines — Cron schedules, event triggers, webhook handlers» |
| Интеграции | SUPPORTED | HTTP webhooks, WASM channels (README) |
| Файлы | SUPPORTED | Локальное хранение данных (README); детали операций — PARTIAL по глубине доказательств |
| Терминал / выполнение кода | SUPPORTED | `shell`-инструмент существует — см. issue о его approval в разделе 4 |
| Браузер / GUI | SUPPORTED | Web Gateway / WebUI с SSE/WebSocket streaming (README) |
| Мессенджеры | SUPPORTED | Telegram, Slack как WASM channels (README) |
| Локальные + облачные LLM-провайдеры | PARTIAL | Onboard предлагает выбор LLM-провайдера и API-ключ (README); полный список провайдеров и локальные модели при проверке не перечислены официально |
| RAG | UNKNOWN | Не подтверждено |
| История / логирование | PARTIAL | Tool audit logs упоминаются в curated-списке https://github.com/upgundecha/awesome-personal-agents ; официальный объём логирования — UNKNOWN |
| Трейсинг | UNKNOWN | Не подтверждено |
| HITL (подтверждение человеком) | SUPPORTED | Approval для shell-инструмента — предмет issues #4862/#4863 (см. ниже); значит, механизм подтверждения существует |
| Песочница / безопасность | SUPPORTED | WASM Sandbox, Credential Protection, Prompt Injection Defense, Endpoint Allowlisting, Docker Sandbox (README, Features) |
| Аутентификация | SUPPORTED | WebUI login token при onboard (README) |
| API / расширяемость | SUPPORTED | HTTP webhooks, WASM channels/tools (README) |
| UI | SUPPORTED | WebUI / Web Gateway (README) |
| Импорт / экспорт | UNKNOWN | Не подтверждено |
| Тестирование | UNKNOWN | Наличие тестового набора при проверке не верифицировано из официальных страниц |
| Production-готовность | PARTIAL | Есть релизы и установщики, но параллельно идёт крупная перестройка (см. критику) |
| Офлайн-режим | PARTIAL | Данные локальны, но LLM-вызовы требуют провайдера; полностью офлайн-сценарий не подтверждён |

## 3. Сильные стороны

- WASM-изоляция недоверенных инструментов с capability-based permissions — источник: официальный README, раздел Security First; тип доказательств: официальная документация.
- Секреты не отдаются инструментам, инжектятся на границе хоста с leak detection — официальный README.
- Prompt-injection defense и endpoint allowlisting — официальный README.
- Локальное шифрованное хранение и отсутствие телеметрии как заявленный принцип — официальный README, Philosophy.
- Cron/event/webhook routines для фоновой автоматизации — официальный README.

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Обход approval у `shell` через GNU `sort --compress-program` (затронуты версии <= 0.29.1) | Высокая (security) | Issue открыт в выдаче; фикс при проверке не верифицирован | https://github.com/nearai/ironclaw/issues/4862 |
| Обход high-risk shell approval через `env`/shell-обёртки после auto-approve `shell` (<= 0.29.1) | Высокая (security) | Аналогично | https://github.com/nearai/ironclaw/issues/4863 |
| Compaction leak: `build_input` отклоняет все non-clean совпадения и «отравляет» восстановление контекста | Средняя | Issue в выдаче | https://github.com/nearai/ironclaw/issues/6853 |
| Крупная переархитектура (Reborn) даёт ежедневные регрессии (TEE failure, Telegram setup) | Средняя | Сторонний дайджест, не официальный источник — относиться как к слабому сигналу | https://github.com/lib182071-prog/agents-radar/issues/161 |

## 5. Незакрытые потребности пользователей

- Безопасность approval-границы shell: пользователи/аудиторы требуют, чтобы подтверждение нельзя было обойти обёртками — https://github.com/nearai/ironclaw/issues/4862 и https://github.com/nearai/ironclaw/issues/4863 .
- Корректное восстановление контекста после compaction без блокировки на leak-совпадениях — https://github.com/nearai/ironclaw/issues/6853 .
- Отдельных тредов Reddit при проверке не открывалось; сообщество существует (https://www.reddit.com/r/ironclawAI/), но конкретные незакрытые запросы оттуда — `UNKNOWN`, не выдумываются.

## 6. Технические ограничения

- Модель выполнения: хост на Rust + WASM-инструменты/каналы, Docker sandbox для jobs (README). Детерминизм LLM-цикла не заявлен — `UNKNOWN`.
- Параллелизм: orchestrator/worker для Docker sandbox заявлен; пределы параллелизма — `UNKNOWN`.
- Отмена/таймауты, идемпотентность, восстановление после падения, версионирование workflow: в проверенных источниках не описаны — `UNKNOWN`.
- Платформенный предел: на Windows полная установка — через WSL, нативно только foreground `ironclaw serve` (README).

## 7. Вердикт и применимость к CyberPipe

IronClaw — ближайший аналог CyberPipe по идеологии: локальный персональный ассистент, Telegram, песочница, HITL-подтверждения, защита секретов. Для CyberPipe (.NET 10, Telegram+Avalonia, Brainstem sandbox process, HitlApprovalNode) это в первую очередь референс по безопасности: credential vault с инжектом на границе хоста, endpoint allowlist, WASM/capability-изоляция инструментов — паттерны, переносимые в Brainstem без переноса Rust-стека. Как зависимость не применим: другой runtime (Rust/WASM против .NET 10), нет подтверждённого DAG уровня Netrunner (SkillNode/ConditionNode/LlmProcessNode), 4-tier routing и TokenBudgetGovernor там не показаны. Итог: изучать и заимствовать security-паттерны, не интегрировать.
