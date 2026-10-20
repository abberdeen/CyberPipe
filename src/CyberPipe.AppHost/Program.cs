// Оркестрация локального контура разработки CyberPipe через .NET Aspire (TASK-095).
//
// Что запускается:
//   postgres  — PostgreSQL+pgvector в контейнере (постоянный том, фиксированный хост-порт),
//               отдельная БД контура Aspire, не трогает cyberpipe-test-db (:5433);
//   grafana   — Grafana LGTM (Loki, Tempo, Prometheus, коллектор OTLP) одним образом;
//   service   — CyberPipe.Service в консольном режиме (--console);
//   desktop   — CyberPipe.Desktop (пропускается при CYBERPIPE_ASPIRE_NO_DESKTOP=1, headless-режим).
//
// Grafana — зеркало eng/grafana/docker-compose.yml. Запускать либо compose,
// либо AppHost, но не оба сразу (общие хост-порты 38473/4317/4318 и том).
//
// Что НЕ запускается и почему:
//   CLI       — разовый инструмент (выполнил команду и вышел), не долгоживущий ресурс;
//   Brainstem — не отдельный ресурс. Desktop, Service и воркер пишутся в artifacts/app,
//               супервизор сам запускает exe с --pipe. Без --pipe процесс сразу выходит.
//   Пароль БД сервис берёт из платформенного хранилища секретов (PlatformSecretStore),
//   первичное заполнение — обычным интерактивным запуском Service (см. README).
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Локальный пароль контура разработки. Только localhost, только dev:
// контейнер публикуется на 127.0.0.1, секрет продакшена сюда не попадает.
const string devDbPassword = "cyberpipe-dev-pw";
const string devDbUser = "cyberpipe";
const string devDbName = "cyberpipe";
const int devDbHostPort = 5434;

// OTLP/HTTP, как в экспорте OpenTelemetry в Grafana: протокол http/protobuf, порт 4318.
// UI Grafana — :38473 (admin/admin). Для Grafana Cloud тот же контракт, другой endpoint и заголовок Authorization.
const string grafanaOtlpHttp = "http://localhost:4318";

var postgres = builder.AddContainer("postgres", "pgvector/pgvector", "pg16")
    .WithEnvironment("POSTGRES_USER", devDbUser)
    .WithEnvironment("POSTGRES_PASSWORD", devDbPassword)
    .WithEnvironment("POSTGRES_DB", devDbName)
    .WithVolume("cyberpipe-aspire-pgdata", "/var/lib/postgresql/data")
    .WithEndpoint(port: devDbHostPort, targetPort: 5432, scheme: "tcp", name: "tcp", isProxied: false)
    .WithLifetime(Aspire.Hosting.ApplicationModel.ContainerLifetime.Persistent);

var grafana = builder.AddContainer("grafana", "grafana/otel-lgtm", "0.37.0")
    .WithEndpoint(port: 38473, targetPort: 3000, scheme: "http", name: "http", isProxied: false)
    .WithEndpoint(port: 4317, targetPort: 4317, scheme: "tcp", name: "otlp-grpc", isProxied: false)
    .WithEndpoint(port: 4318, targetPort: 4318, scheme: "tcp", name: "otlp-http", isProxied: false)
    .WithVolume("cyberpipe-grafana-lgtm", "/data")
    .WithLifetime(Aspire.Hosting.ApplicationModel.ContainerLifetime.Persistent);

builder.AddProject<Projects.CyberPipe_Service>("service")
    .WithArgs("--console")
    .WithEnvironment("CYBERPIPE_Logging__WriteToConsole", "true")
    .WithEnvironment("CYBERPIPE_Logging__MinimumLevel", "Information")
    .WithEnvironment("CYBERPIPE_Database__Host", "localhost")
    .WithEnvironment("CYBERPIPE_Database__Port", devDbHostPort.ToString())
    .WithEnvironment("CYBERPIPE_Database__Database", devDbName)
    .WithEnvironment("CYBERPIPE_Database__Username", devDbUser)
    .WithEnvironment("CYBERPIPE_Telemetry__Enabled", "true")
    .WithEnvironment("CYBERPIPE_Telemetry__OtlpEndpoint", grafanaOtlpHttp)
    .WithEnvironment("CYBERPIPE_Telemetry__OtlpHttp", "true")
    .WithEnvironment("CYBERPIPE_Telemetry__Environment", "aspire")
    .WaitFor(postgres);

if (!string.Equals(
        Environment.GetEnvironmentVariable("CYBERPIPE_ASPIRE_NO_DESKTOP"),
        "1",
        StringComparison.OrdinalIgnoreCase))
{
    builder.AddProject<Projects.CyberPipe_Desktop>("desktop")
        .WithEnvironment("CYBERPIPE_Logging__WriteToConsole", "true")
        .WithEnvironment("CYBERPIPE_Logging__MinimumLevel", "Information")
        .WithEnvironment("CYBERPIPE_Telemetry__Enabled", "true")
        .WithEnvironment("CYBERPIPE_Telemetry__OtlpEndpoint", grafanaOtlpHttp)
        .WithEnvironment("CYBERPIPE_Telemetry__OtlpHttp", "true")
        .WithEnvironment("CYBERPIPE_Telemetry__Environment", "aspire");
}

builder.Build().Run();
