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

// Боевое управление службой: тот же Named Pipe + SCM канал, что у Desktop (TASK-021).
// Router/Chain/Hitl требуют расширения pipe-протокола службы — пока честно остаются null
// и команды отвечают ServiceUnavailable, а не фейковым успехом (FIX-01, docs FIX-TRACKER).
CyberPipe.Contracts.IServiceControlChannel pipeChannel =
    new CyberPipe.ServiceControl.ServiceControlPipeClient(options.Hosting.PipeName);
CyberPipe.Contracts.IServiceControl? serviceControl = OperatingSystem.IsWindows()
    ? new CyberPipe.ServiceControl.ServiceControllerManager(
        pipeChannel, new CyberPipe.ServiceControl.WindowsScmManager(), options, logger)
    : null;

var ctx = new CliContext
{
    Options = options,
    ServiceControl = serviceControl,
};

logger.LogInformation("CyberPipe CLI invoking command with {Count} args", args.Length);

int exitCode;
using (Activity? cliActivity = CyberPipeTelemetry.Cli.StartActivity("cli.invoke"))
{
    cliActivity?.SetTag("cli.command", args.Length > 0 ? args[0] : string.Empty);
    exitCode = await CliApp.RunAsync(args, ctx);
}

return exitCode;
