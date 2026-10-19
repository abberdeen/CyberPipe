using CyberPipe.Contracts;
using CyberPipe.Logging;
using Microsoft.Extensions.Logging;

// Host implementation: TASK-020. Configuration and startup validation: TASK-003. Logging: TASK-004.
CyberPipeOptions options;
try
{
    options = CyberPipeConfigurationLoader.Load();
}
catch (ConfigurationValidationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

using ILoggerFactory loggerFactory = CyberPipeLogging.CreateLoggerFactory("Service", options);
ILogger logger = loggerFactory.CreateLogger("CyberPipe.Service");
logger.LogInformation("CyberPipe Service starting");
return 0;
