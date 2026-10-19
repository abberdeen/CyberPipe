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

        public Task<Result<EmbeddingInstallResult>> EnsureEmbeddingAsync(
            IProgress<ModelInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<EmbeddingInstallResult>.Success(
                new EmbeddingInstallResult { AlreadyInstalled = true, InstallPath = "emb", VectorDimensions = 384 }));
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
    public async Task EnsureEmbedding_UpdatesStateText()
    {
        LocalModelInstallerViewModel vm = CreateViewModel();

        await vm.EnsureEmbeddingAsync();

        Assert.Contains("already installed", vm.EmbeddingStateText, StringComparison.OrdinalIgnoreCase);
    }
}
