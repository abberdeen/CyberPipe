using CyberPipe.Contracts;
using CyberPipe.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CyberPipe.Data.Tests;

public sealed class RepositoryUnitTests
{
    private sealed class NeverCalledFactory : IDatabaseConnectionFactory
    {
        public Task<SqliteConnection> CreateOpenedConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Database must not be called in unit tests.");
    }

    private sealed class ProbeRepository : RepositoryBase
    {
        public ProbeRepository(RepositoryOptions? options = null)
            : base(new NeverCalledFactory(), options)
        {
        }

        public string Order(PagedQuery query) =>
            OrderBy(query, new Dictionary<string, string> { ["created_at"] = "created_at", ["name"] = "name" }, "id");
    }

    private static ChainDefinitionDto SampleChain() => new()
    {
        Id = ChainId.New(),
        Name = "sample",
        Trigger = new ChainTriggerDto { Kind = ChainTriggerKind.Webhook, WebhookPath = "/hook" },
        Skill = new ChainSkillStepDto { SkillId = "skill-1" },
        Script = new ChainScriptStepDto { PackageSlug = "pkg", PackageVersion = "1.0.0" },
        Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Cli, Target = "owner", MessageTemplate = "done" },
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void PagedQuery_DefaultSort_IsSpecifiedAndValidates()
    {
        var query = new PagedQuery();
        Assert.False(string.IsNullOrWhiteSpace(query.SortBy));
        query.Validate(200);
    }

    [Theory]
    [InlineData("", "SortBy")]
    [InlineData("   ", "SortBy")]
    public void PagedQuery_EmptySort_Throws(string sortBy, string expected)
    {
        var ex = Assert.Throws<ArgumentException>(() => new PagedQuery { SortBy = sortBy }.Validate(200));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void PagedQuery_NegativeOffsetAndBadLimit_Throw()
    {
        Assert.Throws<ArgumentException>(() => new PagedQuery { Offset = -1 }.Validate(200));
        Assert.Throws<ArgumentException>(() => new PagedQuery { Limit = 0 }.Validate(200));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedQuery { Limit = 201 }.Validate(200));
    }

    [Fact]
    public void OrderBy_WhitelistOnly_AndInjectionRejected()
    {
        var probe = new ProbeRepository();
        Assert.Contains("created_at DESC", probe.Order(new PagedQuery()));
        Assert.Contains("name ASC", probe.Order(new PagedQuery { SortBy = "name", Descending = false }));
        Assert.Throws<ArgumentException>(() => probe.Order(new PagedQuery { SortBy = "created_at; DROP TABLE app.chains" }));
        Assert.Throws<ArgumentException>(() => probe.Order(new PagedQuery { SortBy = "definition_json" }));
    }

    [Fact]
    public void OptimisticConcurrency_CarriesAggregateAndId()
    {
        var ex = new OptimisticConcurrencyException("approvals", "42");
        Assert.Equal("approvals", ex.Aggregate);
        Assert.Equal("42", ex.Id);
        Assert.Contains("approvals", ex.Message);
    }

    [Fact]
    public void RepositoryOptions_NegativeThreshold_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ProbeRepository(new RepositoryOptions { SlowQueryThreshold = TimeSpan.FromMilliseconds(-1) }));
    }

    [Fact]
    public void RepositoryJson_RoundTripsChainDefinition()
    {
        ChainDefinitionDto chain = SampleChain();
        string json = RepositoryJson.Serialize(chain);
        ChainDefinitionDto restored = RepositoryJson.Deserialize<ChainDefinitionDto>(json);
        Assert.Equal(chain, restored);
    }

    [Fact]
    public void RepositoryJson_EnsureJson_RejectsInvalidAndDefaultsNull()
    {
        Assert.Equal("{}", RepositoryJson.EnsureJson(null, "value"));
        Assert.Throws<ArgumentException>(() => RepositoryJson.EnsureJson("{not-json", "value"));
    }

    [Fact]
    public void ApprovalStatusMapping_RoundTripsAndRejectsUnknown()
    {
        foreach (ApprovalStatus status in Enum.GetValues<ApprovalStatus>())
            Assert.Equal(status, ApprovalStatusMapping.Parse(ApprovalStatusMapping.ToDbValue(status)));
        Assert.Throws<InvalidOperationException>(() => ApprovalStatusMapping.Parse("unknown"));
    }

    [Fact]
    public void TokenBudgetEntry_FromUsage_MapsAllFields()
    {
        var occurred = new DateTimeOffset(2026, 2, 11, 10, 30, 0, TimeSpan.Zero);
        TokenBudgetEntryRecord entry = TokenBudgetEntryRecord.FromUsage(new TokenUsageDto
        {
            ModelId = "model-1",
            HostingType = ModelHostingType.RemoteCloud,
            InputTokens = 120,
            OutputTokens = 30,
            CostUsd = 0.0123m,
            OccurredAtUtc = occurred,
            RequestId = "req-1",
        });
        Assert.Equal(new DateOnly(2026, 2, 11), entry.BudgetDate);
        Assert.Equal("model-1", entry.ModelId);
        Assert.Equal(ModelHostingType.RemoteCloud, entry.HostingType);
        Assert.Equal("usage", entry.EntryType);
        Assert.Equal(120, entry.InputTokens);
        Assert.Equal(30, entry.OutputTokens);
        Assert.Equal(0.0123m, entry.CostUsd);
        Assert.Equal("req-1", entry.RequestId);
    }

    [Fact]
    public void PublicRepositoryContracts_ContainNoNpgsqlTypes()
    {
        Type[] contracts =
        [
            typeof(IChainRepository), typeof(IChainRunRepository), typeof(ICheckpointRepository),
            typeof(IApprovalRepository), typeof(ITokenBudgetRepository), typeof(IKeyValueSettingsRepository),
            typeof(IAuditLogRepository), typeof(IUnitOfWork), typeof(IUnitOfWorkFactory),
            typeof(ChainRunRecord), typeof(CheckpointRecord), typeof(ApprovalRecord),
            typeof(TokenBudgetEntryRecord), typeof(KeyValueSettingRecord), typeof(AuditLogRecord),
            typeof(PagedQuery), typeof(PagedResult<>),
        ];
        foreach (Type type in contracts)
        {
            foreach (System.Reflection.MemberInfo member in type.GetMembers())
                Assert.DoesNotContain("Npgsql", member.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Npgsql", type.FullName, StringComparison.Ordinal);
        }
    }
}
