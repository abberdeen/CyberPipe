# Aider — анализ терминального AI-парного программиста

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Aider |
| Официальный сайт | [aider.chat](https://aider.chat/) |
| Репозиторий | [github.com/Aider-AI/aider](https://github.com/Aider-AI/aider) |
| Документация | [aider.chat/docs](https://aider.chat/docs/) |
| Категория | Терминальный AI pair-programming инструмент (CLI), не фреймворк и не мультиагентная платформа |
| Назначение | Парное программирование с LLM в существующем git-репозитории: правки файлов, repo map, автокоммиты, lint/test после правок |
| Целевая аудитория | Разработчики, работающие в терминале и git |
| Статус на 2026-10-19 | Активный open-source проект; документация и репозиторий доступны на дату проверки — [aider.chat/docs](https://aider.chat/docs/), [github.com/Aider-AI/aider](https://github.com/Aider-AI/aider) |
| Лицензия | Apache-2.0 — [github.com/Aider-AI/aider (LICENSE.txt)](https://github.com/Aider-AI/aider/blob/main/LICENSE.txt) |
| Развёртывание | Локально: Python-пакет `aider-chat`, Docker, GitHub Codespaces, Replit — [aider.chat/docs/install](https://aider.chat/docs/install.html) |

Ключевые механики из документации: режимы чата Code / Ask / Architect / Help, карта репозитория (repo map), подключение почти любого LLM включая локальные модели — [aider.chat/docs/usage/modes](https://aider.chat/docs/usage/modes.html), [github.com/Aider-AI/aider](https://github.com/Aider-AI/aider).

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE. Приведены только применимые строки.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | PARTIAL | Интерактивный pair-programming, а не автономный агент: человек ведёт сессию; Architect-режим разделяет планирование и правки — [aider.chat/docs/usage/modes](https://aider.chat/docs/usage/modes.html) |
| Планирование | PARTIAL | Architect mode (planner-модель + editor-модель); отдельного DAG-планировщика нет — [aider.chat/docs/usage/modes](https://aider.chat/docs/usage/modes.html) |
| Долговременная память | UNSUPPORTED | Долговременной памяти вне чата и git-истории в проверенных источниках не заявлено |
| Сессионный контекст | SUPPORTED | Контекст сессии: добавленные файлы, repo map, история чата — [github.com/Aider-AI/aider](https://github.com/Aider-AI/aider) |
| Персистентность | PARTIAL | Персистентны git-коммиты и файлы; состояние сессии как durable checkpoint не заявлено |
| Инструменты / function-calling | PARTIAL | Собственные edit-форматы вместо LLM function-calling; запрос на интеграцию tools/function-calls открыт — [#2672](https://github.com/Aider-AI/aider/issues/2672) |
| Навыки / плагины | UNKNOWN | Понятия навыков/плагинов уровня CyberPipe Netrunner в проверенных источниках не подтверждено |
| Мультиагентность | UNSUPPORTED | Один ассистент в сессии; мультиагентных команд нет |
| Workflow / DAG | UNSUPPORTED | DAG-оркестрации нет; только интерактивный цикл и автокоммиты |
| Ветвления / циклы | PARTIAL | Цикл «правка → lint/test → исправление»; формальных ветвлений/циклов графа нет |
| Обработка ошибок / повторы / восстановление | PARTIAL | Auto-lint/test с исправлением ошибок, `/undo` через git; durable recovery после краша процесса не заявлено |
| Расписание / триггеры | UNSUPPORTED | Встроенного планировщика нет; watch-режим реагирует на комментарии в коде, но это не cron |
| Интеграции | PARTIAL | Git, редакторы (watch mode), голосовой ввод, изображения и URL — навигация [aider.chat/docs](https://aider.chat/docs/) |
| Работа с файлами | SUPPORTED | Ядро продукта: многофайловые правки, 100+ языков — [github.com/Aider-AI/aider](https://github.com/Aider-AI/aider) |
| Терминал / выполнение кода | SUPPORTED | Терминальный UI, запуск lint/test команд — [aider.chat/docs/usage/lint-test](https://aider.chat/docs/usage/lint-test.html) |
| Браузер / GUI | PARTIAL | Экспериментальный browser-режим в навигации доков; GUI-агента общего назначения нет |
| Мессенджеры | UNSUPPORTED | Интеграций с Telegram/мессенджерами в проверенных источниках нет |
| Локальные + облачные LLM | SUPPORTED | OpenAI, Anthropic, Gemini, DeepSeek, Ollama, LM Studio, OpenRouter и др. — [aider.chat/docs/llms](https://aider.chat/docs/llms.html) |
| RAG | PARTIAL | Repo map (tree-sitter + ранжирование символов) как контекстный механизм; классического векторного RAG нет |
| История / логирование | SUPPORTED | История чата, git-история автокоммитов |
| Трейсинг | UNKNOWN | Отдельного трейсинга/observability в проверенных источниках не найдено |
| HITL | SUPPORTED | Человек подтверждает/отменяет правки, `/undo`, diff до применения — по модели pair-programming |
| Песочница / безопасность | PARTIAL | Работает с правами пользователя в локальном репозитории; песочницы для выполняемого кода в проверенных источниках не описано |
| Аутентификация | PARTIAL | API-ключи провайдеров LLM; собственной auth-модели нет — NOT_APPLICABLE для локального CLI, оставлено PARTIAL |
| API / расширяемость | PARTIAL | Конфигурация YAML/.env, скриптовый запуск; публичного плагин-API в проверенных источниках не показано |
| UI | SUPPORTED | Терминальный UI, опционально browser UI |
| Импорт / экспорт | PARTIAL | Git как механизм экспорта изменений; import/export воркфлоу нет |
| Тестирование | SUPPORTED | Auto-lint и auto-test после каждой правки с автоисправлением — [aider.chat/docs/usage/lint-test](https://aider.chat/docs/usage/lint-test.html) |
| Production-готовность | PARTIAL | Зрелый инструмент разработчика; не серверная платформа |
| Оффлайн-работа | PARTIAL | Возможна с локальными моделями (Ollama/LM Studio); с облачными — нет |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Repo map всего кодбейса помогает работать в больших проектах | [github.com/Aider-AI/aider](https://github.com/Aider-AI/aider) | Репозиторий (primary) |
| Git-интеграция: автокоммиты с осмысленными сообщениями, лёгкий diff/undo | [github.com/Aider-AI/aider](https://github.com/Aider-AI/aider) | Репозиторий (primary) |
| Architect mode разделяет планирование и редактирование между моделями | [aider.chat/docs/usage/modes](https://aider.chat/docs/usage/modes.html) | Официальная документация (primary) |
| Широчайший выбор LLM: облачные и локальные | [aider.chat/docs/llms](https://aider.chat/docs/llms.html) | Официальная документация (primary) |
| Автоматические lint/test после правок с исправлением ошибок | [aider.chat/docs/usage/lint-test](https://aider.chat/docs/usage/lint-test.html) | Официальная документация (primary) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Нет интеграции LLM tools/function-calling | Средняя | Открытый feature request | [#2672](https://github.com/Aider-AI/aider/issues/2672) |
| Repo map показывает сигнатуры, но не граф вызовов и зависимости методов | Средняя | Открытый feature request | [#4538](https://github.com/aider-ai/aider/issues/4538) |
| Ручное добавление файлов утомительно и хрупко в больших проектах | Средняя | Открытый feature request | [#529](https://github.com/aider-ai/aider/issues/529) |
| При программном запуске предупреждения о моделях повторно открывают браузер | Низкая | Открытый feature request | [#5547](https://github.com/aider-ai/aider/issues/5547) |
| Однопоточная модель запросов LLM ограничивает параллельные сценарии | Средняя | Предложение сообщества | [#232](https://github.com/Aider-AI/aider/issues/232) |

## 5. Неудовлетворённые потребности пользователей

- Интеграция tools/function-calling: [#2672 — Integration of LLM-Supported Tools and Function Calls](https://github.com/Aider-AI/aider/issues/2672).
- Анализ графа вызовов поверх repo map: [#4538 — Add Method Call Stack and Call Graph Analysis](https://github.com/aider-ai/aider/issues/4538).
- Автоматическое добавление файлов в сессию: [#529 — Implement Automatic File Addition](https://github.com/aider-ai/aider/issues/529).
- Отключение повторного открытия браузера для предупреждений при автоматизации: [#5547](https://github.com/aider-ai/aider/issues/5547).
- Параллельные LLM-запросы: [#232 — Horizontally-Scalable LLM Requests](https://github.com/Aider-AI/aider/issues/232).
- Отдельных релевантных тредов Reddit с прямыми URL в ходе проверки надёжно верифицировать не удалось — UNKNOWN.

## 6. Технические ограничения

- **Модель исполнения**: интерактивная сессия в терминале, один LLM-цикл на запрос пользователя; не демон и не фоновый агент.
- **Детерминизм**: недетерминирован (LLM генерирует правки); git-коммиты дают аудит, но не воспроизводимость.
- **Параллелизм**: преимущественно однопоточный по запросам LLM (см. #232).
- **Отмена / таймауты**: отмена на уровне интерактивного прерывания и `/undo`; формальных per-step timeout в проверенных источниках не заявлено — UNKNOWN.
- **Идемпотентность**: повторный запрос повторит правки; идемпотентности шагов нет, компенсация — git.
- **Восстановление после сбоя**: файлы и коммиты переживают краш; незакоммиченное состояние сессии — нет.
- **Версионирование**: частые обновления моделей и edit-форматов; совместимость конфигов — через YAML/.env, политика в проверенных страницах не детализирована — UNKNOWN.

## 7. Вердикт и применимость к CyberPipe

**Вердикт**: лучший в своём классе терминальный pair-programming инструмент с выдающимися repo map и git-дисциплиной, но это инструмент разработчика, а не агентная платформа: нет DAG, мультиагентности, расписаний, мессенджеров и долговременной памяти.

**Применимость к CyberPipe** (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-уровневый роутинг Tier0 regex / Tier1 ONNX / Tier2-3 LLM, TokenBudgetGovernor, Netrunner Mini-n8n DAG: SkillNode/NetrunnerScriptNode/ConditionNode/LlmProcessNode/HitlApprovalNode/DeliveryNode, SQLite WAL, Brainstem sandbox process, HITL):

- **Как зависимость — не применим**: Python CLI для кодинга, а не рантайм персонального ассистента; Telegram/Avalonia/DAG/HITL-примитивов нет.
- **Что заимствовать**: (1) идею repo map (ранжированный граф символов) для Tier1-контекста кодовых Netrunner-задач вместо загрузки файлов целиком — экономия для TokenBudgetGovernor; (2) паттерн Architect/Editor (планировщик + исполнитель) как референс для пары LlmProcessNode в Netrunner; (3) git-автокоммит каждой агентной правки как аудит и механизм отката для кодовых скиллов; (4) auto-lint/test после правок как обязательный шаг после NetrunnerScriptNode.
- **Предупреждение**: отсутствие function-calling (#2672) и песочницы показывает, почему CyberPipe правильно делает ставку на Brainstem sandbox process и явные схемы инструментов, а не на текстовые edit-форматы.
- **Итог**: референс по кодовому контексту и git-дисциплине, не интеграция.
