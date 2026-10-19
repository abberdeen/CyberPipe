# Activepieces — анализ

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Activepieces |
| Официальный сайт / документация | [activepieces.com](https://www.activepieces.com), документация — [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction) |
| Репозиторий | [github.com/activepieces/activepieces](https://github.com/activepieces/activepieces) |
| Категория | AI-автоматизация для команд: визуальные flows, AI-агенты, таблицы; open-source альтернатива Zapier-класса инструментов |
| Назначение | Сборка автоматизаций и агентов из «pieces» (коннекторов приложений), MCP в обе стороны, governance для команд |
| Целевая аудитория | Команды и бизнес-пользователи, которым нужна AI-автоматизация с контролем; self-host и cloud |
| Статус на 2026-10-19 | Активно развивается; в проверенных issue/PR видны версии 0.86.x и активная работа над агентами и subflow-retry |
| Лицензия | Core — MIT, enterprise/cloud фичи — Commercial License: [docs/about/license.mdx](https://github.com/activepieces/activepieces/blob/HEAD/docs/about/license.mdx), [LICENSE](https://github.com/activepieces/activepieces/blob/main/LICENSE) |
| Развёртывание | Cloud (cloud.activepieces.com) или self-host одной командой на своей инфраструктуре — [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction) |

Официальное позиционирование: «AI automation for teams, with governance and security built in… Describe what you want in chat, and Activepieces builds the agent or automation for you» — [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction). Строительные блоки: Agents, Flows (триггер + шаги), Tables, 760+ приложений по той же странице.

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE. Приведены только применимые строки.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность агентов | SUPPORTED | Отдельный раздел Agents: создание, управление, инструменты, знания — навигация [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction) |
| Планирование | PARTIAL | AI Router и сборка flows из чата; отдельного планировщика задач в проверенных страницах не выделено |
| Долговременная память | PARTIAL | Agent Knowledge и Tables как хранилище; векторной памяти уровня RAG-фреймворков в проверенном фрагменте не детализировано |
| Сессионный контекст | PARTIAL | Контекст внутри flow run и agent run; межсессионность — через Tables |
| Персистентность | SUPPORTED | Flow runs, Tables, история версий flows — разделы документации [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction) |
| Инструменты / function-calling | SUPPORTED | Agent Tools; каждый piece может отдаваться как MCP-инструмент — [activepieces .agents/skills/design/README](https://github.com/activepieces/activepieces/blob/HEAD/.agents/skills/design/README.md) |
| Навыки / плагины | SUPPORTED | Pieces — коннекторы приложений, сборка своих pieces (Build Pieces в документации) |
| Мультиагентность | PARTIAL | Агенты вызываются из flows и наоборот («An agent can call a flow, a flow can run an agent») — [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction); мультиагентных команд в проверенных страницах нет |
| Workflow / DAG | SUPPORTED | Flows: триггер + шаги на canvas (React + XYFlow) — [activepieces .agents/skills/design/README](https://github.com/activepieces/activepieces/blob/HEAD/.agents/skills/design/README.md) |
| Ветвления / циклы | SUPPORTED | AI Router, условия и циклы шагов flows — структура разделов Flows в документации |
| Обработка ошибок / повторы / восстановление | PARTIAL | Retry-on-failure для subflow есть, но с багами зацикливания: [PR #14632](https://github.com/activepieces/activepieces/pull/14632); упавший agent step мог маскироваться под успех: [PR #15095](https://github.com/activepieces/activepieces/pull/15095) |
| Планирование по расписанию / триггеры | SUPPORTED | Триггеры — первый шаг любого flow; MCP trigger piece отдаёт flow как вызываемый инструмент — [activepieces .agents/features/mcp.md](https://github.com/activepieces/activepieces/blob/HEAD/.agents/features/mcp.md) |
| Интеграции | SUPPORTED | 760+ приложений — [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction) |
| Работа с файлами | PARTIAL | Через pieces приложений; отдельной файловой модели в проверенных страницах не выделено |
| Выполнение кода / терминал | PARTIAL | Code-шаги в flows типичны для класса, но в проверенных в ходе сессии фрагментах прямо не открывались — PARTIAL |
| Браузер / GUI | UNSUPPORTED | В проверенных официальных источниках GUI/браузерного агента не заявлено |
| Мессенджеры | SUPPORTED | Slack и мессенджеры — через pieces каталога приложений (760+); конкретная Telegram-нода отдельно не открывалась, поэтому глубина доказательства PARTIAL при статусе по каталогу |
| Локальные + облачные LLM | PARTIAL | «Bring your own AI keys» заявлено официально — [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction); локальные модели в проверенном фрагменте не детализированы |
| RAG | PARTIAL | Agent Knowledge упомянут в навигации документации; устройство (векторы, чанки) в проверенных страницах не раскрыто |
| История / логирование | SUPPORTED | Debugging Runs, история запусков — разделы Flows в документации |
| Трейсинг / наблюдаемость | PARTIAL | Debugging Runs; распределённого трейсинга в проверенных страницах не заявлено |
| HITL | SUPPORTED | Flow Approvals — отдельный раздел документации — [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction) |
| Песочница / безопасность | PARTIAL | Governance/security позиционирование; при этом найден cross-project баг: упавший subflow мог пометить чужой run — [PR #14320](https://github.com/activepieces/activepieces/pull/14320) |
| Аутентификация | SUPPORTED | Connections к приложениям, enterprise control — разделы документации |
| API / расширяемость | SUPPORTED | API Reference, Embed SDK, Build Pieces, MCP Server — [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction) |
| UI | SUPPORTED | Визуальный flow builder — ядро продукта |
| Импорт / экспорт | SUPPORTED | Import and export для Tables; Template Library — разделы документации |
| Тестирование | PARTIAL | Тестирование автоматизаций через MCP и debugging runs; отдельного test-фреймворка в проверенных страницах нет |
| Production-готовность | PARTIAL | Enterprise Control заявлен, но критичные баги agent step и subflow retry в свежих PR показывают незрелость агентного слоя |
| Оффлайн-работа | PARTIAL | Self-host возможен, но pieces в основном про облачные SaaS API |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| MIT на ядро — мягче, чем fair-code у n8n | [docs/about/license.mdx](https://github.com/activepieces/activepieces/blob/HEAD/docs/about/license.mdx) | Официальная документация (primary) |
| MCP в обе стороны: MCP-сервер платформы и pieces как MCP-инструменты | [docs/mcp/overview.mdx](https://github.com/activepieces/activepieces/blob/HEAD/docs/mcp/overview.mdx), [2sync.com/blog/activepieces-vs-n8n](https://2sync.com/blog/activepieces-vs-n8n) | Официальная документация + вторичное сравнение |
| Агенты, flows и таблицы взаимно вызываются | [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction) | Официальная документация (primary) |
| Flow Approvals как встроенный HITL | [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction) | Официальная документация (primary, навигация) |
| Сборка автоматизации из чата | [activepieces.com/docs](https://www.activepieces.com/docs/getting-started/introduction) | Официальная документация (primary) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| AI Agent падает с INTERNAL_ERROR при любом непустом agentTools | Высокая | Подтверждено issue | [Issue #14469](https://github.com/activepieces/activepieces/issues/14469) |
| Упавший agent step сообщался как успех flow run | Высокая | Исправлено PR | [PR #15095](https://github.com/activepieces/activepieces/pull/15095) |
| Subflow Retry on Failure + Wait for Response зацикливается, maxAttempts не достигается, parent висит PAUSED | Высокая | Исправляется PR с тестом | [PR #14632](https://github.com/activepieces/activepieces/pull/14632) |
| Cross-project: чужой subflow мог провалить run другого проекта через заголовки вебхука | Высокая | Исправлено PR | [PR #14320](https://github.com/activepieces/activepieces/pull/14320) |
| Piece-bundle download через publicApiUrl ломает исполнение на split app/worker деплое | Средняя | Подтверждено issue | [Issue #14654](https://github.com/activepieces/activepieces/issues/14654) |

## 5. Неудовлетворённые потребности пользователей

- Рабочие инструменты агента без INTERNAL_ERROR: [Issue #14469](https://github.com/activepieces/activepieces/issues/14469).
- Корректный retry subflow с ожиданием ответа: [PR #14632](https://github.com/activepieces/activepieces/pull/14632) (закрывает потребность из эксплуатации 0.86.3).
- Надёжное исполнение на split app/worker развёртываниях: [Issue #14654](https://github.com/activepieces/activepieces/issues/14654).
- Отдельных релевантных тредов Reddit с прямыми URL в ходе проверки надёжно верифицировать не удалось — UNKNOWN.

## 6. Технические ограничения

- **Модель исполнения**: flow run исполняется движком по шагам; agent step — вложенный агентный прогон внутри шага; subflow — дочерний run с ожиданием ответа.
- **Детерминизм**: шаги flows детерминированы, agent step — нет; ошибка агента могла замаскироваться под успех до [PR #15095](https://github.com/activepieces/activepieces/pull/15095).
- **Параллелизм**: app/worker сплит поддерживается, но установка pieces на воркерах — источник сбоев ([Issue #14654](https://github.com/activepieces/activepieces/issues/14654)).
- **Отмена / таймауты**: в проверенных источниках семантика отмены agent run не детализирована — UNKNOWN.
- **Идемпотентность**: retry subflow без корректного maxAttempts создавал новый дочерний run каждые 4–5 секунд — [PR #14632](https://github.com/activepieces/activepieces/pull/14632); встроенных идемпотентных ключей не заявлено.
- **Восстановление после сбоя**: PAUSED/waitpoint-механика есть, но именно она стала местом cross-project уязвимости ([PR #14320](https://github.com/activepieces/activepieces/pull/14320)).
- **Версионирование**: Version History flows заявлен в документации; breaking changes публикуются, агентный слой меняется быстро (0.86.x).

## 7. Вердикт и применимость к CyberPipe

**Вердикт**: самый AI-первый из классических автоматизаторов: агенты, flows и таблицы взаимно вызываются, MCP — в обе стороны, ядро под MIT. Но агентный слой незрел: инструменты агента падали, ошибки маскировались под успех, retry зацикливался. Для командной SaaS-автоматизации — интересен, для локального ассистента — только как референс.

**Применимость к CyberPipe** (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-уровневый роутинг Tier0 regex / Tier1 ONNX / Tier2–3 LLM, TokenBudgetGovernor, Netrunner Mini-n8n DAG, SQLite WAL, Brainstem-песочница, HITL):

- **Прямое использование — нет**: серверная платформа на своём стеке, рассчитана на команды и SaaS-интеграции; локальному .NET-ассистенту не нужна.
- **Что заимствовать**: (1) взаимную вызываемость «агент ↔ flow»: LlmProcessNode вызывает подграф Netrunner и наоборот — сильная модель композиции; (2) Tables как простое структурированное хранилище между шагами — аналог таблиц состояния в SQLite WAL; (3) Flow Approvals → HitlApprovalNode: подтверждение как отдельный шаг графа, а не модалка сбоку; (4) MCP в обе стороны для SkillNode: CyberPipe и как MCP-клиент, и как сервер инструментов для внешних моделей.
- **Предупреждения (прямо из их багов)**: упавший шаг никогда не должен сообщаться как SUCCESS (PR #15095) — в Netrunner статус шага определяет только движок; retry обязан иметь жёсткий maxAttempts и идемпотентность (PR #14632); parent/child run связываются с проверкой владельца, а не только по ID (PR #14320) — важно для Brainstem-песочницы.
- **Итог**: Activepieces — референс AI-first композиции и MIT-альтернатива n8n по духу, но CyberPipe берёт идеи, а не платформу.
