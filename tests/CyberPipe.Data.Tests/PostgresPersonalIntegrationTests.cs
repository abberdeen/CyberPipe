using CyberPipe.Contracts;
using Npgsql;
using Xunit;

namespace CyberPipe.Data.Tests;

/// <summary>Живой PG-прогон FIX-03: миграция 0007, личные репозитории и реестр пакетов.</summary>
[Trait("Category", "Integration")]
public sealed class PostgresPersonalIntegrationTests
{
    [PostgresIntegrationFact]
    public async Task PersonalAndPackageRepositories_PersistInPostgres()
    {
        string adminConnection = Environment.GetEnvironmentVariable(PostgresIntegrationTests.EnvVariable)
            ?? throw new InvalidOperationException("env not set");
        var admin = new NpgsqlConnectionStringBuilder(adminConnection);
        string databaseName = "cyberpipe_personal_" + Guid.NewGuid().ToString("N")[..12];
        var options = new PostgresOptions
        {
            Host = admin.Host ?? "localhost",
            Port = admin.Port,
            Database = databaseName,
            Username = admin.Username ?? "postgres",
            PasswordSecretKey = SecretKeys.DatabasePassword,
        };
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, admin.Password ?? string.Empty);

        try
        {
            BootstrapResult bootstrap = await new PostgresBootstrapper(options, store, new FakeSecretPrompt()).EnsureReadyAsync();
            Assert.Equal(BootstrapStatus.Ready, bootstrap.Status);
            var factory = new PostgresConnectionFactory(options, store);
            MigrationReport migration = await new SchemaMigrator(factory).MigrateAsync();
            Assert.True(migration.CurrentVersion >= 7, $"Ожидалась миграция 0007, текущая версия {migration.CurrentVersion}");

            var notes = new PostgresNoteRepository(factory);
            NoteRecord note = await notes.CreateAsync("Shopping", "Buy milk", ["home"]);
            Assert.NotNull(await notes.GetAsync(note.NoteId));
            Assert.Single(await notes.SearchAsync("milk"));

            var finance = new PostgresFinanceRepository(factory);
            await finance.AddTransactionAsync(FinanceTransactionKind.Income, 100m, "USD", "salary");
            await finance.AddTransactionAsync(FinanceTransactionKind.Expense, 30m, "USD", "food");
            Assert.Equal(70m, await finance.GetBalanceAsync("USD"));

            var reminders = new PostgresReminderRepository(factory);
            ReminderItemRecord reminder = await reminders.CreateAsync("Call", DateTimeOffset.UtcNow.AddHours(1));
            Assert.True(await reminders.CompleteAsync(reminder.ReminderId));
            Assert.Equal(ReminderStatus.Completed, (await reminders.GetAsync(reminder.ReminderId))!.Status);

            var contacts = new PostgresContactRepository(factory);
            ContactRecord contact = await contacts.UpsertContactAsync("Alice", new Dictionary<string, string> { ["telegram"] = "@alice" });
            await contacts.AddInteractionAsync(contact.ContactId, "telegram", "hi");
            Assert.Single(await contacts.GetHistoryAsync(contact.ContactId));
            Assert.NotNull(await contacts.FindByNameAsync("alice"));

            var packages = new PostgresPackageRegistryRepository(factory);
            var manifest = new NetrunnerManifestDto
            {
                Slug = "test-pkg",
                Name = "Test",
                Version = "1.0.0",
                Description = "test",
                Runtime = new NetrunnerRuntimeDto { Language = "Python", LanguageVersion = "3.11", Environment = "CPython", EnvironmentVersion = "3.11", Entrypoint = "skill-hack.py" },
                Io = new NetrunnerIoDto { InputSchemaFile = "schema.input.json", OutputSchemaFile = "schema.output.json" },
                Permissions = new NetrunnerPermissionsDto { FilesystemScope = "IsolatedRunWorkspaceOnly" },
                Integrity = new NetrunnerIntegrityDto(),
            };
            PackageRegistrationRecord registered = await packages.RegisterPackageAsync(manifest, "/tmp/test-pkg", "abc123");
            Assert.Equal("test-pkg", registered.Slug);
            Assert.NotNull(await packages.GetPackageAsync("test-pkg", "1.0.0"));
            await Assert.ThrowsAsync<PackageRegistrationException>(
                () => packages.RegisterPackageAsync(manifest, "/tmp/test-pkg", "different-hash"));
        }
        finally
        {
            var builder = new NpgsqlConnectionStringBuilder(adminConnection) { Database = PostgresConnectionFactory.MaintenanceDatabase };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()", connection);
            terminate.Parameters.AddWithValue("name", databaseName);
            await terminate.ExecuteNonQueryAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
