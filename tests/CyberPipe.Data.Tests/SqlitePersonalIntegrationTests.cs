using CyberPipe.Contracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CyberPipe.Data.Tests;

/// <summary>Живой SQLite-прогон FIX-03: миграция 0007, личные репозитории и реестр пакетов.</summary>
[Trait("Category", "Integration")]
public sealed class SqlitePersonalIntegrationTests
{
    [SqliteIntegrationFact]
    public async Task PersonalAndPackageRepositories_PersistInSqlite()
    {
        await using SqliteTestDatabase db = await SqliteTestDatabase.CreateAsync();
        var factory = db.Factory;

            var notes = new SqliteNoteRepository(factory);
            NoteRecord note = await notes.CreateAsync("Shopping", "Buy milk", ["home"]);
            Assert.NotNull(await notes.GetAsync(note.NoteId));
            Assert.Single(await notes.SearchAsync("milk"));

            var finance = new SqliteFinanceRepository(factory);
            await finance.AddTransactionAsync(FinanceTransactionKind.Income, 100m, "USD", "salary");
            await finance.AddTransactionAsync(FinanceTransactionKind.Expense, 30m, "USD", "food");
            Assert.Equal(70m, await finance.GetBalanceAsync("USD"));

            var reminders = new SqliteReminderRepository(factory);
            ReminderItemRecord reminder = await reminders.CreateAsync("Call", DateTimeOffset.UtcNow.AddHours(1));
            Assert.True(await reminders.CompleteAsync(reminder.ReminderId));
            Assert.Equal(ReminderStatus.Completed, (await reminders.GetAsync(reminder.ReminderId))!.Status);

            var contacts = new SqliteContactRepository(factory);
            ContactRecord contact = await contacts.UpsertContactAsync("Alice", new Dictionary<string, string> { ["telegram"] = "@alice" });
            await contacts.AddInteractionAsync(contact.ContactId, "telegram", "hi");
            Assert.Single(await contacts.GetHistoryAsync(contact.ContactId));
            Assert.NotNull(await contacts.FindByNameAsync("alice"));

            var packages = new SqlitePackageRegistryRepository(factory);
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
}
