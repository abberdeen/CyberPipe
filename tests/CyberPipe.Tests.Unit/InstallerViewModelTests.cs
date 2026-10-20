namespace CyberPipe.Tests.Unit;

using CyberPipe.Contracts;
using CyberPipe.Desktop.ViewModels;
using Xunit;

public sealed class InstallerViewModelTests
{
    private sealed class FakeInstaller : ILocalModelInstaller
    {
        public Result<ModelProbeResult>? InstallResult { get; set; }

        public bool InstallShouldHang { get; set; }

        public List<ModelDescriptorDto> InstalledRequests { get; } = [];

        public List<string> RemovedIds { get; } = [];

        public Task<IReadOnlyList<InstalledLocalModel>> ListInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InstalledLocalModel>>([]);

        public async Task<Result<ModelProbeResult>> InstallAsync(
            ModelDescriptorDto model,
            IProgress<ModelInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstalledRequests.Add(model);
            if (InstallShouldHang)
            {
                progress?.Report(new ModelInstallProgress { ModelId = model.ModelId, Percent = 10, Stage = "downloading" });
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            progress?.Report(new ModelInstallProgress { ModelId = model.ModelId, Percent = 100, Stage = "success" });
            return InstallResult ?? Result<ModelProbeResult>.Success(new ModelProbeResult { Succeeded = true });
        }

        public Task<Result> RemoveAsync(string modelId, CancellationToken cancellationToken = default)
        {
            RemovedIds.Add(modelId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<ModelProbeResult>> VerifyAsync(ModelDescriptorDto model, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ModelProbeResult>.Success(new ModelProbeResult { Succeeded = true, SampleText = "ok" }));

        public bool EmbeddingShouldHang { get; set; }

        public async Task<Result<EmbeddingInstallResult>> EnsureEmbeddingAsync(
            IProgress<ModelInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (EmbeddingShouldHang)
            {
                progress?.Report(new ModelInstallProgress { ModelId = "model.onnx", Percent = 35, DownloadedBytes = 3500, TotalBytes = 10000, Stage = "downloading" });
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            progress?.Report(new ModelInstallProgress { ModelId = "model.onnx", Percent = 100, DownloadedBytes = 10000, TotalBytes = 10000, Stage = "success" });
            return Result<EmbeddingInstallResult>.Success(
                new EmbeddingInstallResult { AlreadyInstalled = false, InstallPath = "emb", VectorDimensions = 384 });
        }
    }

    private sealed class FakeRuntime : ILocalRuntimeInstaller
    {
        public bool EnsureCalled { get; private set; }

        public Task<LocalRuntimeInfo> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalRuntimeInfo
            {
                State = LocalRuntimeState.Ready,
                Version = "0.11.0",
                ApiBaseUrl = "http://localhost:11434",
            });

        public Task<Result<LocalRuntimeInfo>> EnsureInstalledAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            EnsureCalled = true;
            return GetStatusAsync(cancellationToken)
                .ContinueWith(t => Result<LocalRuntimeInfo>.Success(t.Result), cancellationToken);
        }
    }

    private sealed class FakeAdvisor : IModelCompatibilityAdvisor
    {
        public Task<IReadOnlyList<ModelCompatibilityEvaluation>> EvaluateLocalModelsAsync(
            ModelTaskCategory? filterByCategory = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ModelCompatibilityEvaluation> result =
            [
                new()
                {
                    Model = TestModel(),
                    Verdict = HardwareFitVerdict.InsufficientResources,
                    RequiredMemoryGb = 8.0,
                    Explanation = "Needs 8 GB.",
                },
            ];
            return Task.FromResult(result);
        }

        public IReadOnlyList<ModelDescriptorDto> GetCloudModels(ModelTaskCategory? filterByCategory = null) => [];

        public Task<HostHardwareProfile> GetHardwareProfileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HostHardwareProfile.Empty);
    }

    private static ModelDescriptorDto TestModel() =>
        new()
        {
            Id = "qwen3-8b",
            Name = "Qwen3-8B",
            HostingType = ModelHostingType.Local,
            RecommendedFor = ModelTaskCategory.HeavyReasoningTier3,
            Provider = "Ollama",
            ModelId = "qwen3:8b",
            ParamsSummary = "8B",
            ContextWindowTokens = 32768,
            DefaultContextTokens = 8192,
            Q4SizeGb = 5.2,
            MinRamGb = 8,
            LicenseOrPricing = "Apache 2.0",
            BestFor = "Reasoning",
        };

    private static LocalModelInstallerViewModel CreateViewModel(
        FakeInstaller? installer = null,
        IModelCompatibilityAdvisor? advisor = null)
    {
        installer ??= new FakeInstaller();
        return new LocalModelInstallerViewModel(
            [TestModel()],
            installer,
            new FakeRuntime(),
            advisor);
    }

    [Fact]
    public async Task LowMemoryVerdict_BlocksInstallUntilRiskConfirmed()
    {
        var installer = new FakeInstaller();
        LocalModelInstallerViewModel vm = CreateViewModel(installer, new FakeAdvisor());
        await vm.RefreshAsync();
        LocalModelItemViewModel item = vm.Items[0];

        await vm.InstallModelAsync(item);

        Assert.True(item.NeedsRiskConfirmation);
        Assert.Empty(installer.InstalledRequests);
        Assert.NotEqual(LocalModelInstallState.Downloading, item.State);

        item.RiskConfirmed = true;
        await vm.InstallModelAsync(item);

        Assert.False(item.NeedsRiskConfirmation);
        Assert.Single(installer.InstalledRequests);
        Assert.Equal(LocalModelInstallState.Installed, item.State);
    }

    [Fact]
    public async Task WithoutAdvisor_InstallIsNotBlocked()
    {
        var installer = new FakeInstaller();
        LocalModelInstallerViewModel vm = CreateViewModel(installer, advisor: null);

        await vm.InstallModelAsync(vm.Items[0]);

        Assert.Single(installer.InstalledRequests);
        Assert.Equal(LocalModelInstallState.Installed, vm.Items[0].State);
    }

    [Fact]
    public async Task FailedInstall_MarksErrorState()
    {
        var installer = new FakeInstaller
        {
            InstallResult = Result<ModelProbeResult>.Failure("Model.PullFailed", "No network."),
        };
        LocalModelInstallerViewModel vm = CreateViewModel(installer);

        await vm.InstallModelAsync(vm.Items[0]);

        Assert.Equal(LocalModelInstallState.Error, vm.Items[0].State);
        Assert.Contains("No network.", vm.Items[0].StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelInstall_StopsDownload()
    {
        var installer = new FakeInstaller { InstallShouldHang = true };
        LocalModelInstallerViewModel vm = CreateViewModel(installer);
        LocalModelItemViewModel item = vm.Items[0];

        Task install = vm.InstallModelAsync(item);
        await Task.Delay(300);
        vm.CancelInstall(item);
        await install;

        Assert.Equal(LocalModelInstallState.NotInstalled, item.State);
        Assert.Contains("cancelled", item.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveModel_CallsInstaller()
    {
        var installer = new FakeInstaller();
        LocalModelInstallerViewModel vm = CreateViewModel(installer);

        await vm.RemoveModelAsync(vm.Items[0]);

        Assert.Equal(["qwen3:8b"], installer.RemovedIds);
        Assert.Equal(LocalModelInstallState.NotInstalled, vm.Items[0].State);
    }

    [Fact]
    public async Task Refresh_ShowsRuntimeStatusAndVerdicts()
    {
        LocalModelInstallerViewModel vm = CreateViewModel(advisor: new FakeAdvisor());

        await vm.RefreshAsync();

        Assert.Contains("0.11.0", vm.RuntimeStatusText, StringComparison.Ordinal);
        Assert.Contains("Low memory", vm.Items[0].VerdictText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureEmbedding_ShowsProgressAndCompletes()
    {
        LocalModelInstallerViewModel vm = CreateViewModel();

        await vm.EnsureEmbeddingAsync();

        Assert.True(vm.IsEmbeddingInstalled);
        Assert.False(vm.IsEmbeddingDownloading);
        Assert.Equal(100, vm.EmbeddingProgressPercent);
        Assert.Contains("emb", vm.EmbeddingStateText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureEmbedding_CanBeCancelled()
    {
        var installer = new FakeInstaller { EmbeddingShouldHang = true };
        LocalModelInstallerViewModel vm = CreateViewModel(installer);

        Task installTask = vm.EnsureEmbeddingAsync();

        Assert.True(vm.IsEmbeddingDownloading);
        Assert.Equal(35, vm.EmbeddingProgressPercent);

        vm.CancelEmbedding();
        await installTask;

        Assert.False(vm.IsEmbeddingDownloading);
        Assert.Contains("отменена", vm.EmbeddingStateText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Embedding_RemoveConfirmation_TogglesAndResetsState()
    {
        LocalModelInstallerViewModel vm = CreateViewModel();
        await vm.EnsureEmbeddingAsync();
        Assert.True(vm.IsEmbeddingInstalled);

        // Request remove -> confirmation shown
        vm.RequestRemoveEmbeddingCommand.Execute(null);
        Assert.True(vm.EmbeddingNeedsRemoveConfirmation);

        // Cancel remove -> confirmation hidden
        vm.CancelRemoveEmbeddingCommand.Execute(null);
        Assert.False(vm.EmbeddingNeedsRemoveConfirmation);

        // Request remove and confirm
        vm.RequestRemoveEmbeddingCommand.Execute(null);
        await vm.ConfirmRemoveEmbeddingAsync();

        Assert.False(vm.EmbeddingNeedsRemoveConfirmation);
        Assert.False(vm.IsEmbeddingInstalled);
        Assert.Contains("удалён", vm.EmbeddingStateText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelsViewModel_CatalogItems_ExposeAssignCommandDirectly()
    {
        var catalog = new List<ModelDescriptorDto>
        {
            new()
            {
                Id = "qwen2.5:7b",
                Name = "Qwen 2.5 7B",
                HostingType = ModelHostingType.Local,
                RecommendedFor = ModelTaskCategory.LightFastTier2,
                Provider = "Ollama",
                ModelId = "qwen2.5:7b",
                ParamsSummary = "7B",
                LicenseOrPricing = "Apache-2.0",
                BestFor = "Tier 2",
            }
        };

        var vm = new ModelsViewModel(catalog: catalog);

        Assert.Single(vm.CatalogItems);
        var catalogItem = vm.CatalogItems[0];
        Assert.Equal("Qwen 2.5 7B", catalogItem.Name);
        Assert.NotNull(catalogItem.AssignCommand);

        var tier2 = vm.Assignments.First(a => a.Tier == IntentTier.Tier2);
        Assert.NotEqual("qwen2.5:7b", tier2.ModelId);

        // Execute AssignCommand directly on the item view model (which is what XAML binds to now)
        catalogItem.AssignCommand.Execute(null);

        Assert.Equal("qwen2.5:7b", tier2.ModelId);
    }

    [Fact]
    public void ModelsViewModel_InstallerItems_WireAssignCommand()
    {
        var localCatalog = new List<ModelDescriptorDto>
        {
            new()
            {
                Id = "deepseek-r1:8b",
                Name = "DeepSeek R1 8B",
                HostingType = ModelHostingType.Local,
                RecommendedFor = ModelTaskCategory.HeavyReasoningTier3,
                Provider = "Ollama",
                ModelId = "deepseek-r1:8b",
                ParamsSummary = "8B",
                LicenseOrPricing = "MIT",
                BestFor = "Tier 3",
            }
        };

        var installerVm = new LocalModelInstallerViewModel(
            localCatalog,
            new FakeInstaller(),
            new FakeRuntime());

        var vm = new ModelsViewModel(
            installer: installerVm);

        Assert.Single(installerVm.Items);
        var item = installerVm.Items[0];
        Assert.NotNull(item.AssignCommand);

        var tier3 = vm.Assignments.First(a => a.Tier == IntentTier.Tier3);
        Assert.NotEqual("deepseek-r1:8b", tier3.ModelId);

        // Execute AssignCommand directly on the installer item view model
        item.AssignCommand.Execute(null);

        Assert.Equal("deepseek-r1:8b", tier3.ModelId);
    }
}
