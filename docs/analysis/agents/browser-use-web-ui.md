# Browser Use Web UI (browser-use-web-ui)

> Дата проверки: 2026-10-19. Источники: только реально проверенные через web_search/web_fetch URL. Где доказательств недостаточно — статус `UNKNOWN`. Цитаты и URL не выдумываются.

> Уточнение имени: канонический репозиторий на дату проверки — [browser-use/web-ui](https://github.com/browser-use/web-ui) (ранее известен как browser-use-web-ui). Ниже анализ именно его.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Browser Use Web UI (web-ui) |
| Официальный сайт | Отдельного сайта нет; страница — репозиторий и [docs.browser-use.com](https://docs.browser-use.com) |
| Репозиторий | [github.com/browser-use/web-ui](https://github.com/browser-use/web-ui) |
| Документация | README репозитория + [docs.browser-use.com](https://docs.browser-use.com) |
| Категория | Gradio веб-интерфейс поверх библиотеки browser-use |
| Назначение | «Run AI Agent in your browser» — дружелюбный UI для взаимодействия с браузерным агентом без кода |
| Аудитория | Пользователи и демо-сценарии: запустить веб-агента из браузера, посмотреть процесс вживую |
| Статус на 2026-10-19 | Поддерживается, но отстаёт по версиям от ядра browser-use (см. критику); релизы с хотфиксами совместимости |
| Лицензия | MIT (файл LICENSE, Copyright Browser Use Inc.; проверен raw) |
| Развёртывание | Локально: Python 3.11 (`uv venv`), `playwright install`, `.env` с ключами, `python webui.py --ip 127.0.0.1 --port 7788`; есть Docker-путь |

## 2. Возможности

Наследует возможности ядра [browser-use](https://github.com/browser-use/browser-use); ниже — только то, что добавляет/ограничивает сам Web UI. Полная таблица ядра — в файле browser-use.md.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Через ядро: задача → автономный прогон из UI |
| Планирование | PARTIAL | Только планирование LLM внутри прогона |
| Долговременная память | UNSUPPORTED | Памяти между задачами нет |
| Контекст сессии | SUPPORTED | Persistent Browser Sessions: окно браузера можно держать открытым между задачами, видна история/состояние |
| Персистентность | PARTIAL | Сессия браузера и профиль; персистентности задач/результатов как данных нет |
| Инструменты / function-calling | PARTIAL | Поддерживает большинство функций browser-use (README); кастомные Tools из UI — `UNKNOWN` |
| Навыки / плагины | UNSUPPORTED | Скиллов/плагинов UI не добавляет |
| Мультиагентность | UNSUPPORTED | Один прогон за раз из UI |
| Workflow / DAG | UNSUPPORTED | Нет |
| Ветвления / циклы | PARTIAL | Только цикл агента ядра |
| Обработка ошибок / повторы / восстановление | PARTIAL | Наследует пробелы ядра (нет восстановления после краха вкладки) |
| Расписание / триггеры | UNSUPPORTED | Нет |
| Интеграции | PARTIAL | LLM-провайдеры через `.env`; интеграций уровня платформы нет |
| Файлы | PARTIAL | Через браузерные действия ядра; deep-research сценарий пишет файлы (см. уязвимость в критике) |
| Терминал / исполнение кода | UNSUPPORTED | Нет |
| Браузер / GUI | SUPPORTED | Свой или управляемый браузер, HD-запись экрана, VNC-сценарий (README/releases) |
| Мессенджеры | UNSUPPORTED | Нет |
| Локальные + облачные LLM | SUPPORTED | Google, OpenAI, Azure OpenAI, Anthropic, DeepSeek, Ollama и др. (README) |
| RAG | UNSUPPORTED | Нет |
| История / логирование | PARTIAL | Визуальная история в сессии браузера; аудит-лог прогонов — `UNKNOWN` |
| Трейсинг | UNKNOWN | Не подтверждён |
| HITL | UNSUPPORTED | Подтверждений в цикле нет; пользователь только наблюдает |
| Песочница / безопасность | PARTIAL | Изоляции нет сверх браузера; известна path traversal в deep_research_agent (issue #619) |
| Аутентификация | SUPPORTED | Свой браузер с существующими логинами (`BROWSER_PATH`/`BROWSER_USER_DATA`) — не нужно перелогиниваться |
| API / расширяемость | UNSUPPORTED | Программного API у самого UI нет; расширяемость — только кодом |
| UI | SUPPORTED | Gradio UI — единственное назначение проекта |
| Импорт / экспорт | UNKNOWN | Не подтверждено |
| Тестирование | UNKNOWN | Не подтверждено |
| Production-готовность | UNSUPPORTED | Демо/локальный инструмент: ручной `.env`, один пользователь, без auth-контура |
| Офлайн | PARTIAL | Ollama + локальный браузер возможны, но есть баг инициализации Ollama (issue #658) |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Gradio UI покрывает большинство функций browser-use и дружелюбен к нетехническим пользователям | [README (raw)](https://raw.githubusercontent.com/browser-use/web-ui/main/README.md) | Официальный README |
| Свой браузер: без повторных логинов, с HD-записью экрана | [README (raw)](https://raw.githubusercontent.com/browser-use/web-ui/main/README.md) | Официальный README |
| Persistent-сессии: окно живёт между задачами, видна полная история взаимодействий | [README (raw)](https://raw.githubusercontent.com/browser-use/web-ui/main/README.md) | Официальный README |
| Широкая поддержка LLM из коробки, включая локальный Ollama | [README (raw)](https://raw.githubusercontent.com/browser-use/web-ui/main/README.md) | Официальный README |
| MIT, простой локальный запуск на порту 7788 | [README (raw)](https://raw.githubusercontent.com/browser-use/web-ui/main/README.md), [LICENSE (raw)](https://raw.githubusercontent.com/browser-use/web-ui/main/LICENSE) | Официальные README/лицензия |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Web UI по умолчанию уходит в OpenAI, игнорируя `LLM_PROVIDER=ollama` при рабочем локальном Ollama, без явной ошибки в логе | Средняя | Открытый баг | [Issue #658](https://github.com/browser-use/web-ui/issues/658) |
| Path traversal в `deep_research_agent.py`: `save_dir` без валидации используется в файловых операциях | Высокая | Заявлена в issue (advisory-оценка) | [Issue #619](https://github.com/browser-use/web-ui/issues/619) |
| Отставание версии browser-use от актуальной (0.1.x в UI при 0.7.x ядра на момент issue) | Средняя | Открытый issue | [Issue #689](https://github.com/browser-use/web-ui/issues/689) |
| Регрессии вкладок в связке UI+ядро (нельзя открыть дополнительные вкладки) | Средняя | Исторический баг связки | [Issue #1151](https://github.com/browser-use/browser-use/issues/1151) |
| Нет auth/HITL: любой с доступом к порту UI управляет браузером с вашими логинами | Средняя | Вывод из README (auth не заявлена) — помечено как вывод, не цитата | [README (raw)](https://raw.githubusercontent.com/browser-use/web-ui/main/README.md) |

## 5. Нереализованные потребности пользователей

| Потребность | Источник (конкретный URL) |
|---|---|
| Корректная инициализация локального Ollama по `.env` без тихого фолбэка в OpenAI | [Issue #658](https://github.com/browser-use/web-ui/issues/658) |
| Синхронизация версии ядра browser-use в Web UI | [Issue #689](https://github.com/browser-use/web-ui/issues/689) |
| Безопасная работа с путями сохранения в deep research | [Issue #619](https://github.com/browser-use/web-ui/issues/619) |
| Reddit/Discussions в этом прогоне отдельно не открывались — потребности оттуда `UNKNOWN` | — |

## 6. Технические ограничения

- **Модель исполнения:** Gradio-процесс оборачивает асинхронный цикл ядра; один UI — один активный прогон.
- **Детерминизм:** отсутствует (LLM + вёрстка).
- **Параллелизм:** не заявлен; конкурентные прогоны из UI — `UNKNOWN`, по архитектуре Gradio-демо не предполагаются.
- **Отмена/таймауты:** остановка вручную закрытием/прерыванием; контрактных таймаутов шагов нет.
- **Идемпотентность:** не гарантируется (наследует ядро).
- **Восстановление:** persistent-окно браузера переживает задачу, но восстановления прогона после сбоев нет.
- **Версионирование:** жёсткая связка версий UI ↔ browser-use; отставание — системный риск (issue #689).

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** удобная витрина браузерного агента для человека, но не production-интерфейс и не компонент ассистента.

Для CyberPipe полезен как референс UX: живое окно браузера + persistent-сессия + запись экрана — хороший паттерн наблюдения за браузерным SkillNode из Avalonia/Telegram («покажи, что делаешь»).

Не переносить: Gradio-стек (у CyberPipe Avalonia + Telegram), отсутствие auth/HITL (в CyberPipe обязателен HitlApprovalNode перед необратимыми веб-действиями), тихое поведение при ошибке провайдера (issue #658 — антипаттерн для TokenBudgetGovernor: фолбэк должен быть явным и залогированным).

**Применимость:** только UX-референс наблюдения за браузером; код и рантайм не переиспользовать.

---
*Проверено: 2026-10-19. Неподтверждённое помечено `UNKNOWN`.*
