# AutoGPT (autogpt)

> Дата проверки: 2026-10-19. Источники: только реально проверенные через web_search/web_fetch URL. Где доказательств недостаточно — статус `UNKNOWN`. Цитаты и URL не выдумываются.

> Уточнение предмета: современный AutoGPT — это платформа (visual builder, блоки, расписание/триггеры, Marketplace) в `autogpt_platform/`. Оригинальный автономный агент 2023 года сохранён как AutoGPT Classic в `classic/`. Ниже — платформа, Classic помечен отдельно.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | AutoGPT |
| Официальный сайт | [agpt.co](https://agpt.co); платформа — [platform.agpt.co](https://platform.agpt.co) |
| Репозиторий | [github.com/Significant-Gravitas/AutoGPT](https://github.com/Significant-Gravitas/AutoGPT) |
| Документация | [docs.agpt.co](https://docs.agpt.co) |
| Категория | Платформа построения и запуска AI-агентов: AutoPilot (агент из описания), visual builder, Marketplace |
| Назначение | «Describe what you want done. AutoGPT builds the agent, runs it, and reports back» (README) |
| Аудитория | Нетехнические пользователи (managed Platform) и команды, self-hostящие платформу |
| Статус на 2026-10-19 | Активно развивается; 185 000+ звёзд (README); single-container дистрибутив в работе, Windows — пока ручной self-hosting гайд |
| Лицензия | Смешанная: `autogpt_platform/` — PolyForm Shield (бесплатно для личного/внутреннего использования, нельзя продавать как конкурирующий hosted-сервис); `classic/` и остальное — MIT (таблица лицензий в README) |
| Развёртывание | Managed Platform (платная подписка + usage) или self-hosted (Docker, свои API-ключи, без лицензионного платежа) |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Агенты выполняют полные workflow и отчитываются (README); AutoPilot строит агента из описания |
| Планирование | PARTIAL | AutoPilot генерирует граф из текста; формального отдельного planner-модуля вне LLM не показано |
| Долговременная память | PARTIAL | Состояние агентов/запусков на платформе; семантическая долговременная память агента — `UNKNOWN` |
| Контекст сессии | SUPPORTED | Дашборд Agents: агенты, запуски, стоимость, действия, требующие внимания |
| Персистентность | SUPPORTED | Платформенное хранение графов, запусков, секретов |
| Инструменты / function-calling | SUPPORTED | Блоки (blocks) как переиспользуемые инструменты; сотни моделей, 45+ платформ-интеграций |
| Навыки / плагины | SUPPORTED | Marketplace готовых агентов, skills seed/catalog (по коммитам репозитория); детали каталога — частично `UNKNOWN` |
| Мультиагентность | PARTIAL | Несколько агентов на аккаунте; агент-агент оркестрация внутри одного графа — `UNKNOWN` |
| Workflow / DAG | SUPPORTED | Visual builder: граф из блоков — ближайший аналог Mini-n8n среди сравниваемых |
| Ветвления / циклы | PARTIAL | Граф блоков предполагает ветвления; семантика циклов/условий в проверенных источниках детально не раскрыта |
| Обработка ошибок / повторы / восстановление | PARTIAL | Известен класс проблем Classic с зацикливаниями и обработкой ошибок git (issue #3320, вторичный разбор); платформенные retry-гарантии — `UNKNOWN` |
| Расписание / триггеры | SUPPORTED | Запуск on demand, по расписанию или от триггера (README) |
| Интеграции | SUPPORTED | Gmail, Google Calendar/Docs/Sheets, GitHub, Slack, Discord, Notion, HubSpot, Linear, Airtable, Jira, Salesforce, Stripe, Webflow и др. (README) |
| Файлы | PARTIAL | Через блоки интеграций; локальной файловой песочницы не заявлено |
| Терминал / исполнение кода | PARTIAL | В Classic — да; в платформе — через блоки, детали `UNKNOWN` |
| Браузер / GUI | PARTIAL | Visual builder UI; браузерная автоматизация как у Browser Use не является ядром |
| Мессенджеры | PARTIAL | Discord/Slack как интеграции; Telegram в списке README не заявлен — `UNKNOWN` |
| Локальные + облачные LLM | PARTIAL | Сотни моделей на платформе; BYO-ключи при self-host; локальные модели (Ollama) в проверенных источниках прямо не подтверждены |
| RAG | UNKNOWN | В проверенных источниках RAG как фича платформы не подтверждён |
| История / логирование | SUPPORTED | Дашборд запусков и стоимости |
| Трейсинг | PARTIAL | Мониторинг запусков; распределённый трейсинг — `UNKNOWN` |
| HITL | PARTIAL | «Actions that need your attention» на дашборде; AutoPilot-workflow встаёт на паузу перед необратимым действием (коммит репозитория про irreversible marker) |
| Песочница / безопасность | PARTIAL | Секреты платформы, генерация секретов на установку при self-host; sandbox-исполнения уровня Brainstem не заявлено |
| Аутентификация | SUPPORTED | Аккаунты платформы; при self-host — своя конфигурация |
| API / расширяемость | SUPPORTED | Блоки, Marketplace, интеграции; публичный API детали — частично `UNKNOWN` |
| UI | SUPPORTED | Веб-UI: AutoPilot-чат, builder, дашборд, Marketplace |
| Импорт / экспорт | PARTIAL | Агенты из Marketplace в библиотеку; экспорт графов — `UNKNOWN` |
| Тестирование | PARTIAL | Classic: agbenchmark; тестирование платформенных графов — `UNKNOWN` |
| Production-готовность | SUPPORTED | Managed Platform как production-путь; self-host требует своей эксплуатации |
| Офлайн | UNSUPPORTED | Платформа и модели облачные; офлайн-сценарий не заявлен |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Четыре поверхности одной платформы: AutoPilot, Agents-дашборд, Marketplace, builder | [README (raw)](https://raw.githubusercontent.com/Significant-Gravitas/AutoGPT/master/README.md) | Официальный README |
| Visual builder + запуск по расписанию/триггерам — полноценный workflow-продукт, а не только чат | [README (raw)](https://raw.githubusercontent.com/Significant-Gravitas/AutoGPT/master/README.md) | Официальный README |
| 45+ интеграций и сотни моделей из коробки | [README (raw)](https://raw.githubusercontent.com/Significant-Gravitas/AutoGPT/master/README.md) | Официальный README |
| Честная модель open-core: self-host бесплатен, платится managed-удобство; лицензионная граница прописана таблицей | [README (raw)](https://raw.githubusercontent.com/Significant-Gravitas/AutoGPT/master/README.md) | Официальный README |
| Пауза перед необратимым действием в AutoPilot-workflow (HITL-паттерн) | Коммит репозитория (через поиск): workflows pause before an irreversible action | Вторичное подтверждение по истории коммитов; в README дословно не зафиксировано |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Classic: зацикливания и плохая обработка ошибок (git-команды «go into loops and doesn't process errors properly») | Высокая (Classic) | Исторически подтверждено; к платформе применимость `UNKNOWN` | [Issue #3320](https://github.com/Significant-Gravitas/AutoGPT/issues/3320) (через вторичный разбор awesome-agent-failures) |
| AutoPilot-ребра в динамические dict-выходы молча теряются в builder; граф confusing/uneditable при рабочем исполнении | Средняя | Открытый баг платформы | [Issue #13409](https://github.com/Significant-Gravitas/AutoGPT/issues/13409) |
| Пользовательский нарратив «красиво, но не работает», дорогие петли расхода API (Classic-эпоха) | Средняя | Мнение сообщества, не официальный баг | [Discussion #3754](https://github.com/Significant-Gravitas/AutoGPT/discussions/3754), [Medium-разбор](https://medium.com/@real.busstechno/autogpt-verdict-not-autonomous-not-user-friendly-limited-usability-fb65903e0268) |
| PolyForm Shield запрещает конкурирующий hosted-сервис на коде платформы | Средняя (для переиспользования) | Действующая лицензия | [README (raw)](https://raw.githubusercontent.com/Significant-Gravitas/AutoGPT/master/README.md) |
| Self-host на Windows — только ручной гайд, single-container installer ещё не выпущен | Низкая | Заявлено в README | [README (raw)](https://raw.githubusercontent.com/Significant-Gravitas/AutoGPT/master/README.md) |

## 5. Нереализованные потребности пользователей

| Потребность | Источник (конкретный URL) |
|---|---|
| Надёжная обработка ошибок и разрыв петель в автономном исполнении (Classic) | [Issue #3320](https://github.com/Significant-Gravitas/AutoGPT/issues/3320) |
| Корректное отображение AutoPilot-графов с динамическими выходами в builder | [Issue #13409](https://github.com/Significant-Gravitas/AutoGPT/issues/13409) |
| «Работающая», а не только красивая автономия (общий запрос сообщества) | [Discussion #3754](https://github.com/Significant-Gravitas/AutoGPT/discussions/3754) |
| Reddit r/AutoGPT потоки про бесконечные петли и стоимость — в этом прогоне дословно не открывались, детали `UNKNOWN` | [r/AutoGPT](https://www.reddit.com/r/AutoGPT/) — ссылка на сообщество из вторичных разборов, содержание тредов не верифицировано |

## 6. Технические ограничения

- **Модель исполнения:** платформенный граф блоков (DAG workflow) + LLM-шаги; Classic — автономный цикл «цель → задачи → исполнение».
- **Детерминизм:** LLM-шаги недетерминированы; детерминированы только не-LLM блоки.
- **Параллелизм:** `UNKNOWN` по проверенным источникам (параллельные ветки графа не подтверждены дословно).
- **Отмена/таймауты:** пауза перед необратимым действием есть; общий cancel/timeout контракт графа — `UNKNOWN`.
- **Идемпотентность:** не заявлена; повтор запуска может дублировать внешние эффекты интеграций.
- **Восстановление:** `UNKNOWN`; дашборд показывает запуски, resume после падения не подтверждён.
- **Версионирование:** графы имеют версии (v1 в issue #13409); версионирование как пользовательская фича — PARTIAL.

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** ближайший аналог Netrunner Mini-n8n по идее (блоки-граф + расписание + триггеры + Marketplace), но облачная платформа, а не локальный ассистент.

Для CyberPipe полезно: модель блоков с типизированными входами/выходами (урок issue #13409 — динамические выходы должны честно отображаться в builder), пауза перед необратимым действием как HitlApprovalNode, дашборд «запуски + стоимость + требует внимания» как аналог TokenBudgetGovernor-отчётности в Telegram/Avalonia.

Не переносить: PolyForm-ограничение (для CyberPipe как личного проекта некритично, но переиспользование кода платформы ограничено), облачную зависимость, Classic-цикл без бюджета (TokenBudgetGovernor в CyberPipe — прямое лекарство от «дорогих петель» AutoGPT).

**Применимость:** главный референс по DAG-builder UX и HITL-паузам; рантайм и стек (.NET/SQLite WAL/Brainstem) CyberPipe не заменяет.

---
*Проверено: 2026-10-19. Неподтверждённое помечено `UNKNOWN`.*
