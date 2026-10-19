# TASK-043 — Раннеры скриптов по языкам и средам

## Цель

Один проверяемый результат: запуск `skill-hack.*` нужным компилятором/интерпретатором по `runtime` из манифеста — с пред-/пост-валидацией по JSON Schema, таймаутом, настраиваемым лимитом памяти, ограниченным выводом и результатом `ScriptRunResult`; неподдерживаемый язык/среда — явная ошибка, а не тихий пропуск.

## Контекст

- Канон: ADR-004 — каждый скрипт исполняется своим компилятором/интерпретатором; поля манифеста: `runtime.language`, `languageVersion`, `environment`, `environmentVersion`, `entrypoint` (`skill-hack.*`).
- Исполнение только через порт песочницы `ISandboxProvider` (TASK-002; реальный Restricted Token + Job Object + Deny-ACL — TASK-023). Прямой `Process.Start` на хосте запрещён: раннеры обращаются только к порту.
- Лимит памяти — настройка пакета/пользователя (`maxMemoryMb`, может быть `null` = без лимита пакета, действует глобальная настройка), жёстких 64 МБ нет (Q9).

## Объём работ

- `IScriptRunner` + реестр `ScriptRunnerRegistry`: ключ — пара (`language`, `environment`) из манифеста; `CanRun(manifest)`, `RunAsync(ScriptRunRequest, CancellationToken) → ScriptRunResult`.
- Раннеры первого набора (каждый — отдельный класс за общим портом песочницы):
  - `CSharp` / `Roslyn` (`.csx`): компиляция в памяти с белым списком сборок.
  - `Python` / `CPython`: интерпретатор в песочнице.
  - `JavaScript` / `Node`: интерпретатор в песочнице.
  - Неизвестная пара язык/среда → `ScriptRunResult.Status = UnsupportedRuntime` с полями пары в ошибке; реестр расширяется регистрацией, не правкой исполнителя цепочки.
- `ScriptRunRequest`: пакет (`slug@version`, проверенный TASK-042), входной JSON, входные файлы (alias → read-only путь в рабочем каталоге запуска), рабочий каталог `data/netrunner-artifacts/{RunId}/{StepId}/` (настройка корня), `TimeoutMs` (из манифеста, перекрываем глобальной настройкой), `MaxMemoryMb?`.
- Конвейер запуска: валидация входа по `schema.input.json` и файлов по `io.inputFiles` (размер/расширение) → запуск в песочнице → сбор stdout/stderr с потолком (настройка, по умолчанию 64 КБ на поток, обрезка помечается) → валидация выхода по `schema.output.json` и обязательных `io.outputFiles` → `ScriptRunResult`.
- `ScriptRunResult`: `Status` (`Succeeded|SchemaValidationFailed|ScriptFailed|TimedOut|Cancelled|UnsupportedRuntime|DeferRetry`), `OutputJson`, `OutputFiles[{Alias, Path, Sha256, SizeBytes}]`, `ExitCode`, `StdoutTail`, `StderrTail`, `DurationMs`.
- Сигнал отложенного повтора: выходной JSON `{"$netrunnerControl":"DeferRetry","retryAfterSeconds":N,"reason":"..."}` или exit code 75 → `Status=DeferRetry` (процесс завершён, память освобождена; планирование повтора — TASK-044/TASK-045).
- Проверка целостности перед запуском: пакет принимается только с `PackageValidationResult.IsValid` от TASK-042; раннер сам файлы пакета не правит и хэши не пересчитывает.

## Вне рамок

- Реализация песочницы ОС (TASK-023; здесь фейк `ISandboxProvider`), валидация пакета (TASK-042), оркестрация цепочки и повторы (TASK-044), синтез скриптов, дополнительные языки (только механизм регистрации).

## Зависимости

- TASK-002
- TASK-042

## Контракты и интерфейсы

- `IScriptRunner`, `ScriptRunRequest`, `ScriptRunResult`, `ScriptRunStatus` (TASK-002 / эта задача).
- `ISandboxProvider.RunAsync(SandboxRunSpec) → SandboxRunResult` — порт из TASK-002 (фейк в тестах: исполняет фикстурный скрипт в временной папке без изоляции ОС; ограничение фиксируется в README тестов).
- `ISchemaValidator.Validate(schemaJson, payloadJson) → IReadOnlyList<string>` — порт (реализация на JSON Schema Draft 2020-12 выбирается внутри задачи).
- Предлагаемое размещение (to-be-created): `src/RouterAndSkills/CyberPipe.Skills.Netrunner/Runners/`.

## Результаты

- Реестр + 3 раннера, конвейер schema-проверок, фикстурные пакеты C#/Python/JS с `sample.fixture.json` ( вход → ожидаемый выход).

## Критерии приёмки

- [ ] Каждый из 3 фикстурных пакетов на своём `sample.fixture.json` даёт `Succeeded` и выход, равный `expectedOutput`; dry-run gate: несоответствие ожидаемому — `ScriptFailed`/`SchemaValidationFailed`, не `Succeeded`.
- [ ] Вход, нарушающий `schema.input.json`, отклоняется до запуска (фейк песочницы не вызван); выход, нарушающий `schema.output.json`, даёт `SchemaValidationFailed`.
- [ ] Скрипт-«вечный цикл» прерывается по `timeoutMs` со статусом `TimedOut`; отмена токеном — `Cancelled`.
- [ ] Манифест с парой (`Ruby`,`MRI`) даёт `UnsupportedRuntime` с именем пары; скрипт с exit 75 — `DeferRetry` с `retryAfterSeconds` из вывода.
- [ ] Ни один раннер не вызывает `Process.Start` напрямую (проверяется поиском по исходникам проекта раннеров в тесте/ревью; все запуски — через фейк-порт в тестах).

## Тестирование

- Уровень: unit + component.
- Как: фейк `ISandboxProvider` (реально запускает фикстурный интерпретатор только там, где он есть в CI; при отсутствии интерпретатора тест помечается `Skip` с причиной, а контракт проверяется на фейке, эмулирующем stdout/exit code), фикстуры пакетов из TASK-042. Без PostgreSQL.

## Команды проверки

```powershell
dotnet build CyberPipe.slnx
dotnet test tests/CyberPipe.Tests.Chains/ --filter "FullyQualifiedName~ScriptRunner"
```

(Путь тестового проекта — to-be-created, имя фиксируется в TASK-001.)

## Статус: Planned
