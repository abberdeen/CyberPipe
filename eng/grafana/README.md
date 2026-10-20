# Локальный Grafana для CyberPipe

Один образ [`grafana/otel-lgtm:0.37.0`](https://github.com/grafana/docker-otel-lgtm):
коллектор OpenTelemetry, Prometheus, Tempo, Loki и UI Grafana.
Приложение шлёт трейсы, метрики и логи по OTLP/HTTP (`http/protobuf`), как в
[гайде OpenTelemetry + Grafana](https://milanjovanovic.tech/blog/monitoring-dotnet-applications-with-opentelemetry-and-grafana).

```powershell
docker compose -f eng/grafana/docker-compose.yml up -d
```

- UI: http://localhost:3000 (`admin` / `admin`) — Explore: Traces (Tempo), Logs (Loki), Metrics (Prometheus).
- OTLP: `4317` gRPC, `4318` HTTP. Локальный контур Aspire использует `http://localhost:4318` и `OtlpHttp=true`.
- Данные лежат в томе `cyberpipe-grafana-lgtm`.

Остановка:

```powershell
docker compose -f eng/grafana/docker-compose.yml stop
```

Grafana Cloud — тот же экспортёр, другие три настройки: `OtlpEndpoint` на шлюз вида
`https://otlp-gateway-….grafana.net/otlp`, `OtlpHttp=true`,
`OtlpHeaders=Authorization=Basic <base64-токен>`.
