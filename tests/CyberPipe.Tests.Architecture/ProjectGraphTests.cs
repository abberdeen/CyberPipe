using System.Xml.Linq;
using Xunit;

namespace CyberPipe.Tests.Architecture;

public sealed class ProjectGraphTests
{
    private static readonly string[] ExpectedSrcProjects =
    [
        "CyberPipe.AppHost",
        "CyberPipe.Brainstem",
        "CyberPipe.Brainstem.Supervisor",
        "CyberPipe.Chains",
        "CyberPipe.Channels.Telegram",
        "CyberPipe.Cli",
        "CyberPipe.Contracts",
        "CyberPipe.Logging",
        "CyberPipe.Data",
        "CyberPipe.Desktop",
        "CyberPipe.Models",
        "CyberPipe.Router",
        "CyberPipe.Execution.Sandbox",
        "CyberPipe.Security",
        "CyberPipe.Service",
        "CyberPipe.ServiceControl",
        "CyberPipe.Setup",
        "CyberPipe.Skills",
    ];

    private static readonly string[] ForbiddenTokens =
    [
        "DevTools",
        "Guest",
        "SQLite",
        "Hangfire",
    ];

    [Fact]
    public void Solution_ContainsAllExpectedProjects()
    {
        Dictionary<string, string[]> graph = LoadGraph();
        foreach (string expected in ExpectedSrcProjects)
        {
            Assert.True(graph.ContainsKey(expected), $"Missing project: {expected}");
        }

        Assert.True(graph.ContainsKey("CyberPipe.Tests.Unit"), "Missing project: CyberPipe.Tests.Unit");
        Assert.True(graph.ContainsKey("CyberPipe.Tests.Architecture"), "Missing project: CyberPipe.Tests.Architecture");
    }

    [Fact]
    public void Contracts_HasNoProjectReferences()
    {
        Dictionary<string, string[]> graph = LoadGraph();
        Assert.Empty(graph["CyberPipe.Contracts"]);
    }

    private static readonly string[] FeatureProjects =
    [
        "CyberPipe.Chains",
        "CyberPipe.Channels.Telegram",
        "CyberPipe.Data",
        "CyberPipe.Desktop",
        "CyberPipe.Execution.Sandbox",
        "CyberPipe.Logging",
        "CyberPipe.Models",
        "CyberPipe.Router",
        "CyberPipe.Security",
        "CyberPipe.ServiceControl",
        "CyberPipe.Setup",
        "CyberPipe.Skills",
    ];

    [Fact]
    public void FeatureProjects_ReferenceOnlyContractsAndLogging()
    {
        // TASK-004/TASK-090: feature projects reference Contracts and at most Logging;
        // cross-area wiring happens only in the Service composition root.
        // TASK-021 exception: Desktop is a composition root for its own process and
        // hosts the real service-control client (Named Pipe + SCM), so it may also
        // reference CyberPipe.ServiceControl — still never Data (see below).
        Dictionary<string, string[]> graph = LoadGraph();
        foreach (string name in FeatureProjects)
        {
            if (!graph.ContainsKey(name))
            {
                continue;
            }

            Assert.Contains("CyberPipe.Contracts", graph[name]);
            Assert.True(
                graph[name].All(r =>
                    r is "CyberPipe.Contracts" or "CyberPipe.Logging"
                    || (name == "CyberPipe.Desktop" && r is "CyberPipe.ServiceControl" or "CyberPipe.Skills")),
                $"{name} references unexpected projects: {string.Join(", ", graph[name])}");
        }
    }

    [Fact]
    public void ServiceComposition_MayReferenceFeatureProjectsOnly()
    {
        // TASK-090: Service is the single composition root; it must not pull
        // UI processes (Desktop/CLI) or the isolated Brainstem worker.
        Dictionary<string, string[]> graph = LoadGraph();
        string[] allowed =
        [
            "CyberPipe.Contracts",
            "CyberPipe.Logging",
            "CyberPipe.Brainstem.Supervisor",
            "CyberPipe.Chains",
            "CyberPipe.Channels.Telegram",
            "CyberPipe.Data",
            "CyberPipe.Execution.Sandbox",
            "CyberPipe.Models",
            "CyberPipe.Router",
            "CyberPipe.Security",
            "CyberPipe.ServiceControl",
            "CyberPipe.Setup",
            "CyberPipe.Skills",
        ];
        Assert.True(
            graph["CyberPipe.Service"].All(r => allowed.Contains(r, StringComparer.Ordinal)),
            $"CyberPipe.Service references unexpected projects: {string.Join(", ", graph["CyberPipe.Service"])}");
    }

    [Fact]
    public void DesktopAndCli_HaveNoDataReference()
    {
        // TASK-090: Desktop and CLI never reference the data project or the
        // PostgreSQL driver; they reach state only through contracts.
        Dictionary<string, string[]> graph = LoadGraph();
        foreach (string name in new[] { "CyberPipe.Desktop", "CyberPipe.Cli" })
        {
            Assert.DoesNotContain("CyberPipe.Data", graph[name]);
        }
    }

    [Fact]
    public void Brainstem_ReferencesOnlyContractsAndLogging()
    {
        // TASK-090: the isolated worker and its supervisor stay free of
        // channels, UI and chain scheduling.
        Dictionary<string, string[]> graph = LoadGraph();
        foreach (string name in new[] { "CyberPipe.Brainstem", "CyberPipe.Brainstem.Supervisor" })
        {
            if (!graph.ContainsKey(name))
            {
                continue;
            }

            Assert.True(
                graph[name].All(r => r is "CyberPipe.Contracts" or "CyberPipe.Logging"),
                $"{name} references unexpected projects: {string.Join(", ", graph[name])}");
        }
    }

    [Fact]
    public void SrcProjects_HaveNoForbiddenPackageReferences()
    {
        // TASK-090/TASK-093 G3: no fixed broker, no Hangfire, no gRPC packages;
        // Npgsql (PostgreSQL driver) is allowed only in Data and Chains
        // (Quartz in the same PostgreSQL).
        string root = FindSolutionRoot();
        string[] forbiddenEverywhere = ["Hangfire", "RabbitMQ", "Kafka", "MassTransit", "NServiceBus", "Grpc"];
        foreach (string projectFile in Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(projectFile);
            XDocument doc = XDocument.Load(projectFile);
            string[] packages = doc.Descendants("PackageReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(include => include is not null)
                .Select(include => include!)
                .ToArray();
            foreach (string package in packages)
            {
                foreach (string forbidden in forbiddenEverywhere)
                {
                    Assert.False(
                        package.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                        $"{name} references forbidden package {package}");
                }

                if (package.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.True(
                        name is "CyberPipe.Data" or "CyberPipe.Chains",
                        $"{name} must not reference the PostgreSQL driver");
                }

                if (package.Contains("SQLite", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Fail($"{name} must not reference SQLite");
                }
            }
        }
    }

    [Fact]
    public void ProjectGraph_IsAcyclic()
    {
        Dictionary<string, string[]> graph = LoadGraph();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inStack = new HashSet<string>(StringComparer.Ordinal);
        foreach (string node in graph.Keys)
        {
            Assert.False(HasCycle(node, graph, visited, inStack), $"Cycle detected at {node}");
        }
    }

    [Fact]
    public void ProjectNames_ContainNoForbiddenIdentifiers()
    {
        Dictionary<string, string[]> graph = LoadGraph();
        foreach (string name in graph.Keys)
        {
            foreach (string token in ForbiddenTokens)
            {
                Assert.False(name.Contains(token, StringComparison.OrdinalIgnoreCase), $"{name} contains forbidden token {token}");
            }

            Assert.False(name.EndsWith(".Git", StringComparison.OrdinalIgnoreCase), $"{name} must not be a Git project");
            Assert.False(name.EndsWith(".Build", StringComparison.OrdinalIgnoreCase), $"{name} must not be a Build project");
        }
    }

    private static bool HasCycle(
        string node,
        Dictionary<string, string[]> graph,
        HashSet<string> visited,
        HashSet<string> inStack)
    {
        if (inStack.Contains(node))
        {
            return true;
        }

        if (!visited.Add(node))
        {
            return false;
        }

        inStack.Add(node);
        if (graph.TryGetValue(node, out string[]? references))
        {
            foreach (string reference in references)
            {
                if (graph.ContainsKey(reference) && HasCycle(reference, graph, visited, inStack))
                {
                    return true;
                }
            }
        }

        inStack.Remove(node);
        return false;
    }

    private static Dictionary<string, string[]> LoadGraph()
    {
        string root = FindSolutionRoot();
        var graph = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string dir in new[] { "src", "tests" })
        {
            foreach (string projectFile in Directory.GetFiles(Path.Combine(root, dir), "*.csproj", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(projectFile);
                XDocument doc = XDocument.Load(projectFile);
                string[] references = doc.Descendants("ProjectReference")
                    .Select(element => (string?)element.Attribute("Include"))
                    .Where(include => include is not null)
                    .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
                    .ToArray();
                graph[name] = references;
            }
        }

        return graph;
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
