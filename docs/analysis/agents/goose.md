# Goose

> Дата проверки: 2026-10-19. Источники — только реально открытые через web_search/web_fetch страницы. Где доказательств недостаточно — UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | goose |
| Официальный сайт / документация | [goose-docs.ai](https://goose-docs.ai/docs/quickstart), ранее block.github.io/goose |
| Репозиторий | [aaif-goose/goose](https://github.com/aaif-goose/goose) (ранее block/goose; передан в Agentic AI Foundation при Linux Foundation) |
| Документация | [Quickstart](https://goose-docs.ai/docs/quickstart), разделы Guides/Tutorials/MCP Servers/Architecture на goose-docs.ai |
| Категория | Нативный open-source AI-агент общего назначения: desktop app, CLI и API |
| Назначение | «Not just for code — research, writing, automation, data analysis» (README); on-machine агент на Rust |
| Аудитория | Разработчики и power users, которым нужен локальный расширяемый агент с выбором модели и инструментов |
| Статус на 2026-10-19 | Активно развивается под AAIF/Linux Foundation; в поиске виден релиз v1.55.0. Дискуссия сообщества о долгосрочном сопровождении после сокращений в Block — [Discussion #7709](https://github.com/block/goose/discussions/7709) |
| Лицензия | Apache-2.0 — проверено по LICENSE и бейджу README |
| Развёртывание | Desktop (macOS/Linux/Windows), CLI (`curl -fsSL https://github.com/aaif-goose/goose/releases/download/stable/download_cli.sh \| bash`), API для встраивания, custom distributions |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Автономные сессии, headless/CI-дружественный CLI |
| Планирование | PARTIAL | Recipes и субагенты; отдельного визуального планировщика нет |
| Долговременная память | PARTIAL | Memory extension и `.goosehints` упоминаются в экосистеме; зрелая многослойная память по открытым источникам не подтверждена |
| Контекст сессии | SUPPORTED | Сессии, рецепты, scheduling поверх сессий |
| Персистентность | SUPPORTED | Локальные конфиги `~/.config/goose/`, scheduled recipes в `~/.local/share/goose/scheduled_recipes` (по стороннему разбору форматов; официальный путь частично подтверждён) |
| Инструменты / function-calling | SUPPORTED | MCP extensions (70+), встроенные расширения |
| Навыки / плагины | SUPPORTED | Extensions = MCP, Skills (SKILL.md), рецепты как portable workflows |
| Мультиагентность | SUPPORTED | Subagents: изолированные goose-инстансы параллельно; ACP-делегирование внешним агентам (Claude Code/Codex) |
| Workflow / DAG | PARTIAL | Recipes (YAML: instructions, extensions, parameters, sub_recipes, retry) — ближе к линейным сценариям, чем к DAG с ConditionNode |
| Ветвления / циклы | PARTIAL | Retry в рецептах; явных condition/loop-конструкций в источниках не показано |
| Обработка ошибок / retry / recovery | PARTIAL | Retry в рецептах есть; но см. issue о потере причины падения субагента (#12988) |
| Планировщик / триггеры | SUPPORTED | Встроенный cron-like scheduling рецептов (`goose schedule add --cron`) |
| Интеграции | SUPPORTED | 70+ MCP extensions: GitHub, Google Drive, SQL, браузер и др. |
| Файлы | SUPPORTED | Файловые MCP, desktop/CLI работа с проектами |
| Терминал / исполнение кода | SUPPORTED | CLI-first, исполнение команд; есть sandbox mode и injection detection (по официальному видео/описанию) |
| Браузер / GUI | PARTIAL | Browser через MCP extensions; собственного GUI-агента уровня Agent Zero нет |
| Мессенджеры | UNKNOWN | Не заявлены |
| Локальные + облачные LLM | SUPPORTED | 15+ провайдеров: Anthropic, OpenAI, Google, Ollama, OpenRouter, Azure, Bedrock; подписки Claude/ChatGPT/Gemini через ACP |
| RAG | PARTIAL | Через MCP (например LightRAG MCP в сторонних материалах); встроенного RAG нет |
| История / логирование | SUPPORTED | Сессии, diagnostics & reporting, OTEL traces (issue #12965 просит пример приёма трейсов) |
| Трейсинг | PARTIAL | OTEL-следы упоминаются; готовых дашбордов в источниках нет |
| HITL | PARTIAL | Режимы `GOOSE_MODE` auto/approve/chat/smart_approve (по стороннему разбору конфига); формального approval-воркфлоу нет |
| Песочница / безопасность | PARTIAL | Injection detection, sandbox mode; но критичные issue: shell может остановить сам Goose и обойти timeout (#12957), arbitrary command execution через `git diff.external` (#12960) |
| Аутентификация | PARTIAL | API keys, ACP-подписки; OAuth 2.0/OIDC JWT по провайдерам — запрашивается (#12978, #12970) |
| API / расширяемость | SUPPORTED | API, MCP, custom distributions, GDK |
| UI | SUPPORTED | Desktop app + CLI; экспериментальный TUI признан unmaintained и удаляется |
| Импорт / экспорт | PARTIAL | Recipe deeplinks и cookbook; экспорт памяти/агентов — UNKNOWN |
| Тестирование | UNKNOWN | Собственного eval-фреймворка в источниках не найдено |
| Production | PARTIAL | Headless/CI сценарии есть; enterprise-гарантий нет |
| Offline | SUPPORTED | Локальные модели через Ollama, «runs entirely on your local machine» для локальных сценариев |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Нативный Rust, desktop+CLI+API, кроссплатформенность | [README](https://github.com/aaif-goose/goose) | Официальная документация |
| 15+ провайдеров и 70+ MCP extensions | [README](https://github.com/aaif-goose/goose) | Официальное заявление проекта |
| Recipes: portable YAML workflows с параметрами и sub-recipes | [goose-docs.ai](https://goose-docs.ai/docs/quickstart), экосистемные разборы форматов | Официальная документация + сторонние разборы |
| Субагенты параллельно, лимиты по turns/timeout (по сторонним разборам — требуют перепроверки) | Поисковые разборы goose-docs.ai | Вторичный источник |
| Governance: Linux Foundation / AAIF, GOVERNANCE.md, custom distros | [README](https://github.com/aaif-goose/goose) | Официальная документация |
| Scheduling из коробки (cron для рецептов) | Сторонние гайды по CLI + issue-контекст | Вторичный источник, PARTIAL-уверенность |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Shell-команды могут остановить сам Goose и обойти timeout | Высокая | Открыт | [#12957](https://github.com/aaif-goose/goose/issues/12957) |
| Arbitrary command execution в CLI через `goose review` (`git diff.external`) | Высокая | Открыт | [#12960](https://github.com/aaif-goose/goose/issues/12960) |
| Причина падения субагента не доходит до родителя: только «Delegated to subagent» | Средняя | Открыт | [#12988](https://github.com/aaif-goose/goose/issues/12988) |
| Делегированный субагент завершает работу, но не возвращает управление в Desktop v1.54.0 | Средняя | Открыт | [#12951](https://github.com/aaif-goose/goose/issues/12951) |
| RepetitionInspector и EgressInspector — no-op, ошибки инспекторов теряют вердикт | Средняя | Открыт | [#12969](https://github.com/aaif-goose/goose/issues/12969) |
| Проблемы запуска MCP при нескольких Goose-клиентах на одном хосте | Средняя | Открыт | [#12987](https://github.com/aaif-goose/goose/issues/12987) |
| Сообщество обсуждает устойчивость сопровождения после сокращений в Block | Средняя | Discussion открыта | [Discussion #7709](https://github.com/block/goose/discussions/7709) |
| Поведение «goes off the rails, flurry of changes» — повод для Trust Framework | Средняя | Официальное признание проблемы в дискуссии | [Discussion #1331](https://github.com/block/goose/discussions/1331) |

## 5. Незакрытые потребности пользователей (Issues/Discussions/Reddit)

Reddit-треды надёжно не подтвердились — UNKNOWN. Ниже GitHub Issues/Discussions с URL.

| Потребность / боль | Источник |
|---|---|
| OAuth 2.0 / OIDC JWT аутентификация у провайдеров (LiteLLM, OpenAI, Anthropic) | [#12978](https://github.com/aaif-goose/goose/issues/12978), [#12970](https://github.com/aaif-goose/goose/issues/12970) |
| Невозможно исключить встроенные скиллы для embedded-клиентов | [#12982](https://github.com/aaif-goose/goose/issues/12982) |
| Per-model порог авто-компакции контекста | [#12933](https://github.com/aaif-goose/goose/issues/12933) |
| 429-ошибки теряют Retry-After подсказку у ряда провайдеров | [#12983](https://github.com/aaif-goose/goose/issues/12983) |
| OpenRouter context-length 400 не классифицируется как ContextLengthExceeded, авто-компакция не срабатывает | [#12964](https://github.com/aaif-goose/goose/issues/12964) |
| Документация: пример отправки OTEL-трейсов в hosted backend | [#12965](https://github.com/aaif-goose/goose/issues/12965) |
| Per-phase лимиты инструментов для скиллов вне разговора | [#12921](https://github.com/aaif-goose/goose/issues/12921) |
| Trust Framework: ожидания предсказуемости поведения агента | [Discussion #1331](https://github.com/block/goose/discussions/1331) |

## 6. Технические ограничения

| Аспект | Оценка |
|---|---|
| Модель исполнения | Локальный агентный цикл (Rust) + MCP-инструменты + субагенты/рецепты |
| Детерминированность | LLM-driven; рецепты повышают воспроизводимость конфигурации, но не результата |
| Параллелизм | Параллельные субагенты; точные лимиты по открытым официальным источникам не зафиксированы — UNKNOWN |
| Отмена / таймауты | Проблемная зона: #12957 (обход timeout), #12951 (невозврат из субагента) |
| Идемпотентность | Не заявлена для рецептов/инструментов |
| Восстановление | Recovery после падений субагентов слабый (#12988: причина теряется) |
| Версионирование | Рецепты как YAML-артефакты версионируются в git пользователем; встроенного версионирования памяти нет |

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** Goose — лучший референс «локальный агент как нативный бинарь + MCP + рецепты» и governance-модели (AAIF). Для CyberPipe это источник паттернов дистрибуции и провайдерной абстракции, но не движка: DAG, HITL и бюджетирование токенов у Goose слабее или отсутствуют.

Применимость к CyberPipe (.NET 10, Telegram+Avalonia, Tier0–Tier3, TokenBudgetGovernor, Netrunner DAG, SQLite WAL, Brainstem sandbox, HITL):

- **Заимствовать:** Recipes → Netrunner script packages (YAML-манифест: инструкции, расширения, параметры, retry) — CyberPipe это усиливает типизированными узлами SkillNode/NetrunnerScriptNode/LlmProcessNode; провайдерную абстракцию 15+ LLM → Tier2/Tier3; режимы approve/smart_approve → политики HitlApprovalNode; custom distributions → профили поставки CyberPipe.
- **Не заимствовать:** исполнение shell без жёсткой песочницы — инциденты #12957/#12960 показывают цену; у CyberPipe Brainstem sandbox + HITL должны закрывать это by design. Потерю ошибок субагентов (#12988) — в DAG ошибки узлов должны быть first-class и попадать в DeliveryNode.
- **Роутинг и бюджет:** Goose не имеет аналога Tier0/Tier1 и TokenBudgetGovernor; запросы сообщества на per-model compaction (#12933) подтверждают, что управление контекстом/бюджетом — реальная боль, которую CyberPipe решает архитектурно.
- **Каналы:** Telegram у Goose нет; Avalonia desktop у CyberPipe ближе к Goose Desktop по духу (нативное приложение), но мобильный/мессенджерный сценарий — собственный.
