# CyberPipe.Data — bootstrap PostgreSQL (TASK-010)

Единственная база CyberPipe — локальный PostgreSQL с расширением pgvector. Этот проект отвечает
только за первый запуск: обнаружение сервера, проверку доступности (`SELECT 1`), создание базы
приложения при отсутствии, включение расширений `vector` и `pgcrypto`, проверку версий и
health-check для Service. Схема и миграции — TASK-011, векторный поиск — TASK-012,
репозитории — TASK-013.

## Как это работает

- `PostgresOptions` — хост, порт, база, пользователь, пул, таймауты. Пароля значением в опциях
  нет: только `PasswordSecretKey` — ссылка на секрет TASK-003 в `ISecretStore`.
- `IPostgresBootstrapper.EnsureReadyAsync` → `BootstrapResult { Status: Ready | NeedsPassword |
  Unavailable, ReasonCode, ServerVersion, PgVectorVersion? }`. Секретов и строки подключения
  в результате, исключениях (`PostgresUnavailableException`) и логах нет.
- Неверный пароль → запрос через `ISecretPrompt` (контракт в CyberPipe.Contracts; UI — TASK-063),
  не более `MaxPasswordPromptAttempts` раз (по умолчанию 3). Отказ пользователя → `NeedsPassword`,
  Service не падает. Подтверждённый сервером пароль из промпта сохраняется в `ISecretStore`.
- Сервер недоступен, нет прав или нет pgvector → `Unavailable` с кодом причины
  (`ServerUnavailable`, `InsufficientPrivilege`, `PgVectorMissing`, `Timeout`), промпт не вызывается.
- Повторный запуск идемпотентен: базу не пересоздаёт, данные не сбрасывает, пароль пользователя
  PostgreSQL не меняет; при валидном сохранённом секрете промпт не требуется.
- Соединения для TASK-011–TASK-013 — только через `IPostgresConnectionFactory`.
- `PostgresHealthProbe` реализует `IDatabaseHealthProbe` (TASK-004): `Healthy`, иначе
  `Degraded` с кодом причины без секретов. Годится для TASK-020 и TASK-063.

## Установка PostgreSQL + pgvector на Windows

Ручное тестирование CyberPipe сфокусировано на Windows. Установка PostgreSQL «в один клик»
инсталлятором приложения и управление службой Windows в задачу не входят: только проверка
и эта инструкция.

### Вариант A. Официальный PostgreSQL + pgvector из исходников

1. Установите PostgreSQL 16 или 17 для Windows с официального сайта postgresql.org
   (инсталлятор EDB). Запомните пароль суперпользователя `postgres` и порт (обычно 5432).
   Служба `postgresql-x64-16` (или `-17`) должна быть запущена:
   `Get-Service postgresql*`.
2. pgvector для Windows ставится одним из способов:
   - через Stack Builder (EDB) при его наличии в поставке;
   - сборкой расширения: откройте «x64 Native Tools Command Prompt for VS», клонируйте
     `https://github.com/pgvector/pgvector`, затем в каталоге репозитория выполните
     `nmake /F Makefile.win` и `nmake /F Makefile.win install` при заданной переменной
     `PGROOT` на каталог установки PostgreSQL (например `C:\Program Files\PostgreSQL\16`).
3. Создайте пользователя и (по желанию) базу приложения — базу bootstrap создаст сам,
   пользователю нужны права `CREATEDB` и право создавать расширения в базе, либо расширение
   заранее создаёт суперпользователь:

   ```sql
   CREATE USER cyberpipe WITH PASSWORD '<пароль-вне-репозитория>' CREATEDB;
   ```

4. Пароль положите в хранилище секретов CyberPipe (TASK-003) под ключом `database.password`
   или задайте через окружение при первом запуске. В `appsettings.json` пароль не пишется.

### Вариант B. Docker (только для разработки и интеграционных тестов)

```powershell
docker run -d --name cyberpipe-pg -e POSTGRES_PASSWORD=<пароль> -p 55432:5432 pgvector/pgvector:pg16
```

## Проверка установки

Проверка через `psql` (входит в поставку PostgreSQL, каталог `bin`):

```powershell
& 'C:\Program Files\PostgreSQL\16\bin\psql.exe' -U postgres -h localhost -p 5432 -c "SELECT version();"
& 'C:\Program Files\PostgreSQL\16\bin\psql.exe' -U postgres -h localhost -p 5432 -c "SELECT name, default_version FROM pg_available_extensions WHERE name IN ('vector','pgcrypto');"
```

Ожидается строка `vector` в результате второго запроса. Та же проверка скриптом
(не требует CyberPipe, только `psql` в PATH или `-PsqlPath`):

```powershell
./scripts/Test-PostgresPgvector.ps1 -HostName localhost -Port 5432 -User postgres
```

После первого запуска CyberPipe состояние на сервере проверяется так:

```sql
SELECT extname, extversion FROM pg_extension WHERE extname IN ('vector', 'pgcrypto');
```

## Диагностика

| Симптом / ReasonCode | Что проверить |
| --- | --- |
| `ServerUnavailable` | Служба запущена (`Get-Service postgresql*`), порт слушается (`Test-NetConnection localhost -Port 5432`), `PostgresOptions.Host/Port` верны |
| `AuthenticationFailed` / `NeedsPassword` | Пароль в секрете `database.password` совпадает с паролем пользователя; `pg_hba.conf` разрешает `scram-sha-256`/`md5` с localhost |
| `InsufficientPrivilege` | У пользователя есть `CREATEDB`; расширение создаёт суперпользователь или пользователь с нужными правами |
| `PgVectorMissing` | `pg_available_extensions` не содержит `vector`: установить pgvector (шаг 2 выше) и перезапустить службу PostgreSQL |
| `Timeout` | Сервер перегружен или блокируется файрволом; проверить `ConnectTimeout`/`CommandTimeout` |

Пароль и строка подключения в логах CyberPipe не печатаются: при проблемах прикладывайте к обращению
только `ReasonCode` и версии `ServerVersion`/`PgVectorVersion` из результата bootstrap.

## Тесты

```powershell
dotnet test tests/CyberPipe.Data.Tests --filter "Category!=Integration"
$env:CYBERPIPE_TEST_POSTGRES = "Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=<пароль>"
dotnet test tests/CyberPipe.Data.Tests --filter "Category=Integration"
```

Без `CYBERPIPE_TEST_POSTGRES` интеграционные тесты пропускаются с явным сообщением.
