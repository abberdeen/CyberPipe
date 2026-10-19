using System.Xml.Linq;
using Xunit;

namespace CyberPipe.Tests.Architecture;

public sealed class ContractsBoundaryTests
{
    private static readonly string[] ForbiddenIdentifiers =
    [
        "Guest",
        "DevTools",
        "SQLite",
        "Hangfire",
        "RabbitMQ",
        "Kafka",
        "MassTransit",
        "NServiceBus",
        "ServiceBus",
        "Grpc",
        "Npgsql",
        "Quartz",
    ];

    private static readonly string[] RequiredPublicTypes =
    [
        "IntentTier",
        "RiskLevel",
        "TaskLifecycleState",
        "ChainTriggerKind",
        "ChannelKind",
        "ChainId",
        "RunId",
        "MessageId",
        "IncomingMessage",
        "OutgoingMessage",
        "HitlApprovalRequest",
        "HitlApprovalResult",
        "IMessageBus",
        "IMessageHandler",
        "IMessageTransport",
        "IIntentRouter",
        "ITokenBudgetGovernor",
        "BudgetStatusDto",
        "ChainDefinitionDto",
        "ChainRunContext",
        "ChainCheckpointDto",
        "DeliveryRequest",
        "DeliveryResult",
        "NetrunnerManifestDto",
        "ModelDescriptorDto",
        "TierModelAssignment",
        "ErrorDto",
        "Result",
        "IServiceControl",
        "IServiceController",
        "SandboxLaunchRequest",
        "SandboxLaunchResult",
        "ISandboxLauncher",
    ];

    [Fact]
    public void ContractsProject_HasNoProjectOrPackageReferences()
    {
        string root = FindSolutionRoot();
        string projectFile = Path.Combine(root, "src", "CyberPipe.Contracts", "CyberPipe.Contracts.csproj");
        XDocument doc = XDocument.Load(projectFile);
        Assert.Empty(doc.Descendants("ProjectReference"));
        Assert.Empty(doc.Descendants("PackageReference"));
    }

    [Fact]
    public void ContractsSources_ContainNoForbiddenIdentifiers()
    {
        string contractsDir = Path.Combine(FindSolutionRoot(), "src", "CyberPipe.Contracts");
        foreach (string file in Directory.GetFiles(contractsDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (string token in ForbiddenIdentifiers)
            {
                Assert.False(
                    text.Contains(token, StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetFileName(file)} contains forbidden identifier {token}");
            }
        }
    }

    [Fact]
    public void ContractsSources_DeclareAllRequiredPublicTypes()
    {
        string contractsDir = Path.Combine(FindSolutionRoot(), "src", "CyberPipe.Contracts");
        string allSources = string.Join(
            "\n",
            Directory.GetFiles(contractsDir, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));
        foreach (string typeName in RequiredPublicTypes)
        {
            Assert.True(
                allSources.Contains(typeName, StringComparison.Ordinal),
                $"Contracts sources do not declare {typeName}");
        }
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CyberPipe.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("CyberPipe.slnx not found above the test assembly.");
    }
}
