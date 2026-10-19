# PicoClaw (picoclaw)

> Дата проверки: 2026-10-19. Источники: только реально проверенные через web_search/web_fetch URL. Где доказательств недостаточно — `UNKNOWN`.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | PicoClaw |
| Официальный сайт | [picoclaw.io](https://picoclaw.io) — единственный официальный домен по предупреждению в README |
| Репозиторий | [github.com/sipeed/picoclaw](https://github.com/sipeed/picoclaw) |
| Документация | [docs.picoclaw.io](https://docs.picoclaw.io/) |
| Категория | Ultra-lightweight personal AI assistant, написанный на Go с нуля (не форк OpenClaw/NanoBot — README) |
| Назначение | Персональный ассистент для дешёвого железа: $10 hardware, <10MB RAM, ms boot |
| Аудитория | Владельцы SBC/RISC-V/старого железа, embedded-энтузиасты, пользователи каналов Telegram/DingTalk/QQ и др. |
| Статус на 2026-10-19 | Ранняя быстрая разработка; официальный запрет production до v1.0; в поиске виден релиз v0.2.9 (2026-05-28, по вторичному SKILL-описанию и README news) |
| Лицензия | MIT (бейдж license-MIT в README) |
| Развёртывание | Единый Go-бинарник x86_64/ARM64/MIPS/RISC-V/LoongArch, Go 1.25+ для сборки; `picoclaw onboard`, `picoclaw agent`, gateway + каналы; Docker упоминается в туториалах |

## 2. Возможности

| Возможность | Статус | Основание |
|---|---|---|
| Автономность | SUPPORTED | Gateway, cron/scheduler по вторичным описаниям; ядро — always-on ассистент |
| Планирование | PARTIAL | Агентный цикл + routing (`pkg/routing` виден в issue #3311); формального planner нет |
| Долговременная память | SUPPORTED | Memory заявлена на сайте/docs как часть ассистента; детали консолидации `UNKNOWN` |
| Контекст сессии | SUPPORTED | `pkg/session` (issue #3311), сессии каналов |
| Персистентность | SUPPORTED | Конфиг `~/.picoclaw/config.json`, workspace (вторичные operator playbooks) |
| Инструменты / function-calling | SUPPORTED | Tools в `pkg/tools`, web_fetch, MCP server management в Web UI (v0.2.9 по README news) |
| Навыки / плагины | PARTIAL | Skills-модель унаследована от жанра; каталога уровня ClawHub в проверенных источниках нет (`UNKNOWN`) |
| Мультиагентность | PARTIAL | Routing между агентами; «internal collaboration bus» только строится по дайджестам |
| Workflow / DAG | UNSUPPORTED | DAG-движка нет |
| Ветвления / циклы | PARTIAL | Агентный цикл; баг бесконечного повтора одинаковых ошибок инструментов до max_tool_iterations (issue #3311) показывает слабость циклов |
| Обработка ошибок / повторы / восстановление | PARTIAL | Есть max_tool_iterations; детект повторяющихся провалов — баг #3311 |
| Расписание / триггеры | SUPPORTED | Scheduler/cron упомянут во вторичных описаниях PicoClaw |
| Интеграции | SUPPORTED | MCP в Web UI, web search (Sogou-backed configurable), провайдеры-пресеты (запрос #3298 на AI Router) |
| Файлы | SUPPORTED | Workspace-инструменты |
| Терминал / исполнение кода | SUPPORTED | CLI agent, shell-инструменты |
| Браузер / GUI | PARTIAL | Web UI/launcher; браузерная автоматизация `UNKNOWN` |
| Мессенджеры | SUPPORTED | Telegram, Discord, Slack, DingTalk, Feishu, WeCom, LINE, QQ и др. (сайт picoclaw.io) |
| Локальные + облачные LLM | SUPPORTED | OpenRouter, LM Studio и пресеты провайдеров (YouTube-туториал, secondary); Ollama-совместимость прямо не верифицирована — `UNKNOWN` |
| RAG | UNKNOWN | Не подтверждён |
| История / логирование | SUPPORTED | История чатов в WebUI (баг видимости истории #2796 по дайджесту) |
| Трейсинг | UNKNOWN | Не подтверждён в проверенных источниках |
| HITL | UNKNOWN | Формальных approval-потоков в проверенных источниках не показано |
| Песочница / безопасность | PARTIAL | `allowed_cidrs` в launcher; при этом security issues #3069/#3077/#2307 (раздел 4). Официально: «may be unresolved security issues. Do not deploy to production before v1.0» |
| Аутентификация | PARTIAL | CIDR-ограничения launcher, токены каналов; RBAC `UNKNOWN` |
| API / расширяемость | SUPPORTED | Go-пакеты `pkg/providers`, `pkg/routing`, `pkg/tools`; MCP |
| UI | SUPPORTED | Web UI (локализация, включая японскую на 968 строк по дайджесту), CLI |
| Импорт / экспорт | UNKNOWN | Не подтверждено |
| Тестирование | PARTIAL | Go-тесты видны в issue #3311 (`pkg/bus` и др.); пользовательское тестирование сценариев `UNKNOWN` |
| Production-готовность | UNSUPPORTED | Прямой запрет в README: не деплоить в production до v1.0 |
| Офлайн | PARTIAL | Локальные модели через LM Studio возможны; основной сценарий — облачные провайдеры |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| <10MB RAM, старт <1с, железо за $10 (LicheeRV-Nano и др.) | [picoclaw.io](https://docs.picoclaw.io/), [README](https://raw.githubusercontent.com/sipeed/picoclaw/main/README.md) | Официальные сайт/README |
| Независимая Go-реализация с нуля, ~95% ядра сгенерировано AI-агентом с human-in-the-loop | [README](https://raw.githubusercontent.com/sipeed/picoclaw/main/README.md) | Официальный README |
| Широкая поддержка архитектур: RISC-V, ARM64, MIPS, LoongArch, x86_64 | [README](https://raw.githubusercontent.com/sipeed/picoclaw/main/README.md) | Официальный README |
| Честное предупреждение о непродакшн-статусе | [README](https://raw.githubusercontent.com/sipeed/picoclaw/main/README.md) | Официальный README |

## 4. Критика

| Проблема | Серьёзность | Статус | Источник |
|---|---|---|---|
| Bypass `allowed_cidrs` launcher через same-host reverse proxy (доверие к RemoteAddr) | High | Открыт (#3069, архивная копия issue) | [Issue #3069](https://github.com/sipeed/picoclaw/issues/3069) |
| SSRF bypass в `web_fetch` через special-use IPv4 `198.18.0.0/15`, affected <=0.2.9 | High | Открыт (#3077) | [Issue #3077](https://github.com/sipeed/picoclaw/issues/3077) |
| Process Hook RCE при публичном launcher | Critical | Сообщено как security policy issue #2307 | [Issue #2307](https://github.com/sipeed/picoclaw/issues/2307) |
| Повторяющиеся одинаковые провалы инструментов крутятся до max_tool_iterations, пользователь не получает ответа | Medium | Открыт | [Issue #3311](https://github.com/sipeed/picoclaw/issues/3311) |
| Ранняя стадия: недавние сборки 10–20MB RAM вместо целевых <10MB | Low | Признано в README | [README](https://raw.githubusercontent.com/sipeed/picoclaw/main/README.md) |

Примечание: источники по #3069/#3077/#2307 в ходе проверки открывались через архивные PDF-копии GitHub issue (archive.cvearchiver.com); сами номера/URL issue — штатные GitHub URL выше.

## 5. Неудовлетворённые потребности пользователей

По Reddit релевантных результатов web_search не найдено — `UNKNOWN`.

- Стриминг ответов в QQ-канале как в Telegram/Web (#3201 по дайджесту; первоисточник не открывался): [Issue #3201](https://github.com/sipeed/picoclaw/issues/3201)
- Пресет провайдера AI Router: [Issue #3298](https://github.com/sipeed/picoclaw/issues/3298)
- Детект зацикливания одинаковых ошибок инструментов вместо молчаливого достижения лимита: [Issue #3311](https://github.com/sipeed/picoclaw/issues/3311)
- Настраиваемое имя бота вместо hardcoded «PicoClaw» — по дайджесту закрыто PR #303; как потребность фиксируется здесь со статусом «удовлетворена»

## 6. Технические ограничения

- **Модель исполнения:** Go gateway + агентный цикл, bus/routing/session пакеты.
- **Детерминизм:** не гарантируется.
- **Параллелизм:** `UNKNOWN` в проверенных источниках.
- **Отмена/тайм-ауты:** max_tool_iterations есть, но без детекта повторных провалов (баг #3311); пользовательский cancel — `UNKNOWN`.
- **Идемпотентность:** не заявлена.
- **Восстановление:** персистентность конфига/сессий; checkpoint шагов нет.
- **Версионирование:** pre-1.0, быстрые слияния PR, возможны регрессии и рост RAM.

## 7. Вердикт и применимость к CyberPipe

**Вердикт:** выдающийся edge-эксперимент (Go, <10–20MB, RISC-V), но единственный из пяти с официальным «не в production до v1.0» и серией security issue в launcher/web_fetch.

**Для CyberPipe:**
- Заимствовать только идеи лёгкости: Tier0/Tier1 CyberPipe должны оставаться дешёвыми как PicoClaw-класс решений — быстрый старт, малый footprint для фонового Brainstem-процесса.
- Урок из #3311 напрямую для Netrunner: детект повторяющихся провалов узла и ранний fail с объяснением пользователю, а не молчаливое исчерпание лимита итераций; добавить в критерии LlmProcessNode/NetrunnerScriptNode повторов.
- SSRF/CIDR-уроки (#3069/#3077) применимы к любому web_fetch в Netrunner-скриптах: не доверять RemoteAddr за reverse proxy, блокировать special-use диапазоны.
- Как зависимость не применим (Go vs .NET 10, pre-1.0).
