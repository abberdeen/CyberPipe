# SWE-agent — анализ исследовательского агента для решения GitHub issues

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | SWE-agent |
| Официальный сайт | [swe-agent.com](https://swe-agent.com/) |
| Репозиторий | [github.com/SWE-agent/SWE-agent](https://github.com/SWE-agent/SWE-agent) |
| Документация | [swe-agent.com/latest](https://swe-agent.com/latest/usage/batch_mode/) (batch mode как проверенная страница) |
| Категория | Исследовательский автономный агент решения GitHub issues (Princeton NLP), эталон для SWE-bench |
| Назначение | «SWE-agent takes a GitHub issue and tries to automatically fix it, using your LM of choice» — [github.com/SWE-agent/SWE-agent](https://github.com/SWE-agent/SWE-agent) |
| Целевая аудитория | Исследователи агентного решения SE-задач, бенчмарк-сообщество SWE-bench, инженеры автоматизации фиксов |
| Статус на 2026-10-19 | Активный исследовательский проект (NeurIPS 2024); документация рекомендует также mini-SWE-agent для новых проектов (вторичная сводка) |
| Лицензия | MIT — вторичные источники фиксируют MIT; прямая проверка файла LICENSE на дату проверки не выполнена, поэтому пометка: MIT по [вторичной сводке ландшафта](https://github.com/tt-wang/dsh-eval-infra/blob/HEAD/docs/landscape.md) |
| Развёртывание | Локально/серверно, исполнение в Docker-контейнерах, batch-режим на SWE-bench |

Ключевая идея — Agent-Computer Interface (ACI): специализированные команды навигации, поиска и редактирования кода, спроектированные под LLM, а не человеческий терминал как есть — [arxiv.org/abs/2405.15793](https://arxiv.org/abs/2405.15793).

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE. Приведены только применимые строки.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Полностью автономное решение issue от постановки до патча — [github.com/SWE-agent/SWE-agent](https://github.com/SWE-agent/SWE-agent) |
| Планирование | PARTIAL | Планирование внутри агентного цикла LLM; отдельного типизированного планировщика нет |
| Долговременная память | UNSUPPORTED | Памяти между задачами нет; каждая задача — изолированный запуск |
| Сессионный контекст | SUPPORTED | Траектория шагов внутри запуска (history действий и наблюдений) |
| Персистентность | PARTIAL | Траектории `.traj` и `preds.json` как артефакты запуска — [swe-agent.com/latest/usage/batch_mode](https://swe-agent.com/latest/usage/batch_mode/) |
| Инструменты / function-calling | SUPPORTED | ACI-команды: навигация, поиск, редактирование, запуск тестов — [arxiv.org/abs/2405.15793](https://arxiv.org/abs/2405.15793) |
| Навыки / плагины | PARTIAL | Конфигурируемые инструменты/команды ACI; системы навыков уровня Netrunner нет |
| Мультиагентность | UNSUPPORTED | Один агент на задачу (single-agent by design) |
| Workflow / DAG | UNSUPPORTED | Нет DAG; последовательный цикл «действие → наблюдение» |
| Ветвления / циклы | PARTIAL | Цикл шагов с лимитами; условная логика — внутри решений LLM, не графа |
| Обработка ошибок / повторы / восстановление | PARTIAL | Лимиты шагов/стоимости и завершение при превышении; recovery прерванного запуска как durable resume не заявлен |
| Расписание / триггеры | UNSUPPORTED | Триггер — ручной/batch запуск; cron нет |
| Интеграции | PARTIAL | GitHub issues и SWE-bench как источники задач |
| Работа с файлами | SUPPORTED | Ядро ACI: просмотр, поиск, редактирование файлов репозитория |
| Терминал / выполнение кода | SUPPORTED | Выполнение команд и тестов в Docker-окружении |
| Браузер / GUI | UNSUPPORTED | Браузерного/GUI-агента нет (отдельные форки для cybersecurity — вне основного продукта) |
| Мессенджеры | UNSUPPORTED | Нет |
| Локальные + облачные LLM | SUPPORTED | «your LM of choice» через LiteLLM-совместимые модели; локальные модели возможны, качество зависит от модели |
| RAG | UNSUPPORTED | Вместо RAG — ACI-навигация по репозиторию |
| История / логирование | SUPPORTED | Траектории запусков с инспектором траекторий |
| Трейсинг | PARTIAL | Траектории как форма трейса; распределённого трейсинга нет |
| HITL | UNSUPPORTED | Автономный режим без человека в цикле по дизайну |
| Песочница / безопасность | SUPPORTED | Docker-изоляция исполнения как базовая модель — [swe-agent.com/latest/usage/batch_mode](https://swe-agent.com/latest/usage/batch_mode/) (batch с docker backend) |
| Аутентификация | PARTIAL | Токены GitHub и ключи LLM; собственной auth нет |
| API / расширяемость | SUPPORTED | YAML-конфиги, кастомные конфиги агента и окружения |
| UI | PARTIAL | CLI и просмотр траекторий; полноценного UI продукта нет |
| Импорт / экспорт | PARTIAL | Экспорт патчей и траекторий; импорт воркфлоу нет |
| Тестирование | SUPPORTED | Запуск тестов репозитория внутри цикла и оценка на SWE-bench |
| Production-готовность | PARTIAL | Исследовательский инструмент; для production требуется своя обвязка (очереди, ретраи, безопасность) |
| Оффлайн-работа | PARTIAL | Возможна с локальными моделями, но Docker-образы и зависимости требуют подготовки |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| ACI как специально спроектированный интерфейс «LLM ↔ компьютер» заметно повышает качество решения SE-задач | [arxiv.org/abs/2405.15793](https://arxiv.org/abs/2405.15793) | Научная статья (primary, peer-reviewed NeurIPS 2024) |
| Воспроизводимая оценка на SWE-bench, batch-режим с лимитами стоимости | [swe-agent.com/latest/usage/batch_mode](https://swe-agent.com/latest/usage/batch_mode/) | Официальная документация (primary) |
| Явные бюджетные ограничители: per-instance cost limit, step limit | [swe-agent.com/latest/usage/batch_mode](https://swe-agent.com/latest/usage/batch_mode/) | Официальная документация (primary) |
| Docker-изоляция каждого запуска | [github.com/SWE-agent/SWE-agent](https://github.com/SWE-agent/SWE-agent) | Репозиторий (primary) |
| Траектории как первоклассный артефакт для разбора ошибок агента | [swe-agent.com/latest/usage/batch_mode](https://swe-agent.com/latest/usage/batch_mode/) | Официальная документация (primary) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Одноагентность: нет мультиагентного разделения ролей | Средняя | Архитектурное ограничение | [github.com/SWE-agent/SWE-agent](https://github.com/SWE-agent/SWE-agent) |
| Зависимость от Docker и тяжёлых окружений на задачу | Средняя | Подтверждено моделью развёртывания | [swe-agent.com/latest/usage/batch_mode](https://swe-agent.com/latest/usage/batch_mode/) |
| Качество сильно зависит от базовой LLM; слабые/локальные модели резко деградируют | Высокая | Следствие дизайна «LM of choice» | [arxiv.org/abs/2405.15793](https://arxiv.org/abs/2405.15793) |
| Стоимость одного решения issue при сильных моделях существенна, нужны жёсткие cost limit | Средняя | Документированы лимиты как обязательная практика | [swe-agent.com/latest/usage/batch_mode](https://swe-agent.com/latest/usage/batch_mode/) |
| Нет HITL: ошибочный патч некому остановить до применения | Средняя | По дизайну | Собственный вывод из автономной модели; отдельного issue в ходе проверки не верифицировано — UNKNOWN в части issue-подтверждения |

## 5. Неудовлетворённые потребности пользователей

- В проверенных в ходе поиска GitHub issues надёжно подтвердить конкретные пользовательские запросы фич SWE-agent прямыми заголовками не удалось, кроме исторической связки с Docker socket: [SWE-agent#20 (комментарий о docker socket)](https://github.com/princeton-nlp/SWE-agent/issues/20#issuecomment-2047506005) — URL получен из результатов поиска по репозиториям-форкам. Остальное — UNKNOWN, ссылки не выдумываем.
- Потребность в более лёгкой версии косвенно подтверждена существованием и рекомендацией mini-SWE-agent для новых проектов (вторичная сводка ландшафта) — [github.com/tt-wang/dsh-eval-infra](https://github.com/tt-wang/dsh-eval-infra/blob/HEAD/docs/landscape.md).
- Отдельных релевантных тредов Reddit с прямыми URL надёжно верифицировать не удалось — UNKNOWN.

## 6. Технические ограничения

- **Модель исполнения**: последовательный цикл одного агента в Docker-контейнере на задачу; batch — параллельные контейнеры.
- **Детерминизм**: недетерминирован (LLM выбирает действия); температура и лимиты лишь ограничивают разброс.
- **Параллелизм**: параллелизм только между задачами (batch, num_workers), не внутри задачи; при Docker backend возможны bottleneck-эффекты — [swe-agent.com/latest/usage/batch_mode](https://swe-agent.com/latest/usage/batch_mode/).
- **Отмена / таймауты**: step/cost-лимиты как механизм остановки; гранулярной отмены отдельного шага в проверенных источниках не детализировано — UNKNOWN.
- **Идемпотентность**: повторный запуск на том же issue не идемпотентен (новый патч может отличаться).
- **Восстановление после сбоя**: прерванный запуск не возобновляется с чекпоинта; траектория остаётся только для анализа.
- **Версионирование**: конфиги YAML версионируются пользователем; совместимость ACI-команд при обновлениях — риск исследовательской кодовой базы.

## 7. Вердикт и применимость к CyberPipe

**Вердикт**: эталонный исследовательский агент решения SE-задач с сильным ACI и честной бенчмарк-культурой, но узкоспециализированный: один агент, один GitHub issue, Docker, без HITL, памяти и пользовательских сценариев персонального ассистента.

**Применимость к CyberPipe** (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-уровневый роутинг Tier0 regex / Tier1 ONNX / Tier2-3 LLM, TokenBudgetGovernor, Netrunner Mini-n8n DAG: SkillNode/NetrunnerScriptNode/ConditionNode/LlmProcessNode/HitlApprovalNode/DeliveryNode, SQLite WAL, Brainstem sandbox process, HITL):

- **Как зависимость — не применим**: Python + Docker исследовательский стек, нет Telegram/Avalonia/DAG/HITL.
- **Что заимствовать**: (1) идею ACI — инструменты NetrunnerScriptNode должны проектироваться под LLM (малые, наблюдаемые, с ограниченным выводом), а не быть «сырым терминалом»; это прямой урок для Tier3-сценариев CyberPipe; (2) жёсткие per-task cost/step-лимиты как референс для TokenBudgetGovernor (лимит на задачу, а не только на вызов); (3) траектории как обязательный артефакт каждого DAG-запуска в SQLite WAL для разбора ошибок; (4) Docker/процессную изоляцию на задачу — аналог Brainstem sandbox process, но легче: CyberPipe правильно избегает Docker на Windows-десктопе.
- **Предупреждение**: отсутствие HITL в SWE-agent неприемлемо для персонального ассистента с доступом к файлам и мессенджеру — HitlApprovalNode в CyberPipe обязателен перед побочными эффектами.
- **Итог**: методологический референс по ACI, бюджетам и траекториям, не интеграция.
