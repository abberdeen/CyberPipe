using CyberPipe.Contracts;
using CyberPipe.Data;
using Xunit;

namespace CyberPipe.Data.Tests;

/// <summary>Живой SQLite-прогон FIX-03: миграция 0007, личные сервисы и реестр пакетов.</summary>
[Trait("Category", "Integration")]
public sealed class SqlitePersonalIntegrationTests
{
    [SqliteIntegrationFact]
    public async Task PersonalAndPackageRepositories_PersistInSqlite()
    {
        await using SqliteTestDatabase db = await SqliteTestDatabase.CreateAsync();
        var factory = db.DbFactory;

        var notes = new NoteStoreService(factory);
        NoteRecord note = await notes.CreateAsync("Shopping", "Buy milk", ["home"]);
        Assert.NotNull(await notes.GetAsync(note.NoteId));
        Assert.Single(await notes.SearchAsync("milk"));
        NoteRecord renamed = await notes.UpdateAsync(note.NoteId, "Groceries", content: null);
        Assert.Equal("Groceries", renamed.Title);
        Assert.True(await notes.DeleteAsync(note.NoteId));
        Assert.Null(await notes.GetAsync(note.NoteId));

        var finance = new FinanceLedgerService(factory);
        await finance.AddTransactionAsync(FinanceTransactionKind.Income, 100m, "USD", "salary");
        await finance.AddTransactionAsync(FinanceTransactionKind.Expense, 30m, "USD", "food");
        Assert.Equal(70m, await finance.GetBalanceAsync("USD"));
        FinanceReportDto report = await finance.GetReportAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "USD");
        Assert.Equal(100m, report.TotalIncome);
        Assert.Equal(30m, report.TotalExpense);
        Assert.Equal(70m, report.NetBalance);
        Assert.Equal(2, (await finance.ListTransactionsAsync(currency: "USD")).Count);

        var reminders = new ReminderStoreService(factory);
        ReminderItemRecord reminder = await reminders.CreateAsync("Call", DateTimeOffset.UtcNow.AddHours(1));
        Assert.True(await reminders.CompleteAsync(reminder.ReminderId));
        Assert.Equal(ReminderStatus.Completed, (await reminders.GetAsync(reminder.ReminderId))!.Status);
        ReminderItemRecord cancellable = await reminders.CreateAsync("Cancel me", DateTimeOffset.UtcNow.AddHours(2));
        Assert.True(await reminders.CancelAsync(cancellable.ReminderId));
        Assert.Equal(ReminderStatus.Cancelled, (await reminders.GetAsync(cancellable.ReminderId))!.Status);
        Assert.False(await reminders.CompleteAsync(Guid.NewGuid()));

        var contacts = new ContactStoreService(factory);
        ContactRecord contact = await contacts.UpsertContactAsync("Alice", new Dictionary<string, string> { ["telegram"] = "@alice" });
        await contacts.AddInteractionAsync(contact.ContactId, "telegram", "hi");
        Assert.Single(await contacts.GetHistoryAsync(contact.ContactId));
        Assert.NotNull(await contacts.FindByNameAsync("alice"));
        Assert.NotEmpty(await contacts.SearchAsync("Ali"));

        var packages = new NetrunnerPackageStoreService(factory);
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
        Assert.Null(await packages.GetPackageAsync("test-pkg", "9.9.9"));
        await Assert.ThrowsAsync<PackageRegistrationException>(
            () => packages.RegisterPackageAsync(manifest, "/tmp/test-pkg", "different-hash"));
    }
}
