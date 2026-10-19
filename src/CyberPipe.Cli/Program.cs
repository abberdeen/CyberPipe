using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Logging;
using Microsoft.Extensions.Logging;

// CLI/TUI skeleton. Implementation: TASK-062. Logging and diagnostics command: TASK-004.
CyberPipeOptions options = CyberPipeConfigurationLoader.LoadFromSources(
    appsettingsPath: null,
    environmentVariables: null,
    uiOverridesPath: null);
using ILoggerFactory loggerFactory =
    CyberPipeLogging.CreateLoggerFactory("Cli", options, writeToConsole: true);
ILogger logger = loggerFactory.CreateLogger("CyberPipe.Cli");

if (args.Length > 0 && args[0] is "health" or "diagnostics")
{
    var diagnostics = new DiagnosticsService(
        "Cli",
        options,
        new UnavailableDatabaseHealthProbe());
    HealthReportDto report = await diagnostics.GetHealthAsync();
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    logger.LogInformation("Diagnostics report collected Status={Status}", report.Status);
    return 0;
}

logger.LogInformation("CyberPipe CLI starting");
return 0;
