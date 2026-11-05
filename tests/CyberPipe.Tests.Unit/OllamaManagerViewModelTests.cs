namespace CyberPipe.Tests.Unit;

using CyberPipe.Contracts;
using CyberPipe.Desktop.ViewModels;
using Xunit;

public sealed class OllamaManagerViewModelTests
{
    private sealed class FakeInstaller : ILocalModelInstaller
    {
        public List<InstalledLocalModel> Installed { get; } = [];

        public List<OllamaRunningModelDto> Running { get; } = [];

        public List<string> Unloaded { get; } = [];

        public List<string> PulledTags { get; } = [];

        public List<string> Removed { get; } = [];

        public Task<IReadOnlyList<InstalledLocalModel>> ListInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InstalledLocalModel>>(Installed.ToList());

        public Task<IReadOnlyList<OllamaRunningModelDto>> ListRunningAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OllamaRunningModelDto>>(Running.ToList());

        public Task<Result> UnloadAsync(string modelId, CancellationToken cancellationToken = default)
        {
            Unloaded.Add(modelId);
            Running.RemoveAll(model => string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(Result.Success());
        }

        public Task<Result<ModelProbeResult>> PullByTagAsync(
            string modelTag,
            IProgress<ModelInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            PulledTags.Add(modelTag);
            progress?.Report(new ModelInstallProgress
            {
                ModelId = modelTag,
                Percent = 40,
                DownloadedBytes = 400,
                TotalBytes = 1000,
                Stage = "downloading",
            });
            progress?.Report(new ModelInstallProgress { ModelId = modelTag, Percent = 100, Stage = "success" });
            Installed.Add(new InstalledLocalModel
            {
                ModelId = modelTag,
                SizeBytes = 4_700_000_000,
                ParameterSize = "7B",
                QuantizationLevel = "Q4_K_M",
            });
            return Task.FromResult(Result<ModelProbeResult>.Success(new ModelProbeResult { Succeeded = true, SampleText = "ok" }));
        }

        public Task<Result<ModelProbeResult>> InstallAsync(
            ModelDescriptorDto model,
            IProgress<ModelInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ModelProbeResult>.Success(new ModelProbeResult { Succeeded = true }));

        public Task<Result> RemoveAsync(string modelId, CancellationToken cancellationToken = default)
        {
            Removed.Add(modelId);
            Installed.RemoveAll(model => string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(Result.Success());
        }

        public Task<Result<ModelProbeResult>> VerifyAsync(ModelDescriptorDto model, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ModelProbeResult>.Success(new ModelProbeResult { Succeeded = true, SampleText = "ok" }));

        public Task<Result<EmbeddingInstallResult>> EnsureEmbeddingAsync(
            IProgress<ModelInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<EmbeddingInstallResult>.Success(new EmbeddingInstallResult
            {
                AlreadyInstalled = false,
                InstallPath = "emb",
                VectorDimensions = 384,
            }));
    }

    private sealed class FakeRuntime : ILocalRuntimeInstaller
    {
        public Task<LocalRuntimeInfo> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalRuntimeInfo
            {
                State = LocalRuntimeState.Ready,
                Version = "0.6.2",
                ApiBaseUrl = "http://localhost:11434",
            });

        public Task<Result<LocalRuntimeInfo>> EnsureInstalledAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LocalRuntimeInfo>.Success(new LocalRuntimeInfo
            {
                State = LocalRuntimeState.Ready,
                Version = "0.6.2",
                ApiBaseUrl = "http://localhost:11434",
            }));
    }

    private sealed class FakeAdvisor : IModelCompatibilityAdvisor
    {
        private readonly IReadOnlyList<ModelCompatibilityEvaluation> _evaluations;

        public FakeAdvisor(IReadOnlyList<ModelCompatibilityEvaluation> evaluations) => _evaluations = evaluations;

        public Task<IReadOnlyList<ModelCompatibilityEvaluation>> EvaluateLocalModelsAsync(
            ModelTaskCategory? filterByCategory = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_evaluations);

        public IReadOnlyList<ModelDescriptorDto> GetCloudModels(ModelTaskCategory? filterByCategory = null) => [];

        public Task<HostHardwareProfile> GetHardwareProfileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HostHardwareProfile
            {
                TotalRamGb = 32,
                AvailableRamGb = 18.4,
                FreeDiskSpaceGb = 120.5,
            });
    }

    private static ModelDescriptorDto Model(
        string id,
        string modelId,
        string name,
        ModelTaskCategory category,
        string bestFor) =>
        new()
        {
            Id = id,
            Name = name,
            HostingType = ModelHostingType.Local,
            RecommendedFor = category,
            Provider = "Ollama",
            ModelId = modelId,
            ParamsSummary = "test",
            Q4SizeGb = 2.5,
            MinRamGb = 4,
            LicenseOrPricing = "Apache-2.0",
            BestFor = bestFor,
        };

    [Fact]
    public async Task Refresh_ShowsEveryInstalledModel_IncludingOnesOutsideCatalog()
    {
        var catalogModel = Model("qwen3-8b", "qwen3:8b", "Qwen3 8B", ModelTaskCategory.HeavyReasoningTier3, "Сложные ответы");
        var installer = new FakeInstaller();
        installer.Installed.Add(new InstalledLocalModel { ModelId = "qwen3:8b", SizeBytes = 5_000_000_000, Family = "qwen3" });
        installer.Installed.Add(new InstalledLocalModel
        {
            ModelId = "qwen2.5-coder:7b",
            SizeBytes = 4_700_000_000,
            ParameterSize = "7.6B",
            QuantizationLevel = "Q4_K_M",
            Family = "qwen2",
        });
        var advisor = new FakeAdvisor(
        [
            new ModelCompatibilityEvaluation
            {
                Model = catalogModel,
                Verdict = HardwareFitVerdict.Optimal,
                RequiredMemoryGb = 6,
                Explanation = "Хватает памяти.",
            },
        ]);

        var vm = new OllamaManagerViewModel([catalogModel], installer, new FakeRuntime(), advisor);
        await vm.RefreshAsync();

        Assert.Equal(2, vm.InstalledModels.Count);
        InstalledOllamaModelItemViewModel known = vm.InstalledModels.Single(item => item.ModelId == "qwen3:8b");
        Assert.Equal("Qwen3 8B", known.DisplayName);
        Assert.Equal("Сложные ответы", known.Description);
        Assert.Contains("Оптимально", known.VerdictText, StringComparison.Ordinal);
        InstalledOllamaModelItemViewModel custom = vm.InstalledModels.Single(item => item.ModelId == "qwen2.5-coder:7b");
        Assert.Equal("qwen2.5-coder:7b", custom.DisplayName);
        Assert.False(custom.IsCatalogModel);
        Assert.Contains("7.6B", custom.MetaText, StringComparison.Ordinal);
        Assert.Contains("Q4_K_M", custom.MetaText, StringComparison.Ordinal);
        Assert.Contains("32.0", vm.HardwareSummaryText, StringComparison.Ordinal);
        Assert.Contains("18.4", vm.HardwareSummaryText, StringComparison.Ordinal);
        Assert.True(vm.IsRuntimeReady);
        Assert.Equal("http://localhost:11434", vm.ApiBaseUrl);
    }

    [Fact]
    public async Task RecommendedFilters_AndSearch_UpdateVisibleModels()
    {
        var fast = Model("fast", "llama3.2:3b", "Llama Fast", ModelTaskCategory.LightFastTier2, "Быстрые ответы");
        var coder = Model("coder", "qwen2.5-coder:7b", "Qwen Coder", ModelTaskCategory.HeavyReasoningTier3, "Код и рассуждения");
        var huge = Model("huge", "big:70b", "Huge Expert", ModelTaskCategory.LightFastTier2, "Очень большая");
        var advisor = new FakeAdvisor(
        [
            new ModelCompatibilityEvaluation
            {
                Model = fast,
                Verdict = HardwareFitVerdict.Optimal,
                RequiredMemoryGb = 3,
                Explanation = "Идеально.",
            },
            new ModelCompatibilityEvaluation
            {
                Model = coder,
                Verdict = HardwareFitVerdict.Compatible,
                RequiredMemoryGb = 8,
                Explanation = "Потянет.",
            },
            new ModelCompatibilityEvaluation
            {
                Model = huge,
                Verdict = HardwareFitVerdict.InsufficientResources,
                RequiredMemoryGb = 40,
                Explanation = "Мало памяти.",
            },
        ]);

        var vm = new OllamaManagerViewModel([huge, coder, fast], new FakeInstaller(), new FakeRuntime(), advisor);
        await vm.RefreshAsync();

        Assert.Equal(["Llama Fast", "Qwen Coder", "Huge Expert"], vm.FilteredRecommendedModels.Select(item => item.DisplayName).ToArray());

        vm.SelectedFilter = "optimal";
        Assert.Equal(["Llama Fast"], vm.FilteredRecommendedModels.Select(item => item.DisplayName).ToArray());

        vm.SelectedFilter = "tier2";
        Assert.Equal(["Llama Fast", "Huge Expert"], vm.FilteredRecommendedModels.Select(item => item.DisplayName).ToArray());

        vm.SelectedFilter = "tier3";
        Assert.Equal(["Qwen Coder"], vm.FilteredRecommendedModels.Select(item => item.DisplayName).ToArray());

        vm.SelectedFilter = "all";
        vm.SearchQuery = "coder";
        Assert.Equal(["Qwen Coder"], vm.FilteredRecommendedModels.Select(item => item.DisplayName).ToArray());
    }

    [Fact]
    public async Task PullCustomTag_DownloadsTag_AndAddsItToInstalledModels()
    {
        var installer = new FakeInstaller();
        var vm = new OllamaManagerViewModel([], installer, new FakeRuntime());
        await vm.RefreshAsync();
        int changes = 0;
        vm.OnInstalledModelsChanged = () => changes++;
        vm.CustomPullTag = "qwen2.5-coder:7b";

        await vm.PullCustomTagCommand.ExecuteAsync();

        Assert.Equal(["qwen2.5-coder:7b"], installer.PulledTags);
        Assert.Contains(vm.InstalledModels, item => item.ModelId == "qwen2.5-coder:7b");
        Assert.Equal(1, changes);
        Assert.Equal(100, vm.CustomPullProgressPercent);
        Assert.Contains("установлена", vm.CustomPullStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsCustomPulling);
    }

    [Fact]
    public async Task UnloadCommand_ReleasesModel_AndRefreshesRunningList()
    {
        var installer = new FakeInstaller();
        installer.Running.Add(new OllamaRunningModelDto
        {
            ModelId = "qwen3:8b",
            SizeBytes = 5_583_457_484,
            SizeVramBytes = 5_153_960_755,
        });
        var vm = new OllamaManagerViewModel([], installer, new FakeRuntime());
        await vm.RefreshAsync();

        Assert.True(vm.HasRunningModels);
        Assert.Contains("VRAM", vm.RunningModels[0].SizeFormatted, StringComparison.Ordinal);
        Assert.Contains("1 модель", vm.MemoryStatusText, StringComparison.Ordinal);

        await vm.RunningModels[0].UnloadCommand!.ExecuteAsync();

        Assert.Equal(["qwen3:8b"], installer.Unloaded);
        Assert.False(vm.HasRunningModels);
        Assert.Contains("Память свободна", vm.MemoryStatusText, StringComparison.Ordinal);
    }
}
