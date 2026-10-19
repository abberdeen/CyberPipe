# 42. Конкурентный анализ CyberPipe

> Дата: 2026-10-19. CyberPipe — docs-only, все его статусы **PLANNED**.
> Статусы матрицы: YES = SUPPORTED в отчёте, PARTIAL, PLANNED (только CyberPipe),
> NO = UNSUPPORTED, UNKNOWN, N/A = неприменимо к классу продукта.
> Источник каждой строки — §2 и §7 соответствующего отчёта в этой папке.
> Колонка «Бюджет» в §2 отчётов отсутствует: оценки по §5–§7 (помечены *), confidence Low–Medium.

## 1. Классификация конкурентов

| Класс | Продукты | Почему |
|---|---|---|
| Direct (тот же сценарий: локальный персональный ассистент) | OpenClaw, NanoClaw, Leon, AnythingLLM, Open WebUI, Agent Zero, IronClaw, CoPaw (QwenPaw), nanobot, PicoClaw, NullClaw, ZeroClaw, Hermes Agent | Self-hosted персональный ассистент, мессенджеры/локальные модели/память |
| Partial (пересечение по Netrunner: workflow/автоматизация) | n8n, Activepieces, Dify, Flowise, AutoGPT | Визуальный/блочный DAG, триггеры, расписания; но серверные/облачные, не персональные |
| Alternative (другой способ решить задачи пользователя: кодовые агенты) | Cline, OpenCode, Aider, Continue, OpenHands, SWE-agent, Open Interpreter, goose | Решают инженерные задачи пользователя без ассистентской оболочки CyberPipe |
| Infrastructure / embeddable (встраиваемые фреймворки, SDK, библиотеки) | LangChain, LangGraph, AutoGen, CrewAI, CAMEL-AI, MetaGPT, Pydantic AI, Semantic Kernel, smolagents, Qwen-Agent, Letta, SuperAGI, Browser Use, Browser Use Web UI | Не продукт для конечного пользователя; источник паттернов или точечный исполнитель |

## 2. Сравнительная матрица

Колонки: DAG | HITL | Бюджет* | Sandbox | Память | Расписание | Мессенджеры | Браузер | Local LLM | RAG | Тесты/Evals.

### 2.1 Direct

| Продукт | DAG | HITL | Бюджет* | Sandbox | Память | Расписание | Мессенджеры | Браузер | Local LLM | RAG | Тесты |
|---|---|---|---|---|---|---|---|---|---|---|---|
| CyberPipe (план) | PLANNED | PLANNED | PLANNED | PLANNED | PLANNED | PLANNED | PLANNED | PLANNED | PLANNED | PLANNED | PLANNED |
| OpenClaw | NO | PARTIAL* | NO* | PARTIAL | YES | PARTIAL | YES | YES | YES | PARTIAL | PARTIAL |
| NanoClaw | UNKNOWN | PARTIAL | NO* | YES | YES | PARTIAL | YES | PARTIAL | YES | UNKNOWN | UNKNOWN |
| Leon | PARTIAL | PARTIAL | NO* | PARTIAL | YES | NO | UNKNOWN | YES | YES | UNKNOWN | UNKNOWN |
| AnythingLLM | PARTIAL | PARTIAL | NO* | PARTIAL | PARTIAL | NO | PARTIAL | PARTIAL | YES | YES | UNKNOWN |
| Open WebUI | NO | NO | NO* | PARTIAL | YES | PARTIAL | NO | NO | YES | YES | UNKNOWN |
| Agent Zero | PARTIAL | PARTIAL | NO* | PARTIAL | YES | UNKNOWN | UNKNOWN | YES | YES | PARTIAL | UNKNOWN |
| IronClaw | UNKNOWN | PARTIAL* | UNKNOWN* | PARTIAL | PARTIAL | NO | YES | YES | YES | UNKNOWN | UNKNOWN |
| CoPaw (QwenPaw) | UNKNOWN | PARTIAL | UNKNOWN* | PARTIAL | YES | UNKNOWN | YES | YES | UNKNOWN | UNKNOWN | UNKNOWN |
| nanobot | NO | PARTIAL | NO* | PARTIAL | YES | PARTIAL | YES | PARTIAL | YES | UNKNOWN | PARTIAL |
| PicoClaw | NO | UNKNOWN | NO* | PARTIAL | YES | NO | YES | PARTIAL | YES | UNKNOWN | NO |
| NullClaw | UNKNOWN | PARTIAL | UNKNOWN* | YES | YES | NO | YES | PARTIAL | YES | UNKNOWN | PARTIAL |
| ZeroClaw | PARTIAL | PARTIAL | NO* | YES | YES | NO | YES | PARTIAL | YES | UNKNOWN | PARTIAL |
| Hermes Agent | NO | PARTIAL | NO* | PARTIAL | YES | NO | YES | PARTIAL | YES | PARTIAL | PARTIAL |

\* Бюджет/HITL у IronClaw и аналогов: по §6–§7 отчётов, не по §2; confidence Medium/Low.

### 2.2 Partial (workflow-платформы)

| Продукт | DAG | HITL | Бюджет* | Sandbox | Память | Расписание | Мессенджеры | Браузер | Local LLM | RAG | Тесты |
|---|---|---|---|---|---|---|---|---|---|---|---|
| n8n | YES | PARTIAL | NO* | PARTIAL | PARTIAL | YES | YES | PARTIAL | YES | PARTIAL | PARTIAL |
| Activepieces | YES | YES | NO* | PARTIAL | PARTIAL | YES | YES | NO | YES | PARTIAL | PARTIAL |
| Dify | YES | YES | NO* | PARTIAL | PARTIAL | YES | UNKNOWN | PARTIAL | YES | YES | PARTIAL |
| Flowise (EOL) | YES | YES | NO* | NO* | PARTIAL | UNKNOWN | UNKNOWN | PARTIAL | YES | YES | PARTIAL |
| AutoGPT | YES | PARTIAL | NO* | PARTIAL | PARTIAL | NO | PARTIAL | PARTIAL | PARTIAL | UNKNOWN | PARTIAL |

### 2.3 Alternative (кодовые агенты)

| Продукт | DAG | HITL | Бюджет* | Sandbox | Память | Расписание | Мессенджеры | Браузер | Local LLM | RAG | Тесты |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Cline | PARTIAL | YES | NO* | PARTIAL | PARTIAL | PARTIAL | NO | YES | YES | PARTIAL | PARTIAL |
| OpenCode | PARTIAL | YES | NO* | PARTIAL | PARTIAL | PARTIAL | NO | PARTIAL | YES | PARTIAL | PARTIAL |
| Aider | NO | YES | NO* | PARTIAL | NO | NO | NO | NO | YES | PARTIAL | YES |
| Continue | NO | YES | NO* | PARTIAL | UNKNOWN | NO | NO | PARTIAL | YES | PARTIAL | UNKNOWN |
| OpenHands | PARTIAL | PARTIAL | NO* | YES | PARTIAL | NO | PARTIAL | PARTIAL | YES | UNKNOWN | PARTIAL |
| SWE-agent | NO | NO | PARTIAL* | YES | NO | NO | NO | NO | YES | NO | YES |
| Open Interpreter | NO | YES | NO* | YES | PARTIAL | NO | NO | PARTIAL | YES | NO | PARTIAL |
| goose | PARTIAL | PARTIAL | NO* | PARTIAL | PARTIAL | NO | UNKNOWN | PARTIAL | YES | PARTIAL | UNKNOWN |

### 2.4 Infrastructure / embeddable

| Продукт | DAG | HITL | Бюджет* | Sandbox | Память | Расписание | Мессенджеры | Браузер | Local LLM | RAG | Тесты |
|---|---|---|---|---|---|---|---|---|---|---|---|
| LangChain | YES | PARTIAL* | NO* | PARTIAL | YES | PARTIAL | UNKNOWN | PARTIAL | YES | YES | YES |
| LangGraph | YES | YES | NO* | PARTIAL | YES | PARTIAL | UNKNOWN | PARTIAL | YES | PARTIAL | PARTIAL |
| AutoGen | PARTIAL | PARTIAL* | NO* | PARTIAL | PARTIAL | UNKNOWN | UNKNOWN | PARTIAL | YES | YES | UNKNOWN |
| CrewAI | YES | UNKNOWN* | NO* | PARTIAL | YES | PARTIAL | UNKNOWN | PARTIAL | YES | PARTIAL | UNKNOWN |
| CAMEL-AI | PARTIAL | UNKNOWN* | NO* | PARTIAL | YES | NO | UNKNOWN | YES | YES | YES | PARTIAL |
| MetaGPT | PARTIAL | PARTIAL* | NO* | PARTIAL | PARTIAL | NO | NO | PARTIAL | PARTIAL | PARTIAL | PARTIAL |
| Pydantic AI | YES | UNKNOWN* | PARTIAL* | PARTIAL | YES | PARTIAL | UNKNOWN | PARTIAL | YES | PARTIAL | YES |
| Semantic Kernel | PARTIAL | PARTIAL | NO* | PARTIAL | PARTIAL | N/A | NO | NO | YES | YES | UNKNOWN |
| smolagents | NO | UNKNOWN* | NO* | YES | NO | NO | UNKNOWN | PARTIAL | YES | PARTIAL | UNKNOWN |
| Qwen-Agent | UNKNOWN | UNKNOWN | UNKNOWN* | PARTIAL | PARTIAL | NO | N/A | YES | YES | YES | UNKNOWN |
| Letta | PARTIAL | YES | NO* | PARTIAL | YES | NO | YES | PARTIAL | YES | YES | NO |
| SuperAGI | PARTIAL | UNKNOWN* | NO* | PARTIAL | YES | UNKNOWN | UNKNOWN | PARTIAL | PARTIAL | PARTIAL | UNKNOWN |
| Browser Use | NO | NO | NO* | PARTIAL | NO | NO | NO | YES | YES | NO | PARTIAL |
| Browser Use Web UI | NO | NO | NO* | PARTIAL | NO | NO | NO | YES | YES | NO | UNKNOWN |

Чтение матрицы: единственная строка без NO/UNKNOWN по ключевым колонкам DAG+HITL+Sandbox —
сам CyberPipe, и только потому, что это план. Среди shipped-продуктов такого сочетания нет:
LangGraph силён в DAG/HITL, но это библиотека без мессенджеров, бюджета и песочницы;
персональные ассистенты сильны в каналах/памяти, но без формального DAG и бюджета.

## 3. SWOT CyberPipe (план против shipped-поля)

| | |
|---|---|
| Strengths | Единственное в выборке сочетание: детерминированный DAG + HITL-чекпоинт + TokenBudgetGovernor + Tier0/Tier1 за 0 токенов + Brainstem-процесс. Telegram-first при Windows-first установке без Docker/WSL (контрпример — NanoClaw/IronClaw требуют WSL). |
| Weaknesses | Кода нет: каждое преимущество — PLANNED. Нет визуального редактора DAG (у n8n/Dify это ядро UX). Нет eval-контура, метрик Tier1-роутинга и доказанного SQLCipher-PoC. Один разработчик против экосистем OpenClaw/n8n. |
| Opportunities | Подтверждённые боли поля без владельца: бюджетная тишина (NanoClaw #2751), token cost governance (ZeroClaw #3565, OpenClaw #35203), гигиена памяти (Leon #575, Agent Zero #1929), надёжная отмена (CrewAI #4135, Letta #5126). Maintenance/EOL конкурентов (AutoGen, SK, Flowise) освобождает .NET-нишу. |
| Threats | OpenClaw-класс ассистентов быстро закрывает DAG/бюджет фичами при живой экосистеме навыков. Microsoft Agent Framework может дать .NET-агентный стек «из коробки» раньше CyberPipe. Локальные модели слабее облачных на Tier3-задачах — риск разочарования в качестве. |

## 4. Рекомендации

### P0 — без этого продукт неконкурентоспособен

| ID | Проблема | Источники | Ценность | Сложность | Риски | Приёмка |
|---|---|---|---|---|---|---|
| R-P0-1 | Durable DAG: чекпоинт после узла, interrupt/resume HITL, идемпотентный DeliveryNode | [langgraph.md](langgraph.md) §7; [41](41-cross-product-gaps.md) G1/G2/G5 | Ядро дифференциации Netrunner | Высокая | Replay повторит побочные эффекты неидемпотентных узлов | Убить процесс посреди DAG → resume с чекпоинта без повтора завершённых узлов и без дубля в Telegram |
| R-P0-2 | Brainstem-песочница: отдельный процесс, allowlist путей/доменов, запрет Process.Start на хосте | [flowise.md](flowise.md) §4/§7; [smolagents.md](smolagents.md) §7; план [16:236-248](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) | Доверие к автономности | Высокая | Производительность IPC; ложное чувство безопасности | Негативный тест: скрипт не читает `data/subsystems/*.db` и `secrets.vault`; арх-тест запрещает прямой запуск |
| R-P0-3 | TokenBudgetGovernor: лимит на задачу и DAG, пауза с чекпоинтом, видимый ответ при исчерпании | [nanoclaw.md](nanoclaw.md) #2751; [zeroclaw.md](zeroclaw.md) #3565; [crewai.md](crewai.md) §5 | Ответ на самую массовую боль поля | Средняя | Паузы раздражают без понятного UX продолжения | При исчерпании: сообщение с расходом и кнопкой продолжения; ни одного «молчаливого» завершения |

### P1 — нужно к первой публичной версии

| ID | Проблема | Источники | Ценность | Сложность | Риски | Приёмка |
|---|---|---|---|---|---|---|
| R-P1-1 | Per-node timeout + процессная отмена + детект повторных провалов | [agent-zero.md](agent-zero.md) #1923; [crewai.md](crewai.md) #4135; [picoclaw.md](picoclaw.md) #3311 | Надёжность фоновых сценариев | Средняя | Отмена посреди LLM-вызова зависит от провайдера | Зависший узел убивается по timeout; повторный провал даёт ранний fail с причиной |
| R-P1-2 | Гигиена памяти: скоупы, importance/decay, валидация записываемых фактов | [leon.md](leon.md) #575; [semantic-kernel.md](semantic-kernel.md) #14006; [open-webui.md](open-webui.md) #24611 | Качество «Второго мозга» | Средняя | Переусложнение схемы на старте | Факт с низким importance вытесняется; отравленная запись отклоняется валидатором |
| R-P1-3 | Evals для Tier1-роутинга и Tier2/3-промптов | [pydantic-ai.md](pydantic-ai.md) §7; [41](41-cross-product-gaps.md) G10 | Предсказуемость 0-токенного пути | Средняя | Нужен размеченный набор бытовых команд | Eval-набор в CI: точность intent Tier1 выше заданного порога; регрессия блокирует релиз |
| R-P1-4 | Надёжные расписания: misfire-политики, очередь, счётчик запусков | [letta.md](letta.md) #5122; план [16:546-550](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md) | Доверие к «поставь на утро» | Низкая | Дубли после сна/перезагрузки ПК | ПК выключен в момент cron → ровно одно поведение по политике, без дублей доставки |

### P2 — усиливает позицию

| ID | Проблема | Источники | Ценность | Сложность | Риски | Приёмка |
|---|---|---|---|---|---|---|
| R-P2-1 | Permissions-модель allow/ask/deny с паттернами для Brainstem и HITL | [opencode.md](opencode.md) §7; [cline.md](cline.md) §7 | Меньше HITL-шума на рутине | Средняя | Слишком широкие allow-паттерны | Безопасные паттерны предразрешены Tier0/Tier1; мутации всегда ask |
| R-P2-2 | Checkpoints/откат состояния ассистента (не только DAG) | [cline.md](cline.md) §7 (три режима Restore) | Обратимость ошибок агента | Высокая | Рост диска без инкрементных снапшотов | Откат данных/диалога/обоих из Telegram в один тап; снапшоты инкрементны |
| R-P2-3 | Взаимная вызываемость агент ↔ flow и MCP в обе стороны | [activepieces.md](activepieces.md) §7 | Композиция сценариев, экосистема | Средняя | Циклы подграфов без лимита глубины | Подграф вызывается из LlmProcessNode и наоборот; глубина ограничена `MaxSubWorkflowCallDepth` |

### P3 — позже, по спросу

| ID | Проблема | Источники | Ценность | Сложность | Риски | Приёмка |
|---|---|---|---|---|---|---|
| R-P3-1 | Визуальный просмотр/отладка DAG в Avalonia (time-travel по чекпоинтам) | [langchain.md](langchain.md) §7; [n8n.md](n8n.md) §7 | Отладка сценариев без JSON | Высокая | Полноценный canvas-editor — отдельный продукт по объёму | Просмотр состояния узла и replay с точки; редактор — только если JSON станет барьером |
| R-P3-2 | Sleep-time консолидация памяти ночным DAG | [letta.md](letta.md) §7; [nanobot.md](nanobot.md) §7 (Dream) | Память улучшается без ходов пользователя | Средняя | Фоновый расход токенов | Ночной сценарий под бюджетом; результат — отчёт, а не тихие изменения |
| R-P3-3 | Каталог пакетов Netrunner (Plugin Hub) с подписью и ревью | [agent-zero.md](agent-zero.md) §7; [flowise.md](flowise.md) §7 (антипаттерн импорта без подписи) | Экосистема навыков | Высокая | Импорт чужого кода = RCE без песочницы | Пакет без подписи/прошедшего fixture-теста не регистрируется |

## 5. Do-not-build

| Не строить | Почему | Источник |
|---|---|---|
| Свой визуальный canvas-редактор DAG уровня n8n в MVP | Годы работы n8n/Dify; в плане достаточно JSON + Telegram HITL | [n8n.md](n8n.md), [dify.md](dify.md) §7 |
| Встраивание n8n/Flowise/AutoGen/Semantic Kernel как зависимости | Node/Python-рантаймы, лицензии (Sustainable Use), maintenance/EOL | §7 соответствующих отчётов; [41](41-cross-product-gaps.md) G12 |
| LLM-цикл как исполнитель вместо DAG («свободный агент») | Недетерминизм, runaway-стоимость, нет отмены | [agent-zero.md](agent-zero.md), [superagi.md](superagi.md) §7 |
| AST-«песочницу» в процессе вместо Brainstem-процесса | Полем опровергнута как граница безопасности | [smolagents.md](smolagents.md) §7 |
| Самоэволюционирующие навыки/промпты без HITL | Конфликт с детерминизмом и аудитом пакетов | [hermes-agent.md](hermes-agent.md), [letta.md](letta.md) §7 |
| Тяжёлый RAG-пайплайн уровня Dify и обязательную векторную БД | Избыточно для персонального ассистента; достаточно FTS5+SIMD | [dify.md](dify.md) §7 |
| Мультитенантность, командный доступ, свой облачный хостинг | Вне аудитории одного владельца; лицензионные/операционные издержки поля | [00](00-cyberpipe-baseline.md) §2; [activepieces.md](activepieces.md) §7 |
| Очередной «универсальный» мультиагентный фреймворк поверх Netrunner | Поле переполнено; ценность CyberPipe — продукт, а не фреймворк | [camel-ai.md](camel-ai.md), [metagpt.md](metagpt.md) §7 |

