# Cline — анализ

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Cline (ранее Claude Dev) |
| Официальный сайт / документация | [cline.bot](https://cline.bot), документация — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) |
| Репозиторий | [github.com/cline/cline](https://github.com/cline/cline) |
| Категория | Автономный AI-агент для программирования: VS Code/JetBrains, CLI, Desktop, SDK на одном агентном ядре |
| Назначение | Чтение/правка файлов, терминальные команды, браузер — каждое действие с одобрения пользователя |
| Целевая аудитория | Разработчики в IDE и терминале; команды — через Enterprise (SSO, RBAC, наблюдаемость) |
| Статус на 2026-10-19 | Активно развивается; монорепозиторий отдаёт VS Code, JetBrains, CLI и SDK с общим ядром — [github.com/cline/cline — apps/cli/README](https://github.com/cline/cline/blob/HEAD/apps/cli/README.md) |
| Лицензия | Apache-2.0 — подтверждено вторично: [github.com/adsumnetworks/adsum-iot-coder — cli/README](https://github.com/adsumnetworks/adsum-iot-coder/blob/HEAD/cli/README.md) со ссылкой на LICENSE в cline/cline (официальный файл LICENSE в ходе проверки отдельно не открывался) |
| Развёртывание | Локально: расширение VS Code (saoudrizwan.claude-dev), JetBrains, Desktop, `npm i -g cline`; BYOK или Cline-провайдер с биллингом |

Официальное определение: «Cline is an AI coding agent that lives in your editor and your terminal. It can read and write files, run terminal commands, use a browser… Every action requires your explicit approval» — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview).

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE. Приведены только применимые строки.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность агентов | SUPPORTED | Автономный агент в IDE/CLI, headless для CI/CD — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview), [apps/cli/README](https://github.com/cline/cline/blob/HEAD/apps/cli/README.md) |
| Планирование | SUPPORTED | Plan/Act режимы разделяются между всеми поверхностями — [apps/cli/README](https://github.com/cline/cline/blob/HEAD/apps/cli/README.md) |
| Долговременная память | PARTIAL | Rules/skills и файлы памяти проекта; отдельной векторной памяти в проверенных страницах не заявлено |
| Сессионный контекст | SUPPORTED | Контекст задачи, auto-compaction по окну модели — релизные заметки [cline/cline releases](https://github.com/cline/cline/releases/tag/sdk/sdk/v0.0.93) |
| Персистентность | SUPPORTED | Checkpoints переживают сессии редактора — [docs.cline.bot — Checkpoints](https://docs.cline.bot/core-workflows/checkpoints.md) |
| Инструменты / function-calling | SUPPORTED | Файлы, команды, браузер, MCP-инструменты — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) |
| Навыки / плагины | SUPPORTED | Skills и MCP Marketplace; plan/act, MCP, checkpoints, rules, skills едины для CLI и расширений — [apps/cli/README](https://github.com/cline/cline/blob/HEAD/apps/cli/README.md) |
| Мультиагентность | PARTIAL | Kanban: много агентов параллельно с worktree на карточку — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview); внутри одной задачи мультиагентность ограничена |
| Workflow / DAG | PARTIAL | DAG нет; есть цепочка зависимостей карточек Kanban |
| Ветвления / циклы | PARTIAL | Агентный цикл Plan/Act; графа ветвлений нет |
| Обработка ошибок / повторы / восстановление | SUPPORTED | Checkpoints после каждого tool use с Restore Files / Task / Files & Task — [docs.cline.bot — Checkpoints](https://docs.cline.bot/core-workflows/checkpoints.md) |
| Планирование по расписанию / триггеры | PARTIAL | Desktop: scheduled tasks заявлены в карточке приложения — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) |
| Интеграции | SUPPORTED | MCP-серверы и маркетплейс, ACP mode для Zed/Neovim — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) |
| Работа с файлами | SUPPORTED | Ядро сценария: чтение, создание, правки с diff review |
| Выполнение кода / терминал | SUPPORTED | Терминальные команды с одобрением, headless CLI для скриптов |
| Браузер / GUI | SUPPORTED | «use a browser» заявлено официально — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) |
| Мессенджеры | UNSUPPORTED | В проверенных официальных источниках интеграций с Telegram/мессенджерами нет |
| Локальные + облачные LLM | SUPPORTED | BYOK: облачные провайдеры и локальные рантаймы; ClinePass и usage-billing — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) |
| RAG | PARTIAL | Понимание структуры проекта агентом; классического векторного RAG в проверенных страницах не заявлено |
| История / логирование | SUPPORTED | История задач и checkpoints; enterprise — OpenTelemetry/Datadog/Grafana/Splunk — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) |
| Трейсинг / наблюдаемость | SUPPORTED | Enterprise Observability: OpenTelemetry и интеграции мониторинга — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) |
| HITL | SUPPORTED | Каждое действие требует explicit approval; auto-approve настраивается по категориям |
| Песочница / безопасность | PARTIAL | Shadow Git для checkpoints, approval-гейты; OS-песочницы исполнения команд в проверенных страницах не заявлено. Enterprise: SSO, RBAC, контроль моделей и инструментов |
| Аутентификация | SUPPORTED | Sign-in Cline-провайдера, BYOK-ключи, enterprise SSO |
| API / расширяемость | SUPPORTED | SDK `@cline/sdk` на том же ядре, MCP, hooks (по вторичным анонсам) |
| UI | SUPPORTED | VS Code sidebar, JetBrains, Desktop, CLI, Kanban web board |
| Импорт / экспорт | PARTIAL | Checkpoints и конфиги провайдеров; экспорта задач как переносимого формата в проверенных страницах не выделено |
| Тестирование | PARTIAL | Агент запускает тесты проекта командами; собственного eval-фреймворка в проверенных страницах нет |
| Production-готовность | PARTIAL | Headless CLI для CI/CD заявлен; продукт прежде всего интерактивный, enterprise — отдельный контур |
| Оффлайн-работа | PARTIAL | Локальные модели через BYOK возможны; Desktop позиционируется для open weight моделей — [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Checkpoints после каждого действия на shadow Git: откат файлов, задачи или обоих, без загрязнения истории проекта | [docs.cline.bot — Checkpoints](https://docs.cline.bot/core-workflows/checkpoints.md) | Официальная документация (primary) |
| Одно ядро — много поверхностей: VS Code, JetBrains, CLI, Desktop, SDK ведут себя одинаково | [apps/cli/README](https://github.com/cline/cline/blob/HEAD/apps/cli/README.md), [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) | Официальная документация (primary) |
| HITL по умолчанию: каждое действие с одобрения, auto-approve — осознанная настройка | [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) | Официальная документация (primary) |
| Свобода моделей: BYOK, локальные рантаймы, ClinePass, бесплатные модели | [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) | Официальная документация (primary) |
| Kanban: параллельные агенты с worktree и auto-commit на карточку | [docs.cline.bot/cline-overview](https://docs.cline.bot/cline-overview) | Официальная документация (primary) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Счётчик токенов контекста показывает неверные значения (падает до ~150, прыгает к ~100k) на длинных задачах | Средняя | Подтверждено issue | [Issue #3861](https://github.com/cline/cline/issues/3861) |
| Checkpoints на больших репозиториях съедают диск и замедляют агента (коммит после каждого tool use) | Средняя | Официально признано в документации | [docs.cline.bot — Checkpoints](https://docs.cline.bot/core-workflows/checkpoints.md) |
| Auto-compaction зависела от неточных оценок токенов (картинки раздували оценку) | Низкая | Исправлено в релизах Desktop/SDK | [cline/cline releases — desktop-v0.0.46](https://github.com/cline/cline/releases/tag/desktop-v0.0.46) |
| Auto-approve UI мог зависать при смене задачи | Низкая | Исправлено PR | [PR #13310](https://github.com/cline/cline/pull/13310) |
| Настройки между окнами VS Code могут расходиться (по вторичным источникам о форках) | Низкая | Вторично, upstream issue №6008 упомянут в источнике | [github.com/edelauna/roo-code/issues/2](https://github.com/edelauna/roo-code/issues/2) |

## 5. Неудовлетворённые потребности пользователей

- Точный учёт токенов контекста на длинных задачах: [Issue #3861](https://github.com/cline/cline/issues/3861).
- Производительность checkpoints на больших репозиториях — документация прямо предлагает отключать фичу при замедлении: [docs.cline.bot — Checkpoints](https://docs.cline.bot/core-workflows/checkpoints.md).
- Синхронизация настроек между окнами редактора — вторично через [roo-code/issues/2](https://github.com/edelauna/roo-code/issues/2); прямой текст upstream issue в ходе проверки не открывался.
- Отдельных релевантных тредов Reddit с прямыми URL в ходе проверки надёжно верифицировать не удалось — UNKNOWN.

## 6. Технические ограничения

- **Модель исполнения**: агентный цикл в процессе редактора/CLI; Plan (без мутаций) и Act (с одобрениями) жёстко разделяют доступ к инструментам.
- **Детерминизм**: недетерминирован на уровне LLM; checkpoints делают мутации обратимыми, а не детерминированными.
- **Параллелизм**: внутри задачи — последовательный цикл; параллелизм — между задачами (Kanban, Desktop parallel sessions).
- **Отмена / таймауты**: отмена пользователем и approval-гейты; семантика таймаутов отдельных инструментов в проверенных страницах не детализирована — PARTIAL.
- **Идемпотентность**: встроенных гарантий нет; компенсация — Restore по checkpoints.
- **Восстановление после сбоя**: сильнейшая сторона: снапшот после каждого tool use, три режима восстановления (Files / Task / Files & Task), переживает сессии — [docs.cline.bot — Checkpoints](https://docs.cline.bot/core-workflows/checkpoints.md).
- **Версионирование**: монорепозиторий с независимыми релизами Desktop/SDK/CLI (теги вида desktop-v0.0.46, sdk/v0.0.93) — следить нужно за каждой поверхностью отдельно.

## 7. Вердикт и применимость к CyberPipe

**Вердикт**: эталон HITL-кодового агента: одобрение каждого действия, Plan/Act и checkpoints после каждого шага делают автономность безопасной для новичка. Платит за это скоростью и диском на больших репозиториях. Как платформа для персонального ассистента не предназначен, как школа UX безопасности — лучший в подборке наряду с OpenCode.

**Применимость к CyberPipe** (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-уровневый роутинг Tier0 regex / Tier1 ONNX / Tier2–3 LLM, TokenBudgetGovernor, Netrunner Mini-n8n DAG, SQLite WAL, Brainstem-песочница, HITL):

- **Прямое использование — нет**: Cline — про код в IDE/терминале; Telegram-ассистенту нужны свои поверхности. Возможен внешний вызов CLI headless из NetrunnerScriptNode для кодовых задач — как и OpenCode, но не оба сразу.
- **Что заимствовать**: (1) checkpoint после каждого мутирующего шага Netrunner: снапшот состояния в SQLite WAL до исполнения NetrunnerScriptNode, Restore в один тап из Telegram — прямое развитие идеи Cline для HitlApprovalNode и DeliveryNode; (2) Plan/Act: Tier2 показывает план, Tier3 исполняет после одобрения — экономит TokenBudgetGovernor; (3) три режима отката (только данные / только диалог / оба) — готовая семантика для Avalonia UI; (4) урок производительности: снапшоты должны быть инкрементными, иначе на больших данных фича станет первой отключаемой.
- **Что не подходит**: привязка к git-модели файлов; у CyberPipe состояние — сообщения, таблицы и файлы разного типа, нужен свой снапшот-механизм поверх SQLite WAL.
- **Итог**: Cline — главный референс HITL и обратимости для CyberPipe. Брать семантику checkpoints и Plan/Act, не код.
