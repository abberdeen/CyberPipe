using CyberPipe.Contracts;
using CyberPipe.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CyberPipe.Data.Tests;

public sealed class SqliteBootstrapTests
{
    private static SqliteOptions Options(string? path = null) => new()
    {
        Path = path ?? Path.Combine(Path.GetTempPath(), "cyberpipe-boot-" + Guid.NewGuid().ToString("N") + ".db"),
        BusyTimeoutMs = 5000,
        CommandTimeout = 30,
    };

    [Fact]
    public async Task EnsureReady_WithValidOptions_ReturnsReady()
    {
        var gateway = new FakeSqliteGateway();
        var bootstrapper = new SqliteBootstrapper(Options(), gateway);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Ready, result.Status);
        Assert.Equal(BootstrapReasonCode.None, result.ReasonCode);
        Assert.Equal("3.50.0", result.ServerVersion);
        Assert.Equal(1, gateway.EnsureCallCount);
    }

    [Fact]
    public async Task EnsureReady_SecondRun_IsIdempotentAndDoesNotChangeData()
    {
        var gateway = new FakeSqliteGateway();
        var bootstrapper = new SqliteBootstrapper(Options(), gateway);

        BootstrapResult first = await bootstrapper.EnsureReadyAsync();
        string markerAfterFirst = gateway.DataMarker;
        BootstrapResult second = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Ready, first.Status);
        Assert.Equal(BootstrapStatus.Ready, second.Status);
        Assert.True(gateway.DatabaseCreated);
        Assert.Equal(markerAfterFirst, gateway.DataMarker);
    }

    [Fact]
    public async Task EnsureReady_InvalidOptions_ReturnsUnavailableWithoutThrowing()
    {
        var bootstrapper = new SqliteBootstrapper(new SqliteOptions { Path = "   " }, new FakeSqliteGateway());

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task EnsureReady_FileUnavailable_ReturnsUnavailable()
    {
        var gateway = new FakeSqliteGateway
        {
            UseFailure = true,
            Failure = BootstrapReasonCode.ServerUnavailable,
        };
        var bootstrapper = new SqliteBootstrapper(Options(), gateway);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Unavailable, result.Status);
        Assert.Equal(BootstrapReasonCode.ServerUnavailable, result.ReasonCode);
    }

    [Fact]
    public async Task EnsureReady_CorruptFile_ReturnsDatabaseCorrupt()
    {
        var gateway = new FakeSqliteGateway
        {
            UseFailure = true,
            Failure = BootstrapReasonCode.DatabaseCorrupt,
        };
        var bootstrapper = new SqliteBootstrapper(Options(), gateway);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Unavailable, result.Status);
        Assert.Equal(BootstrapReasonCode.DatabaseCorrupt, result.ReasonCode);
    }

    [Fact]
    public async Task EnsureReady_PathsNeverAppearInResultExceptionOrLogs()
    {
        string path = Options().Path;
        var gateway = new FakeSqliteGateway();
        var logger = new CapturingLogger();
        var bootstrapper = new SqliteBootstrapper(Options(path), gateway, logger);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Ready, result.Status);
        string captured = string.Join("\n", logger.Messages) + "\n" + result;
        Assert.DoesNotContain(".db-journal", captured);
        Assert.DoesNotContain("Password=", captured);

        var exception = new SqliteUnavailableException(BootstrapReasonCode.ServerUnavailable);
        Assert.DoesNotContain(path, exception.Message);
        Assert.DoesNotContain("Password=", exception.Message);
    }

    [Theory]
    [InlineData(26, BootstrapReasonCode.DatabaseCorrupt)] // SQLITE_NOTADB
    [InlineData(11, BootstrapReasonCode.DatabaseCorrupt)] // SQLITE_CORRUPT
    [InlineData(8, BootstrapReasonCode.InsufficientPrivilege)] // SQLITE_READONLY
    [InlineData(5, BootstrapReasonCode.Timeout)] // SQLITE_BUSY
    [InlineData(6, BootstrapReasonCode.Timeout)] // SQLITE_LOCKED
    [InlineData(14, BootstrapReasonCode.ServerUnavailable)] // SQLITE_CANTOPEN
    [InlineData(10, BootstrapReasonCode.ServerUnavailable)] // SQLITE_IOERR
    public void Classifier_MapsSqliteErrorCodes(int errorCode, BootstrapReasonCode expected)
    {
        var exception = new SqliteException("driver text must not leak", errorCode);
        Assert.Equal(expected, SqliteErrorClassifier.Classify(exception));
    }

    [Fact]
    public void Classifier_MapsTimeoutAndUnknownErrors()
    {
        Assert.Equal(
            BootstrapReasonCode.Timeout,
            SqliteErrorClassifier.Classify(new TimeoutException()));
        Assert.Equal(
            BootstrapReasonCode.InsufficientPrivilege,
            SqliteErrorClassifier.Classify(new UnauthorizedAccessException()));
        Assert.Equal(
            BootstrapReasonCode.Unknown,
            SqliteErrorClassifier.Classify(new InvalidOperationException()));
    }

    [Fact]
    public async Task ConnectionFactory_CreatesDirectoryEnablesWalAndForeignKeys()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "cyberpipe-sqlite-" + Guid.NewGuid().ToString("N"), "sub", "test.db");
        var factory = new SqliteConnectionFactory(new SqliteOptions { Path = path });

        await using (SqliteConnection connection = await factory.CreateOpenedConnectionAsync())
        {
            await using var journal = new SqliteCommand("PRAGMA journal_mode", connection);
            Assert.Equal("wal", (await journal.ExecuteScalarAsync())?.ToString());
            await using var fk = new SqliteCommand("PRAGMA foreign_keys", connection);
            Assert.Equal(1L, Convert.ToInt64(await fk.ExecuteScalarAsync()));
            await using var probe = new SqliteCommand("SELECT 1", connection);
            Assert.Equal(1L, Convert.ToInt64(await probe.ExecuteScalarAsync()));
        }

        SqliteConnection.ClearAllPools();
        Assert.True(File.Exists(path));
        Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(path))!, recursive: true);
    }

    [Fact]
    public void Options_FromDatabaseOptions_MapsAllFields()
    {
        var database = new DatabaseOptions
        {
            Path = "data/test.db",
            BusyTimeoutMs = 2500,
            CommandTimeout = 17,
        };

        SqliteOptions options = SqliteOptions.From(database);

        Assert.Equal("data/test.db", options.Path);
        Assert.Equal(2500, options.BusyTimeoutMs);
        Assert.Equal(17, options.CommandTimeout);
        Assert.Empty(options.Validate());
        Assert.EndsWith("test.db", options.FullPath());
    }

    [Fact]
    public async Task HealthProbe_WhenGatewayFails_ReturnsDegradedWithPath()
    {
        var gateway = new FakeSqliteGateway
        {
            UseFailure = true,
            Failure = BootstrapReasonCode.ServerUnavailable,
        };
        SqliteOptions options = Options();
        var probe = new SqliteHealthProbe(options, gateway);

        DatabaseHealthDto health = await probe.CheckAsync();

        Assert.Equal(HealthStatus.Degraded, health.Status);
        Assert.Equal(options.Path, health.Path);
        Assert.Contains("ServerUnavailable", health.Detail);
        Assert.DoesNotContain("Password=", health.Detail);
    }

    [Fact]
    public async Task HealthProbe_WhenGatewaySucceeds_ReturnsHealthy()
    {
        var probe = new SqliteHealthProbe(Options(), new FakeSqliteGateway());

        DatabaseHealthDto health = await probe.CheckAsync();

        Assert.Equal(HealthStatus.Healthy, health.Status);
        Assert.Null(health.Detail);
    }

    [Fact]
    public void HealthMapping_BootstrapUnavailable_BecomesDegraded()
    {
        DatabaseHealthDto health = SqliteHealthProbe.ToHealthDto(
            BootstrapResult.Unavailable(BootstrapReasonCode.ServerUnavailable),
            Options(),
            DateTimeOffset.UtcNow);

        Assert.Equal(HealthStatus.Degraded, health.Status);
        Assert.Contains("ServerUnavailable", health.Detail);
    }
}
