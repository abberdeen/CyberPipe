# TASK-094 — OpenTelemetry всего проекта + локальный SigNoz

## Статус

Done. Локальный стенд SigNoz позже заменён на Grafana LGTM: `eng/grafana/`, UI `:3000`, OTLP/HTTP `:4318`. Ниже — исходная проверка против SigNoz.

## Зависимости

TASK-004 (логирование и диагностика), TASK-090 (композиционный корень и DI).

## Цель

Сквозная наблюдаемость CyberPipe: трейсы, метрики и логи всех процессов экспортируются
по OTLP в локальный SigNoz. Новая бизнес-логика не вводится: задача только соединяет
и проверяет (правило области 99-integration).

## Объём работ

- Секция конфигурации `Telemetry` (`CYBERPIPE_Telemetry__*`): `Enabled` (по умолчанию false),
  `OtlpEndpoint` (базовый адрес без пути сигнала, по умолчанию `http://localhost:4317`),
  `OtlpHttp` (false — бинарный OTLP :4317, true — OTLP/HTTP protobuf :4318),
  `Environment`, `SampleRatio`. Валидация именованными ошибками `Telemetry:*` только при включённом экспорте.
- Код экспорта — только в `CyberPipe.Logging` (уже разрешённая зависимость всех фиче-проектов):
  `Telemetry/TelemetryConfiguration.cs` (ресурс, сэмплер, маппинг протокола/эндпоинта),
  `TelemetryServiceExtensions.AddCyberPipeTelemetry` (Service, composition root),
  `TelemetryProviders.Start` (CLI, Desktop — ручной жизненный цикл без generic Host),
  Serilog-sink OpenTelemetry в `CyberPipeLogging` (логи в тот же приёмник).
- Инструментация: `ChainRunTrace` (спан `chain.run`, события skill/script/hitl/delivery, исход,
  счётчики `chain.runs.started/finished`, `chain.deliveries.sent`, гистограмма `chain.run.duration`),
  `ChainExecutor`, спан `service.startup` (Worker), спан `cli.invoke` (CLI).
- Локальный стенд SigNoz в `eng/signoz/` из официальных файлов вендора v0.82.0
  (последний тег с `deploy/docker/docker-compose.yaml` + `deploy/common/`); пины:
  clickhouse 24.1.2-alpine, zookeeper:3.9.3, signoz:v0.82.0, otel-collector/schema-migrator v0.111.41.
  Единственная адаптация — ZooKeeper: `bitnami/zookeeper:3.7.1` удалён из Hub,
  заменён на официальный `zookeeper:3.9.3` (+ volumes/healthcheck). Детали — в `eng/signoz/README.md`.
- Исправление транспорта OTLP/HTTP: SDK шлёт запрос ровно на `OtlpExporterOptions.Endpoint`
  без добавления пути (приёмник отвечал 404 на `/`), поэтому к базовому адресу добавляются
  пути сигналов (`v1/traces`, `v1/metrics`); явно заданный путь сохраняется. Покрыто теорией
  `ResolveEndpoint_MapsSignalPath` (6 кейсов).
- Юнит-тесты `tests/CyberPipe.Tests.Unit/TelemetryTests.cs` (15 фактов: дефолты, валидация,
  no-op при выключенном экспорте, источники/инструменты, маппинг эндпоинтов).

## Вне рамок

- Изолированный Brainstem-воркер (`src/Execution/CyberPipe.Brainstem`): логирования там нет вообще
  (только Console), врезка экспорта потребовала бы притащить туда загрузку опций и Serilog —
  расширение рамок закрытой области; оставлен без изменений осознанно.
- Блокеры TASK-093 (F-CLI-SERVICE-CONTROL, ручной чек-лист) — не предмет этой задачи.

## Команды проверки

```powershell
dotnet build CyberPipe.slnx --nologo -v q
dotnet test tests/CyberPipe.Tests.Unit/ -v q
dotnet test tests/CyberPipe.Tests.Architecture/ -v q
docker compose -f eng/signoz/docker-compose.yml ps
Invoke-RestMethod http://localhost:8080/api/v1/health
```

## Факт проверки (2026-10-19/20, локально)

- Сборка решения: 0 ошибок. Юниты: 523 passed + 1 skipped. Архитектура: 12/12.
- Стенд SigNoz здоров: `signoz`, `signoz-otel-collector`, `signoz-clickhouse`,
  `signoz-zookeeper-1`; OTLP :4317/:4318 открыты; `/api/v1/health` → `{"status":"ok"}`.
- Живой прогон (проба через `TelemetryProviders` + `ChainRunTrace` + Serilog, оба транспорта):
  спаны `chain.run` в `signoz_traces.distributed_signoz_index_v3` (бинарный и HTTP варианты),
  логи с маркером в `signoz_logs.distributed_logs_v2`, сэмплы `chain_runs_finished`
  в `signoz_metrics.distributed_samples_v4`; в self-diagnostics SDK нет `HttpRequestFailed`/`Export failed`.
- Диагностированная по пути ловушка: таблицы старой схемы (`signoz_logs.logs`,
  `signoz_traces.distributed_signoz_index_v2`) пусты, т.к. экспортёр пишет в новые схемы
  (`*_v2` для логов, `*_index_v3` для трейсов) — проверять наличие данных только в них.
