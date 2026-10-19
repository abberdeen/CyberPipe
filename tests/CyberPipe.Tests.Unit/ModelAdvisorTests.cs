namespace CyberPipe.Tests.Unit;

using CyberPipe.Contracts;
using CyberPipe.Models;
using Xunit;

public sealed class ModelAdvisorTests
{
    private sealed class FakeCatalog(List<ModelDescriptorDto> local, List<ModelDescriptorDto> cloud) : IModelCatalog
    {
        public string LocalCatalogVersion => "test";

        public string CloudCatalogVersion => "test";

        public string EmbeddingCatalogVersion => "test";

        public IReadOnlyList<ModelDescriptorDto> LocalModels { get; } = local;

        public IReadOnlyList<ModelDescriptorDto> CloudModels { get; } = cloud;

        public EmbeddingModelDescriptor EmbeddingModel => throw new NotSupportedException();

        public IReadOnlyList<EmbeddingModelDescriptor> AlternativeEmbeddingModels => [];

        public ModelDescriptorDto? GetById(string id) => null;

        public IReadOnlyList<ModelDescriptorDto> Find(ModelTaskCategory? category = null, ModelHostingType? hostingType = null) => [];
    }

    private sealed class FakeProbe : IHardwareCapabilitiesProbe
    {
        public HostHardwareProfile Profile { get; set; } = HostHardwareProfile.Empty;

        public bool ShouldThrow { get; set; }

        public int CallCount { get; private set; }

        public Task<HostHardwareProfile> ProbeAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Probe failed.");
            }

            return Task.FromResult(Profile);
        }

        public void InvalidateCache()
        {
        }
    }

    private sealed class FakeMetrics(
        double? total,
        double? available,
        int? cores = 8,
        bool? avx2 = true,
        string? gpu = null,
        double? vram = null,
        double? disk = 100) : ISystemMetricsSource
    {
        public int TotalCalls { get; private set; }

        public double? GetTotalRamGb()
        {
            TotalCalls++;
            return total;
        }

        public double? GetAvailableRamGb() => available;

        public int? GetCpuLogicalCores() => cores;

        public bool? GetSupportsAvx2() => avx2;

        public string? GetGpuModelName() => gpu;

        public double? GetDedicatedVramGb() => vram;

        public double? GetFreeDiskSpaceGb(string? dataDirectory) => disk;
    }

    private static ModelDescriptorDto LocalModel(
        string id = "qwen3-8b",
        double q4 = 5.2,
        double minRam = 8,
        ModelTaskCategory category = ModelTaskCategory.HeavyReasoningTier3) =>
        new()
        {
            Id = id,
            Name = id,
            HostingType = ModelHostingType.Local,
            RecommendedFor = category,
            Provider = "Ollama",
            ModelId = id,
            ParamsSummary = "8B",
            ContextWindowTokens = 32768,
            DefaultContextTokens = 8192,
            Q4SizeGb = q4,
            MinRamGb = minRam,
            LicenseOrPricing = "Apache 2.0",
            BestFor = "Test",
        };

    [Fact]
    public void RequiredMemory_For52GbModel_Is80Gb()
    {
        var advisor = new ModelCompatibilityAdvisor(
            new FakeCatalog([LocalModel()], []),
            new FakeProbe());

        Assert.Equal(8.0, advisor.RequiredMemoryGb(LocalModel()));
    }

    [Fact]
    public async Task TotalRamBelowMin_GivesInsufficientResources()
    {
        var probe = new FakeProbe { Profile = new HostHardwareProfile { TotalRamGb = 4, AvailableRamGb = 20 } };
        var advisor = new ModelCompatibilityAdvisor(new FakeCatalog([LocalModel()], []), probe);

        IReadOnlyList<ModelCompatibilityEvaluation> result = await advisor.EvaluateLocalModelsAsync();

        var single = Assert.Single(result);
        Assert.Equal(HardwareFitVerdict.InsufficientResources, single.Verdict);
        Assert.Contains("8.0", single.Explanation, StringComparison.Ordinal);
        Assert.Contains("20.0", single.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VramCoveringRequired_GivesOptimal()
    {
        var probe = new FakeProbe
        {
            Profile = new HostHardwareProfile { TotalRamGb = 16, AvailableRamGb = 10, DedicatedVramGb = 12 },
        };
        var advisor = new ModelCompatibilityAdvisor(new FakeCatalog([LocalModel()], []), probe);

        IReadOnlyList<ModelCompatibilityEvaluation> result = await advisor.EvaluateLocalModelsAsync();

        Assert.Equal(HardwareFitVerdict.Optimal, Assert.Single(result).Verdict);
    }

    [Fact]
    public async Task RamWithHeadroom_GivesOptimal()
    {
        var probe = new FakeProbe
        {
            Profile = new HostHardwareProfile { TotalRamGb = 16, AvailableRamGb = 12 },
        };
        var advisor = new ModelCompatibilityAdvisor(new FakeCatalog([LocalModel()], []), probe);

        IReadOnlyList<ModelCompatibilityEvaluation> result = await advisor.EvaluateLocalModelsAsync();

        Assert.Equal(HardwareFitVerdict.Optimal, Assert.Single(result).Verdict);
    }

    [Fact]
    public async Task TightMemory_GivesCompatible()
    {
        var probe = new FakeProbe
        {
            Profile = new HostHardwareProfile { TotalRamGb = 8, AvailableRamGb = 8.0 },
        };
        var advisor = new ModelCompatibilityAdvisor(new FakeCatalog([LocalModel()], []), probe);

        IReadOnlyList<ModelCompatibilityEvaluation> result = await advisor.EvaluateLocalModelsAsync();

        var single = Assert.Single(result);
        Assert.Equal(HardwareFitVerdict.Compatible, single.Verdict);
        Assert.Contains("8.0", single.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Results_AreSortedOptimalFirstThenByMemory()
    {
        var probe = new FakeProbe
        {
            Profile = new HostHardwareProfile { TotalRamGb = 16, AvailableRamGb = 12 },
        };
        var catalog = new FakeCatalog(
            [
                LocalModel("big", q4: 5.2, minRam: 8),
                LocalModel("small", q4: 0.4, minRam: 2, category: ModelTaskCategory.LightFastTier2),
                LocalModel("huge", q4: 10, minRam: 32),
            ],
            []);
        var advisor = new ModelCompatibilityAdvisor(catalog, probe);

        IReadOnlyList<ModelCompatibilityEvaluation> result = await advisor.EvaluateLocalModelsAsync();

        Assert.Equal(3, result.Count);
        Assert.Equal("small", result[0].Model.Id);
        Assert.Equal("big", result[1].Model.Id);
        Assert.Equal(HardwareFitVerdict.InsufficientResources, result[2].Verdict);
        Assert.Equal("huge", result[2].Model.Id);
    }

    [Fact]
    public async Task CategoryFilter_KeepsOnlyRequestedTier()
    {
        var probe = new FakeProbe
        {
            Profile = new HostHardwareProfile { TotalRamGb = 32, AvailableRamGb = 24 },
        };
        var catalog = new FakeCatalog(
            [LocalModel("a", category: ModelTaskCategory.LightFastTier2), LocalModel("b")],
            []);
        var advisor = new ModelCompatibilityAdvisor(catalog, probe);

        IReadOnlyList<ModelCompatibilityEvaluation> tier2 =
            await advisor.EvaluateLocalModelsAsync(ModelTaskCategory.LightFastTier2);

        Assert.Single(tier2);
        Assert.Equal("a", tier2[0].Model.Id);
    }

    [Fact]
    public async Task UnknownAvailableMemory_NeverGivesOptimal()
    {
        var probe = new FakeProbe
        {
            Profile = new HostHardwareProfile { TotalRamGb = 64, AvailableRamGb = null, DedicatedVramGb = 24 },
        };
        var advisor = new ModelCompatibilityAdvisor(new FakeCatalog([LocalModel()], []), probe);

        IReadOnlyList<ModelCompatibilityEvaluation> result = await advisor.EvaluateLocalModelsAsync();

        var single = Assert.Single(result);
        Assert.NotEqual(HardwareFitVerdict.Optimal, single.Verdict);
        Assert.Contains("свободн", single.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyProfile_CapsVerdictsAtCompatibleWithNote()
    {
        var advisor = new ModelCompatibilityAdvisor(
            new FakeCatalog([LocalModel("a"), LocalModel("b", q4: 0.4, minRam: 2)], []),
            new FakeProbe { Profile = HostHardwareProfile.Empty });

        IReadOnlyList<ModelCompatibilityEvaluation> result = await advisor.EvaluateLocalModelsAsync();

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.NotEqual(HardwareFitVerdict.Optimal, e.Verdict));
        Assert.All(result, e => Assert.Contains("недоступны", e.Explanation, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ThrowingProbe_DegradesToEmptyProfile()
    {
        var advisor = new ModelCompatibilityAdvisor(
            new FakeCatalog([LocalModel()], []),
            new FakeProbe { ShouldThrow = true });

        IReadOnlyList<ModelCompatibilityEvaluation> result = await advisor.EvaluateLocalModelsAsync();

        var single = Assert.Single(result);
        Assert.NotEqual(HardwareFitVerdict.Optimal, single.Verdict);
    }

    [Fact]
    public void CloudModels_ReturnCatalogEntriesWithoutProbing()
    {
        var probe = new FakeProbe();
        var cloud = Enumerable.Range(0, 8)
            .Select(i => LocalModel($"cloud-{i}") with { HostingType = ModelHostingType.RemoteCloud })
            .ToList();
        var advisor = new ModelCompatibilityAdvisor(new FakeCatalog([], cloud), probe);

        IReadOnlyList<ModelDescriptorDto> result = advisor.GetCloudModels();

        Assert.Equal(8, result.Count);
        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task Probe_CachesSnapshotUntilInvalidated()
    {
        var metrics = new FakeMetrics(total: 16, available: 10);
        var probe = new SystemHardwareProbe(metrics);

        HostHardwareProfile first = await probe.ProbeAsync();
        HostHardwareProfile second = await probe.ProbeAsync();

        Assert.Same(first, second);
        Assert.Equal(1, metrics.TotalCalls);

        probe.InvalidateCache();
        HostHardwareProfile third = await probe.ProbeAsync();

        Assert.NotSame(first, third);
        Assert.Equal(2, metrics.TotalCalls);
    }

    [Fact]
    public async Task Probe_MapsFakeMetricsToProfile()
    {
        var probe = new SystemHardwareProbe(new FakeMetrics(32, 18.4, cores: 16, avx2: true, gpu: "RTX 4070", vram: 12, disk: 200));

        HostHardwareProfile profile = await probe.ProbeAsync();

        Assert.Equal(32, profile.TotalRamGb);
        Assert.Equal(18.4, profile.AvailableRamGb);
        Assert.Equal(16, profile.CpuLogicalCores);
        Assert.True(profile.SupportsAvx2);
        Assert.Equal("RTX 4070", profile.GpuModelName);
        Assert.Equal(12, profile.DedicatedVramGb);
        Assert.Equal(200, profile.FreeDiskSpaceGb);
    }

    [Fact]
    public async Task RealProbe_OnThisMachine_ReturnsSaneValuesWithoutThrowing()
    {
        var probe = new SystemHardwareProbe();

        HostHardwareProfile profile = await probe.ProbeAsync();

        Assert.NotNull(profile);
        Assert.True(!profile.TotalRamGb.HasValue || profile.TotalRamGb.Value > 0);
        Assert.True(!profile.CpuLogicalCores.HasValue || profile.CpuLogicalCores.Value > 0);
        Assert.True(!profile.FreeDiskSpaceGb.HasValue || profile.FreeDiskSpaceGb.Value > 0);
    }
}
