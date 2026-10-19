# TASK-042 — Валидатор пакета скрипта (`skill-hack.*`, `netrunner.manifest.json`)

## Цель

Один проверяемый результат: валидатор пакета скрипта, который пропускает только пакет с корректным `netrunner.manifest.json` (поля `runtime.language/languageVersion/environment/environmentVersion/entrypoint`), существующей точкой входа `skill-hack.*`, схемами и совпавшими SHA-256; остальное отклоняет с кодами ошибок. Скрипт при этом не исполняется.

## Контекст

- Канон: ADR-004 (перекрывает read-only шаблон `.agents/skills/netrunner-script-package/`): точка входа — `skill-hack.*` (`skill-hack.csx`, `skill-hack.py`, `skill-hack.js` и т.п.), не `main.*`; в манифесте обязательны `runtime.language`, `runtime.languageVersion`, `runtime.environment`, `runtime.environmentVersion`, `runtime.entrypoint`.
- Структура пакета (документ 16, §2.1, с правкой ADR-004): `netrunner.manifest.json`, `SKILL.md`, `schema.input.json`, `schema.output.json`, `skill-hack.*`, `tests/sample.fixture.json` в `data/netrunner-packages/<slug>/<version>/` (путь — настройка).
- `maxMemoryMb` жёсткого значения по умолчанию не имеет (Q9) — валидатор не подставляет 64.

## Объём работ

- DTO манифеста `NetrunnerManifest` (TASK-002): `slug`, `name`, `version`, `description`, `runtime{language, languageVersion, environment, environmentVersion, entrypoint, timeoutMs, maxMemoryMb?, deterministic}`, `io{inputSchemaFile, outputSchemaFile, inputFormats, outputFormats, inputFiles[], outputFiles[]}`, `permissions{allowNetwork=false по умолчанию, allowedDomains, filesystemScope, requiredVaultSecretRefs}`, `integrity.filesSha256`.
- `INetrunnerPackageValidator.ValidatePackage(packageDir) → PackageValidationResult` (`IsValid`, `Manifest?`, `Errors[{Code, File, Message}]`), проверки по порядку, все ошибки собираются (не только первая):
  1. Наличие 6 обязательных файлов; отсутствие любого → `Package.MissingFile`.
  2. Манифест — валидный JSON; отсутствуют обязательные поля runtime (`language`, `languageVersion`, `environment`, `environmentVersion`, `entrypoint`) → `Manifest.MissingRuntimeField` с именем поля.
  3. `entrypoint` совпадает с фактическим файлом и имя файла начинается с `skill-hack.`; `main.*` как entrypoint → `Manifest.EntrypointNotSkillHack`.
  4. `slug` — kebab-case, `version` — SemVer, папка `<slug>/<version>` совпадает с манифестом → иначе `Manifest.IdentityMismatch`.
  5. `schema.input.json`/`schema.output.json` парсятся как JSON Schema Draft 2020-12 → иначе `Package.InvalidSchema`.
  6. SHA-256 каждого файла из `integrity.filesSha256` совпадает с диском → иначе `Package.HashMismatch` с именем файла; файл на диске, не перечисленный в integrity (кроме самого манифеста), → `Package.UnlistedFile`.
  7. `tests/sample.fixture.json` содержит `sampleInput` и `expectedOutput` или инварианты → иначе `Package.InvalidFixture`.
  8. `filesystemScope` — только `IsolatedRunWorkspaceOnly`; иное значение → `Manifest.FilesystemScopeNotAllowed`.
- Регистрация пакета (`RegisterPackage`) — только после `IsValid`: запись метаданных через порт репозитория (фейк в тестах; PostgreSQL — TASK-011). Повторная регистрация того же `slug@version` с другим хэшем манифеста отклоняется (`Package.VersionAlreadyRegistered`).

## Вне рамок

- Исполнение скрипта и dry-run на фикстуре (TASK-043), AST-проверки безопасности кода (область 08/TASK-023 по портам), синтез пакетов Tier 3, UI регистрации.

## Зависимости

- TASK-002

## Контракты и интерфейсы

- `NetrunnerManifest`, `NetrunnerRuntime`, `PackageValidationResult`, `PackageError` (TASK-002).
- `INetrunnerPackageValidator` (эта задача); потребитель — TASK-043/TASK-044.
- `IPackageRegistryRepository` — порт (фейк здесь, реализация TASK-011).
- Предлагаемое размещение (to-be-created): `src/RouterAndSkills/CyberPipe.Skills.Netrunner/Packages/`.

## Результаты

- Валидатор + регистрация, набор фикстур-пакетов: 1 валидный (`skill-hack.py`) и по одному сломанному на каждый код ошибки.

## Критерии приёмки

- [ ] Валидный пакет проходит; `Manifest.Runtime.Entrypoint == "skill-hack.py"`, хэши совпали.
- [ ] Пакет с `main.py` как entrypoint отклонён с `EntrypointNotSkillHack`; пакет без `languageVersion` — с `MissingRuntimeField`.
- [ ] Изменённый после подписания `skill-hack.*` отклонён с `HashMismatch`; отсутствующий `tests/sample.fixture.json` — с `MissingFile`.
- [ ] Невалидная JSON Schema и `filesystemScope=Host` отклонены соответствующими кодами; все ошибки пакета с двумя дефектами присутствуют в результате одновременно.
- [ ] Валидатор ни разу не запускает скрипт (фикстура со скриптом-маркером: файл-маркер не создан).

## Тестирование

- Уровень: unit.
- Как: фикстуры пакетов во временных папках тестов, хэши считаются тестовой утилитой; фейк репозитория. Без PostgreSQL и без исполнения скриптов.

## Команды проверки

```powershell
dotnet build CyberPipe.slnx
dotnet test tests/CyberPipe.Tests.Chains/ --filter "FullyQualifiedName~PackageValidator"
```

(Путь тестового проекта — to-be-created, имя фиксируется в TASK-001.)

## Статус: Planned
