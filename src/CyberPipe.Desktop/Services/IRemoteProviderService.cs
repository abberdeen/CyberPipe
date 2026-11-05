using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CyberPipe.Contracts;

namespace CyberPipe.Desktop.Services;

/// <summary>Результат проверки облачного провайдера и загрузки его моделей.</summary>
public sealed record RemoteProviderProbeResult
{
    public required bool Success { get; init; }

    public string? Message { get; init; }

    public long LatencyMs { get; init; }

    public IReadOnlyList<RemoteModelEntryDto> DiscoveredModels { get; init; } = [];
}

/// <summary>
/// Профили облачных провайдеров в <c>ui-overrides.json</c> и их API-ключи в <see cref="ISecretStore"/>.
/// </summary>
public interface IRemoteProviderService
{
    IReadOnlyList<RemoteProviderProfileDto> LoadProviders();

    void SaveProviders(IEnumerable<RemoteProviderProfileDto> providers);

    Task<bool> HasApiKeyAsync(string providerId, CancellationToken ct = default);

    Task SaveApiKeyAsync(string providerId, string plainApiKey, CancellationToken ct = default);

    Task<SecretValue?> GetApiKeyAsync(string providerId, CancellationToken ct = default);

    Task DeleteApiKeyAsync(string providerId, CancellationToken ct = default);

    Task<RemoteProviderProbeResult> ProbeAndDiscoverModelsAsync(
        RemoteProviderProfileDto provider,
        string? unsavedApiKey = null,
        CancellationToken ct = default);

    IReadOnlyList<RemoteProviderProfileDto> GetBuiltInPresets();
}

/// <summary>
/// Хранит профили в секции <c>RemoteProviders</c> и ключи только в <see cref="ISecretStore"/>.
/// </summary>
public sealed class RemoteProviderService : IRemoteProviderService
{
    public const string SectionName = "RemoteProviders";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _uiOverridesPath;
    private readonly ISecretStore _secrets;
    private readonly HttpClient _http;
    private readonly IReadOnlyList<ModelDescriptorDto> _cloudCatalog;
    private readonly object _fileGate = new();

    public RemoteProviderService(
        string? uiOverridesPath = null,
        ISecretStore? secretStore = null,
        HttpClient? httpClient = null,
        IReadOnlyList<ModelDescriptorDto>? cloudCatalog = null)
    {
        _uiOverridesPath = string.IsNullOrWhiteSpace(uiOverridesPath)
            ? CyberPipeConfigurationLoader.GetDefaultUiOverridesPath()
            : uiOverridesPath;
        _secrets = secretStore ?? new DpapiFileSecretStore();
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _cloudCatalog = cloudCatalog ?? [];
    }

    public IReadOnlyList<RemoteProviderProfileDto> GetBuiltInPresets() =>
    [
        Preset("openai", "OpenAI", RemoteProviderProtocol.OpenAiCompatible, "https://api.openai.com/v1",
            ("gpt-4o-mini", "GPT-4o mini", ModelTaskCategory.LightFastTier2),
            ("gpt-4.1", "GPT-4.1", ModelTaskCategory.HeavyReasoningTier3)),
        Preset("anthropic", "Anthropic", RemoteProviderProtocol.Anthropic, "https://api.anthropic.com/v1",
            ("claude-3-5-haiku", "Claude 3.5 Haiku", ModelTaskCategory.LightFastTier2),
            ("claude-3-7-sonnet", "Claude 3.7 Sonnet", ModelTaskCategory.HeavyReasoningTier3)),
        Preset("google", "Google Gemini", RemoteProviderProtocol.GoogleGemini, "https://generativelanguage.googleapis.com/v1beta",
            ("gemini-2.5-flash-lite", "Gemini 2.5 Flash-Lite", ModelTaskCategory.LightFastTier2),
            ("gemini-2.5-pro", "Gemini 2.5 Pro", ModelTaskCategory.HeavyReasoningTier3)),
        Preset("deepseek", "DeepSeek", RemoteProviderProtocol.OpenAiCompatible, "https://api.deepseek.com/v1",
            ("deepseek-chat", "DeepSeek Chat", ModelTaskCategory.LightFastTier2),
            ("deepseek-reasoner", "DeepSeek Reasoner", ModelTaskCategory.HeavyReasoningTier3)),
        Preset("openrouter", "OpenRouter", RemoteProviderProtocol.OpenAiCompatible, "https://openrouter.ai/api/v1",
            ("openai/gpt-4o-mini", "OpenAI — GPT-4o mini", ModelTaskCategory.LightFastTier2),
            ("anthropic/claude-3.7-sonnet", "Anthropic — Claude 3.7 Sonnet", ModelTaskCategory.HeavyReasoningTier3),
            ("deepseek/deepseek-r1", "DeepSeek R1", ModelTaskCategory.HeavyReasoningTier3)),
        Preset("groq", "Groq", RemoteProviderProtocol.OpenAiCompatible, "https://api.groq.com/openai/v1",
            ("llama-3.1-8b-instant", "Llama 3.1 8B Instant", ModelTaskCategory.LightFastTier2),
            ("llama-3.3-70b-versatile", "Llama 3.3 70B", ModelTaskCategory.HeavyReasoningTier3)),
        Preset("custom", "Свой OpenAI-совместимый", RemoteProviderProtocol.OpenAiCompatible, "https://api.example.com/v1"),
    ];

    public IReadOnlyList<RemoteProviderProfileDto> LoadProviders()
    {
        lock (_fileGate)
        {
            JsonObject root = ReadRoot();
            if (root[SectionName] is JsonArray array)
            {
                try
                {
                    return array.Deserialize<List<RemoteProviderProfileDto>>(JsonOptions) ?? [];
                }
                catch (JsonException)
                {
                    return [];
                }
            }

            List<RemoteProviderProfileDto> seeded = CreateInitialProfiles();
            root[SectionName] = JsonSerializer.SerializeToNode(seeded, JsonOptions);
            WriteRoot(root);
            return seeded;
        }
    }

    public void SaveProviders(IEnumerable<RemoteProviderProfileDto> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var sanitized = providers.Select(Sanitize).ToList();
        lock (_fileGate)
        {
            JsonObject root = ReadRoot();
            root[SectionName] = JsonSerializer.SerializeToNode(sanitized, JsonOptions);
            WriteRoot(root);
        }
    }

    public async Task<bool> HasApiKeyAsync(string providerId, CancellationToken ct = default)
    {
        SecretValue? secret = await GetApiKeyAsync(providerId, ct).ConfigureAwait(false);
        return secret is not null && !string.IsNullOrWhiteSpace(secret.Reveal());
    }

    public Task SaveApiKeyAsync(string providerId, string plainApiKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plainApiKey);
        return _secrets.SetAsync(SecretKeys.ForModelProvider(providerId), SecretValue.FromPlainText(plainApiKey.Trim()), ct);
    }

    public Task<SecretValue?> GetApiKeyAsync(string providerId, CancellationToken ct = default) =>
        _secrets.GetAsync(SecretKeys.ForModelProvider(providerId), ct);

    public Task DeleteApiKeyAsync(string providerId, CancellationToken ct = default) =>
        _secrets.DeleteAsync(SecretKeys.ForModelProvider(providerId), ct);

    public async Task<RemoteProviderProbeResult> ProbeAndDiscoverModelsAsync(
        RemoteProviderProfileDto provider,
        string? unsavedApiKey = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        string? apiKey = !string.IsNullOrWhiteSpace(unsavedApiKey)
            ? unsavedApiKey.Trim()
            : (await GetApiKeyAsync(provider.Id, ct).ConfigureAwait(false))?.Reveal();

        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsUri(provider, apiKey));
            ApplyAuthHeaders(request, provider.Protocol, apiKey);
            using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            watch.Stop();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Failed(watch, "Ошибка авторизации (HTTP 401/403): проверьте правильность API-ключа.");
            }

            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Failed(watch, $"Не удалось подключиться (HTTP {(int)response.StatusCode}).");
            }

            IReadOnlyList<RemoteModelEntryDto> merged = MergeModels(provider.Models, ParseModelIds(provider.Protocol, body));
            return new RemoteProviderProbeResult
            {
                Success = true,
                LatencyMs = watch.ElapsedMilliseconds,
                DiscoveredModels = merged,
                Message = $"Подключение успешно. Найдено моделей: {merged.Count} (отклик {watch.ElapsedMilliseconds} мс).",
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            watch.Stop();
            return Failed(watch, $"Не удалось связаться с провайдером: {ex.Message}");
        }
    }

    private List<RemoteProviderProfileDto> CreateInitialProfiles()
    {
        var profiles = GetBuiltInPresets()
            .Where(preset => !string.Equals(preset.Id, "custom", StringComparison.OrdinalIgnoreCase))
            .Select(Clone)
            .ToList();

        foreach (ModelDescriptorDto cloud in _cloudCatalog.Where(model => model.HostingType == ModelHostingType.RemoteCloud))
        {
            int index = profiles.FindIndex(profile => ProviderMatches(profile, cloud.Provider));
            if (index < 0 || profiles[index].Models.Any(model =>
                    string.Equals(model.ModelId, cloud.ModelId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(model.ModelId, cloud.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var models = profiles[index].Models.ToList();
            models.Add(new RemoteModelEntryDto
            {
                ModelId = cloud.ModelId,
                DisplayName = cloud.Name,
                RecommendedFor = cloud.RecommendedFor,
                IsEnabled = true,
                ContextWindowTokens = cloud.ContextWindowTokens == 0 ? 128000 : cloud.ContextWindowTokens,
                InputPricePer1mUsd = cloud.InputPricePer1mUsd,
                OutputPricePer1mUsd = cloud.OutputPricePer1mUsd,
                Description = cloud.BestFor,
            });
            profiles[index] = profiles[index] with { Models = models };
        }

        return profiles;
    }

    private static bool ProviderMatches(RemoteProviderProfileDto profile, string provider)
    {
        if (string.Equals(profile.Id, provider, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.Name, provider, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return profile.Id switch
        {
            "openai" => provider.Contains("openai", StringComparison.OrdinalIgnoreCase),
            "anthropic" => provider.Contains("anthropic", StringComparison.OrdinalIgnoreCase) ||
                           provider.Contains("claude", StringComparison.OrdinalIgnoreCase),
            "google" => provider.Contains("google", StringComparison.OrdinalIgnoreCase) ||
                        provider.Contains("gemini", StringComparison.OrdinalIgnoreCase),
            "deepseek" => provider.Contains("deepseek", StringComparison.OrdinalIgnoreCase),
            "openrouter" => provider.Contains("openrouter", StringComparison.OrdinalIgnoreCase),
            "groq" => provider.Contains("groq", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static RemoteProviderProfileDto Sanitize(RemoteProviderProfileDto provider) =>
        provider with { ApiKeySecretName = SecretKeys.ForModelProvider(provider.Id) };

    private static RemoteProviderProfileDto Clone(RemoteProviderProfileDto provider) =>
        provider with { Models = provider.Models.Select(model => model with { }).ToList() };

    private JsonObject ReadRoot()
    {
        if (!File.Exists(_uiOverridesPath))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(_uiOverridesPath)) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private void WriteRoot(JsonObject root)
    {
        string? directory = Path.GetDirectoryName(_uiOverridesPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_uiOverridesPath, root.ToJsonString(JsonOptions));
    }

    private static Uri BuildModelsUri(RemoteProviderProfileDto provider, string? apiKey)
    {
        string baseUrl = provider.BaseUrl.TrimEnd('/');
        if (provider.Protocol == RemoteProviderProtocol.GoogleGemini && !string.IsNullOrWhiteSpace(apiKey))
        {
            return new Uri($"{baseUrl}/models?key={Uri.EscapeDataString(apiKey)}");
        }

        return new Uri($"{baseUrl}/models");
    }

    private static void ApplyAuthHeaders(HttpRequestMessage request, RemoteProviderProtocol protocol, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        if (protocol == RemoteProviderProtocol.Anthropic)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            return;
        }

        if (protocol == RemoteProviderProtocol.GoogleGemini)
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static IReadOnlyList<string> ParseModelIds(RemoteProviderProtocol protocol, string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var ids = new List<string>();
        if (protocol == RemoteProviderProtocol.GoogleGemini)
        {
            if (root.TryGetProperty("models", out JsonElement models) && models.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement model in models.EnumerateArray())
                {
                    string? name = ReadString(model, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    const string prefix = "models/";
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name[prefix.Length..];
                    }

                    ids.Add(name);
                }
            }

            return ids;
        }

        if (root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in data.EnumerateArray())
            {
                string? id = ReadString(entry, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private static IReadOnlyList<RemoteModelEntryDto> MergeModels(
        IReadOnlyList<RemoteModelEntryDto> existing,
        IReadOnlyList<string> discoveredIds)
    {
        var merged = existing.Select(model => model with { }).ToList();
        foreach (string id in discoveredIds)
        {
            if (merged.Any(model => string.Equals(model.ModelId, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            merged.Add(new RemoteModelEntryDto
            {
                ModelId = id,
                DisplayName = id,
                IsEnabled = false,
                RecommendedFor = ModelTaskCategory.HeavyReasoningTier3,
            });
        }

        return merged;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static RemoteProviderProbeResult Failed(Stopwatch watch, string message) =>
        new()
        {
            Success = false,
            LatencyMs = watch.ElapsedMilliseconds,
            Message = message,
        };

    private static RemoteProviderProfileDto Preset(
        string id,
        string name,
        RemoteProviderProtocol protocol,
        string baseUrl,
        params (string ModelId, string DisplayName, ModelTaskCategory Tier)[] models) =>
        new()
        {
            Id = id,
            Name = name,
            Protocol = protocol,
            BaseUrl = baseUrl,
            ApiKeySecretName = SecretKeys.ForModelProvider(id),
            IsEnabled = !string.Equals(id, "custom", StringComparison.OrdinalIgnoreCase),
            Models = models.Select(model => new RemoteModelEntryDto
            {
                ModelId = model.ModelId,
                DisplayName = model.DisplayName,
                RecommendedFor = model.Tier,
                IsEnabled = true,
            }).ToList(),
        };

    /// <summary>
    /// Локальное хранилище ключей через DPAPI. Открытые значения не попадают в JSON настроек.
    /// </summary>
    private sealed class DpapiFileSecretStore : ISecretStore
    {
        private readonly string _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberPipe",
            "secrets");

        public bool IsPlatformStore => OperatingSystem.IsWindows();

        public Task SetAsync(string key, SecretValue value, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("DPAPI secret store is available on Windows.");
            }

            Directory.CreateDirectory(_directory);
            byte[] plain = Encoding.UTF8.GetBytes(value.Reveal());
            byte[] protectedBytes = Protect(plain);
            File.WriteAllBytes(PathFor(key), protectedBytes);
            return Task.CompletedTask;
        }

        public Task<SecretValue?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            cancellationToken.ThrowIfCancellationRequested();
            string path = PathFor(key);
            if (!File.Exists(path))
            {
                return Task.FromResult<SecretValue?>(null);
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("DPAPI secret store is available on Windows.");
            }

            byte[] plain = Unprotect(File.ReadAllBytes(path));
            return Task.FromResult<SecretValue?>(SecretValue.FromPlainText(Encoding.UTF8.GetString(plain)));
        }

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            cancellationToken.ThrowIfCancellationRequested();
            string path = PathFor(key);
            if (!File.Exists(path))
            {
                return Task.FromResult(false);
            }

            File.Delete(path);
            return Task.FromResult(true);
        }

        private string PathFor(string key)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return Path.Combine(_directory, Convert.ToHexString(hash) + ".bin");
        }

        private static byte[] Protect(byte[] plain)
        {
            var input = new DataBlob { cbData = plain.Length, pbData = Marshal.AllocHGlobal(plain.Length) };
            var output = new DataBlob();
            try
            {
                Marshal.Copy(plain, 0, input.pbData, plain.Length);
                if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref output))
                {
                    throw new InvalidOperationException($"CryptProtectData failed with error {Marshal.GetLastWin32Error()}.");
                }

                byte[] result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero)
                {
                    LocalFree(output.pbData);
                }
            }
        }

        private static byte[] Unprotect(byte[] protectedBytes)
        {
            var input = new DataBlob { cbData = protectedBytes.Length, pbData = Marshal.AllocHGlobal(protectedBytes.Length) };
            var output = new DataBlob();
            try
            {
                Marshal.Copy(protectedBytes, 0, input.pbData, protectedBytes.Length);
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref output))
                {
                    throw new InvalidOperationException($"CryptUnprotectData failed with error {Marshal.GetLastWin32Error()}.");
                }

                byte[] result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero)
                {
                    LocalFree(output.pbData);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string? description,
            IntPtr entropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr entropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            ref DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr handle);
    }
}
