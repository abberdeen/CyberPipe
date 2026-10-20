using CyberPipe.Contracts;
using CyberPipe.Logging.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CyberPipe.Tests.Unit;

/// <summary>
/// Телеметрия OpenTelemetry: дефолты и валидация секции Telemetry,
/// no-op провайдеров при выключенном экспорте.
/// </summary>
public sealed class TelemetryTests
{
    [Fact]
    public void TelemetryOptions_DefaultsToDisabledLocalhostBinary()
    {
        var options = new TelemetryOptions();

        Assert.False(options.Enabled);
        Assert.Equal("http://localhost:4317", options.OtlpEndpoint);
        Assert.False(options.OtlpHttp);
        Assert.Equal("local", options.Environment);
        Assert.Equal(1.0, options.SampleRatio);
    }

    [Fact]
    public void Validate_DisabledTelemetry_HasNoErrors()
    {
        var options = new CyberPipeOptions
        {
            Telemetry = new TelemetryOptions { Enabled = false, OtlpEndpoint = "not-a-uri", SampleRatio = 42 },
        };

        Assert.DoesNotContain(options.Validate(), e => e.StartsWith("Telemetry:", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_BrokenEndpointIsNamed()
    {
        var options = new CyberPipeOptions
        {
            Telemetry = new TelemetryOptions { Enabled = true, OtlpEndpoint = "localhost:4317" },
        };

        Assert.Contains(options.Validate(), e => e == "Telemetry:OtlpEndpoint must be an absolute http(s) URI.");
    }

    [Fact]
    public void Validate_HttpTransportFlag_HasNoTelemetryErrors()
    {
        var options = new CyberPipeOptions
        {
            Telemetry = new TelemetryOptions
            {
                Enabled = true,
                OtlpEndpoint = "http://localhost:4318",
                OtlpHttp = true,
            },
        };

        Assert.DoesNotContain(options.Validate(), e => e.StartsWith("Telemetry:", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SampleRatioOutOfRangeIsNamed()
    {
        var options = new CyberPipeOptions
        {
            Telemetry = new TelemetryOptions { Enabled = true, SampleRatio = 1.5 },
        };

        Assert.Contains(options.Validate(), e => e == "Telemetry:SampleRatio must be between 0 and 1.");
    }

    [Fact]
    public void Validate_EnabledValidOptions_HasNoTelemetryErrors()
    {
        var options = new CyberPipeOptions
        {
            Telemetry = new TelemetryOptions
            {
                Enabled = true,
                OtlpEndpoint = "http://localhost:4318",
                OtlpHttp = true,
                SampleRatio = 0.25,
            },
        };

        Assert.DoesNotContain(options.Validate(), e => e.StartsWith("Telemetry:", StringComparison.Ordinal));
    }

    [Fact]
    public void AddCyberPipeTelemetry_Disabled_RegistersNothingAndDoesNotThrow()
    {
        var services = new ServiceCollection();

        services.AddCyberPipeTelemetry(new TelemetryOptions(), "CyberPipe.Tests");
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.DoesNotContain(provider.GetServices<object>(), s => s.GetType().FullName!.Contains("OpenTelemetry", StringComparison.Ordinal));
    }

    [Fact]
    public void TelemetryProviders_Disabled_ReturnsNull()
    {
        Assert.Null(TelemetryProviders.Start(new TelemetryOptions(), "CyberPipe.Tests"));
    }

    [Theory]
    [InlineData("http://localhost:4317", false, "v1/traces", "http://localhost:4317/")]
    [InlineData("http://localhost:4317", false, "v1/metrics", "http://localhost:4317/")]
    [InlineData("http://localhost:4318", true, "v1/traces", "http://localhost:4318/v1/traces")]
    [InlineData("http://localhost:4318", true, "v1/metrics", "http://localhost:4318/v1/metrics")]
    [InlineData("http://localhost:4318/", true, "v1/traces", "http://localhost:4318/v1/traces")]
    [InlineData("http://localhost:4318/v1/traces", true, "v1/traces", "http://localhost:4318/v1/traces")]
    [InlineData("https://otlp-gateway-prod-eu-west-2.grafana.net/otlp", true, "v1/traces", "https://otlp-gateway-prod-eu-west-2.grafana.net/otlp/v1/traces")]
    [InlineData("https://otlp-gateway-prod-eu-west-2.grafana.net/otlp", true, "v1/metrics", "https://otlp-gateway-prod-eu-west-2.grafana.net/otlp/v1/metrics")]
    public void ResolveEndpoint_MapsSignalPath(
        string configured, bool http, string signal, string expected)
    {
        var options = new TelemetryOptions { Enabled = true, OtlpEndpoint = configured, OtlpHttp = http };

        Uri actual = TelemetryConfiguration.ResolveEndpoint(options, signal);

        Assert.Equal(expected, actual.ToString());
    }

    [Fact]
    public void CyberPipeTelemetry_SourcesAndInstrumentsExist()
    {
        Assert.Equal("CyberPipe.Chains", CyberPipeTelemetry.Chains.Name);
        Assert.Equal("CyberPipe.Service", CyberPipeTelemetry.Service.Name);
        Assert.Equal("CyberPipe.Cli", CyberPipeTelemetry.Cli.Name);
        Assert.Equal("CyberPipe.Chains", CyberPipeTelemetry.ChainsMeter.Name);
        Assert.NotNull(CyberPipeTelemetry.RunsStarted);
        Assert.NotNull(CyberPipeTelemetry.RunsFinished);
        Assert.NotNull(CyberPipeTelemetry.DeliveriesSent);
        Assert.NotNull(CyberPipeTelemetry.RunDurationMs);
    }
}
