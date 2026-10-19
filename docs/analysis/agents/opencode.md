# OpenCode — анализ

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | OpenCode |
| Официальный сайт / документация | [opencode.ai](https://opencode.ai), документация — [opencode.ai/docs](https://opencode.ai/docs/) |
| Репозиторий | [github.com/anomalyco/opencode](https://github.com/anomalyco/opencode) (ранее sst/opencode, в источниках встречается редирект) |
| Категория | Открытый AI-агент для программирования: терминал (TUI), десктоп, IDE-расширение |
| Назначение | Агентное кодирование: чтение и правка файлов, команды, LSP, субагенты, сессии |
| Целевая аудитория | Разработчики, которым нужен model-agnostic терминальный агент без привязки к одному провайдеру |
| Статус на 2026-10-19 | Активно развивается; документация анонсирует OpenCode 2 (v2) с отдельной веткой доков и миграцией — [opencode.ai/docs](https://opencode.ai/docs/) |
| Лицензия | MIT — подтверждено вторичным источником: [nerdleveltech.com/opencode-open-source-ai-coding-agent-explained](http://nerdleveltech.com/opencode-open-source-ai-coding-agent-explained) (официальный файл LICENSE в ходе проверки отдельно не открывался) |
| Развёртывание | Локально: install-скрипт, npm/Bun/pnpm/Yarn, десктоп-приложение, IDE; headless-сервер `opencode serve` |

Официальное определение: «OpenCode is an open source AI coding agent. It's available as a terminal-based interface, desktop app, or IDE extension» — [opencode.ai/docs](https://opencode.ai/docs/).

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE. Приведены только применимые строки.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность агентов | SUPPORTED | Primary agents ведут сессию, subagents выполняют подзадачи — [opencode.ai/docs/agents](https://opencode.ai/docs/agents/) |
| Планирование | SUPPORTED | Встроенный plan-агент наряду с build — [opencode.ai/docs/agents](https://opencode.ai/docs/agents/) |
| Долговременная память | PARTIAL | Rules через AGENTS.md и инструкции проекта; отдельной memory-подсистемы уровня Memory Bank в проверенных страницах нет |
| Сессионный контекст | SUPPORTED | Сессии, compaction, summary — встроенные агенты compaction/title/summary — [opencode.ai/docs/agents](https://opencode.ai/docs/agents/) |
| Персистентность | SUPPORTED | Сессии и снапшоты сохраняются локально; undo изменений заявлен в сценариях использования — [opencode.ai/docs](https://opencode.ai/docs/) |
| Инструменты / function-calling | SUPPORTED | Встроенные tools, custom tools, контроль через permissions — навигация [opencode.ai/docs](https://opencode.ai/docs/) |
| Навыки / плагины | SUPPORTED | Agent Skills, Plugins, Ecosystem — разделы документации [opencode.ai/docs](https://opencode.ai/docs/) |
| Мультиагентность | SUPPORTED | Primary + subagents (general, explore, scout), task permissions — [opencode.ai/docs/agents](https://opencode.ai/docs/agents/) |
| Workflow / DAG | PARTIAL | Агентный цикл и субагенты есть, детерминированного DAG-движка нет |
| Ветвления / циклы | PARTIAL | Max steps у агентов; циклы — на уровне агентного цикла, не графа |
| Обработка ошибок / повторы / восстановление | PARTIAL | Undo изменений; при этом compaction может навсегда ломать сессию — [Issue #48942](https://github.com/anomalyco/opencode/issues/48942) |
| Планирование по расписанию / триггеры | PARTIAL | GitHub/GitLab-интеграции: запуск по комментарию `/opencode` в workflow — [opencode.ai/docs/github](https://opencode.ai/docs/github/) |
| Интеграции | SUPPORTED | MCP-серверы (local/remote/OAuth), LSP 30+ языков по вторичным данным, GitHub/GitLab — [opencode.ai/docs](https://opencode.ai/docs/) |
| Работа с файлами | SUPPORTED | Чтение/правка файлов — базовый сценарий — [opencode.ai/docs](https://opencode.ai/docs/) |
| Выполнение кода / терминал | SUPPORTED | Терминал — основная среда; bash с permission-паттернами |
| Браузер / GUI | PARTIAL | Web-интерфейс и IDE есть; браузерного GUI-агента общего назначения в проверенных страницах нет |
| Мессенджеры | UNSUPPORTED | В проверенных официальных источниках интеграций с мессенджерами нет |
| Локальные + облачные LLM | SUPPORTED | 75+ провайдеров и локальные модели — [nerdleveltech.com](http://nerdleveltech.com/opencode-open-source-ai-coding-agent-explained), страница Providers в [opencode.ai/docs](https://opencode.ai/docs/) |
| RAG | PARTIAL | Explore/scout-агенты исследуют кодовую базу; классического векторного RAG в проверенных страницах не заявлено |
| История / логирование | SUPPORTED | Сессии и их история хранятся; детали логирования в проверенных фрагментах ограничены |
| Трейсинг / наблюдаемость | PARTIAL | Server с SSE-событиями по вторичным описаниям; APM-трейсинга в проверенных официальных страницах нет |
| HITL | SUPPORTED | Permissions: allow/ask/deny по инструментам и паттернам bash — [opencode.ai/docs/permissions](https://opencode.ai/docs/permissions) |
| Песочница / безопасность | PARTIAL | Permissions и policies, external_directory ограничивает доступ к ФС; полноценной OS-песочницы в проверенных страницах не заявлено |
| Аутентификация | SUPPORTED | Provider auth, OAuth для MCP — разделы документации |
| API / расширяемость | SUPPORTED | SDK, Server (headless HTTP), Plugins, ACP Support — [opencode.ai/docs](https://opencode.ai/docs/) |
| UI | SUPPORTED | TUI, Web, IDE, Desktop — [opencode.ai/docs](https://opencode.ai/docs/) |
| Импорт / экспорт | PARTIAL | Конфиг JSON/Markdown-агенты; экспорта сессий как формата в проверенных страницах не выделено |
| Тестирование | PARTIAL | Агент может запускать тесты проекта как команды; собственного eval-фреймворка в проверенных страницах нет |
| Production-готовность | PARTIAL | Headless server и SDK позволяют CI-сценарии; продукт в первую очередь интерактивный |
| Оффлайн-работа | SUPPORTED | Локальные модели через провайдеров (Ollama и совместимые) при локальной установке |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| Model-agnostic: 75+ провайдеров, локальные модели | [nerdleveltech.com](http://nerdleveltech.com/opencode-open-source-ai-coding-agent-explained), [opencode.ai/docs](https://opencode.ai/docs/) | Вторичный обзор + официальная навигация Providers |
| Богатая агентная модель: build/plan/general/explore/scout + скрытые compaction/title/summary | [opencode.ai/docs/agents](https://opencode.ai/docs/agents/) | Официальная документация (primary) |
| Гранулярные permissions allow/ask/deny вплоть до bash-паттернов | [opencode.ai/docs/permissions](https://opencode.ai/docs/permissions) | Официальная документация (primary) |
| Headless server + SDK + ACP: встраивается в другие инструменты | [opencode.ai/docs](https://opencode.ai/docs/) | Официальная документация (primary, навигация) |
| Agent Skills как переносимые навыки | [opencode.ai/docs](https://opencode.ai/docs/) | Официальная документация (primary, навигация) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Compaction навсегда ломает сессию на Azure Foundry Responses (encrypted_content переигрывается в основную цепочку) | Высокая | Подтверждено issue | [Issue #48942](https://github.com/anomalyco/opencode/issues/48942) |
| Compaction отбрасывает регистрации MCP-инструментов | Средняя | Подтверждено issue | [Issue #40901](https://github.com/anomalyco/opencode/issues/40901) |
| У моделей без limit авто-compaction не срабатывает, сессии молча переполняют контекст | Средняя | Подтверждено issue | [Issue #28321](https://github.com/anomalyco/opencode/issues/28321) |
| Переезд sst/opencode → anomalyco/opencode и параллельная ветка v2 создают путаницу установки | Низкая | Подтверждено документацией | [opencode.ai/docs](https://opencode.ai/docs/) |
| Permission ask для субагентов может не доходить до пользователя (по вторичным трекерам известных проблем) | Средняя | Вторичные трекеры, upstream issue №48232 упомянут в источнике | [gaojunran/opencode-acp-bridge](https://github.com/gaojunran/opencode-acp-bridge) |

## 5. Неудовлетворённые потребности пользователей

- Надёжная compaction без потери истории и MCP-инструментов: [Issue #48942](https://github.com/anomalyco/opencode/issues/48942), [Issue #40901](https://github.com/anomalyco/opencode/issues/40901).
- Автоматическая compaction для всех провайдеров, включая модели без объявленного лимита: [Issue #28321](https://github.com/anomalyco/opencode/issues/28321).
- Доставка permission-запросов субагентов в headless/non-interactive режимах — по вторичному трекеру известных проблем OpenCode; прямой upstream-текст в ходе проверки не открывался, поэтому формулировка помечена как вторичная.
- Отдельных релевантных тредов Reddit с прямыми URL в ходе проверки надёжно верифицировать не удалось — UNKNOWN.

## 6. Технические ограничения

- **Модель исполнения**: клиент-серверная сессия на машине разработчика, агентный цикл «модель → инструменты → модель», субагенты как вложенные сессии.
- **Детерминизм**: недетерминирован; воспроизводимость зависит от модели и compaction, которая меняет контекст.
- **Параллелизм**: субагенты могут работать параллельно; лимиты и координация — на уровне сессии (детали в проверенных страницах ограничены).
- **Отмена / таймауты**: max steps ограничивает цикл; семантика отмены посреди вызова инструмента в проверенных фрагментах не детализирована — PARTIAL.
- **Идемпотентность**: встроенных гарантий нет; защита — undo/снапшоты файлов.
- **Восстановление после сбоя**: сессии переживают рестарт, но битая compaction может сделать сессию невосстановимой ([Issue #48942](https://github.com/anomalyco/opencode/issues/48942)).
- **Версионирование**: параллельное существование v1 и v2 с одним именем команды `opencode` — миграционный риск, прямо отмеченный в документации.

## 7. Вердикт и применимость к CyberPipe

**Вердикт**: сильнейший открытый терминальный кодовый агент по широте провайдеров и гранулярности permissions. Для разработчика — полноценная замена проприетарным CLI-агентам. Слабое место — compaction длинных сессий: именно там теряются инструменты и ломается история.

**Применимость к CyberPipe** (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-уровневый роутинг Tier0 regex / Tier1 ONNX / Tier2–3 LLM, TokenBudgetGovernor, Netrunner Mini-n8n DAG, SQLite WAL, Brainstem-песочница, HITL):

- **Прямое использование — частично, как внешний инструмент**: OpenCode можно вызывать headless (`opencode serve`, SDK) из NetrunnerScriptNode для кодовых задач, но встраивать его TypeScript-рантайм в .NET-ядро CyberPipe не следует.
- **Что заимствовать**: (1) permissions-модель allow/ask/deny с паттернами команд — прямой шаблон для Brainstem-песочницы и HitlApprovalNode: Tier0/Tier1 могут предразрешать безопасные паттерны; (2) разделение plan/build агентов — аналог «сначала план в Tier3, потом исполнение дешевле»; (3) Agent Skills как Markdown-навыки — соответствие SkillNode и формату SKILL.md; (4) урок compaction: в CyberPipe суммаризацию истории должен делать TokenBudgetGovernor детерминированно, не теряя регистр инструментов, с явным лимитом контекста на модель.
- **Что не подходит**: фокус только на коде и терминале; Telegram/Avalonia UX, DAG-оркестрация и долговременная память ассистента вне его модели.
- **Итог**: OpenCode — референс permissions и skill-формата и кандидат на внешний кодовый инструмент Netrunner, а не компонент ядра.
