# Browser Use (browser-use)

> Дата проверки: 2026-10-19. Источники: только реально проверенные через web_search/web_fetch URL. Где доказательств недостаточно — статус `UNKNOWN`. Цитаты и URL не выдумываются.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Browser Use |
| Официальный сайт | [browser-use.com](https://browser-use.com) |
| Репозиторий | [github.com/browser-use/browser-use](https://github.com/browser-use/browser-use) |
| Документация | [docs.browser-use.com](https://docs.browser-use.com) |
| Категория | Open-source Python/TypeScript библиотека браузерных агентов + Browser Use Cloud (stealth-браузеры, hosted Agent API) |
| Назначение | «Navigate the web like a human does» — LLM управляет реальным браузером: навигация, формы, CAPTCHA-сценарии, бронирования (README, демо) |
| Аудитория | Разработчики, встраивающие браузерную автоматизацию; команды, покупающие Cloud вместо своей инфраструктуры |
| Статус на 2026-10-19 | Активно развивается; Cloud API V4 — рекомендуемая для новых интеграций, V3 — быстрее/дешевле, V2 — legacy (docs llms.txt) |
| Лицензия | MIT (файл LICENSE, Copyright Gregor Zunic; проверен raw). Cloud — отдельные Terms/Privacy |
| Развёртывание | Локально: Python-библиотека + локальный Chromium/Chrome-профиль; или Browser Use Cloud (`Browser(use_cloud=True)`), stealth от $0.02/браузер-час |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | `Agent(task=...).run()` — автономный цикл по задаче (README) |
| Планирование | PARTIAL | Планирование внутри цикла LLM; внешнего planner/DAG нет |
| Долговременная память | UNSUPPORTED | Долговременной памяти между задачами в библиотеке не заявлено; профили браузера — не память агента |
| Контекст сессии | SUPPORTED | История шагов `history.final_result()`, сессия браузера |
| Персистентность | PARTIAL | Профили/куки Cloud и локального Chrome; состояние задачи между запусками — нет |
| Инструменты / function-calling | SUPPORTED | `Tools` + `@tools.action(...)` — кастомные действия (README) |
| Навыки / плагины | PARTIAL | Skills-экосистема вокруг harness; в самой библиотеке — tools, не skills-маркетплейс |
| Мультиагентность | UNSUPPORTED | Один агент на задачу; мультиагентной оркестрации в проверенных источниках нет |
| Workflow / DAG | UNSUPPORTED | DAG нет; линейный агентный цикл |
| Ветвления / циклы | PARTIAL | Только цикл агента и повторы шагов |
| Обработка ошибок / повторы / восстановление | PARTIAL | Известный пробел: нет автовосстановления после краха вкладки — агент зависает (issue #5067) |
| Расписание / триггеры | UNSUPPORTED | В библиотеке нет; только внешний вызывающий код |
| Интеграции | PARTIAL | Через кастомные Tools и Cloud API |
| Файлы | PARTIAL | Загрузка/скачивание через браузерные действия; файловой подсистемы агента нет |
| Терминал / исполнение кода | UNSUPPORTED | Код не исполняет; только браузер |
| Браузер / GUI | SUPPORTED | Ядро продукта: CDP/Playwright-управление, stealth, профили |
| Мессенджеры | UNSUPPORTED | Нет |
| Локальные + облачные LLM | SUPPORTED | `ChatBrowserUse`, любые провайдеры, Ollama локально (README FAQ) |
| RAG | UNSUPPORTED | Нет |
| История / логирование | SUPPORTED | История прогона, записи сессий в Cloud |
| Трейсинг | PARTIAL | События Cloud API (V4 events); локальный трейсинг — `UNKNOWN` |
| HITL | UNSUPPORTED | Подтверждений человеком в цикле в проверенных источниках не заявлено |
| Песочница / безопасность | PARTIAL | Изоляция браузера/профилей; песочницы исполнения кода нет (кода нет); CAPTCHA обход — без гарантий (README FAQ) |
| Аутентификация | SUPPORTED | Переиспользование Chrome-профиля (`Browser.from_system_chrome()`), Cloud profile sync (куки; без localStorage/IndexedDB/расширений) |
| API / расширяемость | SUPPORTED | Python API, Tools, Cloud Agent API V4/V3 |
| UI | UNSUPPORTED | UI нет (это библиотека); UI — отдельный проект web-ui |
| Импорт / экспорт | PARTIAL | Profile sync; экспорт истории прогона программно |
| Тестирование | PARTIAL | Benchmark-репозиторий; юнит-тестирования пользовательских агентов как фичи нет |
| Production-готовность | SUPPORTED | Два production-пути по README: свои браузеры + Cloud browsers или fully hosted Cloud API |
| Офлайн | PARTIAL | Локальный браузер + Ollama возможны; большинство сценариев требуют сети по определению |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Бесплатная MIT-библиотека, платно только инференс и hosted-браузеры | [README (raw)](https://raw.githubusercontent.com/browser-use/browser-use/main/README.md), [LICENSE (raw)](https://raw.githubusercontent.com/browser-use/browser-use/main/LICENSE) | Официальные README/лицензия |
| Кастомные инструменты одной декораторной функцией | [README (raw)](https://raw.githubusercontent.com/browser-use/browser-use/main/README.md) | Официальный README (пример кода) |
| Аутентификация переиспользуемым реальным Chrome-профилем | [README (raw)](https://raw.githubusercontent.com/browser-use/browser-use/main/README.md) | Официальный README FAQ |
| Честная оговорка: ни одна конфигурация не гарантирует обход каждой CAPTCHA | [README (raw)](https://raw.githubusercontent.com/browser-use/browser-use/main/README.md) | Официальный README FAQ |
| Понятная production-лесенка: библиотека → Cloud browsers → hosted Agent API | [README (raw)](https://raw.githubusercontent.com/browser-use/browser-use/main/README.md), [docs](https://docs.browser-use.com) | Официальные README/docs |
| Прозрачная экономика Cloud: concurrency-тиражи, $0.02/браузер-час, лимиты в llms.txt | [docs llms.txt](https://docs.browser-use.com/llms.txt) | Официальные docs |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Нет автовосстановления после краха вкладки: агент зависает в ожидании состояния страницы | Высокая | Открытый баг | [Issue #5067](https://github.com/browser-use/browser-use/issues/5067) |
| `<span>`/`<label>` не детектятся как интерактивные — ломается Ant Design и подобные UI | Средняя | Открытый баг | [Issue #3742](https://github.com/browser-use/browser-use/issues/3742) |
| Агент не переключается на новую вкладку после click | Средняя | Открытый баг (через вторичный разбор failure-кейсов) | [Issue #3657](https://github.com/browser-use/browser-use/issues/3657) |
| Невозможно открыть дополнительные вкладки в связке с Web-UI (регрессия версий) | Средняя | Исторический баг связки | [Issue #1151](https://github.com/browser-use/browser-use/issues/1151) |
| Profile sync переносит только куки: localStorage/IndexedDB/расширения теряются, часть сайтов требует повторного логина | Низкая | Задокументированное ограничение | [README (raw)](https://raw.githubusercontent.com/browser-use/browser-use/main/README.md) |

## 5. Нереализованные потребности пользователей

| Потребность | Источник (конкретный URL) |
|---|---|
| Автовосстановление после краха страницы вместо зависания | [Issue #5067](https://github.com/browser-use/browser-use/issues/5067) |
| Поддержка Ant Design/Material-подобных кликабельных `<span>`/`<label>` | [Issue #3742](https://github.com/browser-use/browser-use/issues/3742) |
| Надёжное открытие/переключение вкладок | [Issue #3657](https://github.com/browser-use/browser-use/issues/3657), [Issue #1151](https://github.com/browser-use/browser-use/issues/1151) |
| Reddit/Discussions в этом прогоне отдельно не открывались — потребности оттуда `UNKNOWN` | — |

## 6. Технические ограничения

- **Модель исполнения:** асинхронный Python-цикл: LLM → действие браузера → наблюдение; один агент — одна задача.
- **Детерминизм:** отсутствует; зависит от LLM и вёрстки сайтов.
- **Параллелизм:** локально — на вызывающем коде; в Cloud — concurrency-лимиты по тирам (10 бесплатно, далее платные пороги $100/$1000/$5000/$25000 → 50/250/500/1000).
- **Отмена/таймауты:** V4-браузер живёт ~2 мин после активности (keepAlive 120–14400 c); программного cancel-контракта шагов в проверенных источниках — `UNKNOWN`.
- **Идемпотентность:** не гарантируется; повтор шага может повторить отправку формы.
- **Восстановление:** после краха вкладки восстановления нет (issue #5067).
- **Версионирование:** API версионируется (V4/V3/V2 legacy); версионирования задач нет.

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** лучшая открытая библиотека именно браузерного действия, но это один инструмент, а не ассистент.

Для CyberPipe: кандидат на реализацию браузерного NetrunnerScriptNode/SkillNode (веб-шаги внутри Mini-n8n DAG), паттерн `Tools.action` близок к Netrunner skills, а честные лимиты Cloud полезны как модель для TokenBudgetGovernor в браузерных задачах.

Риски для CyberPipe: зависание без восстановления (нужен свой timeout + watchdog в Brainstem), недетерминированность вёрстки, отсутствие HITL — необратимые веб-действия (отправка форм, оплата) должны идти через HitlApprovalNode до, а не после шага.

**Применимость:** встраивать как изолированный браузерный исполнитель внутри DAG, не как ядро ассистента.

---
*Проверено: 2026-10-19. Неподтверждённое помечено `UNKNOWN`.*
