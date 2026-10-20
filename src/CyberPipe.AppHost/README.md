# CyberPipe.AppHost — локальный контур разработки на .NET Aspire

## Назначение

Одна команда для поднятия всего рабочего контура:
PostgreSQL+pgvector, CyberPipe.Service и CyberPipe.Desktop с едиными
настройками БД и телеметрии, плюс дашборд Aspire для логов и перезапуска.

## Предусловия

- Docker (контейнеры PostgreSQL и Grafana). AppHost поднимает их сам.
- Пароль БД контура (`cyberpipe` / `cyberpipe-dev-pw` на `localhost:5434`)
  один раз сохраняется в платформенное хранилище секретов обычным
  интерактивным запуском до первого старта через Aspire:

```powershell
dotnet run --project src/CyberPipe.Service -- --console
```

(бутстрэппер спросит пароль, проверит подключение и сохранит секрет
`database.password`; дальше Service под Aspire соединяется сам).

## Запуск

```powershell
# Полный контур (Service + Desktop + PostgreSQL + дашборд)
dotnet run --project src/CyberPipe.AppHost

# Без Desktop (headless: дашборд + PostgreSQL + Service)
$env:CYBERPIPE_ASPIRE_NO_DESKTOP = "1"
dotnet run --project src/CyberPipe.AppHost
```

Дашборд: адрес печатается в выводе (`https://localhost:15888` и рядом).
Порт PostgreSQL контура — `localhost:5434` (не пересекается с продом `:5432`
и тестовой `cyberpipe-test-db` на `:5433`); том `cyberpipe-aspire-pgdata`
переживает перезапуски. Телеметрия процессов включена в `aspire`-окружение
и летит в локальный Grafana LGTM (OTLP/HTTP `:4318`, UI `:3000`, вход `admin`/`admin`).

## Что не входит и почему

- CLI — разовый инструмент, а не долгоживущий процесс.
- Brainstem-воркер — требует `--pipe` супервизора, сам не стартует.
- Отдельный compose Grafana нужен только без AppHost: `eng/grafana/docker-compose.yml`. Не поднимайте его вместе с AppHost: порты `3000`/`4317`/`4318` общие.
- ServiceDefaults-проект отсутствует осознанно: логирование, OTLP и health
  уже централизованы в `CyberPipe.Logging`, а ссылка Service на новый проект
  нарушила бы контракт `ServiceComposition_MayReferenceFeatureProjectsOnly`.

## Версии

Aspire `13.6.1` (`Aspire.AppHost.Sdk`, `Aspire.Hosting.AppHost`, `Aspire.Hosting`),
пин — в `CyberPipe.AppHost.csproj`. `AspireUseCliBundle=true`: оркестратор и
дашборд работают через `dotnet run` без установки Aspire CLI.
