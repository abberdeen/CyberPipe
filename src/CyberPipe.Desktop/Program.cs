using Avalonia;
using CyberPipe.Contracts;
using CyberPipe.Logging;
using CyberPipe.Logging.Telemetry;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CyberPipe.Tests.Unit")]

namespace CyberPipe.Desktop;

internal static class Program
{
    private static ILoggerFactory? _loggerFactory;

    /// <summary>Опции процесса (TASK-003); фабрика ViewModel берёт имя pipe отсюда.</summary>
    internal static CyberPipeOptions Options { get; private set; } = new CyberPipeOptions();

    /// <summary>Фабрика логеров процесса.</summary>
    internal static ILoggerFactory LoggerFactory =>
        _loggerFactory ?? throw new InvalidOperationException("Desktop logger factory is not initialized.");

    public static void Main(string[] args)
    {
        // Logging: TASK-004. Invalid configuration must not block the desktop shell here;
        // startup validation of the service remains in CyberPipe.Service (TASK-003).
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

        Options = options;
        _loggerFactory = CyberPipeLogging.CreateLoggerFactory("Desktop", options, writeToConsole: true);
        _loggerFactory.CreateLogger("CyberPipe.Desktop").LogInformation("CyberPipe Desktop starting");

        using IDisposable? telemetry = TelemetryProviders.Start(options.Telemetry, "CyberPipe.Desktop");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
