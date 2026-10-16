# Архитектурная документация CyberPipe

Сверхбыстрый, надежный и экономный к токенам умный помощник и оркестратор автономных агентов на платформе **.NET 10** с управлением через **Telegram** и **Avalonia UI**, 4-уровневым локальным маршрутизатором намерений (**0 токенов на бытовые задачи**), жесткими предохранителями безопасности хоста (**Anti-Wipe & Media/Volume Guard**), контроллером бюджета (**TokenBudgetGovernor** с авто-паузой) и подключаемыми песочницами.

---

## 📚 Разделы документации

1. [**01. Фундаментальные принципы**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/01-principles.md)  
   *Простота и скорость без внешних брокеров, каскадный 4-Tier роутер (0 токенов на рутину), защита ПК от вайпа и 18+ контента на полной громкости, контроллер бюджета токенов с авто-паузой и мгновенным стоп-краном.*

2. [**02. Диаграмма взаимодействия и защищенных потоков данных**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/02-architecture-diagram.md)  
   *Mermaid-схема двух путей: мгновенный Fast-Path (< 20 мс, 0 токенов) для скиллов помощника (музыка, звук, таймеры) и защищенный конвейер для сложных агентных задач под контролем `TokenBudgetGovernor`.*

3. [**03. Технологический стек и библиотеки (.NET 10)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md)  
   *Быстрый локальный стек без лишних зависимостей: `System.Threading.Channels`, `SQLite WAL`, `ONNX Runtime + SIMD TensorPrimitives` (локальные эмбеддинги), `NAudio`, `Telegram.Bot`, `Microsoft.Extensions.AI` + `Tokenizers`.*

4. [**04. Каскадный Маршрутизатор (Zero-Token Fast-Path), Контроль Бюджета и Пауза/Стоп**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/04-agent-mesh-proposals.md)  
   *Архитектура `Tier 0` (Regex) ➔ `Tier 1` (Local ONNX + Semantic Cache) ➔ `Tier 2` (Local SLM + Dynamic Tool Pruning) ➔ `Tier 3` (Cloud LLM). Механизмы авто-паузы с кнопками в Telegram и мгновенного `/stop` (< 50 мс).*

5. [**05. Безопасность Хоста (Anti-Wipe & Media Guard), Периметр Telegram и SecurityGuard**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/05-security-agent.md)  
   *Whitelist авторизация Telegram (`Owner` vs `Guest`), запрет Raw Shell из чата, жесткий потолок громкости (`Volume Clamp` 60%/25%), локальный фильтр 18+/NSFW, AST-анализ скриптов и Stealth OSINT.*

6. [**06. Структура Solution и проектов (`CyberPipe.slnx`)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/06-solution-structure.md)  
   *Прозрачное дерево проектов с выделенными модулями `CyberPipe.Router`, `CyberPipe.Skills.Host`, `CyberPipe.Security.Guards`, `CyberPipe.Security.Budget` и `CyberPipe.Storage.Sqlite`.*

7. [**07. Изоляция UI, Панель Контроля Расходов и Архитектурные Тесты (ArchUnitNET)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/07-ui-isolation-and-arch-tests.md)  
   *Дашборд экономии токенов и безопасности в Avalonia UI + автоматические тесты `ArchUnitNET`, гарантирующие невозможность запуска шелла из скиллов хоста или обхода фильтра бюджета.*

8. [**08. План поэтапной реализации**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md)  
   *Прагматичный план на 5 спринтов: уже в Спринте 1–2 запускается быстрый и безопасный Telegram-помощник с управлением музыкой за 0 токенов и контролем бюджета.*

9. [**09. Защита данных, Санитизация, Локальный Кэш и Хранение (SQLite WAL)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/09-data-protection-and-state-hub.md)  
   *Очистка секретов (`[REDACTED]`), локальный сейф ключей (`DPAPI` / `AES-256-GCM`), `SemanticResponseCache` в SQLite WAL и единый прозрачный конфиг лимитов.*

10. [**10. Мониторинг Ресурсов ПК и Сжатие Телеметрии Песочниц**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/10-sandbox-telemetry-and-heuristics.md)  
    *Защита ПК от фризов (`Resource Circuit Breaker`), обрезка вывода тулов (`ToolOutputTruncator` до 1 500 символов для экономии 99% токенов на логах) и внеполосный дайджест песочниц.*

11. [**11. Лог архитектурных изменений (Changelog ревизии)**](file:///c:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/11-changelog.md)  
    *Краткая сводка всех внесенных улучшений: отказ от избыточного брокера, каскадная экономия токенов, защита от вайпа и 18+ на полной громкости, механизмы паузы и остановки.*
