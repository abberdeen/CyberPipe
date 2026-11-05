namespace CyberPipe.Tests.Unit;

using System.Net;
using System.Text;
using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Services;
using CyberPipe.Desktop.ViewModels;
using Xunit;

public sealed class RemoteProvidersViewModelTests
{
    private sealed class MemorySecrets : ISecretStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public bool IsPlatformStore => false;

        public Task SetAsync(string key, SecretValue value, CancellationToken cancellationToken = default)
        {
            Values[key] = value.Reveal();
            return Task.CompletedTask;
        }

        public Task<SecretValue?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.TryGetValue(key, out string? plain) ? SecretValue.FromPlainText(plain) : null);

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.Remove(key));
    }

    private sealed class ProviderHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? Authorization { get; private set; }

        public string? Path { get; private set; }

        public string? Body { get; private set; }

        public HttpStatusCode ModelsStatus { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Path = request.RequestUri?.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.EndsWith("/models", StringComparison.Ordinal))
            {
                if (ModelsStatus != HttpStatusCode.OK)
                {
                    return new HttpResponseMessage(ModelsStatus);
                }

                return Json("""{"data":[{"id":"gpt-4o-mini"},{"id":"gpt-4.1-nano"}]}""");
            }

            if (path.EndsWith("/chat/completions", StringComparison.Ordinal))
            {
                return Json("""
                    {
                      "choices":[{"message":{"role":"assistant","content":"hello from cloud"}}],
                      "usage":{"prompt_tokens":3,"completion_tokens":5}
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }

    private static string TempOverrides()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cyberpipe-providers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ui-overrides.json");
    }

    [Fact]
    public async Task Service_SavesBaseUrl_AndKeepsApiKeyOutOfJson()
    {
        string path = TempOverrides();
        var secrets = new MemorySecrets();
        var service = new RemoteProviderService(path, secrets, new HttpClient(new ProviderHandler()));

        IReadOnlyList<string> presetIds = service.GetBuiltInPresets().Select(preset => preset.Id).ToArray();
        Assert.Contains("openai", presetIds);
        Assert.Contains("anthropic", presetIds);
        Assert.Contains("google", presetIds);
        Assert.Contains("deepseek", presetIds);
        Assert.Contains("openrouter", presetIds);
        Assert.Contains("custom", presetIds);

        IReadOnlyList<RemoteProviderProfileDto> loaded = service.LoadProviders();
        RemoteProviderProfileDto openai = loaded.Single(provider => provider.Id == "openai");
        service.SaveProviders(loaded.Select(provider => provider.Id == "openai"
            ? provider with { BaseUrl = "https://proxy.example/v1" }
            : provider));
        await service.SaveApiKeyAsync("openai", "sk-super-secret");

        string json = File.ReadAllText(path);
        Assert.Contains("https://proxy.example/v1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-super-secret", json, StringComparison.Ordinal);
        Assert.Equal(SecretKeys.ForModelProvider("openai"), openai.ApiKeySecretName);
        Assert.True(await service.HasApiKeyAsync("openai"));
        Assert.Equal("sk-super-secret", (await service.GetApiKeyAsync("openai"))!.Reveal());
    }

    [Fact]
    public async Task Probe_ParsesModelList_AndReportsUnauthorized()
    {
        string path = TempOverrides();
        var handler = new ProviderHandler();
        var service = new RemoteProviderService(path, new MemorySecrets(), new HttpClient(handler));
        RemoteProviderProfileDto openai = service.GetBuiltInPresets().Single(provider => provider.Id == "openai");

        RemoteProviderProbeResult ok = await service.ProbeAndDiscoverModelsAsync(openai, "sk-live");

        Assert.True(ok.Success);
        Assert.Contains(ok.DiscoveredModels, model => model.ModelId == "gpt-4o-mini" && model.IsEnabled);
        RemoteModelEntryDto discovered = ok.DiscoveredModels.Single(model => model.ModelId == "gpt-4.1-nano");
        Assert.False(discovered.IsEnabled);
        Assert.Equal("Bearer sk-live", handler.Authorization);

        handler.ModelsStatus = HttpStatusCode.Unauthorized;
        RemoteProviderProbeResult denied = await service.ProbeAndDiscoverModelsAsync(openai, "sk-bad");
        Assert.False(denied.Success);
        Assert.Equal("Ошибка авторизации (HTTP 401/403): проверьте правильность API-ключа.", denied.Message);
    }

    [Fact]
    public async Task ViewModel_CanAddModel_ToggleIt_AndAssignTier()
    {
        string path = TempOverrides();
        var service = new RemoteProviderService(path, new MemorySecrets(), new HttpClient(new ProviderHandler()));
        var vm = new RemoteProvidersViewModel(service);
        await vm.LoadAsync();

        RemoteProviderItemViewModel openai = vm.Providers.Single(provider => provider.Id == "openai");
        openai.NewModelId = "my-corp-model";
        openai.NewModelDisplayName = "Корпоративная модель";
        openai.AddModelCommand!.Execute(null);

        RemoteProviderModelItemViewModel added = openai.Models.Single(model => model.ModelId == "my-corp-model");
        Assert.Equal("Корпоративная модель", added.DisplayName);
        Assert.True(added.IsEnabled);
        added.IsEnabled = false;
        await vm.SaveAsync();

        IntentTier? assignedTier = null;
        string? assignedId = null;
        vm.OnAssignRemoteModelToTier = (tier, modelId, _) =>
        {
            assignedTier = tier;
            assignedId = modelId;
        };
        added.AssignFastCommand!.Execute(null);

        Assert.Equal(ModelTaskCategory.LightFastTier2, added.RecommendedFor);
        Assert.Equal(IntentTier.Tier2, assignedTier);
        Assert.Equal("my-corp-model", assignedId);

        openai.ModelFilterText = "corp";
        Assert.Equal(["my-corp-model"], openai.FilteredModels.Select(model => model.ModelId).ToArray());

        string json = File.ReadAllText(path);
        Assert.Contains("my-corp-model", json, StringComparison.Ordinal);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement stored = doc.RootElement.GetProperty("RemoteProviders").EnumerateArray()
            .SelectMany(provider => provider.GetProperty("Models").EnumerateArray())
            .Single(model => model.GetProperty("ModelId").GetString() == "my-corp-model");
        Assert.False(stored.GetProperty("IsEnabled").GetBoolean());
        Assert.Equal("LightFastTier2", stored.GetProperty("RecommendedFor").GetString());
    }

    [Fact]
    public async Task ModelTestService_RemoteCloud_CallsProviderAndReturnsTokens()
    {
        string path = TempOverrides();
        var handler = new ProviderHandler();
        var secrets = new MemorySecrets();
        var service = new RemoteProviderService(path, secrets, new HttpClient(new ProviderHandler()));
        service.LoadProviders();
        await service.SaveApiKeyAsync("openai", "sk-cloud");
        var tester = new ModelTestService(new HttpClient(handler), remoteProviders: service);

        ModelTestResponse missingKey = await tester.TestModelAsync(
            "claude-3-5-haiku",
            ModelHostingType.RemoteCloud,
            "привет",
            "Anthropic");
        Assert.False(missingKey.Success);
        Assert.Contains("не настроен API-ключ", missingKey.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Anthropic", missingKey.ErrorMessage, StringComparison.Ordinal);

        int callsBefore = handler.Calls;
        ModelTestResponse response = await tester.TestModelAsync(
            "gpt-4o-mini",
            ModelHostingType.RemoteCloud,
            "привет",
            "OpenAI");

        Assert.True(response.Success);
        Assert.Equal("hello from cloud", response.ResponseText);
        Assert.Equal(3, response.InputTokens);
        Assert.Equal(5, response.OutputTokens);
        Assert.True(handler.Calls > callsBefore);
        Assert.EndsWith("/chat/completions", handler.Path, StringComparison.Ordinal);
        Assert.Equal("Bearer sk-cloud", handler.Authorization);
        Assert.Contains("\"max_tokens\":512", handler.Body, StringComparison.Ordinal);
    }
}
