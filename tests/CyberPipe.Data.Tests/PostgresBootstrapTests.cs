using System.Net.Sockets;
using CyberPipe.Contracts;
using CyberPipe.Data;
using Npgsql;
using Xunit;

namespace CyberPipe.Data.Tests;

public sealed class PostgresBootstrapTests
{
    private const string PromptedSecret = "prompted-s3cret-value";
    private const string StoredSecret = "stored-wrong-value";

    private static PostgresOptions Options(int maxPromptAttempts = 3) => new()
    {
        Host = "db.local",
        Port = 5433,
        Database = "cyberpipe_test",
        Username = "cyberpipe",
        PasswordSecretKey = SecretKeys.DatabasePassword,
        MaxPasswordPromptAttempts = maxPromptAttempts,
    };

    [Fact]
    public async Task EnsureReady_WithValidStoredSecret_ReturnsReadyWithoutPrompt()
    {
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, FakePostgresGateway.CorrectPassword);
        var prompt = new FakeSecretPrompt();
        var gateway = new FakePostgresGateway();
        var bootstrapper = new PostgresBootstrapper(Options(), store, prompt, gateway);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Ready, result.Status);
        Assert.Equal(BootstrapReasonCode.None, result.ReasonCode);
        Assert.Equal("16.4", result.ServerVersion);
        Assert.Equal("0.8.0", result.PgVectorVersion);
        Assert.Equal(0, prompt.CallCount);
        Assert.Equal(1, gateway.EnsureCallCount);
    }

    [Fact]
    public async Task EnsureReady_SecondRun_IsIdempotentAndDoesNotChangeDataOrPrompt()
    {
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, FakePostgresGateway.CorrectPassword);
        var prompt = new FakeSecretPrompt();
        var gateway = new FakePostgresGateway();
        var bootstrapper = new PostgresBootstrapper(Options(), store, prompt, gateway);

        BootstrapResult first = await bootstrapper.EnsureReadyAsync();
        string markerAfterFirst = gateway.DataMarker;
        BootstrapResult second = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Ready, first.Status);
        Assert.Equal(BootstrapStatus.Ready, second.Status);
        Assert.True(gateway.DatabaseCreated);
        Assert.Equal(markerAfterFirst, gateway.DataMarker);
        Assert.Equal(0, prompt.CallCount);
        Assert.Equal(0, store.SetCount);
    }

    [Fact]
    public async Task EnsureReady_WrongStoredPassword_PromptsAndSucceedsWithPromptedPassword()
    {
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, StoredSecret);
        var prompt = new FakeSecretPrompt(FakePostgresGateway.CorrectPassword);
        var gateway = new FakePostgresGateway();
        var bootstrapper = new PostgresBootstrapper(Options(), store, prompt, gateway);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Ready, result.Status);
        Assert.Equal(1, prompt.CallCount);
        Assert.Contains(SecretKeys.DatabasePassword, prompt.Purposes[0]);
        // Подтверждённый пароль сохранён: следующий запуск промпт не потребует.
        Assert.Equal(FakePostgresGateway.CorrectPassword, store.StoredPlainText(SecretKeys.DatabasePassword));
    }

    [Fact]
    public async Task EnsureReady_PromptDeclined_ReturnsNeedsPasswordWithoutThrowing()
    {
        var store = new FakeSecretStore();
        var prompt = new FakeSecretPrompt((string?)null);
        var gateway = new FakePostgresGateway();
        var bootstrapper = new PostgresBootstrapper(Options(), store, prompt, gateway);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.NeedsPassword, result.Status);
        Assert.Equal(1, prompt.CallCount);
        Assert.Equal(1, gateway.EnsureCallCount);
    }

    [Fact]
    public async Task EnsureReady_AlwaysWrongPassword_PromptsAtMostConfiguredTimes()
    {
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, StoredSecret);
        var prompt = new FakeSecretPrompt("bad-1", "bad-2", "bad-3", "bad-4");
        var gateway = new FakePostgresGateway();
        var bootstrapper = new PostgresBootstrapper(Options(maxPromptAttempts: 2), store, prompt, gateway);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.NeedsPassword, result.Status);
        Assert.Equal(BootstrapReasonCode.AuthenticationFailed, result.ReasonCode);
        Assert.Equal(2, prompt.CallCount);
        Assert.Equal(3, gateway.EnsureCallCount);
    }

    [Fact]
    public async Task EnsureReady_ServerUnavailable_ReturnsUnavailableAndNeverPrompts()
    {
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, FakePostgresGateway.CorrectPassword);
        var prompt = new FakeSecretPrompt(FakePostgresGateway.CorrectPassword);
        var gateway = new FakePostgresGateway
        {
            UseFailure = true,
            Failure = BootstrapReasonCode.ServerUnavailable,
        };
        var bootstrapper = new PostgresBootstrapper(Options(), store, prompt, gateway);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Unavailable, result.Status);
        Assert.Equal(BootstrapReasonCode.ServerUnavailable, result.ReasonCode);
        Assert.Equal(0, prompt.CallCount);
    }

    [Fact]
    public async Task EnsureReady_PgVectorMissing_ReturnsUnavailableAndNeverPrompts()
    {
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, FakePostgresGateway.CorrectPassword);
        var prompt = new FakeSecretPrompt(FakePostgresGateway.CorrectPassword);
        var gateway = new FakePostgresGateway
        {
            UseFailure = true,
            Failure = BootstrapReasonCode.PgVectorMissing,
        };
        var bootstrapper = new PostgresBootstrapper(Options(), store, prompt, gateway);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Unavailable, result.Status);
        Assert.Equal(BootstrapReasonCode.PgVectorMissing, result.ReasonCode);
        Assert.Equal(0, prompt.CallCount);
    }

    [Fact]
    public async Task EnsureReady_NoSecretAndNoPrompt_ReturnsNeedsPassword()
    {
        var store = new FakeSecretStore();
        var gateway = new FakePostgresGateway();
        var bootstrapper = new PostgresBootstrapper(Options(), store, secretPrompt: null, gateway);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.NeedsPassword, result.Status);
    }

    [Fact]
    public async Task EnsureReady_SecretsNeverAppearInResultExceptionOrLogs()
    {
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, StoredSecret);
        var prompt = new FakeSecretPrompt(PromptedSecret, FakePostgresGateway.CorrectPassword);
        var gateway = new FakePostgresGateway();
        var logger = new CapturingLogger();
        var bootstrapper = new PostgresBootstrapper(Options(), store, prompt, gateway, logger);

        BootstrapResult result = await bootstrapper.EnsureReadyAsync();

        Assert.Equal(BootstrapStatus.Ready, result.Status);
        string captured = string.Join("\n", logger.Messages) + "\n" + result;
        Assert.DoesNotContain(StoredSecret, captured);
        Assert.DoesNotContain(PromptedSecret, captured);
        Assert.DoesNotContain(FakePostgresGateway.CorrectPassword, captured);
        Assert.DoesNotContain("Password=", captured);

        var exception = new PostgresUnavailableException(BootstrapReasonCode.AuthenticationFailed);
        Assert.DoesNotContain(PromptedSecret, exception.Message);
        Assert.DoesNotContain("Password=", exception.Message);
    }

    [Theory]
    [InlineData("28P01", BootstrapReasonCode.AuthenticationFailed)]
    [InlineData("28000", BootstrapReasonCode.AuthenticationFailed)]
    [InlineData("42501", BootstrapReasonCode.InsufficientPrivilege)]
    [InlineData("58P01", BootstrapReasonCode.PgVectorMissing)]
    [InlineData("0A000", BootstrapReasonCode.PgVectorMissing)]
    [InlineData("57P03", BootstrapReasonCode.ServerUnavailable)]
    public void Classifier_MapsPostgresSqlStates(string sqlState, BootstrapReasonCode expected)
    {
        var exception = new PostgresException("driver text must not leak", "FATAL", "FATAL", sqlState);
        Assert.Equal(expected, PostgresErrorClassifier.Classify(exception));
    }

    [Fact]
    public void Classifier_MapsNetworkAndTimeoutErrors()
    {
        Assert.Equal(
            BootstrapReasonCode.ServerUnavailable,
            PostgresErrorClassifier.Classify(new NpgsqlException("unreachable", new SocketException())));
        Assert.Equal(
            BootstrapReasonCode.Timeout,
            PostgresErrorClassifier.Classify(new TimeoutException()));
        Assert.Equal(
            BootstrapReasonCode.Unknown,
            PostgresErrorClassifier.Classify(new InvalidOperationException()));
    }

    [Fact]
    public void ConnectionString_IsBuiltOnlyFromOptionsAndSecret()
    {
        PostgresOptions options = Options();
        string connectionString = PostgresConnectionFactory.BuildConnectionString(
            options, options.Database, SecretValue.FromPlainText(PromptedSecret));
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal("db.local", builder.Host);
        Assert.Equal(5433, builder.Port);
        Assert.Equal("cyberpipe_test", builder.Database);
        Assert.Equal("cyberpipe", builder.Username);
        Assert.Equal(PromptedSecret, builder.Password);
        Assert.Equal(options.MaxPoolSize, builder.MaxPoolSize);
        Assert.Equal(options.CommandTimeout, builder.CommandTimeout);
        // В самих опциях поля со значением пароля нет: только ссылка на секрет.
        Assert.Null(typeof(PostgresOptions).GetProperty("Password"));
        Assert.NotNull(typeof(PostgresOptions).GetProperty("PasswordSecretKey"));
    }

    [Fact]
    public void Options_FromDatabaseOptions_MapsAllFieldsWithoutPasswordValue()
    {
        var database = new DatabaseOptions
        {
            Host = "pg.local",
            Port = 5544,
            Database = "appdb",
            Username = "appuser",
            PasswordSecretKey = SecretKeys.DatabasePassword,
            MaxPoolSize = 42,
            CommandTimeout = 17,
        };

        PostgresOptions options = PostgresOptions.From(database);

        Assert.Equal("pg.local", options.Host);
        Assert.Equal(5544, options.Port);
        Assert.Equal("appdb", options.Database);
        Assert.Equal("appuser", options.Username);
        Assert.Equal(42, options.MaxPoolSize);
        Assert.Equal(17, options.CommandTimeout);
        Assert.Empty(options.Validate());
    }

    [Fact]
    public async Task HealthProbe_WhenGatewayFails_ReturnsDegradedWithoutSecrets()
    {
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, PromptedSecret);
        var gateway = new FakePostgresGateway
        {
            UseFailure = true,
            Failure = BootstrapReasonCode.ServerUnavailable,
        };
        var probe = new PostgresHealthProbe(Options(), store, gateway);

        DatabaseHealthDto health = await probe.CheckAsync();

        Assert.Equal(HealthStatus.Degraded, health.Status);
        Assert.Equal("db.local", health.Host);
        Assert.Equal(5433, health.Port);
        Assert.Contains("ServerUnavailable", health.Detail);
        Assert.DoesNotContain(PromptedSecret, health.Detail);
        Assert.DoesNotContain("Password=", health.Detail);
    }

    [Fact]
    public async Task HealthProbe_WhenGatewaySucceeds_ReturnsHealthy()
    {
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, FakePostgresGateway.CorrectPassword);
        var probe = new PostgresHealthProbe(Options(), store, new FakePostgresGateway());

        DatabaseHealthDto health = await probe.CheckAsync();

        Assert.Equal(HealthStatus.Healthy, health.Status);
        Assert.Null(health.Detail);
    }

    [Fact]
    public void HealthMapping_BootstrapNeedsPassword_BecomesDegraded()
    {
        DatabaseHealthDto health = PostgresHealthProbe.ToHealthDto(
            BootstrapResult.NeedsPassword(BootstrapReasonCode.PasswordRequired),
            Options(),
            DateTimeOffset.UtcNow);

        Assert.Equal(HealthStatus.Degraded, health.Status);
        Assert.Contains("PasswordRequired", health.Detail);
    }
}
