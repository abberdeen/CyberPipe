# Локальный SigNoz для CyberPipe

Вендорные файлы из SigNoz `v0.82.0` (`deploy/docker/docker-compose.yaml`,
`otel-collector-config.yaml`, `deploy/common/{clickhouse,signoz,dashboards}`,
`.env`): только текстовые конфиги, без данных.

Адаптации CyberPipe (остальное — дословно upstream):

- `docker-compose.yml`: относительные пути `../common/` → `./common/`;
  `zookeeper-1` переведён с удалённого с Docker Hub `bitnami/zookeeper:3.7.1`
  на официальный `zookeeper:3.9.3` (эквивалентный single-node конфиг,
  ruok-healthcheck без внешних утилит); добавлен том `zookeeper-1-log`.
- Пины образов: `clickhouse/clickhouse-server:24.1.2-alpine`,
  `zookeeper:3.9.3`, `signoz/signoz:v0.82.0`,
  `signoz/signoz-otel-collector:v0.111.41`,
  `signoz/signoz-schema-migrator:v0.111.41`.

## Запуск

```powershell
docker compose -f eng/signoz/docker-compose.yml up -d
```

Порты: `8080` — UI и query API (`/api/v1/health`),
`4317` — OTLP/gRPC, `4318` — OTLP/HTTP.
Данные — в именованных томах `signoz-clickhouse`, `signoz-sqlite`,
`signoz-zookeeper-1(-log)` (переживают рестарт).

Остановка с удалением контейнеров (данные целы):

```powershell
docker compose -f eng/signoz/docker-compose.yml stop
```

Приложение шлёт телеметрию на `http://localhost:4317` (gRPC),
включается секцией `Telemetry` (`CYBERPIPE_Telemetry__Enabled=true`).
