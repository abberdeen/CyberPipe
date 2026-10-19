# 41. Сквозные пробелы поля (cross-product gaps)

> Дата: 2026-10-19. Метод: сведены таблицы возможностей §2, критика §4, потребности §5 и
> ограничения §6 всех 40 отчётов в этой папке. Счётчики — из таблиц §2 (30 возможностей;
> в отдельных файлах названия строк варьируются, поэтому счётчики по строкам с вариантами
> названий помечены как приблизительные). Confidence: High — подтверждено ≥5 продуктами
> с прямыми issue/док-ссылками; Medium — 2–4 продукта или вторичные источники; Low — единичные сигналы.
> «Пробел» = возможность слаба/отсутствует у большинства поля, а не у одного продукта.

## G1. Формальный DAG и durable execution — меньшинство поля

| Аспект | Данные |
|---|---|
| Продукты | Workflow/DAG: SUPPORTED только у 9 из 40, PARTIAL у 14, UNSUPPORTED у 12 ([n8n.md](n8n.md), [dify.md](dify.md), [langgraph.md](langgraph.md) §2) |
| Сильные | LangGraph (checkpoint после шага, interrupt/resume), LangChain, Dify, n8n, Activepieces, Pydantic AI (граф как опция) |
| Слабые/нет | Aider, SWE-agent, smolagents, CAMEL-AI (запрос workflow — issue #2492), Letta, Open WebUI, большинство персональных ассистентов |
| Confidence | High |

Вывод для CyberPipe: Mini-n8n DAG — дифференциатор, но эталон семантики один — LangGraph.
Большинство «агентов» — LLM-циклы без графа, чекпоинтов и replay.

## G2. HITL как первоклассный примитив почти отсутствует

| Аспект | Данные |
|---|---|
| Продукты | HITL: SUPPORTED у 10, PARTIAL у 18, UNSUPPORTED/UNKNOWN у 5+ ([cline.md](cline.md), [opencode.md](opencode.md), [langgraph.md](langgraph.md) §2) |
| Сильные | Cline (approve каждого действия, Plan/Act), OpenCode (allow/ask/deny), LangGraph (interrupt), IronClaw (supervised-by-default у ZeroClaw-аналога) |
| Слабые/нет | SWE-agent (нет HITL), MetaGPT (нет формального HITL), SuperAGI, smolagents, OpenHands (только hooks) |
| Confidence | High |

Вывод: HitlApprovalNode с чекпоинтом и resume из Telegram — конкурентное преимущество,
а не «ещё одна фича». У большинства поля подтверждение — модалка или лог, а не состояние графа.

## G3. Управление бюджетом токенов системно не решено

| Аспект | Данные |
|---|---|
| Продукты | Прямые запросы: ZeroClaw #3565 (token usage logging), NanoClaw #2751 (тишина при исчерпании лимита), OpenClaw #7901/#6691 (40–60% затрат — вывод инструментов), Goose #12933 (per-model compaction), CrewAI (runaway-циклы) |
| Сильные | Pydantic AI (Gateway budget control), SWE-agent (per-task cost/step лимиты) — точечно |
| Слабые/нет | У большинства нет ни учёта на задачу, ни паузы, ни детерминированной компакции (OpenCode теряет инструменты при compaction — #48942) |
| Confidence | High |

Вывод: TokenBudgetGovernor (лимит на задачу и на DAG, пауза с чекпоинтом, видимый ответ при
исчерпании) закрывает подтверждённую боль поля. Антипаттерн поля — «silent budget failure».

## G4. Песочница: декларации сильнее изоляции

| Аспект | Данные |
|---|---|
| Продукты | Sandbox/безопасность: SUPPORTED только у 8, PARTIAL у 31 ([flowise.md](flowise.md), [ironclaw.md](ironclaw.md), [smolagents.md](smolagents.md) §2) |
| Провалы | Flowise: нет auth по умолчанию, Custom MCP с OS-доступом, SSRF, импорт флоу как RCE; Goose: shell-инциденты #12957/#12960; smolagents: AST-интерпретатор ≠ граница безопасности; Leon: path traversal #593 |
| Сильные | IronClaw (credential vault, endpoint allowlist, WASM/capability), Open Interpreter (sandbox+approvals), NanoClaw/PicoClaw (контейнер/процесс на агента) — при этом у PicoClaw security-issue в launcher/web_fetch |
| Confidence | High |

Вывод: Brainstem как отдельный процесс с allowlist путей/доменов и HITL на запись — правильный
минимум. «Песочница» внутри процесса агента полем фактически опровергнута.

## G5. Отмена, таймауты и идемпотентность — самое слабое место поля

| Аспект | Данные |
|---|---|
| Продукты | Обработка ошибок/retry/recovery: SUPPORTED лишь у ~5, PARTIAL у ~25; идемпотентность в §6 почти везде «не заявлена/UNKNOWN» |
| Примеры | Agent Zero #1923 (нет timeout, cancellation поглощается), CrewAI #4135 (осиротевшие потоки), Letta #5126/#4911 (нет hard deadline у cron), Goose #12951 (невозврат из субагента), n8n (ретраи дублируют внешние эффекты) |
| Confidence | High |

Вывод: для Netrunner обязательны per-node timeout, процессная отмена (kill песочницы),
идемпотентные ключи DeliveryNode (контрпример — дубли Telegram у Letta #4878) и детект
повторных провалов вместо молчаливого исчерпания итераций (PicoClaw #3311).

## G6. Расписания и триггеры есть, надёжность расписаний — нет

| Аспект | Данные |
|---|---|
| Продукты | Scheduling/триггеры зрелы у n8n, Dify, Activepieces, AnythingLLM (Scheduled Jobs), OpenHands (Automation Server) |
| Боли | Letta: stale recovery scheduled turns (#5122); AnythingLLM: повторный запуск джоба отбрасывается, а не ставится в очередь; NanoClaw: идемпотентность scheduled jobs не специфицирована (UNKNOWN) |
| Confidence | Medium |

Вывод: CyberPipe нужны misfire-политики (в плане есть: FireOnceNow/Skip/AskUser,
[16:546-550](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md)) и очередь запусков,
а не только cron-выражение.

## G7. Мессенджеры: Telegram — ниша, а не стандарт

| Аспект | Данные |
|---|---|
| Продукты | Мессенджеры: SUPPORTED у 12, UNSUPPORTED у 11, UNKNOWN у 13 (§2, строка 16) |
| Сильные | OpenClaw (широчайший gateway+pairing), NanoClaw, IronClaw, Letta, CoPaw (DingTalk/Feishu/QQ) |
| Нет | У фреймворков (LangChain/CrewAI/CAMEL) и кодовых агентов мессенджеров нет by design |
| Confidence | High |

Вывод: Telegram-first у CyberPipe валидирован спросом (OpenClaw/NanoClaw/Letta), но реализация
Bot API, pairing/allowlist и идемпотентной доставки — собственная; готового .NET-референса в поле нет.

## G8. Долговременная память: много деклараций, мало гигиены

| Аспект | Данные |
|---|---|
| Продукты | Long-term memory: SUPPORTED у 18, PARTIAL у 16; при этом запросы на importance/decay (Leon #575), scoped recall (Agent Zero #1929), защиту от отравления памяти (Semantic Kernel #14006, OWASP ASI06), folder-scoped изоляцию (Open WebUI #24611) открыты |
| Сильные | Letta (blocks/recall/archival/MemFS), CrewAI (композитный скоринг), SuperAGI (episodic/semantic LTM — как модель) |
| Confidence | High |

Вывод: CyberPipe стоит закладывать скоринг/устаревание/скоупы памяти в схему SQLite сразу,
а не надстройкой; запись фактов от LLM — только с валидацией (риск poisoning).

## G9. RAG и работа с документами фрагментарны

| Аспект | Данные |
|---|---|
| Продукты | RAG: SUPPORTED у 10, PARTIAL у 16, UNKNOWN у 10 (§2) |
| Сильные | AnythingLLM, Dify, Open WebUI, CAMEL-AI |
| Слабые | У кодовых агентов и DAG-автоматизаторов RAG — внешняя нода/интеграция, а не ядро |
| Confidence | Medium |

Вывод: «Второй мозг» CyberPipe (FTS5+SIMD) ближе к AnythingLLM/Open WebUI; тяжёлый RAG-пайплайн
Dify персональному ассистенту не нужен (см. [dify.md](dify.md) §7).

## G10. Наблюдаемость и тестирование агентов незрелы

| Аспект | Данные |
|---|---|
| Продукты | Тестирование: SUPPORTED у 5, PARTIAL у 21, UNKNOWN у 14; трейсинг преимущественно PARTIAL |
| Сильные | Pydantic AI (Evals, Logfire), LangSmith (платный), SWE-agent (траектории как артефакт) |
| Слабые | У персональных ассистентов и автоматизаторов evals — вторичные сводки или отсутствуют |
| Confidence | High |

Вывод: fixture-тест пакета Netrunner (`sample.fixture.json`) — сильная база; её стоит расширить
evals-контуром для Tier1-роутинга и Tier2/3-промптов по образцу Pydantic Evals.

## G11. Офлайн и локальные модели — позиционирование без гарантий

| Аспект | Данные |
|---|---|
| Продукты | Local+cloud LLM: SUPPORTED у 31, но offline-работа полна только там, где локальна вся цепочка; NanoClaw офлайн UNSUPPORTED (облачная зависимость), Continue документирует офлайн как отдельную фичу |
| Confidence | Medium |

Вывод: преимущество CyberPipe — детерминированные Tier0/Tier1 без сети вообще. Его надо
формулировать как гарантию сценариев (S1/S2), а не как «поддерживает Ollama».

## G12. Жизненный цикл платформ: maintenance, EOL, переименования

| Аспект | Данные |
|---|---|
| Продукты | AutoGen — maintenance mode, наследник Microsoft Agent Framework; Semantic Kernel — развитие ушло в Agent Framework; Flowise — архив/EOL без патчей; CoPaw→QwenPaw — переименование ломает ID и состояние; OpenCode — параллельные v1/v2 |
| Confidence | High (официальные/репозиторные сигналы в §1 и §7 соответствующих отчётов) |

Вывод: ни один внешний фреймворк поля нельзя брать ядром CyberPipe. Заимствуются семантика
и паттерны; кодовые зависимости — только точечные и изолированные (например, браузерный
исполнитель уровня browser-use внутри Brainstem).

## Сводка приоритетов пробелов для CyberPipe

| Приоритет | Пробел поля | Ответ CyberPipe (план) |
|---|---|---|
| 1 | G5 отмена/идемпотентность, G4 песочница | Brainstem-процесс, per-node timeout, idempotent DeliveryNode |
| 2 | G2 HITL, G1 durable DAG | HitlApprovalNode + чекпоинт после узла (эталон — LangGraph) |
| 3 | G3 бюджет токенов | TokenBudgetGovernor с паузой и видимым исчерпанием |
| 4 | G8 гигиена памяти, G10 evals | Скоринг/decay в схеме SQLite; Evals для роутинга и промптов |
| 5 | G6 надёжность расписаний, G7 Telegram-доставка | Misfire-политики, очередь, идемпотентные ключи доставки |
