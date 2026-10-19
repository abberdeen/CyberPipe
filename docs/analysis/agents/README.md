# Анализ агентных продуктов для CyberPipe

> Дата проверки источников по продуктам: 2026-10-19.
> CyberPipe на дату анализа — docs-only план (pre-implementation); все его статусы PLANNED.

## Синтез (читать первым)

| Файл | Содержание |
|---|---|
| [43-executive-summary.md](43-executive-summary.md) | Главные выводы и ответы на вопросы задания |
| [00-cyberpipe-baseline.md](00-cyberpipe-baseline.md) | Что такое CyberPipe по плану: назначение, аудитория, сценарии, архитектура, ограничения, открытые вопросы |
| [41-cross-product-gaps.md](41-cross-product-gaps.md) | 12 сквозных пробелов поля (G1–G12) с продуктами, источниками и confidence |
| [42-cyberpipe-competitive-analysis.md](42-cyberpipe-competitive-analysis.md) | Классификация конкурентов, сравнительная матрица, SWOT, рекомендации P0–P3, do-not-build |
| [research-index.csv](research-index.csv) | Машинный индекс 40 отчётов: продукт, категория, URL, статус, дата проверки, число источников, confidence |
| [44-decision-questions.md](44-decision-questions.md) | 24 вопроса на решение перед стартом разработки: конфликты планов, варианты, рекомендации; по ответам вносятся правки в планы |

## Отчёты по продуктам (40)

Каждый отчёт: идентификация, таблица из 30 возможностей (SUPPORTED/PARTIAL/PLANNED/
UNSUPPORTED/UNKNOWN/NOT_APPLICABLE), сильные стороны, критика, неудовлетворённые
потребности, технические ограничения, вердикт и применимость к CyberPipe.

| Класс | Отчёты |
|---|---|
| Direct — персональные ассистенты | [openclaw](openclaw.md), [nanoclaw](nanoclaw.md), [leon](leon.md), [anythingllm](anythingllm.md), [open-webui](open-webui.md), [agent-zero](agent-zero.md), [ironclaw](ironclaw.md), [copaw](copaw.md), [nanobot](nanobot.md), [picoclaw](picoclaw.md), [nullclaw](nullclaw.md), [zeroclaw](zeroclaw.md), [hermes-agent](hermes-agent.md) |
| Partial — workflow/автоматизация | [n8n](n8n.md), [activepieces](activepieces.md), [dify](dify.md), [flowise](flowise.md), [autogpt](autogpt.md) |
| Alternative — кодовые агенты | [cline](cline.md), [opencode](opencode.md), [aider](aider.md), [continue](continue.md), [openhands](openhands.md), [swe-agent](swe-agent.md), [open-interpreter](open-interpreter.md), [goose](goose.md) |
| Infrastructure / embeddable | [langchain](langchain.md), [langgraph](langgraph.md), [autogen](autogen.md), [crewai](crewai.md), [camel-ai](camel-ai.md), [metagpt](metagpt.md), [pydantic-ai](pydantic-ai.md), [semantic-kernel](semantic-kernel.md), [smolagents](smolagents.md), [qwen-agent](qwen-agent.md), [letta](letta.md), [superagi](superagi.md), [browser-use](browser-use.md), [browser-use-web-ui](browser-use-web-ui.md) |

## Метод и честность данных

- Использованы только доказательства из 40 отчётов выше; неподтверждённое помечено UNKNOWN.
- `research-index.csv` до этой работы в репозитории отсутствовал; перечень из 40 продуктов
  взят из задания на анализ. CSV создан заново по отчётам (source_count — уникальные URL
  в отчёте; confidence — по числу источников и доле UNKNOWN в таблице возможностей).
- Файлы планов-источников: [README архитектурного плана](../../plans/initial_arch/README.md),
  [16 — Netrunner ETL и workflow](../../plans/initial_arch/16-netrunner-etl-and-workflow-engine.md).
