using System.Reflection;
using System.Text.Json;
using CyberPipe.Chains;
using CyberPipe.Contracts;
using CyberPipe.Data;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class ChainDefinitionTests : IAsyncLifetime
{
    private readonly ChainDefinitionValidator _validator = new();
    private TestDatabase? _db;

    private ChainCatalogService Repository => _db!.Catalog;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }

    private static string GetFixturePath(string filename)
    {
        string baseDir = AppContext.BaseDirectory;
        string directPath = Path.Combine(baseDir, "Fixtures", "Chains", filename);
        if (File.Exists(directPath))
        {
            return directPath;
        }

        // Fallback to source directory
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "CyberPipe.Tests.Unit", "Fixtures", "Chains", filename);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Fixture '{filename}' not found.");
    }

    private static string ReadFixture(string filename) =>
        File.ReadAllText(GetFixturePath(filename));

    private static ChainDefinitionDto CreateSampleChain(ChainTriggerKind kind = ChainTriggerKind.Webhook)
    {
        DateTimeOffset now = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
        ChainTriggerDto trigger = kind switch
        {
            ChainTriggerKind.Webhook => new ChainTriggerDto
            {
                Kind = ChainTriggerKind.Webhook,
                WebhookPath = "/api/v1/webhook",
            },
            ChainTriggerKind.Cron => new ChainTriggerDto
            {
                Kind = ChainTriggerKind.Cron,
                CronExpression = "0 0 12 * * ?",
            },
            ChainTriggerKind.FunctionCall => new ChainTriggerDto
            {
                Kind = ChainTriggerKind.FunctionCall,
                FunctionName = "execute_tool",
            },
            ChainTriggerKind.Chat => new ChainTriggerDto
            {
                Kind = ChainTriggerKind.Chat,
                ChatFilter = "cmd:run",
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        return new ChainDefinitionDto
        {
            Id = ChainId.New(),
            Name = $"{kind} Linear Chain",
            IsEnabled = true,
            Trigger = trigger,
            Skill = new ChainSkillStepDto
            {
                SkillId = "sample-skill",
                InputMapping = new Dictionary<string, string> { ["param"] = "{{ trigger.payload.id }}" },
            },
            Script = new ChainScriptStepDto
            {
                PackageSlug = "sample-script",
                PackageVersion = "1.0.0",
                InputMapping = new Dictionary<string, string> { ["data"] = "{{ skill.output.result }}" },
                FileInputBindings = new Dictionary<string, string>(),
            },
            Delivery = new ChainDeliveryStepDto
            {
                Channel = ChannelKind.Telegram,
                Target = "operator",
                MessageTemplate = "Result: {{ script.output.summary }}",
                SendFiles = false,
            },
            RequiresHitl = false,
            RetryPolicy = new ChainRetryPolicyDto { MaxAttempts = 3, BackoffMs = 250 },
            MisfirePolicy = ChainMisfirePolicy.DoNothing,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    [Theory]
    [InlineData("webhook-chain.json", ChainTriggerKind.Webhook)]
    [InlineData("cron-chain.json", ChainTriggerKind.Cron)]
    [InlineData("functioncall-chain.json", ChainTriggerKind.FunctionCall)]
    [InlineData("chat-chain.json", ChainTriggerKind.Chat)]
    public void ValidFixtures_PassValidation_AndDeserializeCorrectly(string fixtureFile, ChainTriggerKind expectedKind)
    {
        string json = ReadFixture(fixtureFile);

        ChainValidationResult jsonResult = _validator.ValidateJson(json);
        Assert.True(jsonResult.IsValid, $"Validation failed: {string.Join(", ", jsonResult.Errors.Select(e => e.Message))}");
        Assert.Empty(jsonResult.Errors);

        ChainDefinitionDto dto = ChainDefinitionSerializer.Deserialize(json);
        Assert.Equal(expectedKind, dto.Trigger.Kind);

        ChainValidationResult dtoResult = _validator.Validate(dto);
        Assert.True(dtoResult.IsValid);
        Assert.Empty(dtoResult.Errors);
    }

    [Theory]
    [InlineData("Trigger")]
    [InlineData("Skill")]
    [InlineData("Script")]
    [InlineData("Delivery")]
    public void MissingSteps_RejectedWithMissingStepCode(string missingStepName)
    {
        string validJson = ReadFixture("webhook-chain.json");
        using JsonDocument doc = JsonDocument.Parse(validJson);
        var dict = new Dictionary<string, object>();
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            if (!prop.Name.Equals(missingStepName, StringComparison.OrdinalIgnoreCase))
            {
                dict[prop.Name] = JsonDocument.Parse(prop.Value.GetRawText()).RootElement;
            }
        }

        string jsonWithoutStep = JsonSerializer.Serialize(dict);
        ChainValidationResult result = _validator.ValidateJson(jsonWithoutStep);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ChainErrorCodes.MissingStep && e.Path.Equals(missingStepName, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("nodes", "[{\"id\": \"node1\"}]")]
    [InlineData("edges", "[{\"from\": \"node1\", \"to\": \"node2\"}]")]
    [InlineData("condition", "\"true\"")]
    [InlineData("conditions", "[{\"if\": \"x > 0\"}]")]
    [InlineData("script2", "{\"packageSlug\": \"extra\"}")]
    [InlineData("second_script", "{\"packageSlug\": \"extra\"}")]
    [InlineData("scripts", "[{\"packageSlug\": \"first\"}, {\"packageSlug\": \"second\"}]")]
    [InlineData("parallel", "true")]
    [InlineData("branches", "[{\"path\": \"a\"}, {\"path\": \"b\"}]")]
    [InlineData("switch", "{\"case\": \"status\"}")]
    [InlineData("workflow", "{\"dag\": true}")]
    [InlineData("graph", "{\"directed\": true}")]
    public void BranchingOrDagProperties_RejectedWithBranchingNotSupported(string propertyName, string propertyValue)
    {
        string validJson = ReadFixture("webhook-chain.json");
        // Inject forbidden property into JSON
        string invalidJson = validJson.TrimEnd('}', ' ', '\r', '\n') + $",\n  \"{propertyName}\": {propertyValue}\n}}";

        ChainValidationResult result = _validator.ValidateJson(invalidJson);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ChainErrorCodes.BranchingNotSupported && e.Path == propertyName);
    }

    [Fact]
    public void UnknownTriggerKind_RejectedWithUnknownTriggerKindCode()
    {
        string validJson = ReadFixture("webhook-chain.json");
        string invalidJson = validJson.Replace("\"kind\": \"webhook\"", "\"kind\": \"unsupported_kind\"", StringComparison.Ordinal);

        ChainValidationResult result = _validator.ValidateJson(invalidJson);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ChainErrorCodes.UnknownTriggerKind && e.Path == "Trigger.Kind");
    }

    [Theory]
    [InlineData("MyPackage")]
    [InlineData("my_package")]
    [InlineData("-start-hyphen")]
    [InlineData("end-hyphen-")]
    [InlineData("two--hyphens")]
    [InlineData("package with spaces")]
    public void InvalidSlug_RejectedWithInvalidSlugCode(string invalidSlug)
    {
        ChainDefinitionDto chain = CreateSampleChain() with
        {
            Script = new ChainScriptStepDto
            {
                PackageSlug = invalidSlug,
                PackageVersion = "1.0.0",
            },
        };

        ChainValidationResult result = _validator.Validate(chain);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ChainErrorCodes.InvalidSlug && e.Path == "Script.PackageSlug");
    }

    [Theory]
    [InlineData("my-package")]
    [InlineData("netrunner-1")]
    [InlineData("pkg")]
    [InlineData("cbr-rates-parser")]
    public void ValidSlug_PassesValidation(string validSlug)
    {
        ChainDefinitionDto chain = CreateSampleChain() with
        {
            Script = new ChainScriptStepDto
            {
                PackageSlug = validSlug,
                PackageVersion = "1.0.0",
            },
        };

        ChainValidationResult result = _validator.Validate(chain);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    [InlineData("latest")]
    [InlineData("1.0.0.0")]
    [InlineData("beta-1")]
    public void InvalidSemVer_RejectedWithInvalidVersionCode(string invalidVersion)
    {
        ChainDefinitionDto chain = CreateSampleChain() with
        {
            Script = new ChainScriptStepDto
            {
                PackageSlug = "valid-slug",
                PackageVersion = invalidVersion,
            },
        };

        ChainValidationResult result = _validator.Validate(chain);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ChainErrorCodes.InvalidVersion && e.Path == "Script.PackageVersion");
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.1.0-alpha.1")]
    [InlineData("2.1.0-beta.2+build.2026")]
    [InlineData("10.20.30")]
    public void ValidSemVer_PassesValidation(string validVersion)
    {
        ChainDefinitionDto chain = CreateSampleChain() with
        {
            Script = new ChainScriptStepDto
            {
                PackageSlug = "valid-slug",
                PackageVersion = validVersion,
            },
        };

        ChainValidationResult result = _validator.Validate(chain);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("{{ llm.response }}")]
    [InlineData("{{ random.value }}")]
    [InlineData("{{ user.input }}")]
    [InlineData("{{ env.SECRET }}")]
    public void UnknownMappingSource_RejectedWithUnknownMappingSourceCode(string invalidTemplate)
    {
        ChainDefinitionDto chain = CreateSampleChain() with
        {
            Delivery = new ChainDeliveryStepDto
            {
                Channel = ChannelKind.Cli,
                Target = "admin",
                MessageTemplate = $"Alert: {invalidTemplate}",
            },
        };

        ChainValidationResult result = _validator.Validate(chain);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ChainErrorCodes.UnknownMappingSource && e.Path == "Delivery.MessageTemplate");
    }

    [Fact]
    public void ForwardMappingInSkill_RejectedWithUnknownMappingSource()
    {
        // Skill cannot reference Script because Script executes AFTER Skill
        ChainDefinitionDto chain = CreateSampleChain() with
        {
            Skill = new ChainSkillStepDto
            {
                SkillId = "enricher",
                InputMapping = new Dictionary<string, string>
                {
                    ["data"] = "{{ script.output }}",
                },
            },
        };

        ChainValidationResult result = _validator.Validate(chain);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ChainErrorCodes.UnknownMappingSource && e.Path == "Skill.InputMapping[data]");
    }

    [Fact]
    public void JsonRoundTrip_EqualsOriginalDefinition()
    {
        ChainDefinitionDto original = CreateSampleChain(ChainTriggerKind.Cron);

        string json = ChainDefinitionSerializer.Serialize(original);
        ChainDefinitionDto deserialized = ChainDefinitionSerializer.Deserialize(json);

        Assert.Equal(original, deserialized);
    }

    [Fact]
    public async Task DisabledChain_IsValid_ButExcludedFromListEnabledAsync()
    {
        ChainDefinitionDto enabledChain = CreateSampleChain(ChainTriggerKind.Webhook);
        ChainDefinitionDto disabledChain = CreateSampleChain(ChainTriggerKind.Cron) with
        {
            Id = ChainId.New(),
            IsEnabled = false,
        };

        // Disabled chain passes validation
        ChainValidationResult validation = _validator.Validate(disabledChain);
        Assert.True(validation.IsValid);

        // Store both in repository
        await Repository.UpsertAsync(enabledChain);
        await Repository.UpsertAsync(disabledChain);

        // ListEnabled returns only enabled chains
        IReadOnlyList<ChainDefinitionDto> enabledList = await Repository.ListEnabledAsync();
        Assert.Contains(enabledList, c => c.Id == enabledChain.Id);
        Assert.DoesNotContain(enabledList, c => c.Id == disabledChain.Id);

        // When toggled to enabled, it appears in ListEnabledAsync
        bool toggled = await Repository.SetEnabledAsync(disabledChain.Id, true);
        Assert.True(toggled);

        IReadOnlyList<ChainDefinitionDto> updatedList = await Repository.ListEnabledAsync();
        Assert.Contains(updatedList, c => c.Id == disabledChain.Id);
    }

    [Fact]
    public async Task UpdatingEnabledChain_IncrementsVersion()
    {
        ChainDefinitionDto chain = CreateSampleChain(ChainTriggerKind.FunctionCall) with
        {
            Version = 1,
            IsEnabled = true,
        };

        ChainDefinitionDto created = await Repository.UpsertAsync(chain);
        Assert.Equal(1, created.Version);

        // Update name
        ChainDefinitionDto updated = await Repository.UpsertAsync(created with { Name = "Updated Function Call Chain" });
        Assert.Equal(2, updated.Version);
        Assert.Equal("Updated Function Call Chain", updated.Name);

        // Update again
        ChainDefinitionDto updatedAgain = await Repository.UpsertAsync(updated with { Name = "Updated Again" });
        Assert.Equal(3, updatedAgain.Version);
    }

    [Fact]
    public async Task ChainDefinitionLoader_LoadsFromConfigAndRepository()
    {
        var loader = new ChainDefinitionLoader(Repository, _validator);

        // 1. Load from config string
        string json = ReadFixture("chat-chain.json");
        ChainDefinitionDto fromConfig = await loader.LoadFromConfigAsync(json);
        Assert.Equal(ChainTriggerKind.Chat, fromConfig.Trigger.Kind);

        // 2. Load from config file
        string filePath = GetFixturePath("chat-chain.json");
        ChainDefinitionDto fromFile = await loader.LoadFromConfigFileAsync(filePath);
        Assert.Equal(fromConfig.Id, fromFile.Id);

        // 3. Load from repository
        await Repository.UpsertAsync(fromConfig);
        ChainDefinitionDto? fromRepo = await loader.LoadFromRepositoryAsync(fromConfig.Id);
        Assert.NotNull(fromRepo);
        Assert.Equal(fromConfig.Id, fromRepo.Id);

        // 4. Non-existent ID returns null
        ChainDefinitionDto? notFound = await loader.LoadFromRepositoryAsync(ChainId.New());
        Assert.Null(notFound);

        // 5. Invalid JSON throws ChainValidationException
        string invalidJson = json.Replace("\"kind\": \"chat\"", "\"kind\": \"unknown_kind\"", StringComparison.Ordinal);
        var ex = await Assert.ThrowsAsync<ChainValidationException>(() => loader.LoadFromConfigAsync(invalidJson));
        Assert.Contains(ex.Errors, e => e.Code == ChainErrorCodes.UnknownTriggerKind);
    }

    [Fact]
    public void PublicTypeNames_ContainNoDagGraphOrN8nIdentifiers()
    {
        Assembly contractsAssembly = typeof(ChainDefinitionDto).Assembly;
        Assembly chainsAssembly = typeof(ChainDefinitionValidator).Assembly;

        string[] forbiddenWords = ["dag", "graph", "n8n"];

        foreach (Assembly assembly in new[] { contractsAssembly, chainsAssembly })
        {
            foreach (Type type in assembly.GetExportedTypes())
            {
                foreach (string forbidden in forbiddenWords)
                {
                    Assert.False(
                        type.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                        $"Public type {type.FullName} contains forbidden identifier '{forbidden}'");
                }
            }
        }
    }
}
