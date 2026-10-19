using System.Xml.Linq;
using Xunit;

namespace CyberPipe.Tests.Architecture;

public sealed class ProjectGraphTests
{
    private static readonly string[] ExpectedSrcProjects =
    [
        "CyberPipe.Brainstem",
        "CyberPipe.Chains",
        "CyberPipe.Channels.Telegram",
        "CyberPipe.Cli",
        "CyberPipe.Contracts",
        "CyberPipe.Logging",
        "CyberPipe.Data",
        "CyberPipe.Desktop",
        "CyberPipe.Models",
        "CyberPipe.Router",
        "CyberPipe.Security",
        "CyberPipe.Service",
        "CyberPipe.ServiceControl",
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

    [Fact]
    public void SrcProjects_ReferenceOnlyContracts()
    {
        // TASK-004: CyberPipe.Logging is the single shared infrastructure project on top of
        // Contracts; every src project references Contracts and at most Logging, nothing else.
        Dictionary<string, string[]> graph = LoadGraph();
        string root = FindSolutionRoot();
        foreach (string projectFile in Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(projectFile);
            if (name == "CyberPipe.Contracts")
            {
                continue;
            }

            Assert.Contains("CyberPipe.Contracts", graph[name]);
            Assert.True(
                graph[name].All(r => r is "CyberPipe.Contracts" or "CyberPipe.Logging"),
                $"{name} references unexpected projects: {string.Join(", ", graph[name])}");
            if (name == "CyberPipe.Logging")
            {
                Assert.Equal(["CyberPipe.Contracts"], graph[name]);
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
