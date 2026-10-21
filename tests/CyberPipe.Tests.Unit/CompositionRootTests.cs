namespace CyberPipe.Tests.Unit;

using CyberPipe.Contracts;
using CyberPipe.Models;
using CyberPipe.Service.Composition;
using CyberPipe.Service.Messaging;
using CyberPipe.Setup;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Xunit;

public sealed class CompositionRootTests
{
    private sealed class RecordingTransport : IMessageTransport
    {
        private readonly List<IMessageHandler<MessageEnvelope>> _handlers = [];

        public List<MessageEnvelope> Sent { get; } = [];

        public Task SendAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Sent.Add(envelope);
            return Task.CompletedTask;
        }

        public IDisposable Subscribe(IMessageHandler<MessageEnvelope> handler)
        {
            _handlers.Add(handler);
            return new Subscription(() => _handlers.Remove(handler));
        }

        private sealed class Subscription(Action unsubscribe) : IDisposable
        {
            private Action? _unsubscribe = unsubscribe;

            public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }

    private static CyberPipeOptions TestOptions() => new();

    private static ServiceProvider BuildComposition(CyberPipeOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddCyberPipeService(options ?? TestOptions());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task FullComposition_ValidatesWithoutDatabaseOrNetwork()
    {
        await using ServiceProvider provider = BuildComposition();

        ConfigurationValidationReport report =
            ServiceComposition.ValidateConfiguration(provider, TestOptions());

        Assert.True(report.IsValid, string.Join("; ", report.Errors));
    }

    [Fact]
    public async Task FullComposition_ResolvesKeyServices()
    {
        await using ServiceProvider provider = BuildComposition();

        Assert.NotNull(provider.GetRequiredService<IMessageBus>());
        Assert.NotNull(provider.GetRequiredService<IMessageTransport>());
        Assert.NotNull(provider.GetRequiredService<IOwnerAuthorizer>());
        Assert.NotNull(provider.GetRequiredService<ITokenBudgetGovernor>());
        Assert.NotNull(provider.GetRequiredService<IChainExecutor>());
        Assert.NotNull(provider.GetRequiredService<ISkillInvoker>());
        Assert.NotNull(provider.GetRequiredService<IDeliveryPort>());
        Assert.NotNull(provider.GetRequiredService<IHitlApprovalService>());
        Assert.NotNull(provider.GetRequiredService<IModelCatalog>());
        Assert.NotNull(provider.GetRequiredService<IChatModelProvider>());
        Assert.NotNull(provider.GetRequiredService<IModelCompatibilityAdvisor>());
        Assert.NotNull(provider.GetRequiredService<SetupWizardEngine>());
        Assert.NotNull(provider.GetRequiredService<IScheduler>());
        Assert.NotNull(provider.GetRequiredService<IChainSchedulerService>());

        IModelCatalog catalog = provider.GetRequiredService<IModelCatalog>();
        Assert.Equal(15, catalog.LocalModels.Count);
        Assert.Equal(8, catalog.CloudModels.Count);
    }

    [Fact]
    public async Task FullComposition_RegistersNoForbiddenServices()
    {
        await using ServiceProvider provider = BuildComposition();

        ConfigurationValidationReport report =
            ServiceComposition.ValidateConfiguration(provider, TestOptions());

        Assert.DoesNotContain(report.Errors, e => e.StartsWith("Forbidden service", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateConfiguration_ReportsInvalidOptions()
    {
        await using ServiceProvider provider = BuildComposition();
        var broken = TestOptions() with
        {
            Database = new DatabaseOptions { Path = string.Empty },
        };

        ConfigurationValidationReport report = ServiceComposition.ValidateConfiguration(provider, broken);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("Database:Path", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MessageTransport_CanBeSwappedByConfiguration()
    {
        var services = new ServiceCollection();
        services.AddCyberPipeService(TestOptions());
        var recording = new RecordingTransport();
        services.AddSingleton<IMessageTransport>(recording);
        using ServiceProvider provider = services.BuildServiceProvider();

        IMessageTransport transport = provider.GetRequiredService<IMessageTransport>();
        Assert.Same(recording, transport);

        MessageEnvelope envelope = InProcessMessageTransport.ToEnvelope(new PingMessage("ping"), "swap");
        await transport.SendAsync(envelope);

        Assert.Single(recording.Sent);
        Assert.Equal("swap", recording.Sent[0].CorrelationId);
    }

    [Fact]
    public async Task StartupCoordinator_WithoutDatabase_EntersConnectionRecovery()
    {
        // Недоступная база: путь под обычным файлом — каталог создать нельзя.
        string blocker = Path.GetTempFileName();
        var broken = new CyberPipeOptions
        {
            Database = new DatabaseOptions
            {
                Path = Path.Combine(blocker, "unreachable.db"),
            },
        };
        await using ServiceProvider provider = BuildComposition(broken);
        var coordinator = new ServiceStartupCoordinator(provider, broken);

        StartupReport report = await coordinator.RunAsync();

        StartupStepResult database = Assert.Single(report.Steps, s => s.Step == "Database");
        Assert.False(database.Ok);
        Assert.True(database.Degraded);
        Assert.True(report.NeedsConnectionRecovery);
        Assert.False(report.IsReady);
        Assert.Contains(report.Steps, s => s.Step == "Migrations" && s.Degraded);
        Assert.Contains(report.Steps, s => s.Step == "Configuration" && s.Ok);
        Assert.Contains(report.Steps, s => s.Step == "ModelCatalogs" && s.Ok);
        Assert.Contains(report.Steps, s => s.Step == "MessageTransport" && s.Ok);

        await provider.GetRequiredService<IScheduler>().Shutdown();
        await coordinator.StopAsync();
    }

    private sealed record PingMessage(string Text);
}
