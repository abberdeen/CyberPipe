# Open WebUI — анализ агентной платформы

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Open WebUI |
| Официальный сайт / документация | [docs.openwebui.com](https://docs.openwebui.com/) |
| Репозиторий | [github.com/open-webui/open-webui](https://github.com/open-webui/open-webui) |
| Категория | Self-hosted AI-платформа / интерфейс для LLM: «a home for AI» |
| Назначение | Provider-agnostic интерфейс к локальным и облачным моделям, расширяемый плагинами, с работой полностью оффлайн — [docs.openwebui.com](https://docs.openwebui.com/) |
| Целевая аудитория | Личные пользователи и команды, которым нужен приватный ChatGPT-подобный UI поверх Ollama/OpenAI-compatible API; Enterprise — отдельно |
| Статус на 2026-10-19 | Активно развивается: README описывает Open Terminal, Channels, Calendar, Skills; документация актуальна |
| Лицензия | Open WebUI License: BSD-3-Clause + ограничение на удаление брендинга, действует с v0.6.6 (2025-04-19); до v0.6.5 включительно — чистый BSD-3 — [docs.openwebui.com/license](https://docs.openwebui.com/license/) |
| Развёртывание | Docker (`ghcr.io/open-webui/open-webui:main`), pip, uv, Kubernetes, desktop-приложение; slim-образ ~197 MB / ~300 MiB RAM idle по докам |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | PARTIAL | Agentic Execution через Open Terminal: многошаговые задачи в чате — README [github.com/open-webui/open-webui](https://github.com/open-webui/open-webui); это не автономный агентный рантайм уровня SuperAGI |
| Планирование | PARTIAL | Live Workflow & Message Flow: чек-листы в реальном времени (README); отдельного планировщика нет |
| Долговременная память | SUPPORTED | Persistent Memory: факты о пользователе между диалогами (README); ограничение — память глобальна на пользователя, см. §5 |
| Сессионный контекст | SUPPORTED | Чаты, folders, notes с инжектом контекста (README, доки) |
| Персистентность | SUPPORTED | Self-hosted БД, тома Docker; история чатов сохраняется |
| Инструменты / function-calling | SUPPORTED | Tools, OpenAPI tool servers, MCP/MCPO — README и [docs.openwebui.com/features](https://docs.openwebui.com/features/) |
| Навыки / плагины | SUPPORTED | Filters, Actions, Pipes, Tools, Skills — README |
| Мультиагентность | PARTIAL | Models & Agents — обёртки моделей с инструкциями/инструментами; Channels с несколькими моделями; оркестрации мультиагентов как в LangGraph нет |
| Workflow / DAG | UNSUPPORTED | DAG-воркфлоу движка нет; есть Pipelines как плагинный механизм, а не граф исполнения CyberPipe-уровня |
| Ветвления / циклы | PARTIAL | Внутри агентного исполнения Open Terminal; детерминированных ветвлений графа нет |
| Обработка ошибок / повторы / восстановление | PARTIAL | Очередь сообщений при стриминге; durable recovery воркфлоу отсутствует |
| Планирование по расписанию / триггеры | PARTIAL | Calendar & AI Scheduling заявлены в README (обрезано в выдаче — детали UNKNOWN) |
| Интеграции | SUPPORTED | LMStudio, GroqCloud, Mistral, OpenRouter, vLLM и любые OpenAI-compatible API — README |
| Работа с файлами | SUPPORTED | Notes, файлы в чате, Open Terminal filesystem |
| Выполнение кода / терминал | SUPPORTED | Open Terminal: терминал и файловая система для агентов; Enterprise Terminals — изоляция по пользователям |
| Браузер / GUI | PARTIAL | Web UI — ядро продукта; browser-агента нет |
| Мессенджеры | UNSUPPORTED | Telegram/мессенджеров как канала доставки в проверенных источниках нет; Channels — внутренний аналог |
| Локальные + облачные LLM | SUPPORTED | Ollama + OpenAI-compatible, «entirely offline» — доки |
| RAG | SUPPORTED | Knowledge bases / RAG — [docs.openwebui.com/features](https://docs.openwebui.com/features/), список фич в README |
| История / логирование | SUPPORTED | История чатов, folders, поиск по чатам |
| Трейсинг | PARTIAL | Логи приложения; трейсинга агентных прогонов уровня LangSmith нет |
| HITL | PARTIAL | Approval flows заявлены в Plugin Support (README); полноценного interrupt/resume для DAG нет |
| Песочница / безопасность | PARTIAL | Granular RBAC & User Groups (README); Open Terminal без Enterprise-изоляции делит окружение |
| Аутентификация | SUPPORTED | Пользователи, группы, роли; Enterprise SSO — вторично |
| API / расширяемость | SUPPORTED | REST API, плагины, MCP — доки |
| UI | SUPPORTED | Сильнейшая сторона: зрелый web UI |
| Импорт / экспорт | PARTIAL | Community presets import (README); экспорта воркфлоу нет, т.к. нет воркфлоу |
| Тестирование | UNKNOWN | Eval-инструментов в проверенных источниках не показано |
| Production-готовность | PARTIAL | Для чат-UI — да; для агентной оркестрации — нет |
| Оффлайн-работа | SUPPORTED | «built to run entirely offline» — [docs.openwebui.com](https://docs.openwebui.com/) |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Полностью оффлайн self-hosted платформа с Ollama и OpenAI-compatible API | [docs.openwebui.com](https://docs.openwebui.com/) | Официальная документация (primary) |
| Богатая плагинная модель: Filters/Actions/Pipes/Tools/Skills + MCP/OpenAPI | [github.com/open-webui/open-webui](https://github.com/open-webui/open-webui) | README (primary) |
| Persistent Memory между диалогами из коробки | README репозитория | Primary |
| Open Terminal даёт агентам терминал/ФС прямо в чате | README репозитория | Primary |
| Granular RBAC и группы пользователей | README репозитория | Primary |
| Лёгкий старт: Docker/pip/uv/desktop, slim-образ | [docs.openwebui.com](https://docs.openwebui.com/) | Primary |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Лицензия с v0.6.6 — не чистый BSD: запрет удаления брендинга, для >50 пользователей без Enterprise нельзя ребрендить | Средняя (для CyberPipe — низкая, персональное использование) | Подтверждена документацией | [docs.openwebui.com/license](https://docs.openwebui.com/license/) |
| Память глобальна на пользователя, нет изоляции по папкам/проектам | Средняя | Открытый запрос сообщества | [discussions/24611](https://github.com/open-webui/open-webui/discussions/24611) |
| Context compaction не срабатывает mid-turn: длинные tool-calling прогоны умирают по лимиту контекста | Высокая для агентных сценариев | Открытый issue | [issues/27599](https://github.com/open-webui/open-webui/issues/27599) |
| Исторические проблемы роста payload с длиной чата | Средняя | Вторичная сводка user guide по GitHub #23733 | Вторичный источник из поиска |
| Нет DAG/оркестрации — не заменяет Netrunner | Высокая применительно к CyberPipe | Архитектурный факт | Отсутствие в [docs.openwebui.com/features](https://docs.openwebui.com/features/) |

## 5. Неудовлетворённые потребности пользователей

- Folder-scoped изоляция памяти вместо глобальной на пользователя — [discussions/24611](https://github.com/open-webui/open-webui/discussions/24611).
- Компактизация контекста внутри хода, чтобы длинные агентные прогоны не падали — [issues/27599](https://github.com/open-webui/open-webui/issues/27599).
- Ограничение search_chats и инструментов подпапкой/подмножеством диалогов — [issues/23096](https://github.com/open-webui/open-webui/issues/23096).
- Настройка default-permissions новых моделей (Public вместо Private) — [discussions/8774](https://github.com/open-webui/open-webui/discussions/8774).
- Поддержка генерации видео и корректная передача reference-файлов — [issues/28313](https://github.com/open-webui/open-webui/issues/28313).

## 6. Технические ограничения

- Модель исполнения: чат-цикл + плагины/пайплайны, а не граф. Детерминизм воркфлоу неприменим.
- Детерминизм: ответы LLM недетерминированы; Tier-маршрутизации (regex/ONNX до LLM) нет — каждый запрос идёт в модель.
- Параллелизм: параллельные модели в Channels возможны; параллельного исполнения узлов DAG нет.
- Отмена/таймауты: остановка генерации в UI есть; таймаутов/отмены шагов воркфлоу нет как концепции.
- Идемпотентность: для плагинных действий не специфицирована.
- Восстановление: прерванный агентный прогон Open Terminal не возобновляется с checkpoint.
- Версионирование: пресеты моделей версионируются сообществом; версионирования «воркфлоу» нет.

## 7. Вердикт и применимость к CyberPipe

**Вердикт: не основа, а референс UI/UX и памяти; ближайший сосед по сценарию «локальный персональный ассистент», но на чужом стеке.** Open WebUI доказывает спрос на оффлайн, Ollama, persistent memory и плагины — ровно то, что CyberPipe делает на .NET 10 + Avalonia + Telegram. Однако Open WebUI — это web-чат, а не DAG-движок: у него нет 4-tier routing, TokenBudgetGovernor, HitlApprovalNode и Brainstem-песочницы.

Применимо точечно:
- Модель Persistent Memory (факты о пользователе, управление в настройках) — образец для CyberPipe-памяти поверх SQLite WAL, но с изоляцией по проектам/папкам, которой Open WebUI не хватает (discussions/24611).
- Плагинная типология Filters/Actions/Pipes/Tools/Skills — как таксономия для расширения Netrunner-узлов.
- Урок из issues/27599: CyberPipe обязан делать компактизацию контекста mid-run и учитывать её в TokenBudgetGovernor.
- Неприменимо: web-UI вместо Avalonia/Telegram, Python-стек, лицензионная оговорка о брендинге при росте, отсутствие детерминированного DAG.

Итог: REFERENCE_ONLY для UX памяти/плагинов и оффлайн-позиционирования; код и рантайм не заимствовать.
