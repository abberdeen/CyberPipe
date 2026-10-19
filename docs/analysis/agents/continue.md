# Continue — анализ открытого AI-ассистента для IDE

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Continue |
| Официальный сайт | [continue.dev](https://www.continue.dev) |
| Репозиторий | [github.com/continuedev/continue](https://github.com/continuedev/continue) |
| Документация | [docs.continue.dev](https://docs.continue.dev/) |
| Категория | Открытый AI code assistant: расширения VS Code и JetBrains + CLI (`cn`) + Hub блоков конфигурации |
| Назначение | Chat, Edit, Autocomplete и Agent-режимы в IDE; создание, шаринг и использование кастомных кодовых агентов |
| Целевая аудитория | Разработчики в VS Code/JetBrains, команды, настраивающие своих агентов через Hub |
| Статус на 2026-10-19 | Активно развивается; документация описывает IDE Extensions и CLI как текущие продукты — [docs.continue.dev](https://docs.continue.dev/) |
| Лицензия | Apache 2.0 — [github.com/continuedev/continue](https://github.com/continuedev/continue) |
| Развёртывание | Локально как расширение IDE и CLI; модели — свои (BYO LLM) или через Continue Hub |

Формулировка из документации: «Continue enables developers to create, share, and use custom AI code agents with our open source VS Code and JetBrains extensions» — [docs.continue.dev](https://docs.continue.dev/).

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE. Приведены только применимые строки.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | PARTIAL | Agent mode выполняет dev-задачи вместе с пользователем; полная автономность не является целью IDE-ассистента — [docs.continue.dev](https://docs.continue.dev/) |
| Планирование | PARTIAL | Plan Mode как отдельный гайд; формального DAG-планировщика нет — [docs.continue.dev/guides/plan-mode-guide](https://docs.continue.dev/guides/plan-mode-guide) (страница в навигации доков) |
| Долговременная память | UNKNOWN | В проверенных страницах долговременной памяти агента не заявлено |
| Сессионный контекст | SUPPORTED | Контекст чата, codebase awareness, @-контексты — [docs.continue.dev](https://docs.continue.dev/) |
| Персистентность | PARTIAL | Конфиги и история чатов в IDE; durable состояния воркфлоу нет |
| Инструменты / function-calling | SUPPORTED | Agent mode + MCP servers как инструменты — [docs.continue.dev/customize/overview](https://docs.continue.dev/customize/overview) |
| Навыки / плагины | PARTIAL | Hub-блоки (models, rules, prompts, docs, MCP) как переиспользуемые единицы; запрос на skills открыт — [#9216](https://github.com/continuedev/continue/issues/9216) |
| Мультиагентность | UNKNOWN | В проверенных официальных страницах мультиагентных команд не показано |
| Workflow / DAG | UNSUPPORTED | DAG-движка нет; есть режимы Agent/Chat/Edit/Autocomplete |
| Ветвления / циклы | PARTIAL | Цикл агента в Agent mode; формальных ветвлений графа нет |
| Обработка ошибок / повторы / восстановление | PARTIAL | Troubleshooting и логи MCP; durable retry/recovery воркфлоу не заявлены |
| Расписание / триггеры | UNKNOWN | В проверенных официальных страницах cron/триггеров не найдено |
| Интеграции | SUPPORTED | MCP servers, провайдеры моделей, IDE-интеграции — [docs.continue.dev/customize/overview](https://docs.continue.dev/customize/overview) |
| Работа с файлами | SUPPORTED | Edit mode и Agent mode правят файлы проекта |
| Терминал / выполнение кода | SUPPORTED | Continue CLI (`cn`) для терминальной работы; Agent выполняет команды через инструменты |
| Браузер / GUI | PARTIAL | GUI — сама IDE; браузерного агента общего назначения нет |
| Мессенджеры | UNSUPPORTED | Интеграций с Telegram/мессенджерами нет |
| Локальные + облачные LLM | SUPPORTED | Любые провайдеры, self-hosted модели, Ollama — [docs.continue.dev/customize/overview](https://docs.continue.dev/customize/overview), гайд «Run Continue Without Internet» в навигации доков |
| RAG | PARTIAL | Codebase/documentation awareness, Custom Code RAG как гайд в навигации доков |
| История / логирование | PARTIAL | История чатов и логи расширения; централизованного аудита нет |
| Трейсинг | UNKNOWN | В проверенных страницах не подтверждён |
| HITL | SUPPORTED | Подтверждения применения правок и вызовов инструментов в Agent mode (модель IDE-ассистента) |
| Песочница / безопасность | PARTIAL | MCP-серверы и команды выполняются с правами пользователя; песочницы уровня Brainstem в проверенных источниках не описано. Известен баг MCP в WSL — [#9151](https://github.com/continuedev/continue/issues/9151) |
| Аутентификация | PARTIAL | Аккаунт Hub и API-ключи провайдеров; локально — NOT_APPLICABLE по сути |
| API / расширяемость | SUPPORTED | config.yaml, блоки Hub, MCP, кастомные prompts/rules — [docs.continue.dev/customize/overview](https://docs.continue.dev/customize/overview) |
| UI | SUPPORTED | IDE UI (VS Code/JetBrains) + CLI |
| Импорт / экспорт | PARTIAL | Шаринг ассистентов через Hub (`uses:` в config.yaml); импорт/экспорт воркфлоу нет |
| Тестирование | UNKNOWN | Выделенного eval-фреймворка в проверенных страницах не найдено |
| Production-готовность | PARTIAL | Продукт для разработчиков; не серверная агентная платформа |
| Оффлайн-работа | SUPPORTED | Отдельный гайд «How to Run Continue Without Internet» в навигации [docs.continue.dev](https://docs.continue.dev/) |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Четыре режима под разные задачи: Agent, Chat, Edit, Autocomplete + CLI | [docs.continue.dev](https://docs.continue.dev/) | Официальная документация (primary) |
| Глубокая кастомизация: провайдеры, model roles, rules, prompts, MCP | [docs.continue.dev/customize/overview](https://docs.continue.dev/customize/overview) | Официальная документация (primary) |
| Разные модели на разные роли (Chat vs Autocomplete) | [docs.continue.dev/customize/overview](https://docs.continue.dev/customize/overview) | Официальная документация (primary) |
| Hub-блоки как переиспользуемые и шаримые единицы конфигурации | [github.com/continuedev/continue](https://github.com/continuedev/continue) | Репозиторий (primary) |
| Полностью офлайн-сценарий с локальными моделями официально документирован | [docs.continue.dev](https://docs.continue.dev/) | Официальная документация (primary) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| MCP-серверы не стартуют в WSL-workspaces (ищет cmd.exe внутри Ubuntu), MCP непригоден для WSL-разработки | Средняя | Подтверждено issue | [#9151](https://github.com/continuedev/continue/issues/9151) |
| Нет поддержки AGENTS.md как стандарта инструкций | Средняя | Открытый feature request | [#6716](https://github.com/continuedev/continue/issues/6716) |
| Нет нативных skills в смысле переиспользуемых пакетов навыков | Средняя | Открытый feature request | [#9216](https://github.com/continuedev/continue/issues/9216) |
| Конфигурационная сложность: миграция config.json → config.yaml, deprecated context providers | Низкая | Видно по структуре доков | [docs.continue.dev](https://docs.continue.dev/) |

## 5. Неудовлетворённые потребности пользователей

- MCP в WSL: [#9151 — Continue VS Code extension fails to start MCP servers in WSL workspaces](https://github.com/continuedev/continue/issues/9151).
- Поддержка AGENTS.md: [#6716](https://github.com/continuedev/continue/issues/6716).
- Skills как переиспользуемые пакеты: [#9216](https://github.com/continuedev/continue/issues/9216).
- Отдельных релевантных тредов Reddit с прямыми URL в ходе проверки надёжно верифицировать не удалось — UNKNOWN.

## 6. Технические ограничения

- **Модель исполнения**: расширение IDE + CLI; агентный цикл выполняется в процессе расширения/CLI, а не отдельным демоном.
- **Детерминизм**: недетерминирован на уровне Agent mode; Autocomplete/Edit детерминированы только как UX-оболочка.
- **Параллелизм**: ограничен IDE-процессом; распределённого исполнения нет.
- **Отмена / таймауты**: отмена на уровне UI-запроса; формальной семантики отмены инструментов в проверенных источниках не детализировано — UNKNOWN.
- **Идемпотентность**: гарантий нет; повторный Agent-запуск повторит правки.
- **Восстановление после сбоя**: состояние — история чата и файлы; checkpoint-resume воркфлоу нет.
- **Версионирование**: блоки Hub версионируются через `uses:` и облачный Hub — зависимость от hub.continue.dev для шаринга.

## 7. Вердикт и применимость к CyberPipe

**Вердикт**: сильный открытый IDE-ассистент с лучшей в подборке моделью кастомизации (model roles, rules, prompts, MCP, Hub-блоки), но это ассистент разработчика внутри IDE, а не персональный агент с DAG, расписаниями и мессенджерами.

**Применимость к CyberPipe** (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-уровневый роутинг Tier0 regex / Tier1 ONNX / Tier2-3 LLM, TokenBudgetGovernor, Netrunner Mini-n8n DAG: SkillNode/NetrunnerScriptNode/ConditionNode/LlmProcessNode/HitlApprovalNode/DeliveryNode, SQLite WAL, Brainstem sandbox process, HITL):

- **Как зависимость — не применим**: TypeScript/IDE-стек против .NET 10; нет Telegram, Avalonia, DAG и Brainstem-песочницы.
- **Что заимствовать**: (1) концепцию model roles — прямое соответствие 4-tier routing CyberPipe (разные модели на Tier1/Tier2/Tier3 и autocomplete-подобные быстрые задачи); (2) Hub-блоки как образец упаковки Netrunner-пакетов (модель+правила+промпт+MCP как единый версионируемый юнит рядом с netrunner.manifest.json); (3) MCP как стандартный протокол инструментов для NetrunnerScriptNode; (4) официально документированный офлайн-режим как UX-ориентир для локальных LLM CyberPipe.
- **Предупреждения**: баг MCP в WSL (#9151) показывает цену запуска инструментов без учёта среды исполнения — в CyberPipe инструменты должны жить только внутри Brainstem sandbox process; отсутствие skills (#9216) подтверждает, что формальный пакет навыка (manifest+схемы+тесты) надо проектировать самим, как в Netrunner.
- **Итог**: референс по model roles и упаковке блоков, не интеграция.
