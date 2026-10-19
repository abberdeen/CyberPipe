// Оркестрация локального контура разработки CyberPipe через .NET Aspire (TASK-095).
//
// Что запускается:
//   postgres  — PostgreSQL+pgvector в контейнере (постоянный том, фиксированный хост-порт),
//               отдельная БД контура Aspire, не трогает cyberpipe-test-db (:5433);
//   service   — CyberPipe.Service в консольном режиме (--console);
//   desktop   — CyberPipe.Desktop (пропускается при CYBERPIPE_ASPIRE_NO_DESKTOP=1, headless-режим).
//
// SigNoz (ClickHouse+ZooKeeper+collector+query-service/UI) поднимается здесь же
// контейнерами — зеркало eng/signoz/docker-compose.yml; запускать либо compose,
// либо AppHost, но не оба сразу (общие хост-порты 8080/4317/4318 и тома).
//
// Что НЕ запускается и почему:
//   CLI       — разовый инструмент (выполнил команду и вышел), не долгоживущий ресурс;
//   Brainstem — изолированный воркер требует --pipe супервизора, сам по себе не стартует.
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

// ---------- SigNoz (полный стек под управлением AppHost) ----------
// Зеркало eng/signoz/docker-compose.yml (SigNoz v0.82.0): те же образы с теми же
// пинами, конфиги монтируются из eng/signoz (единственный источник правды),
// именованные тома совпадают с compose (данные общие, запуск — либо compose,
// либо AppHost, но не оба сразу: хост-порты 8080/4317/4318 общие).
// Имена ресурсов обязаны совпадать с хостнеймами из конфигов:
// clickhouse (DSN приёмников), zookeeper-1 (config.xml кластера).
// Порядок — через WaitFor/WaitForCompletion вместо compose depends_on/healthcheck
// (у Aspire нет ulimits/tty/labels/healthcheck для контейнеров — для dev-контура
// не требуется).
string SignozFile(params string[] parts)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        string candidate = Path.Combine([dir.FullName, "eng", "signoz", .. parts]);
        if (File.Exists(candidate) || Directory.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("eng/signoz not found above the AppHost binary.");
}

var initClickhouse = builder.AddContainer("init-clickhouse", "clickhouse/clickhouse-server", "24.1.2-alpine")
    .WithArgs(
        "bash",
        "-c",
        "version=\"v0.0.1\"\n"
        + "node_os=$(uname -s | tr '[:upper:]' '[:lower:]')\n"
        + "node_arch=$(uname -m | sed s/aarch64/arm64/ | sed s/x86_64/amd64/)\n"
        + "echo \"Fetching histogram-binary for ${node_os}/${node_arch}\"\n"
        + "cd /tmp\n"
        + "wget -O histogram-quantile.tar.gz \"https://github.com/SigNoz/signoz/releases/download/histogram-quantile%2F${version}/histogram-quantile_${node_os}_${node_arch}.tar.gz\"\n"
        + "tar -xvzf histogram-quantile.tar.gz\n"
        + "mv histogram-quantile /var/lib/clickhouse/user_scripts/histogramQuantile\n")
    .WithBindMount(SignozFile("common", "clickhouse", "user_scripts"), "/var/lib/clickhouse/user_scripts/");

var zookeeper = builder.AddContainer("zookeeper-1", "zookeeper", "3.9.3")
    .WithEnvironment("ZOO_MY_ID", "1")
    .WithEnvironment("ZOO_SERVERS", "server.1=0.0.0.0:2888:3888;2181")
    .WithEnvironment("ZOO_4LW_COMMANDS_WHITELIST", "*")
    .WithEnvironment("ZOO_AUTOPURGE_INTERVAL", "1")
    .WithVolume("signoz-zookeeper-1", "/data")
    .WithVolume("signoz-zookeeper-1-log", "/datalog");

var clickhouse = builder.AddContainer("clickhouse", "clickhouse/clickhouse-server", "24.1.2-alpine")
    .WithBindMount(SignozFile("common", "clickhouse", "config.xml"), "/etc/clickhouse-server/config.xml")
    .WithBindMount(SignozFile("common", "clickhouse", "users.xml"), "/etc/clickhouse-server/users.xml")
    .WithBindMount(SignozFile("common", "clickhouse", "custom-function.xml"), "/etc/clickhouse-server/custom-function.xml")
    .WithBindMount(SignozFile("common", "clickhouse", "cluster.xml"), "/etc/clickhouse-server/config.d/cluster.xml")
    .WithBindMount(SignozFile("common", "clickhouse", "user_scripts"), "/var/lib/clickhouse/user_scripts/")
    .WithVolume("signoz-clickhouse", "/var/lib/clickhouse/")
    .WaitFor(zookeeper)
    .WaitForCompletion(initClickhouse, 0);

var migratorSync = builder.AddContainer("schema-migrator-sync", "signoz/signoz-schema-migrator", "v0.111.41")
    .WithArgs("sync", "--dsn=tcp://clickhouse:9000", "--up=")
    .WaitFor(clickhouse);

var migratorAsync = builder.AddContainer("schema-migrator-async", "signoz/signoz-schema-migrator", "v0.111.41")
    .WithArgs("async", "--dsn=tcp://clickhouse:9000", "--up=")
    .WaitFor(clickhouse)
    .WaitForCompletion(migratorSync, 0);

var signoz = builder.AddContainer("signoz", "signoz/signoz", "v0.82.0")
    .WithArgs("--config=/root/config/prometheus.yml", "--use-logs-new-schema=true", "--use-trace-new-schema=true")
    .WithEndpoint(port: 8080, targetPort: 8080, scheme: "http", name: "http", isProxied: false)
    .WithBindMount(SignozFile("common", "signoz", "prometheus.yml"), "/root/config/prometheus.yml")
    .WithBindMount(SignozFile("common", "dashboards"), "/root/config/dashboards")
    .WithVolume("signoz-sqlite", "/var/lib/signoz/")
    .WithEnvironment("SIGNOZ_ALERTMANAGER_PROVIDER", "signoz")
    .WithEnvironment("SIGNOZ_TELEMETRYSTORE_CLICKHOUSE_DSN", "tcp://clickhouse:9000")
    .WithEnvironment("SIGNOZ_SQLSTORE_SQLITE_PATH", "/var/lib/signoz/signoz.db")
    .WithEnvironment("DASHBOARDS_PATH", "/root/config/dashboards")
    .WithEnvironment("STORAGE", "clickhouse")
    .WithEnvironment("GODEBUG", "netdns=go")
    .WithEnvironment("TELEMETRY_ENABLED", "true")
    .WithEnvironment("DEPLOYMENT_TYPE", "docker-standalone-amd")
    .WaitFor(clickhouse)
    .WaitForCompletion(migratorSync, 0);

var collector = builder.AddContainer(
        "otel-collector",
        "signoz/signoz-otel-collector",
        "v0.111.41")
    .WithArgs(
        "--config=/etc/otel-collector-config.yaml",
        "--manager-config=/etc/manager-config.yaml",
        "--copy-path=/var/tmp/collector-config.yaml",
        "--feature-gates=-pkg.translator.prometheus.NormalizeName")
    .WithEndpoint(port: 4317, targetPort: 4317, scheme: "tcp", name: "otlp-grpc", isProxied: false)
    .WithEndpoint(port: 4318, targetPort: 4318, scheme: "tcp", name: "otlp-http", isProxied: false)
    .WithBindMount(SignozFile("otel-collector-config.yaml"), "/etc/otel-collector-config.yaml")
    .WithBindMount(SignozFile("common", "signoz", "otel-collector-opamp-config.yaml"), "/etc/manager-config.yaml")
    .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", "host.name=signoz-host,os.type=linux")
    .WithEnvironment("LOW_CARDINAL_EXCEPTION_GROUPING", "false")
    .WaitFor(signoz)
    .WaitFor(clickhouse)
    .WaitForCompletion(migratorSync, 0);

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
