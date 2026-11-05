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

        public bool PsNotFound { get; set; }

        public bool PsDown { get; set; }

        public string? TagsJsonOverride { get; set; }

        public string? PsJson { get; set; }

        public List<string> GenerateRequestBodies { get; } = [];

        public List<string> PullRequestBodies { get; } = [];

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
                if (TagsJsonOverride is not null)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(TagsJsonOverride, Encoding.UTF8, "application/json"),
                    };
                }

                var models = InstalledModels.Select(m => new { name = m.Name, size = m.Size }).ToArray();
                return Json(new { models });
            }

            if (path == "/api/ps" && request.Method == HttpMethod.Get)
            {
                if (PsDown)
                {
                    throw new HttpRequestException("Connection refused.");
                }

                if (PsNotFound)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(PsJson ?? """{"models":[]}""", Encoding.UTF8, "application/json"),
                };
            }

            if (path == "/api/pull" && request.Method == HttpMethod.Post)
            {
                PullRequestBodies.Add(await request.Content!.ReadAsStringAsync(ct));
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
    public async Task ListInstalled_ParsesTagDetails()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();
        handler.TagsJsonOverride = """
            {
              "models": [
                {
                  "name": "qwen2.5-coder:7b",
                  "size": 4700000000,
                  "modified_at": "2026-03-01T12:30:00Z",
                  "details": {
                    "family": "qwen2",
                    "parameter_size": "7.6B",
                    "quantization_level": "Q4_K_M"
                  }
                }
              ]
            }
            """;

        IReadOnlyList<InstalledLocalModel> installed = await installer.ListInstalledAsync();

        Assert.Single(installed);
        Assert.Equal("qwen2.5-coder:7b", installed[0].ModelId);
        Assert.Equal(4_700_000_000, installed[0].SizeBytes);
        Assert.Equal("qwen2", installed[0].Family);
        Assert.Equal("7.6B", installed[0].ParameterSize);
        Assert.Equal("Q4_K_M", installed[0].QuantizationLevel);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 12, 30, 0, TimeSpan.Zero), installed[0].ModifiedAtUtc);
    }

    [Fact]
    public async Task ListRunning_ReadsMemorySizes()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();
        handler.PsJson = """
            {
              "models": [
                {
                  "name": "qwen3:8b",
                  "size": 5583457484,
                  "size_vram": 5153960755,
                  "expires_at": "2026-04-02T08:15:00Z"
                }
              ]
            }
            """;

        IReadOnlyList<OllamaRunningModelDto> running = await installer.ListRunningAsync();

        Assert.Single(running);
        Assert.Equal("qwen3:8b", running[0].ModelId);
        Assert.Equal(5_583_457_484, running[0].SizeBytes);
        Assert.Equal(5_153_960_755, running[0].SizeVramBytes);
        Assert.Equal(new DateTimeOffset(2026, 4, 2, 8, 15, 0, TimeSpan.Zero), running[0].ExpiresAtUtc);
    }

    [Fact]
    public async Task ListRunning_WhenApiMissing_ReturnsEmpty()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();
        handler.PsNotFound = true;

        IReadOnlyList<OllamaRunningModelDto> missing = await installer.ListRunningAsync();

        handler.PsNotFound = false;
        handler.PsDown = true;
        IReadOnlyList<OllamaRunningModelDto> down = await installer.ListRunningAsync();

        Assert.Empty(missing);
        Assert.Empty(down);
    }

    [Fact]
    public async Task Unload_SendsKeepAliveZero()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();

        Result empty = await installer.UnloadAsync("  ");
        Result result = await installer.UnloadAsync("qwen3:8b");

        Assert.False(empty.IsSuccess);
        Assert.Equal("Model.NoRuntimeId", empty.Error!.Code);
        Assert.True(result.IsSuccess);
        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.GenerateRequestBodies));
        Assert.Equal("qwen3:8b", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("keep_alive").GetInt32());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task Unload_WhenRuntimeRefuses_ReportsFailure()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();
        handler.GenerateShouldFail = true;

        Result result = await installer.UnloadAsync("qwen3:8b");

        Assert.False(result.IsSuccess);
        Assert.Equal("Model.UnloadFailed", result.Error!.Code);
    }

    [Fact]
    public async Task PullByTag_DownloadsModelOutsideCatalog()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();
        var progress = new List<ModelInstallProgress>();

        Result<ModelProbeResult> result = await installer.PullByTagAsync(
            "qwen2.5-coder:7b",
            new Progress<ModelInstallProgress>(progress.Add));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Succeeded);
        using JsonDocument pull = JsonDocument.Parse(Assert.Single(handler.PullRequestBodies));
        Assert.Equal("qwen2.5-coder:7b", pull.RootElement.GetProperty("name").GetString());
        using JsonDocument probe = JsonDocument.Parse(Assert.Single(handler.GenerateRequestBodies));
        Assert.Equal("qwen2.5-coder:7b", probe.RootElement.GetProperty("model").GetString());
        Assert.Equal(8192, probe.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Contains(progress, item => item.ModelId == "qwen2.5-coder:7b");
    }

    [Fact]
    public async Task PullByTag_UsesCatalogDescriptorWhenTagMatches()
    {
        (OllamaLocalModelInstaller installer, FakeOllamaHandler handler) = CreateInstaller();

        Result<ModelProbeResult> result = await installer.PullByTagAsync("qwen3:8b");

        Assert.True(result.IsSuccess);
        using JsonDocument probe = JsonDocument.Parse(Assert.Single(handler.GenerateRequestBodies));
        Assert.Equal("qwen3:8b", probe.RootElement.GetProperty("model").GetString());
        Assert.Equal(8192, probe.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.False(probe.RootElement.TryGetProperty("keep_alive", out _));
    }

    [Fact]
    public async Task PullByTag_EmptyTag_IsRejected()
    {
        (OllamaLocalModelInstaller installer, _) = CreateInstaller();

        Result<ModelProbeResult> result = await installer.PullByTagAsync("   ");

        Assert.False(result.IsSuccess);
        Assert.Equal("Model.NoRuntimeId", result.Error!.Code);
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
    public async Task EnsureEmbedding_FailedRedownload_KeepsExistingFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cyberpipe-emb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string modelPath = Path.Combine(dir, "model.onnx");
        const string pointer = "version https://git-lfs.github.com/spec/v1\noid sha256:abc\nsize 1\n";
        await File.WriteAllTextAsync(modelPath, pointer);
        try
        {
            var options = new LocalModelInstallerOptions
            {
                EmbeddingDataDirectory = dir,
                EmbeddingSourceBaseUrl = "http://localhost/files",
            };
            (OllamaLocalModelInstaller installer, _) = CreateInstaller(options: options);

            Result<EmbeddingInstallResult> result = await installer.EnsureEmbeddingAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(pointer, await File.ReadAllTextAsync(modelPath));
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
    public async Task EnsureEmbedding_ReportsByteWeightedCumulativeProgress()
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
            handler.FileDownloads["/files/model.onnx"] = new byte[80];
            handler.FileDownloads["/files/tokenizer.json"] = new byte[20];

            var progressReports = new List<ModelInstallProgress>();
            var progress = new SyncProgress<ModelInstallProgress>(p => progressReports.Add(p));

            Result<EmbeddingInstallResult> result = await installer.EnsureEmbeddingAsync(progress);

            Assert.True(result.IsSuccess);
            Assert.NotEmpty(progressReports);

            // Progress should never decrease in cumulative bytes
            for (int i = 1; i < progressReports.Count; i++)
            {
                Assert.True(progressReports[i].DownloadedBytes >= progressReports[i - 1].DownloadedBytes,
                    $"DownloadedBytes jumped backwards: {progressReports[i - 1].DownloadedBytes} -> {progressReports[i].DownloadedBytes}");
            }

            var last = progressReports[^1];
            Assert.Contains("tokenizer.json (2/2)", last.ModelId, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(100, last.Percent);
            Assert.Equal(100, last.DownloadedBytes); // 80 + 20 = 100 bytes total
            Assert.Equal(100, last.TotalBytes);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
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

    private sealed class FakeLocalInstaller : ILocalModelInstaller
    {
        public bool EnsureEmbeddingCalled { get; set; }
        public bool InstallCalled { get; set; }
        public string? LastInstalledModelId { get; set; }
        public List<InstalledLocalModel> InstalledModels { get; } = [];

        public Task<IReadOnlyList<InstalledLocalModel>> ListInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InstalledLocalModel>>(InstalledModels);

        public Task<Result<ModelProbeResult>> InstallAsync(
            ModelDescriptorDto model,
            IProgress<ModelInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallCalled = true;
            LastInstalledModelId = model.Id;
            return Task.FromResult(Result<ModelProbeResult>.Success(new ModelProbeResult
            {
                Succeeded = true,
                SampleText = "pong",
            }));
        }

        public Task<Result> RemoveAsync(string modelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<ModelProbeResult>> VerifyAsync(ModelDescriptorDto model, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ModelProbeResult>.Success(new ModelProbeResult
            {
                Succeeded = true,
                SampleText = "pong",
            }));

        public Task<Result<EmbeddingInstallResult>> EnsureEmbeddingAsync(
            IProgress<ModelInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            EnsureEmbeddingCalled = true;
            return Task.FromResult(Result<EmbeddingInstallResult>.Success(new EmbeddingInstallResult
            {
                AlreadyInstalled = false,
                InstallPath = "/fake/embeddings",
                VectorDimensions = 384,
            }));
        }
    }

    [Fact]
    public async Task SetupModelInstallerAdapter_RoutesEmbeddingModel_ToEnsureEmbeddingAsync()
    {
        var installerMock = new FakeLocalInstaller();
        var catalog = new TestCatalog([]);
        var adapter = new SetupModelInstallerAdapter(installerMock, catalog);

        var result = await adapter.InstallModelAsync("multilingual-e5-small");

        Assert.True(result.IsSuccess);
        Assert.True(installerMock.EnsureEmbeddingCalled);
        Assert.False(installerMock.InstallCalled);
    }

    [Fact]
    public async Task SetupModelInstallerAdapter_RoutesLlmModel_ToInstallAsync()
    {
        var installerMock = new FakeLocalInstaller();
        var catalog = new TestCatalog([
            new ModelDescriptorDto
            {
                Id = "qwen2.5:3b",
                Name = "Qwen 2.5 3B",
                HostingType = ModelHostingType.Local,
                RecommendedFor = ModelTaskCategory.LightFastTier2,
                Provider = "Ollama",
                ModelId = "qwen2.5:3b",
                ParamsSummary = "3B",
                LicenseOrPricing = "Apache-2.0",
                BestFor = "General chat",
            }
        ]);
        var adapter = new SetupModelInstallerAdapter(installerMock, catalog);

        var result = await adapter.InstallModelAsync("qwen2.5:3b");

        Assert.True(result.IsSuccess);
        Assert.False(installerMock.EnsureEmbeddingCalled);
        Assert.True(installerMock.InstallCalled);
        Assert.Equal("qwen2.5:3b", installerMock.LastInstalledModelId);
    }
}
