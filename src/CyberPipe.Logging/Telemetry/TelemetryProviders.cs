using CyberPipe.Contracts;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace CyberPipe.Logging.Telemetry;

/// <summary>
/// Ручной жизненный цикл провайдеров для процессов без generic Host
/// (CLI, Desktop, Brainstem): строит TracerProvider+MeterProvider через SDK
/// и возвращает IDisposable; Dispose сбрасывает буферы экспорта.
/// При выключенной секции Telemetry возвращает null (no-op).
/// </summary>
public static class TelemetryProviders
{
    /// <param name="options">Секция Telemetry конфигурации.</param>
    /// <param name="serviceName">Имя сервиса для resource service.name.</param>
    /// <param name="serviceVersion">Версия сервиса; null — из входной сборки.</param>
    public static IDisposable? Start(
        TelemetryOptions options,
        string serviceName,
        string? serviceVersion = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (!options.Enabled)
        {
            return null;
        }

        string version = serviceVersion ?? TelemetryConfiguration.ResolveServiceVersion();
        OpenTelemetry.Resources.ResourceBuilder resource =
            TelemetryConfiguration.CreateResource(serviceName, version, options.Environment);

        var tracerBuilder = Sdk.CreateTracerProviderBuilder().SetResourceBuilder(resource);
        TelemetryConfiguration.ConfigureTracing(tracerBuilder, options);
        TracerProvider tracerProvider = tracerBuilder.Build();

        var meterBuilder = Sdk.CreateMeterProviderBuilder().SetResourceBuilder(resource);
        TelemetryConfiguration.ConfigureMetrics(meterBuilder, options);
        MeterProvider meterProvider = meterBuilder.Build();

        return new TelemetryScope(tracerProvider, meterProvider);
    }

    private sealed class TelemetryScope : IDisposable
    {
        private readonly TracerProvider _tracerProvider;
        private readonly MeterProvider _meterProvider;
        private bool _disposed;

        public TelemetryScope(TracerProvider tracerProvider, MeterProvider meterProvider)
        {
            _tracerProvider = tracerProvider;
            _meterProvider = meterProvider;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _tracerProvider.Dispose();
            _meterProvider.Dispose();
        }
    }
}
