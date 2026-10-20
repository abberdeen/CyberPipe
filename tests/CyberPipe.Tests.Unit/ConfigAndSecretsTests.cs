using CyberPipe.Contracts;
using CyberPipe.Security;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class ConfigAndSecretsTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "CyberPipeTests", Guid.NewGuid().ToString("N"));

    public ConfigAndSecretsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void EmptyConfiguration_StartsWithDefaults()
    {
        CyberPipeOptions options = CyberPipeConfigurationLoader.LoadFromSources(
            appsettingsPath: null,
            new Dictionary<string, string?>(),
            uiOverridesPath: null);

        Assert.Equal(0.50m, options.Budget.DailyCapUsd);
        Assert.Equal(4000, options.Budget.SoftTokenLimit);
        Assert.Equal(12000, options.Budget.HardTokenLimit);
        Assert.Equal(3, options.Budget.MaxCloudTurns);
        Assert.Equal(5, options.Budget.MaxLocalTurns);
        Assert.Equal("multilingual-e5-small", options.Models.EmbeddingModelId);
        Assert.Null(options.Sandbox.MemoryLimitMb);
        Assert.True(options.Router.Tier0Enabled);
        Assert.True(options.Router.Tier3Enabled);
        Assert.Equal(SecretKeys.DatabasePassword, options.Database.PasswordSecretKey);
        Assert.Equal(SecretKeys.TelegramBotToken, options.Channels.TelegramBotTokenSecretKey);
    }

    [Fact]
    public void Merge_FileThenEnvironmentThenUiOverrides()
    {
        string file = WriteJson("appsettings.json", """{ "Budget": { "DailyCapUsd": 1.25 } }""");
        string ui = WriteJson("ui.json", """{ "Budget": { "DailyCapUsd": 2.75 } }""");
        var env = new Dictionary<string, string?> { ["CYBERPIPE_Budget__DailyCapUsd"] = "2.00" };

        CyberPipeOptions fromEnv = CyberPipeConfigurationLoader.LoadFromSources(file, env, uiOverridesPath: null);
        Assert.Equal(2.00m, fromEnv.Budget.DailyCapUsd);

        CyberPipeOptions fromUi = CyberPipeConfigurationLoader.LoadFromSources(file, env, ui);
        Assert.Equal(2.75m, fromUi.Budget.DailyCapUsd);

        CyberPipeOptions fromFile = CyberPipeConfigurationLoader.LoadFromSources(
            file, new Dictionary<string, string?>(), uiOverridesPath: null);
        Assert.Equal(1.25m, fromFile.Budget.DailyCapUsd);
    }

    [Fact]
    public void EnvironmentVariable_ChangesDailyCapWithoutRebuild()
    {
        var env = new Dictionary<string, string?> { ["CYBERPIPE_Budget__DailyCapUsd"] = "9.99" };
        CyberPipeOptions options = CyberPipeConfigurationLoader.LoadFromSources(null, env, null);
        Assert.Equal(9.99m, options.Budget.DailyCapUsd);
    }

    [Fact]
    public void InvalidConfiguration_StopsWithKeyName()
    {
        var env = new Dictionary<string, string?> { ["CYBERPIPE_Budget__DailyCapUsd"] = "-1" };
        ConfigurationValidationException ex = Assert.Throws<ConfigurationValidationException>(
            () => CyberPipeConfigurationLoader.LoadFromSources(null, env, null));
        Assert.Contains("Budget:DailyCapUsd", ex.Message);
    }

    [Fact]
    public void Validation_NamesEveryInvalidKey()
    {
        var options = new CyberPipeOptions
        {
            Budget = new BudgetOptions { DailyCapUsd = -5, SoftTokenLimit = 99999 },
            Sandbox = new SandboxOptions { MemoryLimitMb = 0 },
        };
        IReadOnlyList<string> errors = options.Validate();
        Assert.Contains(errors, e => e.Contains("Budget:DailyCapUsd"));
        Assert.Contains(errors, e => e.Contains("Budget:SoftTokenLimit"));
        Assert.Contains(errors, e => e.Contains("Sandbox:MemoryLimitMb"));
    }

    [Fact]
    public async Task FileSecretStore_RoundTripsWithoutPlainTextOnDisk()
    {
        string path = Path.Combine(_dir, "secrets.json");
        var store = new FileSecretStore(path);
        Assert.False(store.IsPlatformStore);
        Assert.False(store.IsSecure);

        const string plain = "super-secret-value-123";
        await store.SetAsync(SecretKeys.DatabasePassword, SecretValue.FromPlainText(plain));

        SecretValue? read = await store.GetAsync(SecretKeys.DatabasePassword);
        Assert.NotNull(read);
        Assert.Equal(plain, read.Reveal());
        Assert.Equal("[REDACTED]", read.ToString());
        Assert.DoesNotContain(plain, await File.ReadAllTextAsync(path));

        Assert.True(await store.DeleteAsync(SecretKeys.DatabasePassword));
        Assert.Null(await store.GetAsync(SecretKeys.DatabasePassword));
        Assert.False(await store.DeleteAsync(SecretKeys.DatabasePassword));
    }

    [Fact]
    public void SecretValue_ToStringNeverLeaks()
    {
        var secret = SecretValue.FromPlainText("token-abc");
        Assert.Equal("[REDACTED]", secret.ToString());
        Assert.Equal("[REDACTED]", $"{secret}");
        Assert.Equal("token-abc", secret.Reveal());
    }

    [Fact]
    public void SecretKeys_RegistryHasSingleSource()
    {
        Assert.Equal("database.password", SecretKeys.DatabasePassword);
        Assert.Equal("telegram.botToken", SecretKeys.TelegramBotToken);
        Assert.Equal(2, SecretKeys.All.Count);
    }

    [Fact]
    public void Options_HoldOnlySecretReferencesNotValues()
    {
        Assert.Null(typeof(DatabaseOptions).GetProperty("Password"));
        Assert.NotNull(typeof(DatabaseOptions).GetProperty("PasswordSecretKey"));
        Assert.Null(typeof(ChannelOptions).GetProperty("TelegramBotToken"));
        Assert.NotNull(typeof(ChannelOptions).GetProperty("TelegramBotTokenSecretKey"));
    }

    [Fact]
    public async Task PlatformSecretStore_RoundTripsOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var store = new PlatformSecretStore();
        Assert.True(store.IsPlatformStore);
        string key = SecretKeys.DatabasePassword + ".test." + Guid.NewGuid().ToString("N");
        try
        {
            await store.SetAsync(key, SecretValue.FromPlainText("platform-secret-1"));
            SecretValue? read = await store.GetAsync(key);
            Assert.Equal("platform-secret-1", read?.Reveal());
            Assert.True(await store.DeleteAsync(key));
            Assert.Null(await store.GetAsync(key));
        }
        finally
        {
            await store.DeleteAsync(key);
        }
    }

    [Fact]
    public void LoadFromSources_DeserializesHostingTypeEnums_WithoutJsonException()
    {
        string ui = WriteJson("ui-hosting.json", """
            {
              "Router": {
                "Tier2ModelId": "qwen2.5:3b",
                "Tier3ModelId": "deepseek-r1:8b",
                "Tier2HostingType": "Local",
                "Tier3HostingType": "RemoteCloud"
              }
            }
            """);

        CyberPipeOptions options = CyberPipeConfigurationLoader.LoadFromSources(null, null, ui);
        Assert.Equal("qwen2.5:3b", options.Router.Tier2ModelId);
        Assert.Equal("deepseek-r1:8b", options.Router.Tier3ModelId);
        Assert.Equal(ModelHostingType.Local, options.Router.Tier2HostingType);
        Assert.Equal(ModelHostingType.RemoteCloud, options.Router.Tier3HostingType);
    }

    private string WriteJson(string name, string json)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }
}
