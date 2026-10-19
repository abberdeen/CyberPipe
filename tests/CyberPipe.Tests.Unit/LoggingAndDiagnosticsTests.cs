using CyberPipe.Contracts;
using CyberPipe.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class LoggingAndDiagnosticsTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "CyberPipeTests", Guid.NewGuid().ToString("N"));

    public LoggingAndDiagnosticsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SecretValueAndKnownSecretKeys_AreRedactedAtAnyNestingLevel()
    {
        var sink = new InMemoryLogSink();
        using ILoggerFactory factory = CreateFactory(sink);
        ILogger logger = factory.CreateLogger("test");

        var payload = new
        {
            Token = SecretValue.FromPlainText("plain-secret-1"),
            Nested = new Dictionary<string, object?>
            {
                ["telegram.botToken"] = "plain-secret-2",
                ["database.password"] = "plain-secret-3",
                ["apiKey"] = "plain-secret-4",
                ["Visible"] = "visible-value",
            },
        };
        logger.LogInformation("payload {@Payload}", payload);
        logger.LogInformation("direct {telegramBotToken}", "plain-secret-5");

        string text = sink.Text;
        Assert.Contains("[REDACTED]", text);
        Assert.Contains("visible-value", text);
        Assert.DoesNotContain("plain-secret-1", text);
        Assert.DoesNotContain("plain-secret-2", text);
        Assert.DoesNotContain("plain-secret-3", text);
        Assert.DoesNotContain("plain-secret-4", text);
        Assert.DoesNotContain("plain-secret-5", text);
    }

    [Fact]
    public void EventInsideRunIdContext_ContainsRunIdAndEnrichment()
    {
        var sink = new InMemoryLogSink();
        using ILoggerFactory factory = CreateFactory(sink);
        ILogger logger = factory.CreateLogger("test");

        RunId runId = OperationContext.NewRunId();
        ChainId chainId = ChainId.New();
        using (OperationContext.BeginScope(runId, chainId, ChannelKind.Telegram, IntentTier.Tier2))
        {
            logger.LogInformation("inside context");
        }

        logger.LogInformation("outside context");

        string inside = sink.Lines[0];
        Assert.Contains(runId.ToString(), inside);
        Assert.Contains(chainId.ToString(), inside);
        Assert.Contains("Telegram", inside);
        Assert.Contains("Tier2", inside);
        Assert.Contains("\"ProcessName\":\"TestProcess\"", inside.Replace(" ", string.Empty));
        Assert.DoesNotContain(runId.ToString(), sink.Lines[1]);
    }

    [Fact]
    public void NestedScopes_RestoreOuterRunId()
    {
        RunId outer = OperationContext.NewRunId();
        RunId inner = OperationContext.NewRunId();
        Assert.Null(OperationContext.Current);
        using (OperationContext.BeginScope(outer))
        {
            Assert.Equal(outer, OperationContext.Current?.RunId);
            using (OperationContext.BeginScope(inner))
            {
                Assert.Equal(inner, OperationContext.Current?.RunId);
            }

            Assert.Equal(outer, OperationContext.Current?.RunId);
        }

        Assert.Null(OperationContext.Current);
    }

    [Fact]
    public void LogLevel_ChangesViaConfigurationWithoutRebuild()
    {
        var env = new Dictionary<string, string?> { ["CYBERPIPE_Logging__MinimumLevel"] = "Warning" };
        CyberPipeOptions options = CyberPipeConfigurationLoader.LoadFromSources(null, env, null);
        Assert.Equal("Warning", options.Logging.MinimumLevel);

        var sink = new InMemoryLogSink();
        using ILoggerFactory factory = CyberPipeLogging.CreateLoggerFactory(
            "TestProcess", options, sink, _dir, writeToConsole: false);
        ILogger logger = factory.CreateLogger("test");
        logger.LogInformation("should be filtered");
        logger.LogWarning("should pass");

        Assert.Single(sink.Lines);
        Assert.Contains("should pass", sink.Text);
    }

    [Fact]
    public void FileLog_ContainsRedactedSecretAndRunId()
    {
        var options = new CyberPipeOptions
        {
            Logging = new LoggingOptions { MinimumLevel = "Information", LogDirectory = _dir },
        };
        RunId runId = OperationContext.NewRunId();
        using (ILoggerFactory factory = CyberPipeLogging.CreateLoggerFactory(
                   "TestProcess", options, logDirectory: _dir, writeToConsole: false))
        {
            ILogger logger = factory.CreateLogger("test");
            using (OperationContext.BeginScope(runId))
            {
                logger.LogInformation("file event {@Payload}",
                    new { BotToken = SecretValue.FromPlainText("file-secret-9") });
            }
        }

        string[] files = Directory.GetFiles(_dir, "TestProcess-*.log");
        Assert.NotEmpty(files);
        string text = string.Join("\n", files.Select(File.ReadAllText));
        Assert.Contains("[REDACTED]", text);
        Assert.Contains(runId.ToString(), text);
        Assert.DoesNotContain("file-secret-9", text);
    }

    [Fact]
    public void TokenUsage_LogsTokenAndCostFields()
    {
        var sink = new InMemoryLogSink();
        using ILoggerFactory factory = CreateFactory(sink);
        ILogger logger = factory.CreateLogger("test");

        logger.LogTokenUsage(new TokenUsageDto
        {
            ModelId = "model-1",
            HostingType = ModelHostingType.RemoteCloud,
            InputTokens = 100,
            OutputTokens = 50,
            CostUsd = 0.0123m,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        }, IntentTier.Tier3);

        Assert.Contains("model-1", sink.Text);
        Assert.Contains("150", sink.Text);
        Assert.Contains("0.0123", sink.Text);
        Assert.Contains("Tier3", sink.Text);
    }

    [Fact]
    public async Task Diagnostics_WithFakes_ReturnsAllFields()
    {
        var options = new CyberPipeOptions();
        var service = new DiagnosticsService(
            "Service", options, new FakeDatabaseProbe(HealthStatus.Healthy), new FakeTransport(),
            startedAtUtc: DateTimeOffset.UtcNow, version: "1.2.3");

        HealthReportDto report = await service.GetHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Equal("Service", report.ProcessName);
        Assert.Equal("Running", report.ProcessState);
        Assert.Equal("1.2.3", report.Version);
        Assert.Equal(HealthStatus.Healthy, report.Database.Status);
        Assert.Equal("localhost", report.Database.Host);
        Assert.Equal(5432, report.Database.Port);
        Assert.Equal("cyberpipe", report.Database.Database);
        Assert.Equal(typeof(FakeTransport).FullName, report.Transport.ImplementationType);
        Assert.Equal(1, report.PerformanceTargets.Tier0TargetMilliseconds);
        Assert.Equal(50, report.PerformanceTargets.StopTargetMilliseconds);
        Assert.Equal(2, report.Components.Count);
    }

    [Fact]
    public async Task Diagnostics_WhenDatabaseUnavailable_ReturnsDegradedWithoutSecrets()
    {
        var service = new DiagnosticsService(
            "Service", new CyberPipeOptions(), new ThrowingDatabaseProbe(), transport: null);

        HealthReportDto report = await service.GetHealthAsync();

        Assert.Equal(HealthStatus.Degraded, report.Status);
        Assert.Equal(HealthStatus.Degraded, report.Database.Status);
        Assert.Contains("InvalidOperationException", report.Database.Detail);
        Assert.DoesNotContain("db-secret-7", report.Database.Detail);
        Assert.DoesNotContain("db-secret-7", report.Transport.Detail ?? string.Empty);
        Assert.Equal("none", report.Transport.ImplementationType);
    }

    [Fact]
    public void SecretRedaction_RedactsTextAndUnlistedSecretSynonyms()
    {
        Assert.True(SecretRedaction.IsSecretPropertyName("telegram.botToken"));
        Assert.True(SecretRedaction.IsSecretPropertyName("DatabasePassword"));
        Assert.True(SecretRedaction.IsSecretPropertyName("client_secret"));
        Assert.True(SecretRedaction.IsSecretPropertyName("AccessToken"));
        Assert.False(SecretRedaction.IsSecretPropertyName("InputTokens"));
        Assert.False(SecretRedaction.IsSecretPropertyName("Visible"));

        string redacted = SecretRedaction.RedactText(
            "Host=db;Password=db-secret-7; telegram.botToken: token-42")!;
        Assert.DoesNotContain("db-secret-7", redacted);
        Assert.DoesNotContain("token-42", redacted);
        Assert.Contains("[REDACTED]", redacted);
    }

    private ILoggerFactory CreateFactory(InMemoryLogSink sink) =>
        CyberPipeLogging.CreateLoggerFactory(
            "TestProcess", new CyberPipeOptions(), sink, _dir, writeToConsole: false);

    private sealed class FakeDatabaseProbe(HealthStatus status) : IDatabaseHealthProbe
    {
        public Task<DatabaseHealthDto> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DatabaseHealthDto
            {
                Status = status,
                CheckedAtUtc = DateTimeOffset.UtcNow,
            });
    }

    private sealed class ThrowingDatabaseProbe : IDatabaseHealthProbe
    {
        public Task<DatabaseHealthDto> CheckAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Host=db;Password=db-secret-7");
    }

    private sealed class FakeTransport : IMessageTransport
    {
        public Task SendAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public IDisposable Subscribe(IMessageHandler<MessageEnvelope> handler) => new NoopSubscription();

        private sealed class NoopSubscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
