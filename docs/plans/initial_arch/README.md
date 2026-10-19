> **Решения владельца 2026-10-19 ([DECISIONS.md](../DECISIONS.md), [17-adr-mvp.md](17-adr-mvp.md)):** хранение - одна PostgreSQL + pgvector (PostgreSQL/шифрование на уровне PostgreSQL/диска убраны); цепочки только Trigger -> Skill -> Script -> Delivery (термины цепочка/цепочка/workflow/граф удалены); Telegram - первый канал, не единственный; Service и Desktop - разные приложения. При расхождении верны DECISIONS.md и 17-adr-mvp.md.

# Архитектурная документация CyberPipe

> [!CAUTION]
> **Статус плана: pre-implementation (AUD-01).** Репозиторий пока docs-only (`CyberPipe.slnx` пуст, кода `.cs` нет).
> Реализация продукта начинается только после закрытия этапа 0 (ADR + реестр БД + PoC шифрования и изоляции)
> из [`08-implementation-roadmap.md`](08-implementation-roadmap.md).
> До этого допустимы только ADR, реестр БД и проверочные PoC по [`muse-wonder-audit.md`](muse-wonder-audit.md) (раздел K.1).

**Канонические решения (ответы на вопросы L1-L6 аудита, уточнено решениями владельца 2026-10-19):** хранение - одна **PostgreSQL + pgvector** на проект, таблиц сколько угодно, отдельных мелких баз нет; обмен сообщениями пишется через **собственные интерфейсы проекта**, чтобы реализацию транспорта можно было заменить, не переписывая логику (конкретный внешний брокер в планах не фиксируется и обязательным не является); целевые платформы - сборки Avalonia под все ОС при фокусе ручного тестирования на Windows; DevTools убраны полностью (Q14); SLO - целевые ориентиры, а не ворота приемки (сеть Telegram в локальные замеры не входит, методика - см. 15, разд. 2). Полный журнал: [DECISIONS.md](../DECISIONS.md).

Сверхбыстрый, надежный и экономный к токенам умный помощник и оркестратор автономных агентов на платформе **.NET 10** с управлением через **Telegram** и **Avalonia UI**, 4-уровневым локальным маршрутизатором намерений (**0 токенов на бытовые задачи**), жесткими предохранителями безопасности хоста (**Anti-Wipe & Media/Volume Guard**), контроллером бюджета (**TokenBudgetGovernor** с авто-паузой) и подключаемыми песочницами.

---

## 📚 Разделы документации

1. [**01. Фундаментальные принципы**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/01-principles.md)  
   *Простота и скорость без внешних брокеров, каскадный 4-Tier роутер (0 токенов на рутину), защита ПК от вайпа и 18+ контента на полной громкости, контроллер бюджета токенов с авто-паузой и мгновенным стоп-краном.*

2. [**02. Диаграмма взаимодействия и защищенных потоков данных**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/02-architecture-diagram.md)  
   *Mermaid-схема двух путей: мгновенный Fast-Path (локальный бюджет < 20 мс, 0 токенов; сеть Telegram не входит, см. методику SLO в 15) для скиллов помощника (музыка, звук, таймеры) и защищенный конвейер для сложных агентных задач под контролем `TokenBudgetGovernor`.*

3. [**03. Технологический стек и библиотеки (.NET 10)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md)  
   *Быстрый локальный стек без лишних зависимостей: `собственная абстракция сообщений (транспорт транспорт (реализация не фиксируется) по умолчанию, см. 03)`, `PostgreSQL`, `ONNX Runtime + SIMD TensorPrimitives` (локальные эмбеддинги), `NAudio`, `Telegram.Bot`, `Microsoft.Extensions.AI` + `Tokenizers`.*

4. [**04. Каскадный Маршрутизатор (Zero-Token Fast-Path), Контроль Бюджета и Пауза/Стоп**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/04-agent-mesh-proposals.md)  
   *Архитектура `Tier 0` (Regex) ➔ `Tier 1` (ONNX + Semantic Cache) ➔ `Tier 2` (Легкая задача: `Local` или `RemoteCloud` по выбору из UI + Dynamic Tool Pruning) ➔ `Tier 3` (Сложная задача: `Local` или `RemoteCloud`). Авто-пауза в Telegram и мгновенный локальный `/stop` (< 50 мс без учета сети Telegram).*

5. [**05. Безопасность Хоста (Anti-Wipe & Media Guard), Периметр Telegram и SecurityGuard**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/05-security-agent.md)  
   *Whitelist авторизация Telegram (`Owner` vs `Guest (удалён решением Q18: только Owner)`), запрет Raw Shell из чата, жесткий потолок громкости (`Volume Clamp` 60%/25%), локальный фильтр 18+/NSFW, AST-анализ скриптов и Stealth OSINT.*

6. [**06. Структура Solution и проектов (`CyberPipe.slnx`)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/06-solution-structure.md)  
   *Прозрачное дерево проектов с выделенными модулями `CyberPipe.Router`, `CyberPipe.Skills.Host`, `CyberPipe.Security.Guards`, `CyberPipe.Security.Budget` и `CyberPipe.Storage.Postgres`.*

7. [**07. Изоляция UI, Панель Контроля Расходов и Архитектурные Тесты (ArchUnitNET)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/07-ui-isolation-and-arch-tests.md)  
   *Дашборд экономии токенов, Конфигуратор моделей по Tiers (`Local` vs `RemoteCloud`) в Avalonia UI + автоматические тесты `ArchUnitNET`.*

8. [**08. План поэтапной реализации**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md)  
   *Прагматичный план на 5 спринтов с четкими критериями приемки (DoD): уже в Спринте 1 запускаются базовый `CyberPipe.Storage.Postgres`, тесты `ArchUnitNET` и `CyberPipe.Tests.Isolation`, `Brainstem.exe` и управление медиа из Telegram за 0 токенов.*

9. [**09. Защита данных, Санитизация, Локальный Кэш и Хранение (PostgreSQL)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/09-data-protection-and-state-hub.md)  
   *Выходной шлюз очистки секретов (`CentralDataSanitizer` на границе Egress без слепой обратной подстановки), персистентный сейф ключей (`DPAPI` / `LibSecret` / `Keychain` + файловая изоляция ОС), раздельные `SemanticIntentCache` и ролевой `SemanticResponseCache`, миграции и бэкапы PostgreSQL.*

10. [**10. Мониторинг Ресурсов ПК и Сжатие Телеметрии Песочниц**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/10-sandbox-telemetry-and-heuristics.md)  
    *Инспекция железа `IHardwareCapabilitiesProbe`, защита ПК от фризов (`Resource Circuit Breaker`), обрезка вывода тулов (`ToolOutputTruncator` до 1 500 символов) и внеполосный дайджест песочниц.*

11. [**11. Лог архитектурных изменений (Changelog ревизии)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/11-changelog.md)  
    *Краткая сводка всех внесенных улучшений: отказ от избыточного брокера, каскадная экономия токенов, защита от вайпа и 18+ на полной громкости, механизмы паузы и остановки.*

12. [**12. Каталог функций и возможностей: Личный, Инженерный и Бизнес-Помощник**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/12-assistant-capabilities-and-skills.md)  
    *Полный перечень системных возможностей (локальный голос `Whisper.net` за 0 токенов, гибридная память «Второй мозг» на `PostgreSQL: полнотекстовый поиск + pgvector + SIMD`, проактивные брифинги) и прикладных скиллов для личных дел, программирования (`Git Radar`, `Build/Test Watchdog`, мониторинг серверов) и бизнеса (`Personal CRM`, запись и выжимка созвонов через `NAudio`, проверка договоров).*

13. [**13. Конфигурация Tiers (Local vs Remote), Сервис Совместимости Железа и Curated-Каталог Моделей**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/13-model-advisor-and-tier-configuration.md)  
    *Четкое разделение `Local` (через локальный HTTP-сервер Ollama / LM Studio) и `RemoteCloud` моделей в UI и ядре, связка `IHardwareCapabilitiesProbe` + `IModelCompatibilityAdvisor` с учетом KV-Cache, Curated-каталог из 15 локальных и 8 облачных моделей.*

14. [**14. Устойчивость ядра `CyberPipe.exe` к сбоям, Контроль Буферов и Внутрипроцессная Изоляция (`Bulkhead`)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/14-fault-isolation-and-resilience.md)  
    *Внутрипроцессная изоляция `CyberPipe.exe`: `BackgroundServiceExceptionBehavior.Ignore`, `IsolatedWorkerBase` в базовом слое без циклических ссылок, раздельные отсеки `Bulkhead Lanes`, `BoundedChannel` + `QuotaBoundedStream` и журнал `InFlightOperations` для тяжелых и внешних задач.*

15. [**15. Исполняемые файлы, Межпроцессная Изоляция (`CyberPipe.Brainstem`), Безопасность IPC и Системный CLI / TUI**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md)  
    *Каноническая спецификация точек входа (`CyberPipe.exe`, `CyberPipe.UI.Desktop.exe`, `worker/CyberPipe.Brainstem.exe`), кроссплатформенной изоляции воркера (`AppContainer` / `Restricted Token` с `SidsToRestrict` + DACL `Deny` в Windows, `bwrap` в Linux, `Seatbelt` в macOS), раздельных пулов `Whisper` и `SingleUse PDF/DOCX`, проверки `worker.manifest.json`, тестов `CyberPipe.Tests.Isolation` и матрицы команд CLI.*

16. [**16. Ассистент Netrunner: Движок Каскадных Задач (цепочка Trigger → Skill → Script → Delivery), HITL и Стандарт Пакетов Скриптов**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/16-netrunner-etl-and-workflow-engine.md)  
    *Архитектура специализированного ETL-ассистента Netrunner: стандарт пакетов скриптов с 6 обязательными файлами (`netrunner.manifest.json`, `SKILL.md`, `schema.input.json`, `schema.output.json`, `skill-hack.*`, `tests/sample.fixture.json`), каскадный движок цепочка (`SkillNode ➔ NetrunnerScriptNode ➔ ConditionNode ➔ LlmProcessNode ➔ HitlApprovalNode ➔ DeliveryNode`), транзакционные чекпоинты в `netrunner` (PostgreSQL) и контроль целостности SHA-256.*

17. [**Экспертный аудит плана (Muse Wonder Audit)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/muse-wonder-audit.md)  
    *Комплексный стресс-тест архитектурных планов моделью `muse-wonder-1.0`: реестр замечаний (`AUD-01` – `AUD-15`), противоречия шины/БД/шифрования, анализ рисков и поэтапный план доработок (`K.1` – `K.4`).*

18. [**17. Реестр решений MVP (ADR-001 – ADR-003)**](17-adr-mvp.md)
   *Канонические ответы на вопросы L1–L6 аудита: шина, реестр БД, шифрование/шифрование на уровне PostgreSQL/диска, целевая ОС, песочница DevTools, методика SLO.*





