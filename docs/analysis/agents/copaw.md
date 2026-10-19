# CoPaw (ныне QwenPaw)

> Дата проверки: 2026-10-19. Источники — только реально открытые через web_search/web_fetch страницы. Важное уточнение идентификации: проект официально переименован из CoPaw в QwenPaw, поэтому файл `copaw.md` описывает тот же продукт под актуальным именем. Недостаток доказательств — `UNKNOWN`.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное имя | Ранее CoPaw, ныне QwenPaw («CoPaw 正式更名为 QwenPaw» — в форке с описанием переименования: https://github.com/yellowyao/CoPaw ; рефакторинг переименования: https://github.com/agentscope-ai/QwenPaw/pull/3195 ) |
| Сайт / документация | https://qwenpaw.agentscope.io/ (ранее copaw.agentscope.io) |
| Репозиторий | https://github.com/agentscope-ai/QwenPaw (старый адрес https://github.com/agentscope-ai/CoPaw перенаправляет сюда, что видно по зеркалам и аудитам переименований: https://github.com/96loveslife/big_model_radar/commit/d55ff5010fc48f3d515867d0aed06821d7ef7471 ) |
| Категория | Персональная AI-ассистентская рабочая станция на AgentScope (Alibaba) |
| Назначение | «Your Personal AI Assistant; easy to install, deploy on your own machine or on the cloud; supports multiple chat apps with easily extensible capabilities» (описание репозитория) |
| Аудитория | Пользователи экосистемы Alibaba/Qwen и AgentScope, команды в DingTalk/Feishu и др. каналах |
| Статус на 2026-10-19 | Активен под именем QwenPaw; в выдаче виден релиз v2.2.2: https://github.com/agentscope-ai/QwenPaw/releases/tag/v2.2.2 ; PyPI-пакет переименован в `qwenpaw` |
| Лицензия | Apache-2.0 (бейдж в README QwenPaw) |
| Развёртывание | Локально или в облаке; Python 3.11–<3.14 (бейдж README); Docker-образ ранее `agentscope/copaw` (fnOS-порт: https://github.com/conversun/fnos-apps/pull/112 ); есть Desktop-вариант (см. issue #6012) |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Proactive-поведение есть как подсистема (упоминается `proactive_responder.py` в issue #6612) |
| Планирование | UNKNOWN | Отдельный planner в проверенных источниках не выделен |
| Долговременная память | SUPPORTED | Построен в т.ч. на ReMe (memory engine AgentScope) — docs-зеркало intro: https://github.com/necros01/copaw/blob/HEAD/website/public/docs/intro.en.md ; «Memory and personalization under your control» (PyPI: https://pypi.org/project/copaw/ ) |
| Контекст сессии | SUPPORTED | Единая память/конфигурация across channels (вторичное описание: https://addrom.com/copaw-deploying-your-secure-personal-ai-assistant-anywhere/ ) |
| Персистентность | SUPPORTED | Локальное или облачное хранение памяти под контролем пользователя (PyPI) |
| Инструменты / function-calling | SUPPORTED | AgentScope Runtime как база (intro.en.md) |
| Навыки / плагины | SUPPORTED | «Add Skills to extend capabilities (PDF, Office, browser, news, etc.)», custom skill directory с автозагрузкой — README-зеркала и гайд: https://github.com/doithanhan1982/qwenpaw |
| Мультиагентность | UNKNOWN | Для самого CoPaw/QwenPaw при проверке не подтверждено (AgentTeams — отдельный продукт) |
| Workflow / DAG | UNKNOWN | DAG-модель не показана |
| Ветвления / циклы | UNKNOWN | Не подтверждено |
| Обработка ошибок / retry / recovery | PARTIAL | «Show the exact asset and cause when a PawPort import fails so failed items can be retried» в релизе v2.2.2 — точечный retry импорта, не общая семантика |
| Расписание / триггеры | SUPPORTED | «Configure Cron for scheduled tasks and automation», встроенные scheduled tasks (README-зеркала) |
| Интеграции | SUPPORTED | Каналы DingTalk, Feishu/Lark, QQ, Discord, iMessage, Telegram, Mattermost, Matrix, MQTT в разных источниках (PyPI, skill-описание https://github.com/neversight/learn-skills.dev/blob/HEAD/data/skills-md/aradotso/trending-skills/copaw-ai-assistant/SKILL.md ) |
| Файлы | SUPPORTED | Skills для PDF/Office (раздел Skills), Console/Files в релизных заметках v2.2.2 |
| Терминал / выполнение кода | SUPPORTED | Coding Mode «for IDE-style code collaboration» (README-зеркала) |
| Браузер / GUI | SUPPORTED | Browser skill + Web Console (README-зеркала, PyPI) |
| Мессенджеры | SUPPORTED | DingTalk, Feishu, QQ, Discord, iMessage и др. (PyPI https://pypi.org/project/copaw/ ) |
| Локальные + облачные LLM-провайдеры | SUPPORTED | «supports … local and cloud LLMs» (skill-описание выше); привязка к экосистеме Qwen/AgentScope видна по переименованию |
| RAG | UNKNOWN | В проверенных источниках для QwenPaw явно не выделен |
| История / логирование | PARTIAL | Console для чата и конфигурации агента (PyPI/docs-таблица зеркал); глубина аудита — UNKNOWN |
| Трейсинг | PARTIAL | Context usage display упоминается в дайджесте как инвестиция CoPaw в LLM-отладку (https://github.com/deniszheng/agents-radar/issues/804 ) — вторичный источник |
| HITL | PARTIAL | Tool-permission механизм существует (deadlock с ним — issue #6612); полноценный approval UX при проверке не показан |
| Песочница / безопасность | PARTIAL | Security-переименование с secret store keyring и legacy fallback: https://github.com/agentscope-ai/QwenPaw/pull/3192 ; модель изоляции инструментов при проверке не детализирована |
| Аутентификация | PARTIAL | Keyring secret store (PR #3192); пользовательская auth-модель Console — UNKNOWN |
| API / расширяемость | SUPPORTED | Skills & Plugins, AgentScope Runtime база (README/intro) |
| UI | SUPPORTED | Web Console (ранее http://127.0.0.1:8088/ по skill-описанию), Desktop |
| Импорт / экспорт | SUPPORTED | PawPort import с понятными ошибками и retry (релиз v2.2.2) |
| Тестирование | UNKNOWN | Тесты при проверке не верифицированы |
| Production-готовность | PARTIAL | Релизы 2.x и Desktop есть, но переименование и incompatibility с AgentScope ломают существующие установки (раздел 4) |
| Офлайн-режим | PARTIAL | Локальные модели заявлены, но полный офлайн-сценарий не верифицирован |

## 3. Сильные стороны

- Омниканальность из коробки: DingTalk, Feishu, QQ, Discord, iMessage, Telegram и др. при единой памяти — PyPI и docs; тип: официальное описание пакета.
- Skills с автозагрузкой из пользовательской директории без привязки к вендору навыков — userdata-шаблон: https://github.com/wgpsec/redc-template/blob/HEAD/userdata-templates/copaw-bash/README_EN.md .
- Cron и Coding Mode в одном продукте — README QwenPaw (зеркала).
- База AgentScope Runtime + ReMe даёт готовый runtime и память вместо самописных — intro.en.md.
- Активная релизная линия 2.x — релиз v2.2.2.

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Переименование CoPaw→QwenPaw ломает сохранённые настройки: смена provider ID теряет persisted local model settings, смена model ID заставляет перекачивать большие файлы | Высокая для существующих пользователей | Зафиксировано в ревью официального PR переименования | https://github.com/agentscope-ai/QwenPaw/pull/3195 |
| QwenPaw 2.0.1 несовместим с agentscope 2.0.4.post1: proactive падает на типе `Msg.content`, tool-permission deadlock | Высокая | Issue в официальном трекере | https://github.com/agentscope-ai/qwenpaw/issues/6612 |
| Scroll context compression repeatedly triggers MODEL_EXECUTION_ERROR на DeepSeek (`[context compressed]` с role=user вместо system) | Средняя | Issue, версия v2.0.1 | https://github.com/agentscope-ai/qwenpaw/issues/6541 |
| Desktop python-runtime не включает `agentscope`, из-за чего падает auto_memory (Windows 11) | Средняя | Issue | https://github.com/agentscope-ai/qwenpaw/issues/6012 |

## 5. Незакрытые потребности пользователей

- Безболезненная миграция при ребрендинге без потери настроек и перекачки моделей — https://github.com/agentscope-ai/QwenPaw/pull/3195 .
- Совместимость версий QwenPaw ↔ AgentScope без падений proactive и deadlock разрешений — https://github.com/agentscope-ai/qwenpaw/issues/6612 .
- Корректная компрессия контекста на DeepSeek — https://github.com/agentscope-ai/qwenpaw/issues/6541 .
- Полноценный Desktop-бандл со всеми зависимостями — https://github.com/agentscope-ai/qwenpaw/issues/6012 .
- Reddit/Discussions при проверке не дали верифицируемых тредов — `UNKNOWN`.

## 6. Технические ограничения

- Сильная связность с AgentScope: несовпадение версий фреймворка ломает продукт (issue #6612) — архитектурный риск зависимости.
- Модель выполнения: Python-процесс на AgentScope Runtime (intro); детерминизм и DAG-семантика не заявлены.
- Переименование меняет идентификаторы провайдеров/моделей и пути (`~/.copaw` legacy fallback в PR #3192) — версионирование/миграция состояния болезненны.
- Параллелизм, отмена/таймауты, идемпотентность cron-задач: не специфицированы в проверенных источниках — `UNKNOWN`.
- Канал iMessage — только Mac (PyPI/docs).

## 7. Вердикт и применимость к CyberPipe

CoPaw/QwenPaw — референс по омниканальности и skills-автозагрузке: идея «custom skill directory, auto-load» напрямую созвучна Netrunner SKILL.md-пакетам CyberPipe, а PawPort import с retry и понятной причиной ошибки — хороший паттерн для импорта Netrunner-скриптов. Но стек Python/AgentScope и фокус на китайских мессенджерах (DingTalk/Feishu/QQ) не совпадают с .NET 10 + Telegram+Avalonia; история с переименованием — предупреждение для CyberPipe: нельзя менять ID провайдеров/моделей без миграции сохранённого состояния в SQLite WAL. Как зависимость не применим. Итог: заимствовать skills-directory и import UX, учитывать урок версионирования, код не интегрировать.
