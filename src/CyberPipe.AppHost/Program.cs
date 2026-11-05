// Оркестрация локального контура разработки CyberPipe через .NET Aspire (TASK-095).
//
// Что запускается:
//   grafana   — Grafana LGTM (Loki, Tempo, Prometheus, коллектор OTLP) одним образом,
//               только если Docker/Podman отвечает. Иначе контейнер не регистрируется,
//               а зависший опрос движка не доходит до оркестратора: Service и Desktop
//               стартуют без неё;
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
using System.ComponentModel;
using System.Diagnostics;
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// OTLP/HTTP, как в экспорте OpenTelemetry в Grafana: протокол http/protobuf, порт 4318.
// UI Grafana — :38473 (admin/admin). Для Grafana Cloud тот же контракт, другой endpoint и заголовок Authorization.
const string grafanaOtlpHttp = "http://localhost:4318";

// Том cyberpipe-grafana-lgtm Aspire создаёт до старта остальных ресурсов.
// Если движок не отвечает, `dcp info` не завершается за DependencyCheckTimeout
// и Run() падает с DistributedApplicationException ещё до проектов.
// В режиме run контейнер регистрируется только при живом docker/podman.
bool containerRuntimeReady = !builder.ExecutionContext.IsRunMode || ContainerRuntimeProbe.IsResponsive();
if (containerRuntimeReady)
{
    builder.AddContainer("grafana", "grafana/otel-lgtm", "0.37.0")
        .WithEndpoint(port: 38473, targetPort: 3000, scheme: "http", name: "http", isProxied: false)
        .WithEndpoint(port: 4317, targetPort: 4317, scheme: "tcp", name: "otlp-grpc", isProxied: false)
        .WithEndpoint(port: 4318, targetPort: 4318, scheme: "tcp", name: "otlp-http", isProxied: false)
        .WithVolume("cyberpipe-grafana-lgtm", "/data")
        .WithLifetime(Aspire.Hosting.ApplicationModel.ContainerLifetime.Persistent);
}
else
{
    // Несуществующий канал: docker CLI сразу получает «pipe not found» и dcp info
    // возвращает containers.running=false за доли секунды, с кодом 0.
    // Контейнеров в модели нет, поэтому Aspire не считает это ошибкой запуска.
    Environment.SetEnvironmentVariable("DOCKER_HOST", ContainerRuntimeProbe.UnavailableDockerHost);
    Console.WriteLine(
        "Grafana LGTM пропущена: среда контейнеров недоступна. Service и Desktop запускаются без неё. " +
        "Телеметрия на http://localhost:4318 остаётся необязательной.");
}

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

/// <summary>
/// Короткая проверка docker/podman. Не отвечает за несколько секунд — считаем, что Grafana поднять нельзя.
/// </summary>
file static class ContainerRuntimeProbe
{
    internal static string UnavailableDockerHost { get; } = OperatingSystem.IsWindows()
        ? "npipe:////./pipe/cyberpipe-docker-unavailable"
        : "unix:///tmp/cyberpipe-docker-unavailable.sock";

    private static readonly string[] CliNames = ["docker.exe", "docker", "podman.exe", "podman"];

    internal static bool IsResponsive()
    {
        string? cli = ResolveCli();
        if (cli is null)
            return false;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cli,
                    Arguments = "info --format {{.ServerVersion}}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            if (!process.Start())
                return false;

            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(TimeSpan.FromSeconds(3));
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception killEx) when (killEx is Win32Exception or InvalidOperationException)
                {
                }

                process.WaitForExit(TimeSpan.FromSeconds(1));
            }

            _ = stdout.GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();
            return exited && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or System.IO.IOException)
        {
            return false;
        }
    }

    private static string? ResolveCli()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string name in CliNames)
                {
                    string candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        string desktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Docker", "Docker", "resources", "bin", "docker.exe");
        return File.Exists(desktop) ? desktop : null;
    }
}
