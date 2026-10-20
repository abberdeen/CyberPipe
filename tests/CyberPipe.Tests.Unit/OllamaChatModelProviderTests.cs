using System.Net;
using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Models;
using CyberPipe.Router;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class OllamaChatModelProviderTests
{
    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(handler(request));
        }
    }

    private static IModelCatalog CreateCatalog()
    {
        return new FakeModelCatalog(
        [
            new ModelDescriptorDto
            {
                Id = "gemma-3n-e2b",
                Name = "Gemma 3n E2B",
                HostingType = ModelHostingType.Local,
                RecommendedFor = ModelTaskCategory.LightFastTier2,
                Provider = "Ollama",
                ModelId = "gemma3n:e2b",
                ParamsSummary = "2B",
                LicenseOrPricing = "Open",
                BestFor = "On-device",
                DefaultContextTokens = 8192
            },
            new ModelDescriptorDto
            {
                Id = "qwen3-0.6b",
                Name = "Qwen3-0.6B",
                HostingType = ModelHostingType.Local,
                RecommendedFor = ModelTaskCategory.LightFastTier2,
                Provider = "Ollama",
                ModelId = "qwen3:0.6b",
                ParamsSummary = "0.6B",
                LicenseOrPricing = "Apache 2.0",
                BestFor = "Instant JSON",
                DefaultContextTokens = 8192
            },
            new ModelDescriptorDto
            {
                Id = "deepseek-r1:8b",
                Name = "DeepSeek R1 8B",
                HostingType = ModelHostingType.Local,
                RecommendedFor = ModelTaskCategory.HeavyReasoningTier3,
                Provider = "Ollama",
                ModelId = "deepseek-r1:8b",
                ParamsSummary = "8B",
                LicenseOrPricing = "MIT",
                BestFor = "Reasoning",
                DefaultContextTokens = 8192
            }
        ]);
    }

    private sealed class FakeModelCatalog(IReadOnlyList<ModelDescriptorDto> localModels) : IModelCatalog
    {
        public string LocalCatalogVersion => "1.0.0";
        public string CloudCatalogVersion => "1.0.0";
        public string EmbeddingCatalogVersion => "1.0.0";
        public IReadOnlyList<ModelDescriptorDto> LocalModels => localModels;
        public IReadOnlyList<ModelDescriptorDto> CloudModels => [];
        public EmbeddingModelDescriptor EmbeddingModel => throw new NotImplementedException();
        public IReadOnlyList<EmbeddingModelDescriptor> AlternativeEmbeddingModels => [];

        public ModelDescriptorDto? GetById(string id) =>
            localModels.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<ModelDescriptorDto> Find(ModelTaskCategory? category = null, ModelHostingType? hostingType = null) =>
            localModels.Where(m =>
                (!category.HasValue || m.RecommendedFor == category.Value) &&
                (!hostingType.HasValue || m.HostingType == hostingType.Value)).ToList();
    }

    [Fact]
    public async Task CompleteAsync_WhenModelIdPassedInRequest_ResolvesToOllamaTag()
    {
        string? requestedModelInBody = null;
        var handler = new DelegatingHandlerStub(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/chat"))
            {
                using var stream = req.Content!.ReadAsStream();
                using var doc = JsonDocument.Parse(stream);
                requestedModelInBody = doc.RootElement.GetProperty("model").GetString();

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                            "message": { "role": "assistant", "content": "{\"intent\":\"test\"}" },
                            "prompt_eval_count": 10,
                            "eval_count": 20
                        }
                        """)
                };
                return response;
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var http = new HttpClient(handler);
        var catalog = CreateCatalog();
        var provider = new OllamaChatModelProvider(http, catalog);

        var response = await provider.CompleteAsync(new ChatModelRequest
        {
            ModelClass = ChatModelClass.Light,
            ModelId = "qwen3-0.6b",
            Prompt = "classify this"
        });

        Assert.Equal("qwen3:0.6b", requestedModelInBody);
        Assert.Equal("qwen3:0.6b", response.ModelId);
        Assert.Equal("{\"intent\":\"test\"}", response.Text);
    }

    [Fact]
    public async Task CompleteAsync_WhenTier2ModelIdConfiguredInOptions_UsesConfiguredModel()
    {
        string? requestedModelInBody = null;
        var handler = new DelegatingHandlerStub(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/chat"))
            {
                using var stream = req.Content!.ReadAsStream();
                using var doc = JsonDocument.Parse(stream);
                requestedModelInBody = doc.RootElement.GetProperty("model").GetString();

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                            "message": { "role": "assistant", "content": "ok" },
                            "prompt_eval_count": 5,
                            "eval_count": 15
                        }
                        """)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var http = new HttpClient(handler);
        var catalog = CreateCatalog();
        var options = new CyberPipeOptions
        {
            Router = new RouterOptions
            {
                Tier2ModelId = "qwen3-0.6b"
            }
        };
        var provider = new OllamaChatModelProvider(http, catalog, cyberPipeOptions: options);

        // Request has no explicit ModelId, but options has Tier2ModelId
        var response = await provider.CompleteAsync(new ChatModelRequest
        {
            ModelClass = ChatModelClass.Light,
            Prompt = "classify this"
        });

        Assert.Equal("qwen3:0.6b", requestedModelInBody);
        Assert.Equal("qwen3:0.6b", response.ModelId);
    }

    [Fact]
    public async Task CompleteAsync_WhenRequestedModelNotInstalled_FallsBackToInstalledOllamaModel()
    {
        var modelsRequested = new List<string>();
        var handler = new DelegatingHandlerStub(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/chat"))
            {
                using var stream = req.Content!.ReadAsStream();
                using var doc = JsonDocument.Parse(stream);
                string model = doc.RootElement.GetProperty("model").GetString()!;
                modelsRequested.Add(model);

                // gemma3n:e2b is NOT installed
                if (model == "gemma3n:e2b")
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("{\"error\":\"model not found\"}")
                    };
                }

                // fallback model succeeded
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                            "message": { "role": "assistant", "content": "{\"intent\":\"greeting\"}" },
                            "prompt_eval_count": 10,
                            "eval_count": 15
                        }
                        """)
                };
            }

            if (req.RequestUri!.AbsolutePath.EndsWith("/api/tags"))
            {
                // Return installed models in Ollama: qwen3:0.6b is installed
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                            "models": [
                                { "name": "qwen3:0.6b" }
                            ]
                        }
                        """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var http = new HttpClient(handler);
        var catalog = CreateCatalog();
        var provider = new OllamaChatModelProvider(http, catalog);

        // Explicitly request gemma-3n-e2b (which returns 404 in handler)
        // Since gemma3n:e2b returns 404, provider should query /api/tags and fall back to qwen3:0.6b!
        var response = await provider.CompleteAsync(new ChatModelRequest
        {
            ModelClass = ChatModelClass.Light,
            ModelId = "gemma-3n-e2b",
            Prompt = "hello"
        });

        Assert.Contains("gemma3n:e2b", modelsRequested);
        Assert.Contains("qwen3:0.6b", modelsRequested);
        Assert.Equal("qwen3:0.6b", response.ModelId);
        Assert.Equal("{\"intent\":\"greeting\"}", response.Text);
    }

    [Fact]
    public async Task CompleteAsync_WhenNoModelSpecified_AutomaticallyPrefersInstalledOllamaModel()
    {
        var modelsRequested = new List<string>();
        var handler = new DelegatingHandlerStub(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/chat"))
            {
                using var stream = req.Content!.ReadAsStream();
                using var doc = JsonDocument.Parse(stream);
                string model = doc.RootElement.GetProperty("model").GetString()!;
                modelsRequested.Add(model);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                            "message": { "role": "assistant", "content": "{\"intent\":\"greeting\"}" },
                            "prompt_eval_count": 10,
                            "eval_count": 15
                        }
                        """)
                };
            }

            if (req.RequestUri!.AbsolutePath.EndsWith("/api/tags"))
            {
                // qwen3:0.6b is installed, but gemma3n:e2b is not
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                            "models": [
                                { "name": "qwen3:0.6b" }
                            ]
                        }
                        """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var http = new HttpClient(handler);
        var catalog = CreateCatalog();
        var provider = new OllamaChatModelProvider(http, catalog);

        // No model specified; should choose qwen3:0.6b because it's installed, skipping uninstalled gemma3n:e2b
        var response = await provider.CompleteAsync(new ChatModelRequest
        {
            ModelClass = ChatModelClass.Light,
            Prompt = "hello"
        });

        Assert.Single(modelsRequested);
        Assert.Equal("qwen3:0.6b", modelsRequested[0]);
        Assert.Equal("qwen3:0.6b", response.ModelId);
    }

    [Fact]
    public async Task Tier23Router_PassesConfiguredModelIdInChatModelRequest()
    {
        string? passedModelId = null;
        var chatProvider = new RecordingChatProvider(req =>
        {
            passedModelId = req.ModelId;
            return new ChatModelResponse
            {
                Text = """
                    {
                        "intent": "system.help",
                        "confidence": 0.95
                    }
                    """,
                InputTokens = 10,
                OutputTokens = 10,
                ModelId = req.ModelId ?? "unknown"
            };
        });

        var governor = new FakeBudgetGovernor(allowAll: true);
        var options = new CyberPipeOptions
        {
            Router = new RouterOptions
            {
                Tier2ModelId = "qwen3-0.6b",
                Tier3ModelId = "deepseek-r1:8b"
            }
        };

        var router = new Tier23LlmRouter(chatProvider, governor, options: options);

        RouteResult result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "Help needed",
            Channel = ChannelKind.Cli
        });

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("qwen3-0.6b", passedModelId);
    }

    private sealed class RecordingChatProvider(Func<ChatModelRequest, ChatModelResponse> handler) : IChatModelProvider
    {
        public Task<ChatModelResponse> CompleteAsync(ChatModelRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(handler(request));
    }

    private sealed class FakeBudgetGovernor(bool allowAll) : ITokenBudgetGovernor
    {
        public Task<BudgetDecision> TryAuthorizeAsync(BudgetRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BudgetDecision { IsAllowed = allowAll });

        public Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<BudgetStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BudgetStatusDto
            {
                BudgetDate = DateOnly.FromDateTime(DateTime.UtcNow),
                SpentTodayUsd = 0m,
                RemainingTodayUsd = 1.0m,
                TokensUsedToday = 0,
                CloudTurnsUsed = 0,
                LocalTurnsUsed = 0,
                Limits = new BudgetLimitsDto { DailyCapUsd = 1.0m, SoftTokenLimit = 4000, HardTokenLimit = 12000, MaxCloudTurns = 3, MaxLocalTurns = 5 }
            });
    }
}
