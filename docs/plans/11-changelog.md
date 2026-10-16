# 11. Лог архитектурных изменений (Changelog ревизии плана)

**Цель ревизии:** Убрать избыточное «энтерпрайз-усложнение», сделать систему **максимально быстрой, прозрачной и надежной**, превратить оркестратор в отзывчивого умного помощника (управление через Telegram и UI за миллисекунды), защитить хост от вайпа и аудио/медиа-саботажа (18+ на полную громкость) и **сократить расход токенов на 1–2 порядка** с жесткими механизмами паузы и остановки.

---

### 1. Удаление лишней сложности (Ускорение в ~100 раз и рост надежности)
* **Было:** Обязательный внешний брокер `RabbitMQ + MassTransit`, СУБД `PostgreSQL + pgvector` и тяжелое сквозное перешифрование каждого сообщения внутри одного процесса (`Envelope Encryption DEK/KEK`). Из-за этого простой запрос проходил десятки сетевых сериализаций, а без поднятых контейнеров система не могла запуститься.
* **Стало:** Сверхбыстрая внутрипроцессная шина на базе **`System.Threading.Channels`** (задержка `< 0.05 мс`) + локальная база **`SQLite в режиме WAL`** с асинхронной записью. Приложение запускается за `< 1 секунду` без единого внешнего контейнера. Шифрование (`DPAPI` / `AES-256-GCM`) оставлено там, где оно реально нужно: для хранения ключей/секретов на диске и чувствительных столбцов БД.

---

### 2. Детерминированный Оркестратор вместо «LLM-бюрократии»
* **Было:** `CEO Agent` и `SecurityAgent` проектировались как отдельные вызовы LLM, общающиеся с `Worker Agent` через саги. Один запрос пользователя вызывал 3–4 обращения к нейросети подряд.
* **Стало:** **`OrchestratorEngine`** и **`SecurityGuard`** переведены на **100% детерминированный C#-код (0 токенов)**. ИИ вызывается только тогда, когда задачу невозможно решить локальными правилами.

---

### 3. Каскадный Маршрутизатор (`4-Tier Intent Router`) — Снижение расхода токенов на порядки
Внедрен 4-уровневый фильтр на входе (`CyberPipe.Router`), отсекающий платные запросы к ИИ:
1. **`Tier 0` (Deterministic Fast-Path, `< 1 мс`, `0 токенов`):** Слэш-команды (`/play`, `/vol 40`, `/stop`) и регулярные шаблоны (*«включи музыку»*, *«пауза»*, *«потише»*) сразу вызывают компилированные C#-скиллы.
2. **`Tier 1` (Local ONNX Semantic Router + Cache, `5–15 мс`, `0 токенов`):** Локальная модель эмбеддингов в памяти (`multilingual-e5-small` + SIMD `TensorPrimitives`) понимает фразы своими словами и с опечатками, сопоставляя их со скиллами или забирая готовый ответ из локального `SemanticResponseCache`.
3. **`Tier 2` (Local SLM / Flash-Lite + Dynamic Tool Pruning, `~150 мс`):** Локальная микро-модель (`Ollama` 1.5B–3B за 0$) или самая дешевая модель парсит сложные аргументы. В промпт передаются только **2–3 подходящих тула** вместо 50 (экономия ~80% входных токенов).
4. **`Tier 3` (Cloud LLM + Sandbox):** Включается только для сложных задач (~5–10% запросов) с жесткой обрезкой вывода тулов (`ToolOutputTruncator` до 1 500 символов) и кэшированием системного префикса.

---

### 4. Железная защита ПК при управлении из Telegram (`HostAndMediaSafetyGuard`)
Чтобы через Telegram нельзя было вайпнуть систему или включить порно на полную громкость:
* **Жесткий периметр (`TelegramAuthGuard`):** Белый список числовых `TelegramUserId` с ролями `Owner` (владелец) и `Guest` (гость/семья). Чужие сообщения отбрасываются за `0.1 мс` (0 токенов).
* **Анти-Вайп (`Anti-Wipe & No Raw Shell`):** Команды из чата никогда не попадают в `cmd` / `powershell` хоста — они могут вызывать только закрытый список безопасных C#-скиллов (`MediaControlSkill`, `AudioVolumeSkill`). На уровне AST-фильтра жестко заблокированы любые команды удаления/форматирования (`rm -rf`, `Format-Volume`, `del /s`, реестр).
* **Ограничитель громкости (`Volume Clamp`):** Программный потолок громкости (`Math.Clamp`: например, максимум `60%` днем и `25%` ночью с `23:00` до `08:00`, плюс запрет резкого скачка `> +20%`). Даже если написать *«громкость 100%»*, выше безопасного лимита звук не поднимется.
* **Анти-18+ / NSFW фильтр и Белый список плееров:** Музыка включается только через доверенные источники (`Spotify`, `Яндекс.Музыка`, локальная папка). Поисковый запрос за `< 0.5 мс` проверяется локальным словарем стоп-слов 18+/порно/шок-контента, а произвольные внешние ссылки в браузере со звуком не открываются.

---

### 5. Механизмы Паузы, Остановки и Контроля Расходов (`TokenBudgetGovernor`)
* **Предварительный подсчет и 3 уровня лимитов:** Токены считаются локально (`Microsoft.ML.Tokenizers`) *до* вызова API. Действуют лимиты: на одну задачу (`Soft`/`Hard`), на скорость сжигания в минуту (`Velocity Circuit Breaker`) и дневной потолок в долларах (`DailyBudgetUsdCap`).
* **Автоматическая Пауза (`TaskPausedByBudget`):** Если сложная задача съела 70% своего бюджета или сделала > 3 шагов LLM, она **автоматически ставится на паузу** (состояние сохраняется в SQLite), а в Telegram приходят кнопки: `[▶️ Продолжить (+5k токенов)]`, `[⚡ Выдать что есть]`, `[🛑 Стоп]`.
* **Мгновенный Стоп-Кран (`Priority Kill-Switch`):** Команды `/stop` и `/pause` в Telegram (и кнопка в UI) идут по приоритетному каналу в обход очереди и за `< 50 мс` вызывают `CancellationTokenSource.Cancel()`, мгновенно обрывая HTTP-стрим LLM и убивая дочерние процессы.

---

### Сводка затронутых файлов документации:
* [`01-principles.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/01-principles.md) — Добавлены принципы простоты, 4-Tier роутера, защиты хоста из Telegram и контроллера бюджета.
* [`02-architecture-diagram.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/02-architecture-diagram.md) — Новая схема с быстрым путем (Fast-Path < 15 мс), `HostAndMediaSafetyGuard`, `TokenBudgetGovernor` и `Kill-Switch`.
* [`03-tech-stack-libraries.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md) — Замена тяжелых брокеров на `Channels`, `SQLite WAL`, `ONNX Runtime + TensorPrimitives`, `NAudio` и `Tokenizers`.
* [`04-agent-mesh-proposals.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/04-agent-mesh-proposals.md) — Детальное проектирование 4-Tier роутера, экономии токенов, авто-паузы бюджета и мгновенного `/stop`.
* [`05-security-agent.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/05-security-agent.md) — Правила защиты от вайпа ПК, `Volume Clamp` (60%/25%), NSFW/18+ фильтр для Telegram и детерминированный `SecurityGuard`.
* [`06-solution-structure.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/06-solution-structure.md) — Оптимизированное дерево проектов с модулями `CyberPipe.Router`, `CyberPipe.Skills.Host` и `CyberPipe.Security.Budget`.
* [`07-ui-isolation-and-arch-tests.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/07-ui-isolation-and-arch-tests.md) — Дашборд экономии токенов в UI и тесты `ArchUnitNET`, запрещающие обход роутера и запуск Raw Shell из скиллов.
* [`08-implementation-roadmap.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md) — Перестроен план спринтов: Спринт 1 и 2 сразу дают рабочего, безопасного и экономного Telegram-помощника.
* [`09-data-protection-and-state-hub.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/09-data-protection-and-state-hub.md) — Локальный `SemanticResponseCache` в SQLite WAL, `DPAPI` сейф секретов и пример единого конфига лимитов.
* [`10-sandbox-telemetry-and-heuristics.md`](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/10-sandbox-telemetry-and-heuristics.md) — Защита ПК от тормозов (`Resource Circuit Breaker`) и двойная обрезка логов (`ToolOutputTruncator` до 1 500 символов для экономии токенов).
