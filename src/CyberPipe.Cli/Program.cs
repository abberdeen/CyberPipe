using System.Diagnostics;
using CyberPipe.Cli;
using CyberPipe.Contracts;
using CyberPipe.Logging;
using CyberPipe.Logging.Telemetry;
using Microsoft.Extensions.Logging;

// CLI entry point: TASK-062. Logging and configuration: TASK-003, TASK-004.
CyberPipeOptions options;
try
{
    options = CyberPipeConfigurationLoader.LoadFromSources(
        appsettingsPath: null,
        environmentVariables: null,
        uiOverridesPath: null);
}
catch (ConfigurationValidationException)
{
    options = new CyberPipeOptions();
}

using ILoggerFactory loggerFactory =
    CyberPipeLogging.CreateLoggerFactory("Cli", options, writeToConsole: false);
ILogger logger = loggerFactory.CreateLogger("CyberPipe.Cli");

using IDisposable? telemetry = TelemetryProviders.Start(options.Telemetry, "CyberPipe.Cli");

var ctx = new CliContext
{
    Options = options,
};

logger.LogInformation("CyberPipe CLI invoking command with {Count} args", args.Length);

int exitCode;
using (Activity? cliActivity = CyberPipeTelemetry.Cli.StartActivity("cli.invoke"))
{
    cliActivity?.SetTag("cli.command", args.Length > 0 ? args[0] : string.Empty);
    exitCode = await CliApp.RunAsync(args, ctx);
}

return exitCode;
