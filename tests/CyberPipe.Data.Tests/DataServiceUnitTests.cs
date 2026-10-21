using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CyberPipe.Data.Tests;

public sealed class DataServiceUnitTests
{
    private sealed class NeverCalledFactory : IDbContextFactory<CyberPipeDbContext>
    {
        public CyberPipeDbContext CreateDbContext() =>
            throw new InvalidOperationException("Database must not be called in unit tests.");

        public Task<CyberPipeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Database must not be called in unit tests.");
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
    public void ServiceCtor_NegativeOptions_ThrowBeforeDb()
    {
        var bad = new RepositoryOptions { SlowQueryThreshold = TimeSpan.FromMilliseconds(-1) };
        Assert.Throws<ArgumentException>(() => new ChainCatalogService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new ChainRunRecordService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new CheckpointStoreService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new ApprovalLedgerService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new TokenBudgetLedgerService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new KvSettingsService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new AuditLogService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new HitlApprovalStoreService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new NetrunnerPackageStoreService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new NoteStoreService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new FinanceLedgerService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new ReminderStoreService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new ContactStoreService(new NeverCalledFactory(), bad));
        Assert.Throws<ArgumentException>(() => new EmbeddingStoreService(new NeverCalledFactory(), bad));
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
    public void PublicServiceContracts_ContainNoNpgsqlTypes()
    {
        Type[] contracts =
        [
            typeof(ChainCatalogService), typeof(ChainRunRecordService), typeof(CheckpointStoreService),
            typeof(ApprovalLedgerService), typeof(TokenBudgetLedgerService), typeof(KvSettingsService),
            typeof(AuditLogService), typeof(ChainExecutionStoreService), typeof(HitlApprovalStoreService),
            typeof(NetrunnerPackageStoreService), typeof(NoteStoreService), typeof(FinanceLedgerService),
            typeof(ReminderStoreService), typeof(ContactStoreService), typeof(EmbeddingStoreService),
            typeof(BudgetLedgerService), typeof(IBudgetLedgerService),
            typeof(IChainCatalogService), typeof(IChainRunRecordService), typeof(ICheckpointStoreService),
            typeof(IApprovalLedgerService), typeof(ITokenBudgetLedgerService), typeof(IKvSettingsService),
            typeof(IAuditLogService), typeof(IChainExecutionStoreService), typeof(IHitlApprovalStoreService),
            typeof(INetrunnerPackageStoreService), typeof(INoteStoreService), typeof(IFinanceLedgerService),
            typeof(IReminderStoreService), typeof(IContactStoreService), typeof(IEmbeddingStoreService),
            typeof(DataServiceMetrics), typeof(IDataServiceMetrics),
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
