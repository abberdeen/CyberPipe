using CyberPipe.Contracts;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Reflection;

namespace CyberPipe.Logging.Telemetry;

/// <summary>
/// Общая конфигурация OpenTelemetry SDK для хостингового
/// (<see cref="TelemetryServiceExtensions"/>) и ручного
/// (<see cref="TelemetryProviders"/>) подключения.
/// </summary>
internal static class TelemetryConfiguration
{
    internal const string ChainsSourcePrefix = "CyberPipe.";
    internal const string ChainsMeterName = "CyberPipe.Chains";

    /// <summary>Версия сервиса из informational version входной сборки.</summary>
    internal static string ResolveServiceVersion() =>
        Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0-dev";

    internal static ResourceBuilder CreateResource(string serviceName, string serviceVersion, string environment) =>
        ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
            .AddAttributes(new[] { new KeyValuePair<string, object>("deployment.environment", environment) });

    internal static OtlpExportProtocol MapProtocol(TelemetryOptions options) =>
        options.OtlpProtocol.Equals("HttpProtobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;

    internal static Sampler CreateSampler(TelemetryOptions options) =>
        options.SampleRatio >= 1.0
            ? new ParentBasedSampler(new AlwaysOnSampler())
            : new ParentBasedSampler(new TraceIdRatioBasedSampler(options.SampleRatio));

    internal static void ConfigureTracing(TracerProviderBuilder builder, TelemetryOptions options)
    {
        Uri endpoint = new(options.OtlpEndpoint);
        OtlpExportProtocol protocol = MapProtocol(options);
        builder
            .AddSource("CyberPipe.*")
            .SetSampler(CreateSampler(options))
            .AddOtlpExporter(o =>
            {
                o.Endpoint = endpoint;
                o.Protocol = protocol;
            });
    }

    internal static void ConfigureMetrics(MeterProviderBuilder builder, TelemetryOptions options)
    {
        Uri endpoint = new(options.OtlpEndpoint);
        OtlpExportProtocol protocol = MapProtocol(options);
        builder
            .AddMeter(ChainsMeterName)
            .AddOtlpExporter(o =>
            {
                o.Endpoint = endpoint;
                o.Protocol = protocol;
            });
    }
}
