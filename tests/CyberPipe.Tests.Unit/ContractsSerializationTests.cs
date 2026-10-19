using System.Text.Json;
using System.Text.Json.Serialization;
using CyberPipe.Contracts;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class ContractsSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public void NetrunnerManifestDto_RoundTripsThroughJson()
    {
        var manifest = new NetrunnerManifestDto
        {
            Slug = "cbr-rates",
            Name = "Rates",
            Version = "1.0.0",
            Description = "Parses rates.",
            Runtime = new NetrunnerRuntimeDto
            {
                Language = "python",
                LanguageVersion = "3.12",
                Environment = "python",
                EnvironmentVersion = "3.12.4",
                Entrypoint = "skill-hack.py",
                TimeoutMs = 15000,
                Deterministic = true,
            },
            Io = new NetrunnerIoDto
            {
                InputSchemaFile = "schema.input.json",
                OutputSchemaFile = "schema.output.json",
                InputFormats = ["application/json"],
                OutputFormats = ["application/json"],
                InputFiles =
                [
                    new NetrunnerInputFileDto
                    {
                        Alias = "raw",
                        Required = true,
                        AllowedExtensions = [".json"],
                        MimeTypes = ["application/json"],
                        MaxSizeBytes = 1024,
                    },
                ],
                OutputFiles =
                [
                    new NetrunnerOutputFileDto
                    {
                        Alias = "summary",
                        FilenamePattern = "summary.json",
                        MimeType = "application/json",
                        Required = true,
                        MaxSizeBytes = 2048,
                    },
                ],
            },
            Permissions = new NetrunnerPermissionsDto
            {
                AllowNetwork = false,
                FilesystemScope = "IsolatedRunWorkspaceOnly",
            },
            Integrity = new NetrunnerIntegrityDto
            {
                FilesSha256 = new Dictionary<string, string> { ["skill-hack.py"] = "abc123" },
            },
        };

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        Assert.Contains("\"entrypoint\":\"skill-hack.py\"", json);
        Assert.Contains("\"languageVersion\":\"3.12\"", json);
        NetrunnerManifestDto? roundTripped = JsonSerializer.Deserialize<NetrunnerManifestDto>(json, JsonOptions);
        Assert.Equal(manifest, roundTripped);
    }

    [Fact]
    public void ChainDefinitionDto_RoundTripsThroughJson()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var definition = new ChainDefinitionDto
        {
            Id = ChainId.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Daily rates",
            IsEnabled = true,
            Trigger = new ChainTriggerDto
            {
                Kind = ChainTriggerKind.Cron,
                CronExpression = "0 0 9 * * ?",
                Parameters = new Dictionary<string, string> { ["tz"] = "UTC" },
            },
            Skill = new ChainSkillStepDto
            {
                SkillId = "http-fetch",
                InputMapping = new Dictionary<string, string> { ["url"] = "{{ trigger.payload.url }}" },
            },
            Script = new ChainScriptStepDto
            {
                PackageSlug = "cbr-rates",
                PackageVersion = "1.0.0",
                InputMapping = new Dictionary<string, string> { ["raw"] = "{{ skill.output }}" },
                FileInputBindings = new Dictionary<string, string> { ["raw"] = "skill:raw" },
            },
            Delivery = new ChainDeliveryStepDto
            {
                Channel = ChannelKind.Telegram,
                Target = "owner",
                MessageTemplate = "{{ script.output }}",
                SendFiles = true,
            },
            RequiresHitl = true,
            RetryPolicy = new ChainRetryPolicyDto { MaxAttempts = 3, BackoffMs = 500 },
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        string json = JsonSerializer.Serialize(definition, JsonOptions);
        ChainDefinitionDto? roundTripped = JsonSerializer.Deserialize<ChainDefinitionDto>(json, JsonOptions);
        Assert.Equal(definition, roundTripped);
        Assert.Equal(ChainTriggerKind.Cron, roundTripped!.Trigger.Kind);
    }

    [Fact]
    public void BudgetStatusDto_RoundTripsThroughJson_AndDefaultsMatchQ23()
    {
        var status = new BudgetStatusDto
        {
            Limits = BudgetLimitsDto.Default,
            SpentTodayUsd = 0.10m,
            RemainingTodayUsd = 0.40m,
            TokensUsedToday = 1200,
            CloudTurnsUsed = 1,
            LocalTurnsUsed = 2,
            BudgetDate = new DateOnly(2026, 10, 19),
        };

        Assert.Equal(0.50m, status.Limits.DailyCapUsd);
        Assert.Equal(4000, status.Limits.SoftTokenLimit);
        Assert.Equal(12000, status.Limits.HardTokenLimit);
        Assert.Equal(3, status.Limits.MaxCloudTurns);
        Assert.Equal(5, status.Limits.MaxLocalTurns);

        string json = JsonSerializer.Serialize(status, JsonOptions);
        BudgetStatusDto? roundTripped = JsonSerializer.Deserialize<BudgetStatusDto>(json, JsonOptions);
        Assert.Equal(status, roundTripped);
    }

    [Fact]
    public void Result_WrapsValueAndError()
    {
        Result<int> ok = Result<int>.Success(42);
        Assert.True(ok.IsSuccess);
        Assert.Equal(42, ok.Value);

        Result<int> failed = Result<int>.Failure("Test.Code", "broken");
        Assert.False(failed.IsSuccess);
        Assert.Equal("Test.Code", failed.Error!.Code);
    }

    [Fact]
    public void TypedIds_AreDistinctTypesWithValues()
    {
        var guid = Guid.NewGuid();
        Assert.Equal(guid, new ChainId(guid).Value);
        Assert.Equal(guid, RunId.Parse(guid.ToString()).Value);
        Assert.NotEqual(MessageId.New(), MessageId.New());
        Assert.NotEqual(ChainId.New(), ChainId.New());
    }
}
