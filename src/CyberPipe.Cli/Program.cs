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

// Боевой транспорт к службе: Named Pipe RPC (TASK-020/021) — управление службой,
// роутер, бюджет, цепочки, HITL. При остановленной службе адаптеры честно бросают
// недоступность канала, команды отвечают ServiceUnavailable (FIX-01).
var pipeClient = new CyberPipe.ServiceControl.ServiceControlPipeClient(options.Hosting.PipeName);
CyberPipe.Contracts.IServiceControl? serviceControl = OperatingSystem.IsWindows()
    ? new CyberPipe.ServiceControl.ServiceControllerManager(
        pipeClient, new CyberPipe.ServiceControl.WindowsScmManager(), options, logger)
    : null;

var ctx = new CliContext
{
    Options = options,
    ServiceControl = serviceControl,
    Router = new CyberPipe.ServiceControl.PipeIntentRouter(pipeClient),
    BudgetGovernor = new CyberPipe.ServiceControl.PipeBudgetGovernor(pipeClient),
    ChainRepo = new CyberPipe.ServiceControl.PipeChainDefinitionRepository(pipeClient),
    ExecutionRepo = new CyberPipe.ServiceControl.PipeChainExecutionRepository(pipeClient),
    TriggerInvoker = new CyberPipe.ServiceControl.PipeTriggerInvoker(pipeClient),
    HitlService = new CyberPipe.ServiceControl.PipeHitlApprovalService(pipeClient),
};

logger.LogInformation("CyberPipe CLI invoking command with {Count} args", args.Length);

int exitCode;
using (Activity? cliActivity = CyberPipeTelemetry.Cli.StartActivity("cli.invoke"))
{
    cliActivity?.SetTag("cli.command", args.Length > 0 ? args[0] : string.Empty);
    exitCode = await CliApp.RunAsync(args, ctx).ConfigureAwait(false);
}

return exitCode;
