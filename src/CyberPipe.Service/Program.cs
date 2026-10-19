using CyberPipe.Contracts;
using CyberPipe.Logging;
using CyberPipe.Service;
using CyberPipe.Service.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Service;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        bool isConsole = args.Any(arg => string.Equals(arg, "--console", StringComparison.OrdinalIgnoreCase));
        bool validateOnly = args.Any(arg => string.Equals(arg, "--validate-config", StringComparison.OrdinalIgnoreCase));

        CyberPipeOptions options;
        try
        {
            options = CyberPipeConfigurationLoader.Load();
            if (isConsole && !options.Logging.WriteToConsole)
            {
                options = options with
                {
                    Logging = options.Logging with { WriteToConsole = true }
                };
            }
        }
        catch (ConfigurationValidationException ex)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            return 1;
        }

        using ILoggerFactory loggerFactory = CyberPipeLogging.CreateLoggerFactory("Service", options);
        ILogger logger = loggerFactory.CreateLogger("CyberPipe.Service");

        if (validateOnly)
        {
            return RunValidateConfig(options, logger);
        }

        logger.LogInformation(
            "CyberPipe Service initializing. Mode: {Mode}, ServiceName: {ServiceName}",
            isConsole ? "Console" : "WindowsService",
            options.Hosting.ServiceName);

        try
        {
            IHostBuilder builder = Host.CreateDefaultBuilder(args);

            if (OperatingSystem.IsWindows() && !isConsole)
            {
                builder.UseWindowsService(o =>
                {
                    o.ServiceName = options.Hosting.ServiceName;
                });
            }

            builder.ConfigureLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
            });

            builder.ConfigureServices((_, services) =>
            {
                services.AddSingleton<ILoggerFactory>(loggerFactory);
                services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
                services.AddCyberPipeService(options);
                services.AddSingleton<ServiceStatusTracker>();
                services.AddSingleton(sp =>
                {
                    var tracker = sp.GetRequiredService<ServiceStatusTracker>();
                    var lifetime = sp.GetRequiredService<IHostApplicationLifetime>();
                    var serverLogger = sp.GetService<ILogger<ServiceControlPipeServer>>();

                    return new ServiceControlPipeServer(
                        options.Hosting.PipeName,
                        tracker,
                        stopCallback: () =>
                        {
                            lifetime.StopApplication();
                            return Task.CompletedTask;
                        },
                        restartCallback: () =>
                        {
                            lifetime.StopApplication();
                            return Task.CompletedTask;
                        },
                        logger: serverLogger);
                });

                services.Configure<HostOptions>(hostOptions =>
                {
                    hostOptions.ShutdownTimeout = TimeSpan.FromSeconds(options.Hosting.ShutdownTimeoutSeconds);
                });

                services.AddHostedService<CyberPipeWorker>();
            });

            IHost host = builder.Build();
            await host.RunAsync().ConfigureAwait(false);

            logger.LogInformation("CyberPipe Service finished execution.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Fatal error running CyberPipe Service.");
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Диагностический прогон конфигурации (TASK-090): связность DI-графа, каталоги
    /// моделей, запрещённые сервисы. Без запуска каналов и без записи в БД.
    /// </summary>
    private static int RunValidateConfig(CyberPipeOptions options, ILogger logger)
    {
        var services = new ServiceCollection();
        services.AddCyberPipeService(options);
        using ServiceProvider provider = services.BuildServiceProvider();

        ConfigurationValidationReport report = ServiceComposition.ValidateConfiguration(provider, options);
        if (report.IsValid)
        {
            Console.WriteLine("Configuration is valid: DI graph resolves, model catalogs 15+8 are present.");
            logger.LogInformation("Validate-config succeeded.");
            return 0;
        }

        foreach (string error in report.Errors)
        {
            Console.Error.WriteLine($"Invalid: {error}");
            logger.LogWarning("Validate-config error: {Error}", error);
        }

        return 1;
    }
}
