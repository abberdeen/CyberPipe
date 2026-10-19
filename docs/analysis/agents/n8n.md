# n8n — анализ

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | n8n |
| Официальный сайт / документация | [n8n.io](https://n8n.io), документация — [docs.n8n.io](https://docs.n8n.io/) |
| Репозиторий | [github.com/n8n-io/n8n](https://github.com/n8n-io/n8n) |
| Категория | Визуальная автоматизация рабочих процессов (workflow automation) с нативными AI-возможностями |
| Назначение | Соединение сервисов нодами в граф: триггеры, трансформации, интеграции, AI-агентные ноды |
| Целевая аудитория | Технические команды, ops/автоматизаторы, разработчики интеграций; self-host и cloud |
| Статус на 2026-10-19 | Активно развивается; в проверенных результатах видны релизы ветки 2.x (например 2.38, 2026-09-01) — [github.com/n8n-io/n8n-docs — changelog](https://github.com/n8n-io/n8n-docs/blob/HEAD/docs/changelog/README.md) |
| Лицензия | Fair-code, Sustainable Use License + n8n Enterprise License; сам проект подчёркивает, что не называет себя open source: [github.com/n8n-io/n8n/blob/master/LICENSE.md](https://github.com/n8n-io/n8n/blob/master/LICENSE.md), разбор — [getorchestra.io/blog/n8n-vs-airflow](https://www.getorchestra.io/blog/n8n-vs-airflow) |
| Развёртывание | Self-host (Docker Compose, cloud-провайдеры, one-line setup) или n8n Cloud — [docs.n8n.io](https://docs.n8n.io/) |

Официальное определение: «fair-code licensed workflow automation tool that combines AI features with business process automation» — [docs.n8n.io](https://docs.n8n.io/).

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE. Приведены только применимые строки.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность агентов | PARTIAL | AI Agent ноды есть (пример «AI agent chat» в официальных воркфлоу), но центр продукта — детерминированные графы, а не автономные агенты — [docs.n8n.io](https://docs.n8n.io/) |
| Планирование | PARTIAL | Планирование выполняет человек в редакторе графа; LLM-планировщика уровня фреймворков в проверенных страницах нет |
| Долговременная память | PARTIAL | Через внешние хранилища и ноды интеграций; встроенной агентной памяти уровня фреймворков в проверенных страницах не выделено |
| Сессионный контекст | PARTIAL | Данные текут между нодами внутри execution; межсессионный контекст — через внешние БД |
| Персистентность | SUPPORTED | Executions сохраняются в БД; есть режимы хранения execution data, включая проблемы queue mode — [Issue #29555](https://github.com/n8n-io/n8n/issues/29555) |
| Инструменты / function-calling | SUPPORTED | AI-ноды вызывают инструменты/ноды; MCP: n8n выступает MCP-сервером для Claude Desktop/Claude Code — [docs.n8n.io](https://docs.n8n.io/) |
| Навыки / плагины | SUPPORTED | Community nodes и кастомные ноды; каталог интеграций — [n8n.io/integrations](https://n8n.io/integrations) |
| Мультиагентность | PARTIAL | Несколько AI Agent нод в одном графе; первоклассной мультиагентной оркестрации в проверенных источниках нет |
| Workflow / DAG | SUPPORTED | Ядро продукта: направленный граф нод и связей — [docs.n8n.io](https://docs.n8n.io/) |
| Ветвления / циклы | SUPPORTED | Flow logic — отдельный раздел концепций — [docs.n8n.io](https://docs.n8n.io/) |
| Обработка ошибок / повторы / восстановление | SUPPORTED | Retry On Fail / Max Tries / Wait Between Tries на нодах, Error Workflow, Stop And Error нода: [docs.n8n.io — Stop And Error](https://docs.n8n.io/nodes/n8n-nodes-base.stopAndError/), [acridautomation.com](https://acridautomation.com/learn/how-acrid-n8n-error-alerts-failed-workflows/) |
| Планирование по расписанию / триггеры | SUPPORTED | Schedule/webhook и иные триггеры — базовая модель воркфлоу; error executions исключены из квоты в 2.38 — [n8n-docs changelog](https://github.com/n8n-io/n8n-docs/blob/HEAD/docs/changelog/README.md) |
| Интеграции | SUPPORTED | 400+ интеграций по вторичным описаниям репозитория; каталог — [n8n.io/integrations](https://n8n.io/integrations) |
| Работа с файлами | SUPPORTED | Binary data в executions; есть регрессии на стыке binary + queue mode — [Issue #35335](https://github.com/n8n-io/n8n/issues/35335) |
| Выполнение кода / терминал | SUPPORTED | Code-ноды (JS/Python) внутри воркфлоу — общеизвестная часть модели нод; в проверенных фрагментах прямо упомянуты code nodes во вторичных источниках |
| Браузер / GUI | PARTIAL | Через интеграции и HTTP-ноды; встроенного GUI-агента в проверенных страницах нет |
| Мессенджеры | SUPPORTED | Telegram и другие мессенджеры — через ноды интеграций каталога [n8n.io/integrations](https://n8n.io/integrations) (конкретная нода в ходе проверки отдельно не открывалась — PARTIAL по глубине доказательства, статус оставлен SUPPORTED по каталогу) |
| Локальные + облачные LLM | SUPPORTED | AI-ноды с разными провайдерами; self-host позволяет локальные модели — детали провайдеров в проверенных фрагментах ограничены |
| RAG | PARTIAL | Vector store ноды в AI-стеках n8n широко известны, но в проверенных в ходе сессии страницах прямо не открывались — помечено PARTIAL |
| История / логирование | SUPPORTED | Executions history и логи исполнений — базовая часть продукта — [docs.n8n.io](https://docs.n8n.io/) |
| Трейсинг / наблюдаемость | PARTIAL | Логи executions; распределённого трейсинга уровня APM в проверенных страницах не заявлено |
| HITL | PARTIAL | Wait-ноды и ручные подтверждения через формы/мессенджеры собираются из нод; отдельного HitlApproval-примитива в проверенных страницах нет |
| Песочница / безопасность | PARTIAL | Self-host даёт контроль над периметром; потеря N8N_ENCRYPTION_KEY делает креды нерасшифруемыми — риск эксплуатации, отмеченный во вторичных источниках |
| Аутентификация | SUPPORTED | Credentials-хранилище с шифрованием, auth для webhook/пользователей — базовая модель продукта |
| API / расширяемость | SUPPORTED | REST API, кастомные ноды, MCP-сервер — [docs.n8n.io](https://docs.n8n.io/) |
| UI | SUPPORTED | Визуальный редактор графов — ядро продукта — [docs.n8n.io](https://docs.n8n.io/) |
| Импорт / экспорт | SUPPORTED | Воркфлоу экспортируются в JSON — подтверждено вторичными источниками с примерами экспортов |
| Тестирование | PARTIAL | Evaluations упомянуты в контексте queue mode PR — [PR #33591](https://github.com/n8n-io/n8n/pull/33591); зрелость в проверенных страницах ограничена |
| Production-готовность | SUPPORTED | Queue mode с воркерами, масштабирование; при этом есть гонки и регрессии queue mode (см. критику) |
| Оффлайн-работа | PARTIAL | Self-host возможен без облака n8n, но многие ноды требуют внешних API |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Визуальный DAG-редактор + кодовые ноды как escape hatch | [docs.n8n.io](https://docs.n8n.io/) | Официальная документация (primary) |
| Богатая модель ошибок: per-node retry, Error Workflow, Stop And Error | [docs.n8n.io — Stop And Error](https://docs.n8n.io/nodes/n8n-nodes-base.stopAndError/), [acridautomation.com](https://acridautomation.com/learn/how-acrid-n8n-error-alerts-failed-workflows/) | Официальная документация + вторичный разбор |
| Self-host одной установкой или Docker Compose | [docs.n8n.io](https://docs.n8n.io/) | Официальная документация (primary) |
| MCP-сервер: воркфлоу доступны внешним AI-клиентам | [docs.n8n.io](https://docs.n8n.io/) | Официальная документация (primary) |
| Queue mode для production-нагрузок | [Issue #24274](https://github.com/n8n-io/n8n/issues/24274) (косвенно, по устройству деплоя) | Issue с описанием архитектуры (primary) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Гонка queue mode: воркер не находит данные execution («Worker failed to find data for execution») даже с задержками 2–5 c | Высокая | Подтверждено issue | [Issue #24274](https://github.com/n8n-io/n8n/issues/24274) |
| Execution data остаётся в БД при отключённом сохранении в queue mode | Средняя | Подтверждено issue | [Issue #29555](https://github.com/n8n-io/n8n/issues/29555) |
| Filesystem-режим хранения ломает все executions в queue mode | Высокая | Подтверждено issue | [Issue #27266](https://github.com/n8n-io/n8n/issues/27266) |
| Регрессии Wait/webhook-resume в queue mode (404 на ожидающее исполнение) | Средняя | Подтверждено issue | [Issue #32424](https://github.com/n8n-io/n8n/issues/32424) |
| AI-агентные воркфлоу в production хрупки: модель «креативно» ретраит инструменты и выдумывает данные | Средняя | Вторичный разбор | [dev.to — Why your n8n AI agent workflows fail in production](https://dev.to/payload-tools/why-your-n8n-ai-agent-workflows-fail-in-production-and-how-to-fix-them-pi8) |
| Лицензия не open source: ограничения Sustainable Use License на хостинг как сервис и white-label | Средняя | Подтверждено лицензией | [getorchestra.io/blog/n8n-vs-airflow](https://www.getorchestra.io/blog/n8n-vs-airflow) |

## 5. Неудовлетворённые потребности пользователей

- Устранение гонки queue mode при многих воркерах на PostgreSQL+Redis: [Issue #24274](https://github.com/n8n-io/n8n/issues/24274).
- Корректная очистка execution data при отключённом сохранении: [Issue #29555](https://github.com/n8n-io/n8n/issues/29555).
- Рабочий filesystem-режим хранения в queue mode: [Issue #27266](https://github.com/n8n-io/n8n/issues/27266).
- Стабильность webhook-waiting при `saveDataSuccessExecution: none`: [Issue #32424](https://github.com/n8n-io/n8n/issues/32424).
- Отдельных релевантных тредов Reddit с прямыми URL в ходе проверки надёжно верифицировать не удалось — UNKNOWN.

## 6. Технические ограничения

- **Модель исполнения**: TypeScript/Node.js runtime исполняет направленный граф нод; execution — единица запуска, данные нод сериализуются между шагами.
- **Детерминизм**: сам граф детерминирован; AI Agent ноды вносят недетерминизм и риск «креативных» ретраев на уровне промпта — [dev.to](https://dev.to/payload-tools/why-your-n8n-ai-agent-workflows-fail-in-production-and-how-to-fix-them-pi8).
- **Параллелизм**: параллельные ветки графа и queue mode с воркерами; гонки доставки данных воркерам — известная боль ([Issue #24274](https://github.com/n8n-io/n8n/issues/24274)).
- **Отмена / таймауты**: есть cutoff на старт execution (60 c упомянут в [PR #36640](https://github.com/n8n-io/n8n/pull/36640)); семантика отмены посреди LLM-вызова в проверенных страницах не детализирована.
- **Идемпотентность**: встроенных гарантий нет; ретраи нод могут дублировать внешние эффекты — ответственность автора воркфлоу.
- **Восстановление после сбоя**: Wait-ноды и resume по webhook есть, но регрессии resume в queue mode ([Issue #32424](https://github.com/n8n-io/n8n/issues/32424)) показывают хрупкость.
- **Версионирование**: ветка 2.x активно ломает поведение (regression-issue на 2.27/2.31), breaking changes публикуются отдельно.

## 7. Вердикт и применимость к CyberPipe

**Вердикт**: эталонный визуальный DAG-автоматизатор и прямой прототип Netrunner Mini-n8n. Сильнейшие стороны — модель нод, retry/error workflow и триггеры. Слабые — queue-mode надёжность под нагрузкой, fair-code лицензия и хрупкость AI-агентных нод без жёстких guardrails.

**Применимость к CyberPipe** (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-уровневый роутинг Tier0 regex / Tier1 ONNX / Tier2–3 LLM, TokenBudgetGovernor, Netrunner Mini-n8n DAG, SQLite WAL, Brainstem-песочница, HITL):

- **Прямое использование — нет**: n8n — тяжёлый Node.js сервер с собственной БД и лицензией Sustainable Use; встраивать его в локального персонального ассистента на .NET 10 избыточно и лицензионно неудобно.
- **Что заимствовать (это главный референс Netrunner)**: (1) модель «нода с настройками Retry On Fail / Max Tries / Wait» — прямо в ConditionNode/NetrunnerScriptNode; (2) Error Workflow как отдельный граф на провал — аналог DeliveryNode с алертом в Telegram; (3) Stop And Error с кастомным сообщением; (4) экспорт графа в JSON как формат хранения сценариев Netrunner; (5) Wait/resume по внешнему событию — основа HitlApprovalNode.
- **Предупреждения из issue**: не повторять ошибки queue mode — в CyberPipe один локальный процесс и SQLite WAL, поэтому данные execution должны писаться транзакционно до постановки в очередь, а resume ожидающего HITL-шага не должен зависеть от настроек сохранения истории.
- **Итог**: n8n — архитектурный прототип Netrunner Mini-n8n (недаром имя), а не зависимость. Заимствовать семантику нод и ошибок, не runtime.
