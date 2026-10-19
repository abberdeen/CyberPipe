using CyberPipe.Contracts;
using CyberPipe.Security;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class AntiWipeGuardTests
{
    private readonly AntiWipeGuard _guard = new();

    [Theory]
    [InlineData("DROP TABLE app.chains;", DestructiveOperationKind.SqlStatement, WipeGuardDecisionOutcome.RequireApproval, WipeGuardReasonCodes.DropStatement)]
    [InlineData("TRUNCATE TABLE app.messages;", DestructiveOperationKind.SqlStatement, WipeGuardDecisionOutcome.RequireApproval, WipeGuardReasonCodes.TruncateStatement)]
    [InlineData("TRUNCATE app.chain_runs;", DestructiveOperationKind.SqlStatement, WipeGuardDecisionOutcome.RequireApproval, WipeGuardReasonCodes.TruncateStatement)]
    public async Task SqlStatements_DropAndTruncate_NeverReturnAllow(
        string sql,
        DestructiveOperationKind kind,
        WipeGuardDecisionOutcome expectedOutcome,
        string expectedReason)
    {
        var op = new DestructiveOperation
        {
            Kind = kind,
            Target = "app.table",
            StatementOrCommand = sql,
        };

        WipeGuardDecision decision = await _guard.EvaluateAsync(op);

        Assert.Equal(expectedOutcome, decision.Outcome);
        Assert.Equal(expectedReason, decision.ReasonCode);
    }

    [Fact]
    public async Task DropDatabaseOrSchema_WithStrictDeny_ReturnsDeny()
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.SqlStatement,
            Target = "cyberpipe",
            StatementOrCommand = "DROP DATABASE cyberpipe;",
        };

        WipeGuardDecision decision = await _guard.EvaluateAsync(op);

        Assert.Equal(WipeGuardDecisionOutcome.Deny, decision.Outcome);
        Assert.Equal(WipeGuardReasonCodes.DropStatement, decision.ReasonCode);
    }

    [Fact]
    public async Task DeleteWithoutWhere_ReturnsRequireApproval()
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.SqlStatement,
            Target = "app.users",
            StatementOrCommand = "DELETE FROM app.users;",
        };

        WipeGuardDecision decision = await _guard.EvaluateAsync(op);

        Assert.Equal(WipeGuardDecisionOutcome.RequireApproval, decision.Outcome);
        Assert.Equal(WipeGuardReasonCodes.MissingWhereClause, decision.ReasonCode);
    }

    [Fact]
    public async Task UpdateWithoutWhere_ReturnsRequireApproval()
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.SqlStatement,
            Target = "app.users",
            StatementOrCommand = "UPDATE app.users SET active = false;",
        };

        WipeGuardDecision decision = await _guard.EvaluateAsync(op);

        Assert.Equal(WipeGuardDecisionOutcome.RequireApproval, decision.Outcome);
        Assert.Equal(WipeGuardReasonCodes.MissingWhereClause, decision.ReasonCode);
    }

    [Fact]
    public async Task DeleteWithWhere_BelowThreshold_ReturnsAllow()
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.SqlStatement,
            Target = "app.users",
            StatementOrCommand = "DELETE FROM app.users WHERE id = 42;",
            EstimatedCount = 1,
        };

        WipeGuardDecision decision = await _guard.EvaluateAsync(op);

        Assert.Equal(WipeGuardDecisionOutcome.Allow, decision.Outcome);
        Assert.Equal(WipeGuardReasonCodes.SafeOperation, decision.ReasonCode);
    }

    [Fact]
    public async Task DeleteWithWhere_AboveThreshold_ReturnsRequireApproval()
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.SqlStatement,
            Target = "app.logs",
            StatementOrCommand = "DELETE FROM app.logs WHERE created_at < '2026-01-01';",
            EstimatedCount = 500, // Threshold is 50
        };

        WipeGuardDecision decision = await _guard.EvaluateAsync(op);

        Assert.Equal(WipeGuardDecisionOutcome.RequireApproval, decision.Outcome);
        Assert.Equal(WipeGuardReasonCodes.BulkThresholdExceeded, decision.ReasonCode);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("data/pgdata/base")]
    [InlineData("config")]
    [InlineData("secrets.json")]
    [InlineData("appsettings.json")]
    [InlineData("cyberpipe.db")]
    public async Task ProtectedPath_ReturnsDenyOrRequireApproval(string protectedPath)
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.FileDelete,
            Target = protectedPath,
        };

        WipeGuardDecision decision = await _guard.EvaluateAsync(op);

        Assert.NotEqual(WipeGuardDecisionOutcome.Allow, decision.Outcome);
        Assert.Equal(WipeGuardReasonCodes.ProtectedPath, decision.ReasonCode);
    }

    [Fact]
    public async Task FileDelete_NonProtected_BelowThreshold_ReturnsAllow()
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.FileDelete,
            Target = "tmp/scratch.txt",
            EstimatedCount = 1,
        };

        WipeGuardDecision decision = await _guard.EvaluateAsync(op);

        Assert.Equal(WipeGuardDecisionOutcome.Allow, decision.Outcome);
        Assert.Equal(WipeGuardReasonCodes.SafeOperation, decision.ReasonCode);
    }

    [Fact]
    public async Task FileDelete_NonProtected_AboveThreshold_ReturnsRequireApproval()
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.FileDelete,
            Target = "tmp/cache_dir",
            EstimatedCount = 200, // Bulk delete
        };

        WipeGuardDecision decision = await _guard.EvaluateAsync(op);

        Assert.Equal(WipeGuardDecisionOutcome.RequireApproval, decision.Outcome);
        Assert.Equal(WipeGuardReasonCodes.BulkThresholdExceeded, decision.ReasonCode);
    }

    [Fact]
    public async Task ServiceReset_AlwaysRequiresApproval()
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.ServiceReset,
            Target = "service.config",
        };

        WipeGuardDecision decision = await _guard.EvaluateAsync(op);

        Assert.Equal(WipeGuardDecisionOutcome.RequireApproval, decision.Outcome);
        Assert.Equal(WipeGuardReasonCodes.ServiceReset, decision.ReasonCode);
    }

    [Fact]
    public async Task FailClosed_OnEmptyTargetOrNull()
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.FileDelete,
            Target = "   ",
        };

        WipeGuardDecision decision = await _guard.EvaluateAsync(op);

        Assert.Equal(WipeGuardDecisionOutcome.Deny, decision.Outcome);
        Assert.Equal(WipeGuardReasonCodes.ClassifierFailure, decision.ReasonCode);
    }

    [Fact]
    public async Task ApprovalProof_MatchingFingerprint_AllowsOperation()
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.SqlStatement,
            Target = "app.messages",
            StatementOrCommand = "TRUNCATE TABLE app.messages;",
        };

        WipeGuardDecision initialDecision = await _guard.EvaluateAsync(op);
        Assert.Equal(WipeGuardDecisionOutcome.RequireApproval, initialDecision.Outcome);

        var proof = new ApprovalProof
        {
            ApprovalId = Guid.NewGuid(),
            OperationFingerprint = initialDecision.OperationFingerprint,
            ApprovedBy = "owner",
        };

        WipeGuardDecision confirmedDecision = await _guard.EvaluateConfirmedAsync(op, proof);

        Assert.Equal(WipeGuardDecisionOutcome.Allow, confirmedDecision.Outcome);
        Assert.Equal(WipeGuardReasonCodes.ProofAccepted, confirmedDecision.ReasonCode);
    }

    [Fact]
    public async Task ApprovalProof_MismatchedFingerprint_RejectsProof()
    {
        var opOriginal = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.FileDelete,
            Target = "tmp/file1.txt",
        };

        var opTampered = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.FileDelete,
            Target = "tmp/file2.txt", // Changed target!
        };

        string fingerprintOriginal = opOriginal.ComputeFingerprint();

        var proof = new ApprovalProof
        {
            ApprovalId = Guid.NewGuid(),
            OperationFingerprint = fingerprintOriginal,
        };

        // Try applying proof to tampered operation
        WipeGuardDecision result = await _guard.EvaluateConfirmedAsync(opTampered, proof);

        Assert.Equal(WipeGuardDecisionOutcome.RequireApproval, result.Outcome);
        Assert.Equal(WipeGuardReasonCodes.InvalidProof, result.ReasonCode);
    }

    [Fact]
    public async Task ApprovalProof_ReplayAttempt_ReturnsProofAlreadyUsed()
    {
        var op = new DestructiveOperation
        {
            Kind = DestructiveOperationKind.SqlStatement,
            Target = "app.test",
            StatementOrCommand = "TRUNCATE TABLE app.test;",
        };

        var proof = new ApprovalProof
        {
            ApprovalId = Guid.NewGuid(),
            OperationFingerprint = op.ComputeFingerprint(),
        };

        // First use succeeds
        WipeGuardDecision first = await _guard.EvaluateConfirmedAsync(op, proof);
        Assert.Equal(WipeGuardDecisionOutcome.Allow, first.Outcome);

        // Second use of same ApprovalId fails (one-time proof)
        WipeGuardDecision second = await _guard.EvaluateConfirmedAsync(op, proof);
        Assert.Equal(WipeGuardDecisionOutcome.RequireApproval, second.Outcome);
        Assert.Equal(WipeGuardReasonCodes.ProofAlreadyUsed, second.ReasonCode);
    }
}
