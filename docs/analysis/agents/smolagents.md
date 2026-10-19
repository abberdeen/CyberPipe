# smolagents (Hugging Face) — анализ агентной библиотеки

> Дата проверки источников: 2026-10-19. Все URL ниже получены через web_search/web_fetch в ходе проверки. Если доказательств недостаточно, статус помечен как UNKNOWN.

## 1. Идентификация

| Поле | Значение |
|---|---|
| Официальное название | smolagents |
| Официальный сайт / документация | [huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index) |
| Репозиторий | [github.com/huggingface/smolagents](https://github.com/huggingface/smolagents) |
| Категория | Минималистичная открытая Python-библиотека построения агентов («barebones library for agents that think in code») |
| Назначение | Быстрое создание агентов в несколько строк кода; акцент на CodeAgent, который пишет действия сразу на Python, а не JSON tool-calls |
| Целевая аудитория | Python-разработчики и исследователи, которым нужна прозрачная, хакaбельная агентная логика без тяжёлых абстракций |
| Статус на 2026-10-19 | Активный проект Hugging Face; документация и репозиторий доступны и развиваются. Точную последнюю версию релиза на дату проверки независимо перепроверить не удалось — UNKNOWN |
| Лицензия | Apache-2.0 — [huggingface.co/blog/tegridydev/ai-agents-list-2026](https://huggingface.co/blog/tegridydev/ai-agents-list-2026-frameworks-agentic-harnesses), бейдж лицензии в репозитории [github.com/huggingface/smolagents](https://github.com/huggingface/smolagents) |
| Развёртывание | Локально как Python-пакет (`pip install 'smolagents[toolkit]'`); исполнение кода — локально или в песочницах Modal/Blaxel/E2B/Docker |

Философия из официальных доков: логика агента укладывается примерно в тысячу строк кода, абстракций — минимум поверх «сырого» кода ([huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index)).

## 2. Возможности

Статусы: SUPPORTED / PARTIAL / PLANNED / UNSUPPORTED / UNKNOWN / NOT_APPLICABLE. Приведены только применимые строки.

| Возможность | Статус | Основание |
|---|---|---|
| Автономность агентов | SUPPORTED | Multi-step агент: цикл «LLM решает → действие → наблюдение» — [intro_agents](https://huggingface.co/docs/smolagents/en/conceptual_guides/intro_agents) |
| Планирование | PARTIAL | Планирование — внутри кодогенерации CodeAgent (циклы, условия, функции в одном действии); отдельного планировщика в проверенных страницах нет |
| Долговременная память | UNSUPPORTED | В проверенных официальных страницах долговременной памяти между запусками не заявлено; есть только память шагов внутри прогона (PARTIAL внутри сессии, см. ниже) |
| Сессионный контекст | SUPPORTED | Memory списка шагов внутри `agent.run()` — [intro_agents](https://huggingface.co/docs/smolagents/en/conceptual_guides/intro_agents) |
| Персистентность | UNSUPPORTED | В проверенных источниках сохранения/восстановления прогонов не заявлено |
| Инструменты / function-calling | SUPPORTED | ToolCallingAgent (JSON/text) + CodeAgent (код) — [huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index) |
| Навыки / плагины | PARTIAL | Инструменты с Hub (Spaces как инструменты), MCP, LangChain-инструменты; понятия «навыков» как в CyberPipe — нет |
| Мультиагентность | PARTIAL | Агент может запускать другого агента (высший уровень agency в таблице доков), но командной оркестрации уровня CrewAI/AutoGen нет — [intro_agents](https://huggingface.co/docs/smolagents/en/conceptual_guides/intro_agents) |
| Workflow / DAG | UNSUPPORTED | DAG-воркфлоу нет; воркфлоу — это сгенерированный Python-код внутри прогона |
| Ветвления / циклы | SUPPORTED | Нативно в коде CodeAgent: loops, conditionals, function nesting — [huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index) |
| Обработка ошибок / повторы / восстановление | PARTIAL | Ошибки исполнения попадают в наблюдения и цикл продолжается; формальных retries/recovery-политик в проверенных страницах не описано |
| Планирование по расписанию / триггеры | UNSUPPORTED | В проверенных официальных источниках планировщика нет |
| Интеграции | SUPPORTED | Hub Spaces, MCP-серверы, LangChain-инструменты — [huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index) |
| Работа с файлами | PARTIAL | Через инструменты и код; выделенного файлового слоя в проверенных страницах нет |
| Выполнение кода / терминал | SUPPORTED | Ядро CodeAgent: исполнение сгенерированного Python — [secure_code_execution](https://huggingface.co/docs/smolagents/en/tutorials/secure_code_execution) |
| Браузер / GUI | PARTIAL | Vision-пример web_browser в туториалах; полноценного GUI-агента в проверенных страницах нет |
| Мессенджеры | UNKNOWN | В проверенных официальных источниках интеграция с Telegram/мессенджерами не подтверждена |
| Локальные + облачные LLM | SUPPORTED | Model-agnostic: Inference Providers, OpenAI/Anthropic через LiteLLM, локально Transformers/Ollama — [huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index) |
| RAG | PARTIAL | Паттерн agentic RAG показан в курсе HF (CodeAgent + поиск), встроенного RAG-движка нет — вторичный источник [huggingface.co/learn/agents-course](https://huggingface.co/learn/agents-course/unit2/smolagents/retrieval_agents) |
| История / логирование | PARTIAL | Шаги прогона доступны программно; отдельной системы логов/аудита в проверенных страницах не описано |
| Трейсинг / наблюдаемость | UNKNOWN | В проверенных официальных страницах трейсинг не подтверждён |
| HITL (человек в цикле) | UNKNOWN | В проверенных официальных страницах approval-механизма не найдено |
| Песочница / безопасность | SUPPORTED | Sandboxed execution: Modal, Blaxel, E2B, Docker; собственный LocalPythonExecutor с AST-ограничениями — [secure_code_execution](https://huggingface.co/docs/smolagents/en/tutorials/secure_code_execution). Важно: локальный исполнитель прямо назван небезопасной границей (см. раздел 4) |
| Аутентификация | UNKNOWN | В проверенных официальных страницах не подтверждена (доступ к Hub — по токенам HF, вне проверенного фрагмента) |
| API / расширяемость | SUPPORTED | Кастомные инструменты, кастомные модели, CLI (smolagent, webagent) |
| UI | PARTIAL | Gradio Spaces через Hub-интеграцию; собственного UI у библиотеки нет |
| Импорт / экспорт | PARTIAL | Share/load агентов и инструментов через Hub; форматов экспорта воркфлоу нет |
| Тестирование | UNKNOWN | В проверенных официальных страницах eval/test-фреймворка не найдено |
| Production-готовность | PARTIAL | Позиционируется как лёгкая библиотека; production-функции (durable execution, трейсинг, HITL) нужно достраивать |
| Оффлайн-работа | SUPPORTED | Локальные модели Transformers/Ollama без облака — [huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index) |

## 3. Сильные стороны

| Утверждение | Источник | Тип доказательства |
|---|---|---|
| CodeAgent пишет действия кодом: композиция, циклы и условия в одном шаге вместо цепочки JSON-вызовов; по вторичным оценкам — до ~30% меньше шагов траектории | [huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index), [businessanalytics.substack.com](https://businessanalytics.substack.com/p/freemium-how-test-time-compute-overturns) | Официальная документация (primary) + вторичный анализ |
| Минимализм: ~1000 строк агентной логики, легко аудировать и форкать | [huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index) | Официальная документация (primary) |
| Честная модель угроз исполнения кода: перечислены LLM-ошибки, supply-chain, prompt injection, злоупотребление публичными агентами | [secure_code_execution](https://huggingface.co/docs/smolagents/en/tutorials/secure_code_execution) | Официальная документация (primary) |
| Model-agnostic и tool-agnostic: LiteLLM, локальные модели, MCP, LangChain-инструменты, Hub Spaces | [huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index) | Официальная документация (primary) |
| Мультимодальность: текст, vision, video, audio | [huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index) | Официальная документация (primary) |

## 4. Критика

| Проблема | Серьёзность | Статус | Источники |
|---|---|---|---|
| Встроенный LocalPythonExecutor — не security boundary: ограничения обходятся, нельзя использовать как границу безопасности | Высокая | Официально признано | [github.com/huggingface/smolagents](https://github.com/huggingface/smolagents) (README-формулировка, зафиксированная в поиске по зеркалам репозитория) и [secure_code_execution](https://huggingface.co/docs/smolagents/en/tutorials/secure_code_execution) |
| Исполнение LLM-кода по умолчанию локально — риск вредоносных команд при ошибке модели или prompt injection | Высокая | Официально признано | [secure_code_execution](https://huggingface.co/docs/smolagents/en/tutorials/secure_code_execution) |
| Нет персистентности, долговременной памяти, планировщика, HITL из коробки | Средняя | Следует из проверенных доков (отсутствие функций) | [huggingface.co/docs/smolagents/en/index](https://huggingface.co/docs/smolagents/en/index) |
| Мультиагентность минималистична: нет командных паттернов, делегирования и консенсуса уровня CrewAI/AutoGen | Средняя | Следует из доков | [intro_agents](https://huggingface.co/docs/smolagents/en/conceptual_guides/intro_agents) |

Прямых пользовательских issue-тредов с конкретными номерами в ходе проверки надёжно верифицировать удалось меньше, чем по другим продуктам; раздел 5 фиксирует это честно, без выдуманных ссылок.

## 5. Неудовлетворённые потребности пользователей

- Безопасное локальное исполнение без внешних песочниц: официальная документация сама подталкивает к Modal/E2B/Docker, то есть «безопасно из коробки локально» пользователи не получают — [secure_code_execution](https://huggingface.co/docs/smolagents/en/tutorials/secure_code_execution).
- Персистентность и продолжение прерванных прогонов: в проверенных источниках функции нет, потребность выводится из отсутствия (косвенное доказательство, помечено как вывод, а не цитата пользователя).
- Конкретных открытых GitHub Issues/Discussions/Reddit-тредов с прямыми URL, которые удалось открыть и верифицировать в ходе этой проверки, недостаточно для отдельного списка — UNKNOWN. Ссылки не выдумываем.

## 6. Технические ограничения

- **Модель исполнения**: синхронный цикл multi-step агента; CodeAgent исполняет сгенерированный Python (локальный AST-интерпретатор или внешняя песочница).
- **Детерминизм**: низкий. Код генерирует LLM на каждом шаге; одинаковая задача может дать разный код и разное число шагов.
- **Параллелизм**: внутри сгенерированного кода возможен, фреймворк параллельные прогоны/команды не оркестрирует.
- **Отмена / таймауты**: лимит шагов есть концептуально (цикл до удовлетворительного состояния), но формальной семантики cancel/timeout в проверенных страницах не зафиксировано — UNKNOWN.
- **Идемпотентность**: отсутствует как понятие; сгенерированный код может повторять побочные эффекты при ретраях цикла.
- **Восстановление после сбоя**: не поддерживается; падение процесса = потеря прогона.
- **Версионирование**: библиотека молодая и минималистичная; API компактный, но стабильность гарантий в проверенных источниках не зафиксирована — UNKNOWN.

## 7. Вердикт и применимость к CyberPipe

**Вердикт**: лучшая в подборке иллюстрация принципа «агент пишет код, а не JSON». Идеальна для лёгких, аудируемых, локальных агентов и экспериментов. Для production-систем с памятью, расписанием, HITL и восстановлением — только ядро, всё остальное придётся строить вокруг.

**Применимость к CyberPipe** (локальный персональный ассистент, .NET 10, Telegram+Avalonia, 4-уровневый роутинг Tier0 regex / Tier1 ONNX / Tier2-3 LLM, TokenBudgetGovernor, Netrunner Mini-n8n DAG, SQLite WAL, Brainstem-песочница, HITL):

- **Как зависимость — нет** (Python-библиотека в .NET-продукте), **как концептуальный референс — да, сильный**.
- **Что заимствовать**: (1) CodeAgent-паттерн для NetrunnerScriptNode: LLM пишет скрипт целиком (циклы/условия внутри), а не дёргает DAG по одному узлу — это экономит токены и ходы, что прямо поддерживает TokenBudgetGovernor; (2) честную модель угроз исполнения кода (LLM-ошибка, prompt injection, supply-chain) — готовая основа для threat-model Brainstem; (3) шкалу agency из intro_agents для калибровки Tier2/Tier3: чем выше автономность, тем строже песочница и HITL.
- **Главное предупреждение**: LocalPythonExecutor нельзя копировать как «песочницу». CyberPipe правильно делает ставку на отдельный Brainstem-процесс; smolagents подтверждает, что AST-ограничения в процессе — не граница безопасности.
- **Итог**: smolagents — эталон минимализма и кодовых действий для LlmProcessNode/NetrunnerScriptNode, но не источник памяти, DAG или надёжности.
