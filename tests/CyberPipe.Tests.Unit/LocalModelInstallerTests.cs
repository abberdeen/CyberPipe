namespace CyberPipe.Tests.Unit;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CyberPipe.Contracts;
using CyberPipe.Models;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class LocalModelInstallerTests
{
    private sealed class TestCatalog : IModelCatalog
    {
        public TestCatalog(List<ModelDescriptorDto> local)
        {
            LocalModels = local;
        }

        public string LocalCatalogVersion => "test-1";

        public string CloudCatalogVersion => "test-1";

        public string EmbeddingCatalogVersion => "test-1";

        public IReadOnlyList<ModelDescriptorDto> LocalModels { get; }

        public IReadOnlyList<ModelDescriptorDto> CloudModels => [];

        public EmbeddingModelDescriptor EmbeddingModel { get; } = new()
        {
            Model = new ModelDescriptorDto
            {
                Id = "multilingual-e5-small",
                Name = "Multilingual E5 Small",
                HostingType = ModelHostingType.Local,
                RecommendedFor = ModelTaskCategory.EmbeddingTier1,
                Provider = "ONNX",
                ModelId = "intfloat/multilingual-e5-small",
                ParamsSummary = "118M",
                ContextWindowTokens = 512,
                DefaultContextTokens = 512,
                Q4SizeGb = 0.5,
                MinRamGb = 2,
                LicenseOrPricing = "MIT",
                BestFor = "Tier 1 embeddings",
            },
            VectorDimensions = 384,
            Normalization = "l2",
        };

        public IReadOnlyList<EmbeddingModelDescriptor> AlternativeEmbeddingModels => [];

        public ModelDescriptorDto? GetById(string id) =>
            LocalModels.FirstOrDefault(m => m.Id == id);

        public IReadOnlyList<ModelDescriptorDto> Find(ModelTaskCategory? category = null, ModelHostingType? hostingType = null) =>
            LocalModels
                .Where(m => (!category.HasValue || m.RecommendedFor == category.Value) &&
                            (!hostingType.HasValue || m.HostingType == hostingType.Value))
                .ToList();
    }

    private sealed class FakeDiskProbe(long? freeBytes) : IDiskSpaceProbe
    {
        public Task<long?> GetFreeBytesAsync(string? directory, CancellationToken cancellationToken = default) =>
            Task.FromResult(freeBytes);
    }

    private sealed class FakeOllamaHandler : HttpMessageHandler
    {
        public bool RuntimeDown { get; set; }

        public List<(string Name, long Size)> InstalledModels { get; } = [];

        public bool PullShouldFail { get; set; }

        public bool PullShouldHang { get; set; }

        public bool GenerateShouldFail { get; set; }

        public bool DeleteShouldFail { get; set; }

        public List<string> GenerateRequestBodies { get; } = [];

        public Dictionary<string, byte[]> FileDownloads { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/version")
            {
                if (RuntimeDown)
                {
                    throw new HttpRequestException("Connection refused.");
                }

                return Json(new { version = "0.11.0" });
            }

            if (path == "/api/tags" && request.Method == HttpMethod.Get)
            {
                var models = InstalledModels.Select(m => new { name = m.Name, size = m.Size }).ToArray();
                return Json(new { models });
            }

            if (path == "/api/pull" && request.Method == HttpMethod.Post)
            {
                if (PullShouldHang)
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }

                var lines = new List<string>
                {
                    """{"status":"pulling manifest"}""",
                    """{"status":"downloading","completed":100,"total":1000}""",
                    """{"status":"downloading","completed":1000,"total":1000}""",
                };
                lines.Add(PullShouldFail ? """{"status":"error","error":"broken"}""" : """{"status":"success"}""");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(string.Join("\n", lines), Encoding.UTF8, "application/x-ndjson"),
                };
            }

            if (path == "/api/generate" && request.Method == HttpMethod.Post)
            {
                GenerateRequestBodies.Add(await request.Content!.ReadAsStringAsync(ct));
                if (GenerateShouldFail)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                return Json(new { response = "ok" });
            }

            if (path == "/api/delete" && request.Method == HttpMethod.Delete)
            {
                if (DeleteShouldFail)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                string body = await request.Content!.ReadAsStringAsync(ct);
                string? name = JsonNode.Parse(body)!["name"]!.GetValue<string>();
                InstalledModels.RemoveAll(m => m.Name == name);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (request.Method == HttpMethod.Get && FileDownloads.TryGetValue(path, out byte[]? bytes))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json"),
            };
    }

    private static ModelDescriptorDto TestModel(string id = "qwen3-8b", string modelId = "qwen3:8b") =>
        new()
        {
            Id = id,
            Name = "Qwen3-8B",
            HostingType = ModelHostingType.Local,
            RecommendedFor = ModelTaskCategory.HeavyReasoningTier3,
            Provider = "Ollama",
            ModelId = modelId,
            ParamsSummary = "8B",
            ContextWindowTokens = 32768,
            DefaultContextTokens = 8192,
            Q4SizeGb = 5.2,
            MinRamGb = 8,
            LicenseOrPricing = "Apache 2.0",
            BestFor = "Reasoning",
        };

    private static (OllamaLocalModelInstaller Installer, FakeOllamaHandler Handler) CreateInstaller(
        TestCatalog? catalog = null,
        long? freeBytes = 100L * 1024 * 1024 * 1024,
        LocalModelInstallerOptions? options = null)
    {
        var handler = new FakeOllamaHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var installer = new OllamaLocalModelInstaller(
            http,
            catalog ?? new TestCatalog([TestModel()]),
            Options.Create(options ?? new LocalModelInstallerOptions()),
            new FakeDiskProbe(freeBytes));
        return (installer, handler);
    }

    [Fact]
    public async Task Install_LaunchesWithDefaultContextTokensNotMax()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();
        var seen = new List<ModelInstallProgress>();
        var progress = new Progress<ModelInstallProgress>(p => seen.Add(p));

        Result<ModelProbeResult> result = await installer.InstallAsync(TestModel(), progress);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Succeeded);
        Assert.NotEmpty(seen);
        Assert.All(seen, p => Assert.InRange(p.Percent, 0, 100));
        Assert.Single(handler.GenerateRequestBodies);
        JsonNode body = JsonNode.Parse(handler.GenerateRequestBodies[0])!;
        Assert.Equal(8192, body["options"]!["num_ctx"]!.GetValue<int>());
    }

    [Fact]
    public async Task Install_UsesSmallWindowForSmallModels()
    {
        var tiny = TestModel("tinyllama-1.1b", "tinyllama:1.1b") with
        {
            ContextWindowTokens = 2048,
            DefaultContextTokens = 2048,
            Q4SizeGb = 0.7,
            MinRamGb = 2,
        };
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) =
            CreateInstaller(new TestCatalog([tiny]));

        Result<ModelProbeResult> result = await installer.InstallAsync(tiny);

        Assert.True(result.IsSuccess);
        JsonNode body = JsonNode.Parse(handler.GenerateRequestBodies[0])!;
        Assert.Equal(2048, body["options"]!["num_ctx"]!.GetValue<int>());
    }

    [Fact]
    public async Task Install_CancelledMidPull_StopsAndCleansUp()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();
        handler.PullShouldHang = true;
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(200);

        Result<ModelProbeResult> result = await installer.InstallAsync(TestModel(), cancellationToken: cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("Model.InstallCancelled", result.Error!.Code);
    }

    [Fact]
    public async Task Install_WhenPullFails_ReportsError()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();
        handler.PullShouldFail = true;

        Result<ModelProbeResult> result = await installer.InstallAsync(TestModel());

        Assert.False(result.IsSuccess);
        Assert.Equal("Model.PullFailed", result.Error!.Code);
    }

    [Fact]
    public async Task Install_WhenProbeFails_ReportsError()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();
        handler.GenerateShouldFail = true;

        Result<ModelProbeResult> result = await installer.InstallAsync(TestModel());

        Assert.False(result.IsSuccess);
        Assert.Equal("Model.ProbeFailed", result.Error!.Code);
    }

    [Fact]
    public async Task Install_WhenDiskTooSmall_FailsBeforeNetwork()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller(freeBytes: 1024);

        Result<ModelProbeResult> result = await installer.InstallAsync(TestModel());

        Assert.False(result.IsSuccess);
        Assert.Equal("Model.NoDiskSpace", result.Error!.Code);
        Assert.Empty(handler.GenerateRequestBodies);
    }

    [Fact]
    public async Task Install_NonLocalModel_IsRejected()
    {
        (OllamaLocalModelInstaller installer, _) = CreateInstaller();
        var cloud = TestModel() with { HostingType = ModelHostingType.RemoteCloud };

        Result<ModelProbeResult> result = await installer.InstallAsync(cloud);

        Assert.False(result.IsSuccess);
        Assert.Equal("Model.NotLocal", result.Error!.Code);
    }

    [Fact]
    public async Task Remove_DeletesModel()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();
        handler.InstalledModels.Add(("qwen3:8b", 1024));

        Result result = await installer.RemoveAsync("qwen3:8b");

        Assert.True(result.IsSuccess);
        Assert.Empty(handler.InstalledModels);
    }

    [Fact]
    public async Task ListInstalled_ReturnsRuntimeModels()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();
        handler.InstalledModels.Add(("qwen3:8b", 5_200_000_000));

        IReadOnlyList<InstalledLocalModel> installed = await installer.ListInstalledAsync();

        Assert.Single(installed);
        Assert.Equal("qwen3:8b", installed[0].ModelId);
    }

    [Fact]
    public async Task EnsureEmbedding_WhenInstalled_IsNoOp()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cyberpipe-emb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir, "model.onnx"), [1, 2, 3]);
            var options = new LocalModelInstallerOptions { EmbeddingDataDirectory = dir };
            (OllamaLocalModelInstaller installer, _) = CreateInstaller(options: options);

            Result<EmbeddingInstallResult> result = await installer.EnsureEmbeddingAsync();

            Assert.True(result.IsSuccess);
            Assert.True(result.Value!.AlreadyInstalled);
            Assert.Equal(384, result.Value.VectorDimensions);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureEmbedding_WhenMissing_DownloadsFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cyberpipe-emb-{Guid.NewGuid():N}");
        try
        {
            var options = new LocalModelInstallerOptions
            {
                EmbeddingDataDirectory = dir,
                EmbeddingSourceBaseUrl = "http://localhost/files",
            };
            (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller(options: options);
            handler.FileDownloads["/files/model.onnx"] = [10, 20, 30];
            handler.FileDownloads["/files/tokenizer.json"] = [40, 50];

            Result<EmbeddingInstallResult> result = await installer.EnsureEmbeddingAsync();

            Assert.True(result.IsSuccess);
            Assert.False(result.Value!.AlreadyInstalled);
            Assert.Equal(dir, result.Value.InstallPath);
            Assert.True(new FileInfo(Path.Combine(dir, "model.onnx")).Length > 0);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeStatus_ReadyWhenApiAnswers()
    {
        var handler = new FakeOllamaHandler();
        var runtime = new OllamaLocalRuntimeInstaller(
            new HttpClient(handler),
            Options.Create(new LocalModelInstallerOptions()));

        LocalRuntimeInfo info = await runtime.GetStatusAsync();

        Assert.Equal(LocalRuntimeState.Ready, info.State);
        Assert.Equal("0.11.0", info.Version);
    }

    [Fact]
    public async Task RuntimeStatus_NotInstalledWhenApiDown()
    {
        var handler = new FakeOllamaHandler { RuntimeDown = true };
        var runtime = new OllamaLocalRuntimeInstaller(
            new HttpClient(handler),
            Options.Create(new LocalModelInstallerOptions()));

        LocalRuntimeInfo info = await runtime.GetStatusAsync();

        Assert.Equal(LocalRuntimeState.NotInstalled, info.State);
    }

    [Fact]
    public async Task RuntimeEnsure_WhenReady_DoesNotInstall()
    {
        var handler = new FakeOllamaHandler();
        bool hookCalled = false;
        var runtime = new OllamaLocalRuntimeInstaller(
            new HttpClient(handler),
            Options.Create(new LocalModelInstallerOptions()),
            (_, _, _) =>
            {
                hookCalled = true;
                return Task.FromResult(true);
            });

        Result<LocalRuntimeInfo> result = await runtime.EnsureInstalledAsync();

        Assert.True(result.IsSuccess);
        Assert.False(hookCalled);
    }

    [Fact]
    public async Task RuntimeEnsure_WhenMissing_InstallsAndWaitsForReady()
    {
        var handler = new FakeOllamaHandler { RuntimeDown = true };
        var runtime = new OllamaLocalRuntimeInstaller(
            new HttpClient(handler),
            Options.Create(new LocalModelInstallerOptions()),
            (_, _, _) =>
            {
                handler.RuntimeDown = false;
                return Task.FromResult(true);
            });

        Result<LocalRuntimeInfo> result = await runtime.EnsureInstalledAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(LocalRuntimeState.Ready, result.Value!.State);
    }
}
