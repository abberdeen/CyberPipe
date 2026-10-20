using CyberPipe.Contracts;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CyberPipe.Tests.Unit")]

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
        options.OtlpHttp
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;

    /// <summary>
    /// Конечная точка экспортёра для сигнала OTLP. SDK шлёт HTTP-запрос ровно на
    /// <see cref="OtlpExporterOptions.Endpoint"/> без добавления пути, поэтому для OTLP/HTTP
    /// к базовому адресу добавляется путь сигнала (v1/traces, v1/metrics); приёмник без него
    /// отвечает 404 и данные теряются. Для бинарного OTLP адрес используется как есть.
    /// Явно заданный путь сигнала в конфигурации сохраняется без изменений.
    /// </summary>
    /// <param name="options">Секция Telemetry конфигурации.</param>
    /// <param name="signalPath">Путь сигнала без ведущего слэша (например "v1/traces").</param>
    /// <returns>URI экспортёра.</returns>
    internal static Uri ResolveEndpoint(TelemetryOptions options, string signalPath)
    {
        var baseUri = new Uri(options.OtlpEndpoint);
        if (!options.OtlpHttp)
        {
            return baseUri;
        }

        string signal = signalPath.Trim('/');
        string path = baseUri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/" + signal, StringComparison.OrdinalIgnoreCase))
        {
            return baseUri;
        }

        return new UriBuilder(baseUri)
        {
            Path = string.IsNullOrEmpty(path) ? signal : path + "/" + signal,
        }.Uri;
    }

    internal static void ApplyExporter(OtlpExporterOptions exporter, TelemetryOptions options, string signalPath)
    {
        exporter.Endpoint = ResolveEndpoint(options, signalPath);
        exporter.Protocol = MapProtocol(options);
        if (!string.IsNullOrWhiteSpace(options.OtlpHeaders))
        {
            exporter.Headers = options.OtlpHeaders;
        }
    }

    internal static Sampler CreateSampler(TelemetryOptions options) =>
        options.SampleRatio >= 1.0
            ? new ParentBasedSampler(new AlwaysOnSampler())
            : new ParentBasedSampler(new TraceIdRatioBasedSampler(options.SampleRatio));

    internal static void ConfigureTracing(TracerProviderBuilder builder, TelemetryOptions options)
    {
        builder
            .AddSource("CyberPipe.*")
            .SetSampler(CreateSampler(options))
            .AddOtlpExporter(o => ApplyExporter(o, options, "v1/traces"));
    }

    internal static void ConfigureMetrics(MeterProviderBuilder builder, TelemetryOptions options)
    {
        builder
            .AddMeter(ChainsMeterName)
            .AddOtlpExporter(o => ApplyExporter(o, options, "v1/metrics"));
    }
}
