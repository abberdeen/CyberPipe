// Оркестрация локального контура разработки CyberPipe через .NET Aspire (TASK-095).
//
// Что запускается:
//   grafana   — Grafana LGTM (Loki, Tempo, Prometheus, коллектор OTLP) одним образом;
//   service   — CyberPipe.Service в консольном режиме (--console);
//   desktop   — CyberPipe.Desktop (пропускается при CYBERPIPE_ASPIRE_NO_DESKTOP=1, headless-режим).
//
// База данных — встроенная SQLite (WAL): файл data/cyberpipe.db рядом с Service,
// отдельных контейнеров и серверов БД контур не требует.
//
// Grafana — зеркало eng/grafana/docker-compose.yml. Запускать либо compose,
// либо AppHost, но не оба сразу (общие хост-порты 38473/4317/4318).
//
// Что НЕ запускается и почему:
//   CLI       — разовый инструмент (выполнил команду и вышел), не долгоживущий ресурс;
//   Brainstem — не отдельный ресурс: exe копируется рядом со Service при его сборке,
//               супервизор сам запускает его с --pipe. Без --pipe процесс сразу выходит.
//   Пароли БД отсутствуют: SQLite аутентификации не требует.
//   Путь к файлу базы задаётся CYBERPIPE_Database__Path (по умолчанию data/cyberpipe.db
//   относительно рабочего каталога Service).
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// OTLP/HTTP, как в экспорте OpenTelemetry в Grafana: протокол http/protobuf, порт 4318.
// UI Grafana — :38473 (admin/admin). Для Grafana Cloud тот же контракт, другой endpoint и заголовок Authorization.
const string grafanaOtlpHttp = "http://localhost:4318";

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
    .WithEnvironment("CYBERPIPE_Database__Path", "data/cyberpipe.db")
    .WithEnvironment("CYBERPIPE_Telemetry__Enabled", "true")
    .WithEnvironment("CYBERPIPE_Telemetry__OtlpEndpoint", grafanaOtlpHttp)
    .WithEnvironment("CYBERPIPE_Telemetry__OtlpHttp", "true")
    .WithEnvironment("CYBERPIPE_Telemetry__Environment", "aspire");

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
