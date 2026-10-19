# Leon

> Дата проверки: 2026-10-19. Источники — только реально открытые через web_search/web_fetch страницы. Где доказательств недостаточно — UNKNOWN.
> Важно: публичный сайт и старые гайды описывают legacy-архитектуру; актуальное состояние — ветка `develop` (Leon 2.0 Developer Preview), документация 2.0 «not ready yet» (README).

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Leon |
| Официальный сайт | [getleon.ai](https://getleon.ai/), roadmap — [roadmap.getleon.ai](http://roadmap.getleon.ai), блог — [blog.getleon.ai](https://blog.getleon.ai/the-story-behind-leon/) |
| Репозиторий | [leon-ai/leon](https://github.com/leon-ai/leon) (активная ветка `develop`, стабильный legacy — `master`) |
| Документация | Новая документация 2.0 не готова; источники истины — `core/context/LEON.md` и `core/context/ARCHITECTURE.md` в репозитории; legacy-сайт docs отражает старую архитектуру |
| Категория | Open-source персональный AI-ассистент (local-first) |
| Назначение | «Your open-source personal AI assistant built around tools, context, memory, and agentic execution» (README develop) |
| Аудитория | Приватность-ориентированные пользователи и разработчики self-hosted ассистентов; голос+текст |
| Статус на 2026-10-19 | 2.0 Developer Preview (notice от 2026-03-29 в README): ядро переписывается, контрибьюторы онбордятся постепенно, разработка «largely during spare time» |
| Лицензия | MIT — бейдж в README и файл LICENSE.md |
| Развёртывание | Node.js >= 24, `pnpm install && pnpm start`, локально `http://localhost:5366`; Linux/macOS/Windows; Python TCP server и bridges в стеке |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | PARTIAL | `agent` mode планирует пошагово; `smart` выбирает режим, `controlled` — детерминированные native skills |
| Планирование | PARTIAL | Agent mode «can plan step by step»; отдельного планировщика/DAG нет |
| Долговременная память | SUPPORTED | Layered memory: durable preferences, day-to-day context, recent discussion; компактная self-model |
| Контекст сессии | SUPPORTED | Контекст окружения машины и недавних обсуждений |
| Персистентность | SUPPORTED | Локальное хранение памяти/контекста на сервере пользователя |
| Инструменты / function-calling | SUPPORTED | Цепочка Skills → Actions → Tools → Functions (→ Binaries), toolkits и bridges (Node.js/Python) |
| Навыки / плагины | SUPPORTED | Native skills (controlled) + agent skills (SKILL.md), модульная система packages/skills с сайта |
| Мультиагентность | UNKNOWN | В источниках не заявлена |
| Workflow / DAG | PARTIAL | Deterministic workflows в controlled mode; DAG-движка нет |
| Ветвления / циклы | UNKNOWN | Не подтверждено |
| Обработка ошибок / retry / recovery | PARTIAL | Заявлено «recover when something goes wrong»; детали retry в источниках отсутствуют |
| Планировщик / триггеры | PARTIAL | Bounded proactive pulse system; полноценного cron-планировщика в источниках не видно |
| Интеграции | PARTIAL | Bridges/toolkits: search, productivity, system utilities, media, coding, voice/audio (по README) |
| Файлы | PARTIAL | Работа с окружением машины; детального файлового контура в источниках нет |
| Терминал / исполнение кода | PARTIAL | Coding assistance skills и binaries; полноценного terminal-агента не заявлено |
| Браузер / GUI | SUPPORTED | Computer use: desktop и browser интерфейсы, верификация визуальных исходов, локально или через Satellite, progressive loading инструментов |
| Мессенджеры | UNKNOWN | В актуальных источниках не заявлены |
| Локальные + облачные LLM | SUPPORTED | «Supports both local and remote AI providers»; сайт legacy: TTS/STT облачные и offline варианты |
| RAG | UNKNOWN | Не заявлен |
| История / логирование | PARTIAL | Recent discussion context; полного аудит-лога в источниках нет |
| Трейсинг | UNKNOWN | Не найден |
| HITL | PARTIAL | Controlled mode как детерминированная альтернатива агентной; формального approval-гейта нет |
| Песочница / безопасность | PARTIAL | Privacy/local-first, «you decide if you wish to make use of any third party»; но path traversal в `/download` (см. §4) |
| Аутентификация | UNKNOWN | Не раскрыта |
| API / расширяемость | SUPPORTED | HTTP API в `server/`, skills/toolkits/bridges/binaries |
| UI | SUPPORTED | Web app `app/` + Aurora UI components, голос (TTS/STT) |
| Импорт / экспорт | UNKNOWN | Не подтверждено |
| Тестирование | UNKNOWN | Не найдено |
| Production | PARTIAL | Developer Preview 2.0; legacy master стабильнее, но архитектурно устарел |
| Offline | SUPPORTED | Сайт: Leon может общаться offline для приватности; локальные модели и offline TTS/STT (Flite, Coqui STT на legacy-сайте) |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Три режима исполнения: smart / controlled (детерминированные native skills) / agent (пошаговое планирование) | [README develop](https://github.com/leon-ai/leon) | Официальная документация |
| Слоёная память + компактная self-model + bounded proactive pulse без заваливания контекста | [README develop](https://github.com/leon-ai/leon) | Официальная документация |
| Computer use с верификацией визуального исхода, progressive disclosure инструментов | [README develop](https://github.com/leon-ai/leon) | Официальная документация |
| Приватность: локальные модели и контекст, опциональность третьих сторон | [README develop](https://github.com/leon-ai/leon), [getleon.ai](https://getleon.ai/) | Официальные заявления |
| Голос: несколько TTS/STT, включая offline (Flite, Coqui STT) | [getleon.ai](https://getleon.ai/) | Официальный сайт (legacy-описание) |
| Долгая история проекта: с 2017/2019, сообщество и спонсоры | [README develop](https://github.com/leon-ai/leon), [story](https://blog.getleon.ai/the-story-behind-leon/) | Официальные материалы |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Path traversal в команде `/download` модели позволяет запись произвольного файла | Высокая | В списке issues | [#593](https://github.com/leon-ai/leon/issues/593) |
| Документация 2.0 не готова, публичные гайды отстают от ядра; репозиторий — единственный источник истины | Средняя | Признано проектом | [README develop](https://github.com/leon-ai/leon) |
| Разработка в основном в свободное время, прогресс неравномерный, онбординг контрибьюторов ограничен | Средняя | Признано проектом | [README develop](https://github.com/leon-ai/leon) |
| Хрупкость установки/рантайма legacy-стека: TCP server, python bridge, llama.cpp, GLIBC | Средняя | Серия issues | [#554](https://github.com/leon-ai/leon/issues/554), [#546](https://github.com/leon-ai/leon/issues/546), [#543](https://github.com/leon-ai/leon/issues/543), [#534](https://github.com/leon-ai/leon/issues/534) |
| Скачивание Windows TCP server архива обрывается/повреждено | Низкая | Открыт | [#555](https://github.com/leon-ai/leon/issues/555) |
| «Can't talk to leon, no skills work» — пользовательский тупик без диагностики | Средняя | Открыт | [#519](https://github.com/leon-ai/leon/issues/519) |
| Исторически: «The docs are really confusing» | Низкая | Старый issue, релевантен на фоне отсутствия docs 2.0 | [#295](https://github.com/leon-ai/leon/issues/295) |

## 5. Незакрытые потребности пользователей (Issues/Discussions/Reddit)

| Потребность / боль | Источник |
|---|---|
| Опциональный внешний провайдер памяти (MCP) за persistent/daily/discussion слоями | [#577](https://github.com/leon-ai/leon/issues/577) |
| Memory Importance Scoring и автоматический decay памяти | [#575](https://github.com/leon-ai/leon/issues/575) |
| Поддержка Ollama как OpenAI-compatible backend | [#566](https://github.com/leon-ai/leon/issues/566) |
| FunASR как опция ASR-парсера | [#570](https://github.com/leon-ai/leon/issues/570) |
| Keyless Parallel Search MCP в `search_web` | [#590](https://github.com/leon-ai/leon/issues/590) |
| Простой путь установки для новичков («How do i download this») | [#557](https://github.com/leon-ai/leon/issues/557) |
| Reddit-истоки проекта: тизеры и фидбэк r/Python и r/javascript (исторический спрос на self-hosted ассистента) | [блог со ссылками на Reddit](https://blog.getleon.ai/the-story-behind-leon/), [r/Python teaser](https://www.reddit.com/r/Python/comments/85axig/im_building_l%C3%A9on_an_opensource_personal_assistant/) |

## 6. Технические ограничения

| Аспект | Оценка |
|---|---|
| Модель исполнения | Гибрид: детерминированные native skills (Skills→Actions→Tools→Functions→Binaries) + LLM agent mode; роутер smart mode выбирает путь |
| Детерминированность | Controlled mode детерминирован; agent mode — LLM-driven. Smart-роутинг — ближайший аналог Tier-роутинга, но без дешёвых Tier0/Tier1 классификаторов в источниках |
| Параллелизм | UNKNOWN |
| Отмена / таймауты | UNKNOWN в источниках |
| Идемпотентность | Не заявлена |
| Восстановление | Заявлен recovery на уровне поведения; механизмов checkpoint/resume в источниках нет — UNKNOWN |
| Версионирование | Skills как пакеты; версионирование памяти/контекста — предмет feature requests (#575, #577) |

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** Leon — самый близкий к CyberPipe по позиционированию (локальный персональный ассистент, приватность, голос, память), и одновременно самый незрелый инженерно на дату проверки: 2.0 Developer Preview без документации, разработка в свободное время, хрупкий legacy-стек. Ценность для CyberPipe — продуктовые идеи режимов и памяти, а не код.

Применимость к CyberPipe (.NET 10, Telegram+Avalonia, Tier0 regex/Tier1 ONNX/Tier2-3 LLM, TokenBudgetGovernor, Netrunner DAG, SQLite WAL, Brainstem sandbox, HITL):

- **Заимствовать:** триаду режимов как UX-объяснение роутинга — controlled ≈ Tier0/Tier1 + детерминированные Netrunner-сценарии, agent ≈ Tier2/Tier3 с LlmProcessNode; слоистую память (durable preferences / daily / discussion) поверх SQLite WAL; bounded proactive pulse → проактивные Netrunner-триггеры с жёстким TokenBudgetGovernor; progressive disclosure инструментов (не тащить весь tool surface в каждый ход).
- **Не заимствовать:** голосовой стек и Node/Python bridges — у CyberPipe Telegram+Avalonia и .NET 10; отсутствие sandbox-дисциплины (path traversal #593) — для Brainstem обязательны allowlist путей и HITL на запись файлов.
- **Память:** запросы сообщества Leon на importance scoring/decay (#575) и внешний MCP-провайдер памяти (#577) подтверждают спрос — CyberPipe может заложить скоринг/устаревание памяти сразу в схему SQLite, а не как послесловие.
- **Каналы и offline:** Leon силён в offline/voice, CyberPipe — в Telegram; это взаимодополняющие референсы: offline-first принципы Leon применимы к локальным Tier0/Tier1 без сети.
