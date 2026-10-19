# 43. Executive summary

> Дата: 2026-10-19. Основа: [00 — базовый профиль CyberPipe](00-cyberpipe-baseline.md),
> [41 — сквозные пробелы поля](41-cross-product-gaps.md),
> [42 — конкурентный анализ](42-cyberpipe-competitive-analysis.md),
> 40 отчётов продуктов и [research-index.csv](research-index.csv) в этой папке.
> CyberPipe на дату анализа — docs-only план; все его статусы PLANNED.

## Главные выводы

1. **Ниши «локальный персональный ассистент с детерминированным workflow-движком» на рынке нет.**
   Из 40 продуктов формальный DAG поддержан только у 9, HITL как примитив рантайма — у 10,
   а сочетание DAG + HITL + песочница + бюджет токенов не даёт ни один shipped-продукт.
   Персональные ассистенты (OpenClaw, NanoClaw, Leon) сильны каналами и памятью, но это
   LLM-циклы без графа, чекпоинтов и бюджетной политики.

2. **Самые массовые незакрытые боли поля — ровно то, что CyberPipe запланировал ядром:**
   управление бюджетом токенов (ZeroClaw #3565, NanoClaw #2751, OpenClaw #7901),
   надёжная отмена/таймауты (CrewAI #4135, Letta #5126, Agent Zero #1923),
   гигиена долговременной памяти (Leon #575, Semantic Kernel #14006),
   настоящая песочница вместо деклараций (Flowise, Goose, smolagents).

3. **Эталоны заимствования семантики, а не кода:** LangGraph — чекпоинты/interrupt/resume;
   n8n и Dify — модель узлов, retry и триггеров; Cline/OpenCode — HITL-UX и permissions;
   Pydantic AI — контракты узлов и evals; IronClaw — security-паттерны персонального ассистента.

4. **Брать внешние зависимости в ядро нельзя:** AutoGen и Semantic Kernel заморожены в пользу
   Microsoft Agent Framework, Flowise архивирован без патчей, n8n — fair-code сервер на Node.
   Все 40 отчётов сходятся: CyberPipe заимствует паттерны; рантайм — свой на .NET 10.

## Ответы на вопросы задания

| Вопрос | Ответ |
|---|---|
| Что такое CyberPipe и для кого? | Локальный персональный ассистент одного владельца ПК (Windows) на .NET 10: Telegram + Avalonia, 4-tier роутинг с 0 токенов на рутину, Netrunner Mini-n8n DAG для ETL/автоматизаций. Не командный и не SaaS-продукт. См. [00](00-cyberpipe-baseline.md) §1–2. |
| Кто прямые конкуренты? | OpenClaw, NanoClaw, Leon, AnythingLLM, Open WebUI, Agent Zero, IronClaw, CoPaw и лёгкие клоны (nanobot/PicoClaw/NullClaw/ZeroClaw/Hermes). Частичные — n8n, Activepieces, Dify, Flowise, AutoGPT по Netrunner. См. [42](42-cyberpipe-competitive-analysis.md) §1. |
| Где CyberPipe сильнее поля (по плану)? | Детерминированный DAG с чекпоинтами, HitlApprovalNode как состояние графа, TokenBudgetGovernor с паузой, Tier0/Tier1 без сети и LLM, Brainstem-процесс, Windows-установка без Docker/WSL. |
| Где CyberPipe слабее поля сегодня? | Везде, где нужен код: продукта нет. Нет визуального редактора DAG, eval-контура, метрик роутинга, доказанного шифрования (SQLCipher — гипотеза до PoC), экосистемы навыков. |
| Какие пробелы поля стоит закрывать первыми? | P0: durable DAG + идемпотентная доставка; Brainstem-песочница; бюджетный губернатор с видимым исчерпанием. P1: отмена/timeout, гигиена памяти, evals, надёжные расписания. См. [42](42-cyberpipe-competitive-analysis.md) §4. |
| Что не строить? | Canvas-редактор в MVP, встраивание чужих рантаймов, «свободного агента» вместо DAG, AST-песочницу, самоэволюцию без HITL, тяжёлый RAG, мультитенантность, собственный фреймворк. См. [42](42-cyberpipe-competitive-analysis.md) §5. |
| Насколько достоверна база выводов? | 40 отчётов, 6–22 источника на продукт, проверка 2026-10-19. Confidence по продуктам — в [research-index.csv](research-index.csv): High у 10, Medium у 19, Low у 11 (мало источников или много UNKNOWN: IronClaw, Leon, NanoClaw, Qwen-Agent, CoPaw). Колонка «Бюджет» в матрице оценена по §5–§7, а не §2, — confidence Low–Medium. |

## Честные ограничения этого анализа

- Все преимущества CyberPipe — плановые. Сравнение «PLANNED против shipped» показывает
  направление, а не текущее лидерство.
- Отчёты продуктов опираются на публичные доки, репозитории и issue; Reddit/Discussions
  во многих проверках надёжно не верифицированы и помечены UNKNOWN в исходных отчётах.
- Продукты поля развиваются быстро; статусы maintenance/EOL и версии актуальны на 2026-10-19
  и требуют перепроверки перед архитектурными решениями.
