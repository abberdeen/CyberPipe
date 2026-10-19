# NullClaw

> Дата проверки: 2026-10-19. Источники — только реально открытые через web_search/web_fetch страницы. Недостаток доказательств помечается `UNKNOWN`.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное имя | NullClaw |
| Сайт | https://nullclaw.io/ ; документация: https://nullclaw.io/nullclaw/docs/getting-started |
| Репозиторий | https://github.com/nullclaw/nullclaw |
| Документация | https://nullclaw.github.io (бейдж в README) и https://nullclaw.io/nullclaw/docs/getting-started |
| Категория | Минималистичный автономный AI-ассистент / агентная инфраструктура на Zig |
| Назначение | «The smallest fully autonomous AI assistant infrastructure» — статический Zig-бинарник 678 KB, ~1 MB RAM, старт <2 ms (заявления README) |
| Аудитория | Владельцы дешёвых SBC/edge-устройств, энтузиасты self-hosting |
| Статус на 2026-10-19 | Репозиторий активен; экосистема NullHub (UI) — в бете (README: «currently in beta») |
| Лицензия | MIT (бейдж в README) |
| Развёртывание | Сборка из исходников: `zig build -Doptimize=ReleaseSmall`, требуется Zig 0.16.0 (docs getting-started); ARM/x86/RISC-V |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Самоописание «fully autonomous AI assistant infrastructure» (README, tagline репозитория) |
| Планирование | UNKNOWN | Отдельный планировщик в проверенных источниках не показан |
| Долговременная память | SUPPORTED | «10 memory engines» (README, Feature-Complete); есть docs-раздел Memory: https://nullclaw.io/nullclaw/docs/getting-started (меню Runtime → Memory) |
| Контекст сессии | SUPPORTED | Агентный CLI-цикл `nullclaw agent` (docs) |
| Персистентность | SUPPORTED | Конфиг `~/.nullclaw/config.json`, memory engines (docs/README) |
| Инструменты / function-calling | PARTIAL | «35+ tools» (README), но у Ollama-провайдера нативный tool calling не использовался — issue #375 |
| Навыки / плагины | PARTIAL | «Pluggable everything», vtable-интерфейсы (README); отдельный формат skills при проверке не верифицирован |
| Мультиагентность | SUPPORTED | «subagents» в списке возможностей README; A2A protocol v0.3.0 — в форк-зеркалах README (официальность A2A в main при проверке видна только в зеркалах, поэтому детали — PARTIAL) |
| Workflow / DAG | UNKNOWN | DAG-модель не показана |
| Ветвления / циклы | UNKNOWN | Не подтверждено |
| Обработка ошибок / retry / recovery | PARTIAL | `nullclaw doctor` для диагностики (docs); retry/recovery семантика — UNKNOWN |
| Расписание / триггеры | UNKNOWN | В проверенных официальных страницах cron не показан |
| Интеграции | SUPPORTED | 19 channels, 50+ providers, MCP, tunnels, hardware peripherals (README) |
| Файлы | SUPPORTED | Workspace scoping (README, Secure by design) |
| Терминал / выполнение кода | SUPPORTED | Инструменты выполнения входят в «35+ tools»; песочницы landlock/firejail/bubblewrap/docker (README) |
| Браузер / GUI | PARTIAL | WebChannel chat UI через gateway + pairing (docs); NullHub UI — бета и отдельный репозиторий https://github.com/nullclaw/nullhub |
| Мессенджеры | SUPPORTED | 19 channels (README); конкретный список при проверке полностью не перечислен — детали PARTIAL |
| Локальные + облачные LLM-провайдеры | SUPPORTED | «50+ providers», OpenAI-compatible + custom endpoints, пример OpenRouter при onboard (README/docs) |
| RAG | UNKNOWN | Не подтверждено |
| История / логирование | PARTIAL | Observers как vtable-интерфейс (README); глубина логов — UNKNOWN |
| Трейсинг | PARTIAL | NullWatch observability в экосистеме NullHub (README) — отдельный компонент, не ядро |
| HITL | PARTIAL | Pairing и allowlists (README); полноценный approval-flow в ядре при проверке не показан; NullHub заявляет «visual human-in-the-loop» (nullclaw.io) |
| Песочница / безопасность | SUPPORTED | Pairing, strict sandboxing (landlock, firejail, bubblewrap, docker), allowlists, encrypted secrets (README) |
| Аутентификация | SUPPORTED | Pairing mode, PIN для локального WebChannel (docs getting-started) |
| API / расширяемость | SUPPORTED | Vtable-интерфейсы провайдеров/каналов/инструментов/памяти (README) |
| UI | PARTIAL | Chat UI (WebChannel) + бета NullHub |
| Импорт / экспорт | UNKNOWN | Не подтверждено |
| Тестирование | SUPPORTED | «5,300+ tests», `zig build test` (README) |
| Production-готовность | PARTIAL | Заявлен feature-complete стек, но есть критичные connectivity/crash issues (раздел 4) и бета-UI |
| Офлайн-режим | PARTIAL | Локальный бинарник и локальные провайдеры возможны архитектурно; подтверждённого офлайн-сценария в docs при проверке нет |

## 3. Сильные стороны

- Экстремально малый размер и старт: 678 KB, ~1 MB RSS, <2 ms — официальный README, раздел Benchmark Snapshot; тип: официальные заявления с методикой `/usr/bin/time -l` на ReleaseSmall.
- Портативность: один статический бинарник ARM/x86/RISC-V — README.
- Полностью заменяемые подсистемы через vtable (providers, channels, tools, memory, tunnels, peripherals, observers, runtimes) — README.
- Многослойная песочница и шифрованные секреты — README, Secure by design.
- Большой тестовый набор по заявлению проекта (5 300+) — README.

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Ollama: нативный tool calling не используется, `tools` не попадает в тело запроса | Средняя | Issue открыта в выдаче | https://github.com/nullclaw/nullclaw/issues/375 |
| `web_search` непрактичен на слабых устройствах без прямой поддержки DuckDuckGo | Средняя (для edge-сценария — высокая) | Issue с формулировкой «critical limitation» от автора issue | https://github.com/nullclaw/nullclaw/issues/871 |
| Критичные unresolved connectivity issues; SIGSEGV gateway при входящем Telegram-сообщении | Высокая | Сторонние дайджесты agents-radar, не официальный трекер — слабый тип доказательств, требует перепроверки в самом репозитории | https://github.com/bianzhilong2-ctrl/agents-radar/blob/HEAD/digests/2026-07-17/ai-agents-en.md |
| Поддержка/активность: «only Dependabot PR in 43 days» в одном из дайджестов | Средняя | Сторонний дайджест | https://github.com/deniszheng/agents-radar/issues/1586 |

## 5. Незакрытые потребности пользователей

- Рабочий локальный tool calling через Ollama — https://github.com/nullclaw/nullclaw/issues/375 .
- Поиск без тяжёлых внешних сервисов на слабом железе (прямой DuckDuckGo backend) — https://github.com/nullclaw/nullclaw/issues/871 .
- Стабильность gateway/Telegram по сторонним дайджестам (раздел 4); прямых подтверждений из официального трекера при проверке получено меньше, поэтому часть потребностей помечается как недостаточно верифицированная.
- Reddit-треды при проверке не открывались — конкретные запросы оттуда `UNKNOWN`.

## 6. Технические ограничения

- Модель выполнения: один статический Zig-процесс, gateway-режим для каналов (docs). Детерминизм не заявлен.
- Сборка завязана на Zig 0.16.0 и feature-флаги (`-Dchannels=...`, `-Dengines=...`): «Feature configured but unavailable: rebuild with needed feature flags» — docs getting-started. Это жёсткое ограничение расширяемости в рантайме.
- Параллелизм/отмена/таймауты/идемпотентность/recovery/versioning: в проверенных источниках не специфицированы — `UNKNOWN`.
- Требование пересборки под фичи конфликтует с идеей плагинов «на лету».

## 7. Вердикт и применимость к CyberPipe

NullClaw интересен CyberPipe как антипример по весу и как референс по sandbox-стеку (landlock/firejail/bubblewrap/docker, pairing, allowlists) и по идее capability-инвентаризации (`capabilities --json`, `doctor` — аналог можно сделать для Netrunner-нод). Как компонент не применим: Zig-стек несовместим с .NET 10, DAG/ветвления/HITL уровня Mini-n8n (HitlApprovalNode, ConditionNode) не подтверждены, Telegram-стабильность под вопросом по сторонним сигналам. Итог: заимствовать doctor/capabilities и sandbox-чеклист, код не интегрировать.
