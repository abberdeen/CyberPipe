# TASK-001 — Каркас решения (solution skeleton)

## Цель

Создать пустой, собираемый каркас решения .NET 10: структуру проектов, правила зависимостей между проектами и тестовые проекты-заглушки, чтобы все последующие задачи добавляли код в готовые места без перестройки решения.

## Контекст

- Кода ещё нет: `CyberPipe.slnx` в корне репозитория пуст (`<Solution />`). Все пути ниже — к созданию (to-be-created).
- Канон: [DECISIONS.md](../../plans/DECISIONS.md), [17-adr-mvp.md](../../plans/initial_arch/17-adr-mvp.md), структура проектов — [06-solution-structure.md](../../plans/initial_arch/06-solution-structure.md) с поправками ADR: одна PostgreSQL + pgvector (без отдельных баз подсистем), Quartz.NET в той же PostgreSQL, собственные интерфейсы сообщений без фиксированного брокера, без DevTools/Git/Build-проектов, без роли Guest.
- Продукт: локальный персональный ассистент; Avalonia для всех ОС (фокус ручного тестирования — Windows); Service (служба Windows, `Program Files`) и Desktop — отдельные приложения, Desktop управляет Service.
- Цепочки только `Trigger -> Skill -> Script -> Delivery`; точки входа скриптовых пакетов — `skill-hack.*` (детали в TASK-042/TASK-043, здесь только место проекта-исполнителя в структуре).

## Объём работ

1. Заполнить `CyberPipe.slnx` проектами по структуре ниже (все — to-be-created, target framework `net10.0`):
   - `src/CyberPipe.Contracts/` — уровень 0, ноль ссылок на другие проекты решения.
   - `src/CyberPipe.Service/` — хост службы Windows (реализация хоста — TASK-020; здесь только пустой проект с точкой входа).
   - `src/CyberPipe.Desktop/` — Avalonia Desktop (реализация UI — TASK-061; здесь пустой стартовый проект Avalonia).
   - `src/CyberPipe.Cli/` — CLI/TUI (реализация — TASK-062; здесь пустой проект).
   - `src/CyberPipe.Brainstem/` — изолированный worker (реализация — TASK-022; здесь пустой проект, ссылается только на Contracts).
   - Проекты-заглушки под будущие области без реализации логики: `CyberPipe.Data` (02-data), `CyberPipe.Router` (04-router), `CyberPipe.Chains` (05-chains), `CyberPipe.Skills` (06-skills), `CyberPipe.Channels.Telegram` (07-channels-ui), `CyberPipe.Security` (08-security), `CyberPipe.Models` (09-models). Каждый — пустой class library с единственной допустимой ссылкой на `CyberPipe.Contracts`.
2. Настроить общие свойства сборки в `Directory.Build.props` (to-be-created, корень репозитория): `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`, единый `LangVersion`.
3. Создать тестовые проекты-заглушки (to-be-created): `tests/CyberPipe.Tests.Unit/`, `tests/CyberPipe.Tests.Architecture/` (проверка графа зависимостей), оба подключаются к решению.
4. Зафиксировать ациклический граф ссылок: все проекты ссылаются только на `CyberPipe.Contracts`; ссылки между feature-проектами на этом этапе запрещены (их добавит TASK-090 при компоновке).
5. Добавить `.editorconfig` (to-be-created) с базовыми правилами стиля, принятыми в решении.

## Вне рамок

- Реализация любых интерфейсов, DTO и бизнес-логики (TASK-002 и далее).
- Подключение PostgreSQL, миграции, Quartz.NET (TASK-010, TASK-011, TASK-045).
- Реализация Service/Desktop/CLI сверх пустой точки входа (TASK-020, TASK-021, TASK-061, TASK-062).
- Пакетный менеджер и версии библиотек сверх стандартных шаблонов .NET 10 и Avalonia; выбор библиотек — в профильных задачах.

## Зависимости

- Нет.

## Контракты и интерфейсы

- Контракт задачи — сам граф проектов: `CyberPipe.Contracts` не ссылается ни на один проект решения; `CyberPipe.Brainstem` и `CyberPipe.Desktop` ссылаются только на `CyberPipe.Contracts`.
- Имена проектов и путей из раздела «Объём работ» являются контрактом для всех последующих задач: переименования после этой задачи запрещены без отдельной задачи на переименование.

## Результаты

- Заполненный `CyberPipe.slnx`, собираемый одной командой.
- Проекты-заглушки по списку выше, `Directory.Build.props`, `.editorconfig`.
- Тестовые проекты-заглушки, запускаемые через `dotnet test`.

## Критерии приёмки

- `dotnet build CyberPipe.slnx` завершается без ошибок и предупреждений на чистом клоне.
- `dotnet test CyberPipe.slnx` проходит (допустимы только пустые/заглушечные тесты).
- Архитектурный тест подтверждает: у `CyberPipe.Contracts` нет ссылок на проекты решения; граф ссылок ацикличен.
- В решении нет проектов и идентификаторов, связанных с DevTools/Git/Build-навыками, ролью Guest, SQLite или Hangfire.

## Тестирование

- Уровень: unit (архитектурные тесты) + проверка сборки.
- Как: архитектурный тест в `CyberPipe.Tests.Architecture` перечисляет сборки решения и проверяет запрещённые ссылки; сборка и тесты запускаются командами ниже.

## Команды проверки

```powershell
dotnet build CyberPipe.slnx
dotnet test CyberPipe.slnx
```

## Статус: Done
