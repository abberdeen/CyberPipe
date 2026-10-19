using CyberPipe.Contracts;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CyberPipe.Logging.Telemetry;

/// <summary>
/// Подключение OpenTelemetry к хосту (Service): трейсы и метрики через OTLP.
/// Логи идут через Serilog-sink из <see cref="CyberPipeLogging"/> тем же эндпоинтом.
/// При выключенной секции Telemetry — no-op, провайдеры не создаются.
/// </summary>
public static class TelemetryServiceExtensions
{
    /// <param name="services">Коллекция сервисов хоста.</param>
    /// <param name="options">Секция Telemetry конфигурации.</param>
    /// <param name="serviceName">Имя сервиса для resource service.name.</param>
    /// <param name="serviceVersion">Версия сервиса; null — из входной сборки.</param>
    public static IServiceCollection AddCyberPipeTelemetry(
        this IServiceCollection services,
        TelemetryOptions options,
        string serviceName,
        string? serviceVersion = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (!options.Enabled)
        {
            return services;
        }

        string version = serviceVersion ?? TelemetryConfiguration.ResolveServiceVersion();
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: serviceName, serviceVersion: version)
                .AddAttributes(new[] { new KeyValuePair<string, object>("deployment.environment", options.Environment) }))
            .WithTracing(b => TelemetryConfiguration.ConfigureTracing(b, options))
            .WithMetrics(b => TelemetryConfiguration.ConfigureMetrics(b, options));
        return services;
    }
}
