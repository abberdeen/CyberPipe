// Оркестрация локального контура разработки CyberPipe через .NET Aspire (TASK-095).
//
// Что запускается:
//   postgres  — PostgreSQL+pgvector в контейнере (постоянный том, фиксированный хост-порт),
//               отдельная БД контура Aspire, не трогает cyberpipe-test-db (:5433);
//   service   — CyberPipe.Service в консольном режиме (--console);
//   desktop   — CyberPipe.Desktop (пропускается при CYBERPIPE_ASPIRE_NO_DESKTOP=1, headless-режим).
//
// Что НЕ запускается и почему:
//   CLI       — разовый инструмент (выполнил команду и вышел), не долгоживущий ресурс;
//   Brainstem — изолированный воркер требует --pipe супервизора, сам по себе не стартует;
//   SigNoz    — внешний стек из eng/signoz/docker-compose.yml (ClickHouse+ZK+collector+UI),
//               должен быть поднят отдельно; AppHost лишь указывает экспортёры на его OTLP.
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

// Внешний SigNoz (eng/signoz): OTLP/HTTP приёмник и UI. Сам стек AppHost не поднимает.
const string signozOtlpHttp = "http://localhost:4318";

var postgres = builder.AddContainer("postgres", "pgvector/pgvector", "pg16")
    .WithEnvironment("POSTGRES_USER", devDbUser)
    .WithEnvironment("POSTGRES_PASSWORD", devDbPassword)
    .WithEnvironment("POSTGRES_DB", devDbName)
    .WithVolume("cyberpipe-aspire-pgdata", "/var/lib/postgresql/data")
    .WithEndpoint(port: devDbHostPort, targetPort: 5432, scheme: "tcp", name: "tcp", isProxied: false)
    .WithLifetime(Aspire.Hosting.ApplicationModel.ContainerLifetime.Persistent);

builder.AddProject<Projects.CyberPipe_Service>("service")
    .WithArgs("--console")
    .WithEnvironment("CYBERPIPE_Database__Host", "localhost")
    .WithEnvironment("CYBERPIPE_Database__Port", devDbHostPort.ToString())
    .WithEnvironment("CYBERPIPE_Database__Database", devDbName)
    .WithEnvironment("CYBERPIPE_Database__Username", devDbUser)
    .WithEnvironment("CYBERPIPE_Telemetry__Enabled", "true")
    .WithEnvironment("CYBERPIPE_Telemetry__OtlpEndpoint", signozOtlpHttp)
    .WithEnvironment("CYBERPIPE_Telemetry__OtlpHttp", "true")
    .WithEnvironment("CYBERPIPE_Telemetry__Environment", "aspire")
    .WaitFor(postgres);

if (!string.Equals(
        Environment.GetEnvironmentVariable("CYBERPIPE_ASPIRE_NO_DESKTOP"),
        "1",
        StringComparison.OrdinalIgnoreCase))
{
    builder.AddProject<Projects.CyberPipe_Desktop>("desktop")
        .WithEnvironment("CYBERPIPE_Telemetry__Enabled", "true")
        .WithEnvironment("CYBERPIPE_Telemetry__OtlpEndpoint", signozOtlpHttp)
        .WithEnvironment("CYBERPIPE_Telemetry__OtlpHttp", "true")
        .WithEnvironment("CYBERPIPE_Telemetry__Environment", "aspire");
}

builder.Build().Run();
