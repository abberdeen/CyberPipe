# Qwen-Agent

> Дата проверки: 2026-10-19. Источники — только реально открытые через web_search/web_fetch страницы. Недостаток доказательств — `UNKNOWN`. Принципиальное отличие от остальных файлов подборки: это не готовый персональный ассистент, а фреймворк для разработки LLM-приложений.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное имя | Qwen-Agent |
| Сайт | Документация: https://qwenlm.github.io/Qwen-Agent/en/guide/ ; продукт на его базе: https://chat.qwen.ai/ |
| Репозиторий | https://github.com/QwenLM/Qwen-Agent |
| Документация | https://qwenlm.github.io/Qwen-Agent/en/ (Guide + Benchmarks, включая DeepPlanning) |
| Категория | Фреймворк для разработки LLM-приложений/агентов на базе Qwen |
| Назначение | «framework for developing LLM applications based on the instruction following, tool usage, planning, and memory capabilities of Qwen» (README и docs overview) |
| Аудитория | Разработчики, строящие собственных агентов на Qwen>=3.0 |
| Статус на 2026-10-19 | Активен; в README: Qwen3.5 (16.02.2026), бенчмарк DeepPlanning (27.01.2026), служит бэкендом Qwen Chat («Now Qwen-Agent plays as the backend of Qwen Chat») |
| Лицензия | Apache-2.0 (шапка README: «Licensed under the Apache License, Version 2.0») |
| Развёртывание | Python-библиотека: `pip install -U "qwen-agent[gui,rag,code_interpreter,mcp]"` или из исходников `pip install -e .` (README) |

## 2. Возможности

В этой таблице `NOT_APPLICABLE` означает: функция уровня готового ассистента, которую фреймворк осознанно не поставляет «из коробки», а оставляет приложению.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | PARTIAL | Агентный цикл `bot.run(messages=...)` (docs overview); автономный «всегда включённый» режим — NOT_APPLICABLE |
| Планирование | SUPPORTED | Planning заявлен в самоописании; отдельный бенчмарк планирования DeepPlanning: https://qwenlm.github.io/Qwen-Agent/en/benchmarks/deepplanning/ |
| Долговременная память | PARTIAL | Memory заявлена в самоописании фреймворка (README); готовое долговременное хранилище уровня ассистента при проверке не показано |
| Контекст сессии | SUPPORTED | Раздел Context Management в docs: https://qwenlm.github.io/Qwen-Agent/en/guide/ (меню Core Moduls) |
| Персистентность | PARTIAL | Зависит от приложения; у фреймворка своя БД ассистента не заявлена |
| Инструменты / function-calling | SUPPORTED | Function Calling, native API tool call, parallel/multi-step/multi-turn tool calls (README News) |
| Навыки / плагины | PARTIAL | MCP как механизм расширения инструментов (README, docs MCP); формата «skills» уровня ассистента нет |
| Мультиагентность | UNKNOWN | В проверенных официальных страницах мультиагентная оркестрация как фича не выделена |
| Workflow / DAG | UNKNOWN | DAG-движок не заявлен; поток задаётся кодом приложения |
| Ветвления / циклы | PARTIAL | Ветвления реализуются кодом Python вокруг агента, не декларативным DAG |
| Обработка ошибок / retry / recovery | PARTIAL | В релизах: «support set sse_read_timeout and automatic reconnection for mcp» (https://github.com/QwenLM/Qwen-Agent/releases ); общая retry-семантика шагов — UNKNOWN |
| Расписание / триггеры | NOT_APPLICABLE | Планировщика у фреймворка нет |
| Интеграции | SUPPORTED | MCP (stdio/SSE/streamable-http судя по релизным заметкам), Chrome extension, Browser Assistant (README) |
| Файлы | SUPPORTED | Инструменты работы с файлами в составе RAG/code interpreter примеров (README, extras `[rag]`, `[code_interpreter]`) |
| Терминал / выполнение кода | SUPPORTED с оговоркой | Code Interpreter есть, но официальный README предупреждает о примере без песочницы: «The python executor is not sandboxed and is intended for local testing only, not for production use» (заметка про Qwen2.5-Math demo) |
| Браузер / GUI | SUPPORTED | Browser Assistant, Chrome extension, Gradio GUI (`[gui]`, WebUI в docs overview) |
| Мессенджеры | NOT_APPLICABLE | Каналов мессенджеров у фреймворка нет |
| Локальные + облачные LLM-провайдеры | SUPPORTED | Модель задаётся конфигом (`model_type: qwen_dashscope` в примере docs); поддержка vLLM native tool call parsing (README News 23.07.2025) |
| RAG | SUPPORTED | Отдельный раздел RAG в docs и extra `[rag]` (README/docs) |
| История / логирование | UNKNOWN | В проверенных источниках не детализировано |
| Трейсинг | UNKNOWN | Не подтверждено |
| HITL | UNKNOWN | Approval-механизм в проверенных источниках не показан |
| Песочница / безопасность | PARTIAL | См. оговорку про не-песочничный python executor выше; системной sandbox-модели уровня IronClaw/NullClaw в источниках нет |
| Аутентификация | UNKNOWN | Не относится к фреймворку / не показано |
| API / расширяемость | SUPPORTED | Собственные инструменты через function_list, MCP-серверы как инструменты (`mcpServers` в примере docs) |
| UI | PARTIAL | Gradio WebUI как пример/демо, не продуктовый UI ассистента |
| Импорт / экспорт | UNKNOWN | Не применимо на уровне фреймворка в проверенных источниках |
| Тестирование | PARTIAL | Есть бенчмарки (DeepPlanning), юнит-тестирование агентов как фича не выделено |
| Production-готовность | PARTIAL | Используется как бэкенд Qwen Chat (README) — сильный production-сигнал для серверного сценария, но не для персонального ассистента |
| Офлайн-режим | PARTIAL | Архитектурно возможны локальные модели через vLLM-совместимые endpoint'ы; готовый офлайн-режим не заявлен |

## 3. Сильные стороны

- Зрелый function calling: parallel, multi-step, multi-turn вызовы, native API tool call через vLLM — официальный README, News.
- MCP из коробки с reconnection и streamable-http — релизные заметки https://github.com/QwenLM/Qwen-Agent/releases .
- RAG и Code Interpreter как официальные extras одной установкой — README Installation.
- Production-подтверждение: бэкенд Qwen Chat — README.
- Измеримое планирование: открытый бенчмарк DeepPlanning вместо маркетинговых заявлений — docs Benchmarks.
- Apache-2.0 — шапка README.

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Python executor в примерах не изолирован: «not sandboxed … not for production use» | Высокая при прямом переносе примеров в прод | Официальное предупреждение в README; статус — ограничение примеров, а не обязательно ядра | https://github.com/QwenLM/Qwen-Agent (README, News 18.09.2024) |
| Установка с extras падает: `Could not find a version that satisfies the requirement mcp (from qwen-agent==0.0.31)` | Средняя | Issue в официальном трекере | https://github.com/QwenLM/Qwen-Agent/issues/731 |
| Конфликты версий зависимостей (вопрос про фиксированную версию pydantic для qwen-agent 0.3.33) | Низкая–средняя | Issue-вопрос | https://github.com/qwenlm/qwen-agent/issues/776 |

## 5. Незакрытые потребности пользователей

- Стабильная установка всех extras одной командой без конфликтов зависимостей — https://github.com/QwenLM/Qwen-Agent/issues/731 и https://github.com/qwenlm/qwen-agent/issues/776 .
- Безопасное выполнение кода без самостоятельной постройки песочницы — следует из официального предупреждения README (раздел 4); отдельного issue при проверке не фиксировалось, поэтому формулируется как потребность из документации, а не из трекера.
- Discussions/Reddit при проверке не дали верифицируемых тредов — `UNKNOWN`.

## 6. Технические ограничения

- Модель выполнения: синхронный генераторный цикл `bot.run(...)` в процессе приложения (docs overview). Детерминизм ограничен детерминизмом модели.
- Нет собственного runtime: расписание, каналы, персистентность, HITL, sandbox приложению нужно строить самому.
- Параллелизм инструментов заявлен на уровне tool calls; параллелизм агентов/DAG, отмена/таймауты шагов, идемпотентность и recovery не специфицированы в проверенных источниках — `UNKNOWN`.
- Привязка к экосистеме Qwen: модельные шаблоны function calling заточены под Qwen-серии (fncall prompts в README News).

## 7. Вердикт и применимость к CyberPipe

Qwen-Agent нельзя сравнивать с остальными четырьмя как готовый продукт: это конкурент не CyberPipe-ассистенту, а потенциальной внутренней библиотеке агентного цикла — но в чужой экосистеме (Python + Qwen против .NET 10). Для CyberPipe применимы идеи, а не код: схема MCP-инструментов как `function_list`, Context Management как отдельный модуль (аналог управления контекстом вокруг Tier2-3 LLM), DeepPlanning как образец честного бенчмарка планирования для оценки Tier-маршрутизации. Предупреждение про не-песочничный executor подтверждает правильность Brainstem sandbox process и HitlApprovalNode в CyberPipe. Итог: методологический референс по tool-calling/MCP/RAG, интеграция не применима.
