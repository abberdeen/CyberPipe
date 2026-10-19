# Agent Zero

> Дата проверки: 2026-10-19. Источники — только реально открытые через web_search/web_fetch страницы (см. ссылки). Где доказательств недостаточно — статус UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Agent Zero |
| Официальный сайт | [agent-zero.ai](https://www.agent-zero.ai/) |
| Репозиторий | [agent0ai/agent-zero](https://github.com/agent0ai/agent-zero) |
| Документация | [docs/ в репозитории](https://github.com/agent0ai/agent-zero/tree/main/docs), [DeepWiki](https://deepwiki.com/agent0ai/agent-zero) |
| Категория | Открытый агентный фреймворк / персональный агент с собственным Linux-компьютером |
| Назначение | Автономная работа шире чата: Dockerized Linux desktop, браузер с DOM-аннотациями, совместное редактирование документов, проекты, навыки, плагины, мост к хост-машине (формулировка README: «Give your agent a full Linux computer») |
| Аудитория | Разработчики и продвинутые пользователи, которым нужен настраиваемый self-hosted агент; сценарии из README: software engineering, исследования, анализ данных, офисные документы, QA |
| Статус на 2026-10-19 | Активно развивается: README ссылается на A0 Launcher v1.9, Plugin Hub «100+ community plugins», OAuth-подключение OpenAI Codex plan. Точную дату последнего релиза по открытым страницам не фиксирую — UNKNOWN |
| Лицензия | MIT (Copyright (c) 2025 Agent Zero, s.r.o) — проверено по файлу LICENSE в репозитории |
| Развёртывание | Docker (`docker run -p 80:80 -v a0_usr:/a0/usr agent0ai/agent-zero`), A0 Launcher (macOS/Linux/Windows, x86+ARM64), A0 Install для серверов/SSH, VPS от «$6 VPS or Raspberry Pi» по README |

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Сайт: «Autonomous agentic AI that runs on its own computer, uses and creates tools, learns, self-corrects» |
| Планирование | PARTIAL | Декомпозиция через субагентов и TODO-документы в Canvas; отдельного детерминированного планировщика/DAG в источниках не показано |
| Долговременная память | SUPPORTED | Projects and memory, Memory guide (`docs/guides/memory.md` в оглавлении README), изоляция памяти по проектам |
| Контекст сессии | SUPPORTED | Чаты, история браузера со скриншотами, Time Travel снапшоты `/a0/usr` |
| Персистентность | SUPPORTED | Том `/a0/usr`, проекты, Time Travel (snapshot/diff/revert) |
| Инструменты / function-calling | SUPPORTED | `tools/`, custom tools, MCP setup guide, A2A connectors |
| Навыки / плагины | SUPPORTED | Skills guide, SKILL.md, Plugin Hub 100+, create-plugin tutorial |
| Мультиагентность | SUPPORTED | «Every agent can create subordinate agents… superior gives tasks and receives reports» |
| Workflow / DAG | PARTIAL | Есть scheduler и multi-agent delegation, но явного DAG-движка с типизированными узлами в источниках нет |
| Ветвления / циклы | UNKNOWN | Явных данных о condition/loop-узлах не найдено |
| Обработка ошибок / retry / recovery | PARTIAL | Self-correct заявлен; Time Travel даёт rollback файлов, но issue о зависании backend (см. §5) показывает слабые места |
| Планировщик / триггеры | SUPPORTED | Оглавление: scheduler, cron; сценарий «Scheduled operations» в README |
| Интеграции | SUPPORTED | MCP, A2A, Plugin Hub, Git Projects, A0 CLI Connector |
| Файлы | SUPPORTED | Markdown Editor, LibreOffice Writer/Calc/Impress (ODT/ODS/ODP), файловый доступ в контейнере и через A0 CLI на хосте |
| Терминал / исполнение кода | SUPPORTED | Linux desktop с терминалом, A0 CLI host bridge |
| Браузер / GUI | SUPPORTED | Встроенный Browser с Annotate mode (Change/Inspect/Lift/Comment), XFCE desktop, Bring Your Own Browser (Chrome/Edge/Brave/Opera/Vivaldi/Chromium) |
| Мессенджеры | UNKNOWN | В просмотренных README/docs мессенджеры типа Telegram не заявлены |
| Локальные + облачные LLM | SUPPORTED | Model Presets (fast/balanced/cheap/local/high-power), OpenAI Codex OAuth; «More integrations are coming, including Gemini CLI and Claude Code» — то есть часть PLANNED |
| RAG | PARTIAL | `document_query` и knowledge в проектах упоминаются в экосистеме; полноценный векторный RAG-контур по открытым страницам не подтверждён — PARTIAL |
| История / логирование | SUPPORTED | История чатов, browser screenshots, Time Travel diff |
| Трейсинг | UNKNOWN | Отдельного tracing/OpenTelemetry в источниках не найдено |
| HITL | PARTIAL | Пользователь разделяет тот же desktop («you can intervene at any moment»), аннотации как директивы; формального approval-гейта уровня HitlApprovalNode не показано |
| Песочница / безопасность | PARTIAL | Docker-изоляция + явный opt-in доступ к хосту через A0 CLI; при этом найден issue про утечку секретов между проектами (см. §4–5) |
| Аутентификация | PARTIAL | OAuth для Codex plan; полноценная multi-user auth в источниках не раскрыта |
| API / расширяемость | SUPPORTED | Plugins, MCP, A2A, custom prompts в `prompts/`, extensions guide |
| UI | SUPPORTED | Web UI + Canvas (Desktop/Browser/редакторы), A0 Launcher |
| Импорт / экспорт | PARTIAL | Git clone проектов, backup `/a0/usr`; форматы экспорта агентов не подтверждены |
| Тестирование | UNKNOWN | Собственного eval/test-контура для агентов в источниках не найдено |
| Production | PARTIAL | VPS/серверные сценарии заявлены, но maturity для production — по issue-трекеру есть критичные зависания |
| Offline | PARTIAL | Локальные модели через presets возможны; полностью offline-стек (STT/TTS и т.п.) не заявлен |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Полноценный Linux desktop (XFCE) внутри Canvas, агент управляет реальным GUI-софтом (Blender, терминал, файлы) | [README репозитория](https://github.com/agent0ai/agent-zero) | Официальная документация |
| Browser с Annotate mode: Change/Inspect/Lift/Comment по DOM-элементам | [README](https://github.com/agent0ai/agent-zero), раздел Browser guide | Официальная документация |
| Совместное редактирование Markdown и LibreOffice-документов в реальном времени | [README](https://github.com/agent0ai/agent-zero) | Официальная документация |
| Plugin Hub: 100+ community plugins, установка из Web UI | [README](https://github.com/agent0ai/agent-zero) | Официальное заявление проекта (не независимый аудит) |
| Изоляция по проектам: файлы, инструкции, секреты, память, репозитории, model presets | [README](https://github.com/agent0ai/agent-zero) | Официальная документация; но см. контрпример в §5 |
| Time Travel: снапшоты, diff, revert для `/a0/usr` | [README](https://github.com/agent0ai/agent-zero) | Официальная документация |
| Прозрачность: промпты в `prompts/`, инструменты в `tools/`, всё инспектируемо | [README](https://github.com/agent0ai/agent-zero) | Официальная документация |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Агент одного проекта может читать и использовать секреты другого проекта (`.a0proj/secrets.env`) — изоляция секретов пробита | Высокая | Открыт (по списку issues на дату проверки) | [issue #1955](https://github.com/agent0ai/agent-zero/issues/1955) |
| Полное зависание backend: event loop freezes, все чаты стопорятся, сообщения молча теряются, «no trace left» | Высокая | Открыт | [issue #1889](https://github.com/agent0ai/agent-zero/issues/1889) |
| Мёртвый worker event-loop в Browser plugin навсегда вешает ход агента, нет timeout в `BrowserRuntime.call_for`, cancellation поглощается | Высокая | Открыт | [issue #1923](https://github.com/agent0ai/agent-zero/issues/1923) |
| Рост потребления RAM со временем, нельзя полностью остановить из UI | Средняя | Открыт | [issue #1892](https://github.com/agent0ai/agent-zero/issues/1892) |
| FileNotFoundError для замаскированных секретами путей картинок, сохранённых в истории | Средняя | Открыт | [issue #1958](https://github.com/agent0ai/agent-zero/issues/1958) |
| Заявленная «self-corrects / transparent workflows» автономность не подкреплена в источниках детерминированными гарантиями повторов и идемпотентности | Средняя | Наблюдение автора по отсутствию данных (UNKNOWN-гарантии) | [сайт](https://www.agent-zero.ai/) |

## 5. Незакрытые потребности пользователей (Issues/Discussions/Reddit)

Только конкретные URL из трекера; Reddit-треды по Agent Zero в поиске надёжно не подтвердились — по Reddit статус UNKNOWN, не выдумываю.

| Потребность / боль | Источник |
|---|---|
| Scoped MemCode recall для проектов (предложение по памяти) | [issue #1929](https://github.com/agent0ai/agent-zero/issues/1929) |
| Поддержка wrapped embedding responses от custom provider + лучшая отчётность об ошибках | [issue #1942](https://github.com/agent0ai/agent-zero/issues/1942) |
| Hook `tool_execute_before` не может модифицировать аргументы инструмента | [issue #1926](https://github.com/agent0ai/agent-zero/issues/1926), [issue #1925](https://github.com/agent0ai/agent-zero/issues/1925) |
| Автопереименование чатов затирает ручные имена; заголовки «Hello»/«Greeting» не обновляются | [issue #1941](https://github.com/agent0ai/agent-zero/issues/1941), [issue #1895](https://github.com/agent0ai/agent-zero/issues/1895) |
| Remote Control Cloudflare links часто падают с первой попытки | [issue #1906](https://github.com/agent0ai/agent-zero/issues/1906) |
| Бэкапы дают обрезанные 1KB файлы, auto-updater падает на ZIP timestamps | [issue #1904](https://github.com/agent0ai/agent-zero/issues/1904) |
| Нет «Add Secrets» на мобильном UI (Android) | [issue #1950](https://github.com/agent0ai/agent-zero/issues/1950) |

## 6. Технические ограничения

| Аспект | Оценка |
|---|---|
| Модель исполнения | LLM-цикл внутри Docker-контейнера + субагенты; не детерминированный DAG |
| Детерминированность | Низкая по построению (LLM-driven); гарантий воспроизводимости в источниках нет — UNKNOWN |
| Параллелизм | Субагенты и несколько чатов; лимиты/конкурентность в источниках не специфицированы — UNKNOWN |
| Отмена / таймауты | Слабое место: issue #1923 прямо фиксирует отсутствие timeout и поглощение cancellation в браузере |
| Идемпотентность | Не заявлена; повторный запуск задачи может повторить сайд-эффекты — UNKNOWN |
| Восстановление | Сильная сторона для файлов (Time Travel), слабая для рантайма (issue #1889: зависание без следа) |
| Версионирование | Снапшоты workspace есть; версионирование промптов/скиллов как артефактов — PARTIAL/UNKNOWN |

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** Agent Zero — ближайший по духу «персональный агент с компьютером»: Docker-песочница, GUI/браузер, память по проектам, плагины и scheduler. Для CyberPipe это прежде всего референс UX и изоляции, а не готовый движок: нет детерминированного Mini-n8n DAG, нет формального HITL-гейта, нет TokenBudgetGovernor.

Применимость к CyberPipe (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-tier routing Tier0 regex / Tier1 ONNX / Tier2-3 LLM, Netrunner DAG: SkillNode/NetrunnerScriptNode/ConditionNode/LlmProcessNode/HitlApprovalNode/DeliveryNode, SQLite WAL, Brainstem sandbox, HITL):

- **Заимствовать:** модель Projects (изоляция памяти/секретов/пресетов моделей — аналог скоупов для Netrunner-скиллов), Time Travel как идею снапшотов перед мутациями, Annotate-подобные директивы для Avalonia UI, Plugin Hub как каталог Netrunner script packages.
- **Не заимствовать:** LLM-цикл как исполнитель вместо DAG — CyberPipe сильнее за счёт ConditionNode/HitlApprovalNode и идемпотентных NetrunnerScriptNode; у Agent Zero отмена/таймауты и межпроектная изоляция секретов уже дали публичные сбои (#1923, #1955, #1889) — для Brainstem sandbox это аргумент за жёсткие timeout/cancel и per-project secret scoping с первого дня.
- **Роутинг:** Tier0/Tier1 (regex/ONNX) у CyberPipe дешевле и детерминированнее, чем «сразу LLM-агент» у Agent Zero; Model Presets Agent Zero — аналог Tier2/Tier3 выбора модели, но без TokenBudgetGovernor.
- **Каналы:** Telegram у Agent Zero не подтверждён (UNKNOWN) — здесь CyberPipe идёт своим путём, референсом не является.
