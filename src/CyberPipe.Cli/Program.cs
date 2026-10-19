using CyberPipe.Cli;
using CyberPipe.Contracts;
using CyberPipe.Logging;
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

var ctx = new CliContext
{
    Options = options,
};

logger.LogInformation("CyberPipe CLI invoking command with {Count} args", args.Length);

int exitCode = await CliApp.RunAsync(args, ctx);

return exitCode;
