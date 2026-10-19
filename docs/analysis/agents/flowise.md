# Flowise — анализ агентной платформы

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | Flowise |
| Официальный сайт | [flowiseai.com](https://flowiseai.com/) — упоминается в поисковых материалах; прямой fetch в этой проверке не выполнялся |
| Репозиторий | [github.com/FlowiseAI/Flowise](https://github.com/FlowiseAI/Flowise) |
| Документация | docs.flowiseai.com — ссылки из репозитория/сообщества; fetch docs в этой проверке завершился timeout, содержимое доков помечено как вторичное |
| Категория | Визуальный low-code конструктор AI-агентов: Chatflow (single-agent/RAG) и AgentFlow (multi-agent) |
| Назначение | «Build AI Agents, Visually» — заголовок README |
| Целевая аудитория | Разработчики и low-code команды, собирающие LLM-флоу перетаскиванием узлов |
| Статус на 2026-10-19 | **Архивирован / End of Life.** README: «Flowise has been archived. Refer to Future of Flowise» — [discussions/6727](https://github.com/FlowiseAI/Flowise/discussions/6727). Вторичные источники: code freeze 2026-07-29, архив репозитория 2026-08-13, EOL 2026-08-31 — [getdynamiq.ai](https://www.getdynamiq.ai/post/best-flowise-alternatives-for-ai-workflow-automation) |
| Лицензия | Apache 2.0 для Community; Enterprise-часть — коммерческая (вторично: agentscamp из поиска). Текст LICENSE построчно в этой проверке не извлекался |
| Развёртывание | Self-host: npm `flowise`, Docker Compose, Node >=20; Flowise Cloud (исторически) |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | PARTIAL | AgentFlow-агенты с инструментами; автономность в рамках визуального флоу |
| Планирование | PARTIAL | Вторичные обзоры AgentFlow V2; отдельного планировщика в первичных источниках этой проверки нет |
| Долговременная память | PARTIAL | Memory-узлы LangChain-экосистемы (вторично); собственной LTM-модели не показано |
| Сессионный контекст | SUPPORTED | Chatflow сессии, Flow State в AgentFlow V2 (вторично: startups.rip из поиска) |
| Персистентность | PARTIAL | БД приложения для флоу и логов; durable resume уровня LangGraph не заявлен |
| Инструменты / function-calling | SUPPORTED | Tools-узлы, Custom MCP — в т.ч. из advisory [GHSA-2vv2-3x8x-4gv7](https://github.com/advisories/GHSA-2vv2-3x8x-4gv7) |
| Навыки / плагины | PARTIAL | Кастомные узлы/компоненты mono-repo `components`; skills-системы нет |
| Мультиагентность | SUPPORTED | AgentFlow / AgentFlow V2 для multi-agent оркестрации (вторичные обзоры) |
| Workflow / DAG | SUPPORTED | Визуальный canvas Chatflow/AgentFlow — ядро продукта |
| Ветвления / циклы | SUPPORTED | Condition, branching, loops в AgentFlow V2 (вторично) |
| Обработка ошибок / повторы / восстановление | PARTIAL | Execution queue и resumable human approvals (вторично); retry-семантика узлов UNKNOWN |
| Планирование по расписанию / триггеры | UNKNOWN | В проверенных источниках cron-триггер не подтверждён |
| Интеграции | SUPPORTED | 100+ интеграций LLM/embeddings/vector stores (вторичные обзоры) |
| Работа с файлами | SUPPORTED | Document loaders для RAG (вторично) |
| Выполнение кода / терминал | PARTIAL | Custom Function / code-узлы; именно это — источник RCE-рисков, см. §4 |
| Браузер / GUI | PARTIAL | Web UI конструктора; browser-агента нет |
| Мессенджеры | UNKNOWN | Telegram-канал доставки в проверенных источниках не подтверждён |
| Локальные + облачные LLM | SUPPORTED | Ollama и облачные провайдеры через LangChain-компоненты (вторично) |
| RAG | SUPPORTED | Chatflow для retrieval-систем (вторично) |
| История / логирование | SUPPORTED | Логи выполнений; запрос portable execution evidence — [issues/6193](https://github.com/flowiseai/flowise/issues/6193) (URL из поиска, регистр org в выдаче строчный) |
| Трейсинг | PARTIAL | `@flowiseai/observe`: execution traces, node-level steps, HITL interactions — [packages/observe/README](https://github.com/FlowiseAI/Flowise/blob/HEAD/packages/observe/README.md) |
| HITL | SUPPORTED | Observe-пакет прямо описывает human-in-the-loop interactions; resumable approvals в AgentFlow V2 (вторично) |
| Песочница / безопасность | UNSUPPORTED как сильная сторона | Минимальная auth-модель, RCE/SSRF advisory — §4 |
| Аутентификация | PARTIAL | API key auth; RBAC отсутствовал/минимален по advisory GHSA-2vv2-3x8x-4gv7 |
| API / расширяемость | SUPPORTED | REST API, swagger api-documentation в mono-repo |
| UI | SUPPORTED | React UI визуального конструктора |
| Импорт / экспорт | PARTIAL | Импорт/экспорт флоу JSON; именно импорт — вектор атаки CVE-2026-40933 по вторичному анализу |
| Тестирование | PARTIAL | Evaluations есть, но уязвимы (SSRF в evaluations) и по вторичным оценкам слабее конкурентов |
| Production-готовность | UNSUPPORTED сейчас | Проект EOL: production-внедрение в 2026-10 — на свой риск без патчей |
| Оффлайн-работа | PARTIAL | Self-host + локальные модели возможны; air-gapped заявлен во вторичных сводках |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Визуальная сборка агентов без кода, быстрый старт `npx flowise start` | [github.com/FlowiseAI/Flowise](https://github.com/FlowiseAI/Flowise) | README (primary) |
| Три режима: Assistant / Chatflow / AgentFlow V2 под разные уровни сложности | Вторичный обзор madappgang из поиска | Вторичное |
| Observe-слой: трейсы выполнений, шаги узлов, HITL-взаимодействия как отдельный SDK | [packages/observe/README](https://github.com/FlowiseAI/Flowise/blob/HEAD/packages/observe/README.md) | Primary |
| Большая экосистема узлов на базе LangChain-компонентов | README mono-repo структура | Primary |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Проект архивирован, разработка заморожена, поддержка прекращена | Критическая для внедрения | Подтверждена README + вторичными источниками | [discussions/6727](https://github.com/FlowiseAI/Flowise/discussions/6727), [getdynamiq.ai](https://www.getdynamiq.ai/post/best-flowise-alternatives-for-ai-workflow-automation) |
| RCE через Custom MCP: OS-команды при минимальной auth-модели, до 3.0.1 дефолт без аутентификации | Критическая | Официальный advisory CVE-2025-8943 | [GHSA-2vv2-3x8x-4gv7](https://github.com/advisories/GHSA-2vv2-3x8x-4gv7) |
| SSRF в HTTP Node: доступ к RFC1918/localhost/cloud metadata без ограничений по умолчанию | Высокая | Advisory CVE-2026-31829 | [GHSA-fvcw-9w9r-pxc7](https://github.com/advisories/GHSA-fvcw-9w9r-pxc7) |
| SSRF через Host Header в Evaluations (3.0.1–3.1.2) | Высокая | Issue с разбором | [issues/6687](https://github.com/FlowiseAI/Flowise/issues/6687) |
| Импорт вредоносного workflow даёт контроль над сервером (CVE-2026-40933) | Критическая | Вторичный анализ Obsidian Security через СМИ | [infosecurity-magazine.com](https://www.infosecurity-magazine.com/news/flowise-mcp-rce-poc/) |
| AgentFlow V2 менее зрелый, чем workflow-движок Dify, для сложной оркестрации | Средняя | Вторичный сравнительный обзор | madappgang из поиска |

## 5. Неудовлетворённые потребности пользователей

- Портативные доказательства выполнения (.epi артефакты): логи не переносимы между системами — [issues/6193](https://github.com/flowiseai/flowise/issues/6193).
- Лимит входных токенов в чате как встроенная настройка — [discussions/1775](https://github.com/FlowiseAI/Flowise/discussions/1775).
- Корректная работа за прокси / default baseURL для внутренних API-вызовов — [issues/4618](https://github.com/FlowiseAI/Flowise/issues/4618).
- Будущее проекта после архивации — центральная тема [discussions/6727](https://github.com/FlowiseAI/Flowise/discussions/6727); потребность сообщества — поддерживаемый форк, что подтверждает вторичный fork-проект Flow-Wiser из поиска.
- Безопасные дефолты (auth, SSRF-защита) — из advisory в §4; после EOL исправлений ждать неоткуда.

## 6. Технические ограничения

- Модель исполнения: Node.js сервер, визуальный DAG Chatflow/AgentFlow поверх LangChain-компонентов.
- Детерминизм: структура детерминирована, LLM-узлы — нет; Tier-маршрутизации без LLM нет.
- Параллелизм: execution queue и shared Flow State (вторично); гарантии порядка/отмены UNKNOWN.
- Отмена/таймауты: resumable approvals есть; системных таймаутов узлов в проверенных источниках не показано.
- Идемпотентность: не специфицирована.
- Восстановление: resume после HITL-паузы; восстановления после краша процесса с checkpoint каждого узла не заявлено.
- Версионирование: слабое по вторичным сравнительным оценкам; GitOps нет.
- Жизненный цикл: EOL — главный технический ограничитель: уязвимости больше не патчатся.

## 7. Вердикт и применимость к CyberPipe

**Вердикт: не внедрять ни в каком виде; использовать как анти-пример безопасности и урок жизненного цикла.** Flowise исторически — прямой аналог Netrunner Mini-n8n (визуальный DAG, узлы, HITL, observability), поэтому его судьба прямо релевантна CyberPipe: проект с десятками тысяч звёзд за годы пришёл к архивации, а его дефолты (нет auth, Custom MCP с OS-доступом, SSRF в HTTP-узле, импорт флоу как RCE) — каталог того, чего Brainstem-песочница и HitlApprovalNode обязаны не допустить.

Применимо точечно:
- Observe-идея: трейсы выполнений с шагами узлов и HITL-событиями как отдельный слой поверх SQLite WAL — образец для логирования Netrunner.
- AgentFlow V2 словарь (branching/loops/shared Flow State/resumable approvals) — чек-лист полноты Netrunner-узлов.
- Анти-паттерны: никогда не выполнять импортированные workflow без подписи/ревью; HTTP-узел — только allowlist; код-узлы — только в Brainstem sandbox; auth по умолчанию включена даже для локального режима.
- Неприменимо: сам код (EOL, уязвимости без патчей), Node-стек, визуальный canvas вместо Telegram/Avalonia, отсутствие TokenBudgetGovernor и детерминированной Tier-маршрутизации.

Итог: REFERENCE_ONLY как негативный референс безопасности и EOL-риска; для позитивной нодовой модели предпочесть Dify/LangGraph.
