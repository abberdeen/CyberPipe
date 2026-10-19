# Letta

> Дата проверки: 2026-10-19. Источники — только реально открытые через web_search/web_fetch страницы. Где доказательств недостаточно — UNKNOWN.
> Важно: репозиторий `letta-ai/letta` теперь исторический — актуальный код переехал в `letta-ai/letta-code` (указано в README letta).

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Letta (ранее MemGPT) |
| Официальный сайт | [letta.com](https://docs.letta.com) (документация), [chat.letta.com](https://chat.letta.com) |
| Репозиторий | [letta-ai/letta-code](https://github.com/letta-ai/letta-code) (активный), [letta-ai/letta](https://github.com/letta-ai/letta) (V1 API server в архивной ветке) |
| Документация | [docs.letta.com](https://docs.letta.com) |
| Категория | Платформа stateful-агентов с обучаемой памятью / агентный harness |
| Назначение | «Build stateful agents with memory that can learn and improve over time» (README); агенты с идентичностью, памятью и опытом во времени |
| Аудитория | Разработчики stateful-приложений и всегда-включённых персональных агентов; TypeScript Agent SDK, desktop/web/mobile |
| Статус на 2026-10-19 | Активно развивается: npm `@letta-ai/letta-code`, desktop app, Letta Cloud; V1-сервер заморожен в ветке `archive` |
| Лицензия | Apache-2.0 — проверено по LICENSE репозитория letta |
| Развёртывание | `npm install -g @letta-ai/letta-code`, `letta` (TUI), `letta server` (App Server, self-hosted), desktop (macOS/Windows/Linux), chat.letta.com, Letta Cloud |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Always-on агенты, proactive работа, heartbeats/crons |
| Планирование | PARTIAL | Агентное планирование + субагенты; детерминированного DAG нет |
| Долговременная память | SUPPORTED | Ядро продукта: memory blocks, archival/recall, MemFS (git-backed контекст), sleep-time/dreaming, skill learning |
| Контекст сессии | SUPPORTED | Stateful агенты, Sessions & durability в Agent SDK (по оглавлению docs) |
| Персистентность | SUPPORTED | MemFS как git-репозиторий контекста, sync в GitHub через `/memory-repository` |
| Инструменты / function-calling | SUPPORTED | Client/server tools, built-in tools, MCP tools |
| Навыки / плагины | SUPPORTED | Skills: глобальные `~/.letta`, проектные `.agents/skills`, агентные в MemFS; `/skill-creator`, mods и hooks |
| Мультиагентность | SUPPORTED | Subagents (general-purpose, forked, recall), shared memory blocks, multi-agent groups в V1 API (supervisor/round-robin/sleep-time — по описанию API) |
| Workflow / DAG | PARTIAL | Hooks и crons есть; DAG-движка нет |
| Ветвления / циклы | UNKNOWN | Не подтверждено |
| Обработка ошибок / retry / recovery | PARTIAL | Есть `/doctor [symptom]`, но см. серию cron/memory issue в §4–5 |
| Планировщик / триггеры | SUPPORTED | Crons & Schedules, heartbeats, scheduled messages |
| Интеграции | SUPPORTED | Slack, Telegram, Discord, custom channels, MCP |
| Файлы | SUPPORTED | MemFS, файлы как контекст, exec-окружение агента |
| Терминал / исполнение кода | SUPPORTED | CLI/desktop агент с exec; песочница инструментов в V1 описывалась как sandboxed Python (по API-разборам) |
| Браузер / GUI | PARTIAL | Browser и React Native в Agent SDK (по оглавлению docs); GUI-автоматизации уровня computer-use не заявлено |
| Мессенджеры | SUPPORTED | Telegram, Slack, Discord каналы к тому же агенту (README letta-code) |
| Локальные + облачные LLM | SUPPORTED | Локальный backend, OpenRouter и др.; Letta Cloud; BYOM remote computers |
| RAG | SUPPORTED | Archival memory с семантическим поиском (по описаниям V1 memory hierarchy) |
| История / логирование | SUPPORTED | Message search `/search` по всем сообщениям и агентам |
| Трейсинг | UNKNOWN | Отдельного tracing в просмотренных источниках не видно |
| HITL | SUPPORTED | Human-in-the-loop tools в V1 docs (оглавление), Permissions: режимы auto-approved/auto-denied |
| Песочница / безопасность | PARTIAL | Permissions и secrets (обфускация env); но issue о permission check префиксов папок (#4834) показывает риски |
| Аутентификация | PARTIAL | Sign-in для Cloud/remote/secrets; self-hosted auth детали — UNKNOWN |
| API / расширяемость | SUPPORTED | REST API V1 (архив), Agent SDK (TypeScript), App Server, mods |
| UI | SUPPORTED | TUI, desktop app, web chat.letta.com, mobile web |
| Импорт / экспорт | SUPPORTED | AgentFile (.af) в V1-концептах; MemFS git-sync как переносимый контекст |
| Тестирование | PARTIAL | Tool Graders / evals в оглавлении V1 docs; зрелость — PARTIAL |
| Production | PARTIAL | Letta Cloud есть; self-hosted production-кейсы по открытым источникам ограничены |
| Offline | PARTIAL | Локальный backend возможен; полностью offline-сценарий не заявлен как основной |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Агенты переписывают собственную память, промпты и даже harness (mods), учатся на длинных горизонтах | [letta-code README](https://github.com/letta-ai/letta-code) | Официальная документация |
| MemFS: весь контекст трекается в git, синхронизация в GitHub-репозиторий | [letta-code README](https://github.com/letta-ai/letta-code) | Официальная документация |
| Sleep-time/dreaming: фоновые агенты делят memory blocks с основным и консолидируют память | Экосистема docs (`guides/agents/architectures/sleeptime` в поисковых разборах) | Официальные guides (через поисковую выдачу) |
| Один агент доступен из CLI, desktop, web, Telegram/Slack/Discord | [letta-code README](https://github.com/letta-ai/letta-code) | Официальная документация |
| Трёхслойная память: core blocks (in-context), recall (история), archival (семантический поиск) | Описания memory hierarchy в docs-разборах | Официальная концепция (MemGPT lineage) |
| Permissions и secrets как отдельные продукты: auto-approved/auto-denied, обфускация секретов | [letta-code README](https://github.com/letta-ai/letta-code) | Официальная документация |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Cron/headless ходы без hard deadline; невыполненные tool calls должны становиться ошибками | Высокая | Открыт | [#5126](https://github.com/letta-ai/letta-code/issues/5126) |
| Scheduled turns доходят до агента, но unattended tool calls висят pending до stale recovery | Высокая | Открыт | [#5122](https://github.com/letta-ai/letta-code/issues/5122) |
| Cron/Wake задачи помечаются fired/queued, но не запускаются, когда scheduler lease держит App Server | Высокая | Открыт | [#4911](https://github.com/letta-ai/letta-code/issues/4911) |
| Memory checkout clone-swap больших shared-memory репозиториев упирается в ~3 мин deadline и абортит команды | Средняя | Открыт | [#5091](https://github.com/letta-ai/letta-code/issues/5091) |
| Telegram-агент повторно шлёт успешные MessageChannel вызовы, хотя «понимает», что надо остановиться | Средняя | Открыт | [#4878](https://github.com/letta-ai/letta-code/issues/4878) |
| Permission check рабочего каталога считает sibling-папки с общим префиксом «внутри» | Средняя (безопасность) | Открыт | [#4834](https://github.com/letta-ai/letta-code/issues/4834) |
| Preflight compaction игнорирует mid-conversation `<memory_update>`, коммит памяти может ужать `max_tokens=1` | Средняя | Открыт | [#4893](https://github.com/letta-ai/letta-code/issues/4893) |
| Внешние оценки называют Letta архитектурно амбициозной, но «not production-ready» | Средняя | Внешняя оценка | Поисковые исследовательские разборы памяти (вторичный источник) |

## 5. Незакрытые потребности пользователей (Issues/Discussions/Reddit)

| Потребность / боль | Источник |
|---|---|
| Hard deadline для cron/headless ходов | [#5126](https://github.com/letta-ai/letta-code/issues/5126) |
| Надёжное исполнение scheduled turns без stale recovery | [#5122](https://github.com/letta-ai/letta-code/issues/5122) |
| Spell check в поле ввода чата (accessibility) | [#4897](https://github.com/letta-ai/letta-code/issues/4897) |
| Per-message read-aloud и voice conversation mode | [#4897](https://github.com/letta-ai/letta-code/issues/4897) / [#4877](https://github.com/letta-ai/letta-code/issues/4877) |
| Slack Agents experience: assistant threads, split view, suggested prompts | [#4926](https://github.com/letta-ai/letta-code/issues/4926) |
| Исправление stale Slack connection после удаления агента/workspace | [#5027](https://github.com/letta-ai/letta-code/issues/5027) |
| Обсуждение управления памятью MemGPT (историческое) | [Discussion #214](https://github.com/cpacker/MemGPT/discussions/214) |
| Reddit: дискуссия об agent memory и self-editing memory blocks Letta/MemGPT | [r/LocalLLaMA: agent_memory](https://www.reddit.com/r/LocalLLaMA/comments/1gvhpjj/agent_memory/) |

## 6. Технические ограничения

| Аспект | Оценка |
|---|---|
| Модель исполнения | Stateful агентный цикл с self-editing контекстом; фоновые sleep-time агенты |
| Детерминированность | Низкая: агент переписывает собственные промпты/память/harness — поведение дрейфует во времени by design |
| Параллелизм | Субагенты и multi-agent groups; лимиты в источниках не специфицированы — UNKNOWN |
| Отмена / таймауты | Открытая боль: нет hard deadline у cron/headless (#5126), unattended вызовы висят (#5122) |
| Идемпотентность | Не заявлена; повторная отправка в Telegram (#4878) — симптом отсутствия идемпотентных ключей доставки |
| Восстановление | Stale recovery для pending tool calls упоминается как костыль, а не гарантия |
| Версионирование | Сильная сторона: MemFS = git-история контекста; версионирование поведения агента при self-modification — риск, а не фича |

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** Letta — эталон stateful-памяти (blocks + archival + MemFS + sleep-time), но её само-модифицируемость конфликтует с детерминизмом. Для CyberPipe это главный референс по памяти и мульти-канальности (Telegram к тому же агенту), и анти-референс по контролю исполнения.

Применимость к CyberPipe (.NET 10, Telegram+Avalonia, Tier0–Tier3, TokenBudgetGovernor, Netrunner DAG, SQLite WAL, Brainstem sandbox, HITL):

- **Заимствовать:** слои памяти — core (в контексте) / recall (история в SQLite WAL) / archival (семантический поиск по логам и документам); MemFS-идею git-версии контекста для аудита; sleep-time консолидацию как офлайн-Netrunner сценарий (ночной DAG: суммаризация, чистка памяти) под TokenBudgetGovernor; модель «один агент — много каналов» (Telegram + Avalonia к одному состоянию).
- **Не заимствовать:** self-rewriting промптов и harness без HITL — в CyberPipe изменения скиллов должны идти через HitlApprovalNode и версионируемые Netrunner packages; отсутствие hard deadline у scheduled ходов (#5126/#4911) — для Netrunner обязательны timeout/cancel на узел и идемпотентность DeliveryNode (контрпример — Telegram-дубли #4878).
- **Роутинг:** у Letta нет Tier0/Tier1 — каждая мелочь идёт в LLM; CyberPipe экономит бюджет детерминированными ярусами, а Letta-подобную память подключать только на Tier2/Tier3.
- **Каналы:** Telegram-поддержка Letta подтверждает спрос на мессенджерный доступ к stateful-агенту — валидация выбора CyberPipe, но реализация (Bot API, идемпотентные delivery keys) — своя.
