using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Skills.Personal;
using CyberPipe.Skills.Personal.Data;
using CyberPipe.Skills.Personal.Registry;
using CyberPipe.Skills.Personal.Skills;
using Xunit;

namespace CyberPipe.Tests.Unit.Skills.Personal;

public sealed class SkillsPersonalTests
{
    #region Notes Tests

    [Fact]
    public async Task Notes_CrudAndSearch_WorkCorrectly()
    {
        var repo = new InMemoryNoteRepository();
        var createSkill = new NoteCreateSkill(repo);
        var searchSkill = new NoteSearchSkill(repo);
        var getSkill = new NoteGetSkill(repo);
        var updateSkill = new NoteUpdateSkill(repo);
        var deleteSkill = new NoteDeleteSkill(repo);

        // 1. Create
        HostSkillResult createResult = await createSkill.ExecuteAsync(
            "{\"title\":\"Shopping List\",\"content\":\"Milk, eggs, coffee\",\"tags\":[\"groceries\",\"daily\"]}");
        Assert.True(createResult.IsSuccess);

        using var doc = JsonDocument.Parse(createResult.OutputJson);
        Guid noteId = doc.RootElement.GetProperty("NoteId").GetGuid();

        // 2. Get
        HostSkillResult getResult = await getSkill.ExecuteAsync($"{{\"noteId\":\"{noteId}\"}}");
        Assert.True(getResult.IsSuccess);
        Assert.Contains("Shopping List", getResult.OutputJson);

        // 3. Search
        HostSkillResult searchResult = await searchSkill.ExecuteAsync("{\"query\":\"eggs\"}");
        Assert.True(searchResult.IsSuccess);
        Assert.Contains("Shopping List", searchResult.OutputJson);

        // 4. Update
        HostSkillResult updateResult = await updateSkill.ExecuteAsync(
            $"{{\"noteId\":\"{noteId}\",\"content\":\"Milk, eggs, coffee, apples\"}}");
        Assert.True(updateResult.IsSuccess);
        Assert.Contains("apples", updateResult.OutputJson);

        // 5. Delete
        HostSkillResult deleteResult = await deleteSkill.ExecuteAsync($"{{\"noteId\":\"{noteId}\"}}");
        Assert.True(deleteResult.IsSuccess);
        Assert.Contains("\"deleted\":true", deleteResult.OutputJson);

        // 6. Verify deleted note is not found in search or get
        HostSkillResult searchAfterDelete = await searchSkill.ExecuteAsync("{\"query\":\"eggs\"}");
        Assert.True(searchAfterDelete.IsSuccess);
        Assert.Equal("[]", searchAfterDelete.OutputJson.Trim());

        HostSkillResult getAfterDelete = await getSkill.ExecuteAsync($"{{\"noteId\":\"{noteId}\"}}");
        Assert.False(getAfterDelete.IsSuccess);
    }

    [Fact]
    public async Task Notes_EmptyTitleAndContent_ReturnsValidationError()
    {
        var repo = new InMemoryNoteRepository();
        var createSkill = new NoteCreateSkill(repo);

        HostSkillResult result = await createSkill.ExecuteAsync("{\"title\":\" \",\"content\":\"\"}");
        Assert.False(result.IsSuccess);
        Assert.Equal(SkillResultStatus.ValidationError, result.Status);
    }

    #endregion

    #region Finance Tests

    [Fact]
    public async Task Finance_IncomeExpenseAndReport_CalculatesAccurately()
    {
        var clock = new ManualTestTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryFinanceRepository(clock);
        var expenseSkill = new FinanceAddExpenseSkill(repo);
        var incomeSkill = new FinanceAddIncomeSkill(repo);
        var balanceSkill = new FinanceBalanceSkill(repo);
        var reportSkill = new FinanceReportSkill(repo, clock);

        // Добавляем доход 100 000 RUB
        HostSkillResult incResult = await incomeSkill.ExecuteAsync(
            "{\"amount\":100000,\"currency\":\"RUB\",\"category\":\"Salary\",\"description\":\"October pay\"}");
        Assert.True(incResult.IsSuccess);

        // Добавляем расходы 15 000 и 5 000 RUB
        HostSkillResult exp1 = await expenseSkill.ExecuteAsync(
            "{\"amount\":15000,\"currency\":\"RUB\",\"category\":\"Food\",\"description\":\"Supermarket\"}");
        Assert.True(exp1.IsSuccess);

        HostSkillResult exp2 = await expenseSkill.ExecuteAsync(
            "{\"amount\":5000,\"currency\":\"RUB\",\"category\":\"Transport\",\"description\":\"Fuel\"}");
        Assert.True(exp2.IsSuccess);

        // Баланс: 100 000 - 20 000 = 80 000
        HostSkillResult balResult = await balanceSkill.ExecuteAsync("{\"currency\":\"RUB\"}");
        Assert.True(balResult.IsSuccess);
        Assert.Contains("\"balance\":80000", balResult.OutputJson);

        // Отчёт за октябрь
        HostSkillResult repResult = await reportSkill.ExecuteAsync(
            "{\"from\":\"2026-10-01T00:00:00Z\",\"to\":\"2026-10-31T23:59:59Z\",\"currency\":\"RUB\"}");
        Assert.True(repResult.IsSuccess);

        using var doc = JsonDocument.Parse(repResult.OutputJson);
        decimal totalIncome = doc.RootElement.GetProperty("TotalIncome").GetDecimal();
        decimal totalExpense = doc.RootElement.GetProperty("TotalExpense").GetDecimal();
        decimal netBalance = doc.RootElement.GetProperty("NetBalance").GetDecimal();

        Assert.Equal(100000m, totalIncome);
        Assert.Equal(20000m, totalExpense);
        Assert.Equal(80000m, netBalance);
    }

    [Theory]
    [InlineData("{\"amount\":0,\"currency\":\"RUB\"}")]
    [InlineData("{\"amount\":-100,\"currency\":\"RUB\"}")]
    public async Task Finance_InvalidAmount_ReturnsValidationError(string payload)
    {
        var repo = new InMemoryFinanceRepository();
        var expenseSkill = new FinanceAddExpenseSkill(repo);

        HostSkillResult result = await expenseSkill.ExecuteAsync(payload);
        Assert.False(result.IsSuccess);
        Assert.Equal(SkillResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task Finance_UnsupportedCurrency_ReturnsValidationError()
    {
        var repo = new InMemoryFinanceRepository();
        var expenseSkill = new FinanceAddExpenseSkill(repo);

        HostSkillResult result = await expenseSkill.ExecuteAsync("{\"amount\":100,\"currency\":\"BITCOIN\"}");
        Assert.False(result.IsSuccess);
        Assert.Equal(SkillResultStatus.ValidationError, result.Status);
    }

    #endregion

    #region Reminders Tests

    [Fact]
    public async Task Reminders_DueInPast_ReturnsValidationError()
    {
        var clock = new ManualTestTimeProvider(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryReminderRepository(clock);
        var createSkill = new ReminderCreateSkill(repo, clock);

        // Дата в прошлом (18 октября 2026 относительно 19 октября)
        HostSkillResult result = await createSkill.ExecuteAsync(
            "{\"title\":\"Past Reminder\",\"dueTime\":\"2026-10-18T10:00:00Z\"}");

        Assert.False(result.IsSuccess);
        Assert.Equal(SkillResultStatus.ValidationError, result.Status);
        Assert.Contains("past", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reminders_Lifecycle_CreateListCancelAndComplete()
    {
        var clock = new ManualTestTimeProvider(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryReminderRepository(clock);
        var createSkill = new ReminderCreateSkill(repo, clock);
        var listSkill = new ReminderListSkill(repo);
        var cancelSkill = new ReminderCancelSkill(repo);
        var completeSkill = new ReminderCompleteSkill(repo);

        // 1. Create future reminder
        HostSkillResult createResult = await createSkill.ExecuteAsync(
            "{\"title\":\"Call Doctor\",\"dueTime\":\"2026-10-20T10:00:00Z\"}");
        Assert.True(createResult.IsSuccess);

        using var doc = JsonDocument.Parse(createResult.OutputJson);
        Guid reminderId = doc.RootElement.GetProperty("ReminderId").GetGuid();

        // 2. Visible in list
        HostSkillResult listBefore = await listSkill.ExecuteAsync("{\"status\":\"Pending\"}");
        Assert.True(listBefore.IsSuccess);
        Assert.Contains("Call Doctor", listBefore.OutputJson);

        // 3. Complete
        HostSkillResult compResult = await completeSkill.ExecuteAsync($"{{\"reminderId\":\"{reminderId}\"}}");
        Assert.True(compResult.IsSuccess);
        Assert.Contains("\"completed\":true", compResult.OutputJson);

        // 4. Not in pending list
        HostSkillResult listPendingAfter = await listSkill.ExecuteAsync("{\"status\":\"Pending\"}");
        Assert.True(listPendingAfter.IsSuccess);
        Assert.DoesNotContain("Call Doctor", listPendingAfter.OutputJson);

        // 5. Cancel second reminder
        HostSkillResult create2 = await createSkill.ExecuteAsync(
            "{\"title\":\"Buy tickets\",\"cron\":\"0 12 * * *\"}");
        using var doc2 = JsonDocument.Parse(create2.OutputJson);
        Guid id2 = doc2.RootElement.GetProperty("ReminderId").GetGuid();

        HostSkillResult cancelResult = await cancelSkill.ExecuteAsync($"{{\"reminderId\":\"{id2}\"}}");
        Assert.True(cancelResult.IsSuccess);
        Assert.Contains("\"cancelled\":true", cancelResult.OutputJson);
    }

    #endregion

    #region CRM Tests

    [Fact]
    public async Task Crm_UpsertFindAndHistory_WorkCorrectly()
    {
        var repo = new InMemoryContactRepository();
        var upsertSkill = new CrmContactUpsertSkill(repo);
        var findSkill = new CrmContactFindSkill(repo);
        var interactionSkill = new CrmInteractionAddSkill(repo);
        var historySkill = new CrmInteractionHistorySkill(repo);

        // 1. Upsert contact
        HostSkillResult upsertResult = await upsertSkill.ExecuteAsync(
            "{\"name\":\"Bob Smith\",\"channels\":{\"telegram\":\"@bob\",\"email\":\"bob@example.com\"},\"notes\":\"Key architect\"}");
        Assert.True(upsertResult.IsSuccess);

        using var doc = JsonDocument.Parse(upsertResult.OutputJson);
        Guid contactId = doc.RootElement.GetProperty("ContactId").GetGuid();

        // 2. Find contact
        HostSkillResult findResult = await findSkill.ExecuteAsync("{\"query\":\"Bob\"}");
        Assert.True(findResult.IsSuccess);
        Assert.Contains("Bob Smith", findResult.OutputJson);

        // 3. Record interaction
        HostSkillResult intResult = await interactionSkill.ExecuteAsync(
            $"{{\"contactId\":\"{contactId}\",\"channel\":\"telegram\",\"note\":\"Agreed on MVP milestone\"}}");
        Assert.True(intResult.IsSuccess);

        // 4. History
        HostSkillResult histResult = await historySkill.ExecuteAsync($"{{\"contactId\":\"{contactId}\"}}");
        Assert.True(histResult.IsSuccess);
        Assert.Contains("Agreed on MVP milestone", histResult.OutputJson);
    }

    [Fact]
    public async Task Crm_EmptyName_ReturnsValidationError()
    {
        var repo = new InMemoryContactRepository();
        var upsertSkill = new CrmContactUpsertSkill(repo);

        HostSkillResult result = await upsertSkill.ExecuteAsync("{\"name\":\" \"}");
        Assert.False(result.IsSuccess);
        Assert.Equal(SkillResultStatus.ValidationError, result.Status);
    }

    #endregion

    #region Registry Tests

    [Fact]
    public async Task PersonalSkillRegistry_RegistersAll17Skills_AndInvokesViaSkillInvoker()
    {
        var notesRepo = new InMemoryNoteRepository();
        var financeRepo = new InMemoryFinanceRepository();
        var reminderRepo = new InMemoryReminderRepository();
        var contactRepo = new InMemoryContactRepository();

        var registry = new PersonalSkillRegistry(notesRepo, financeRepo, reminderRepo, contactRepo);

        IReadOnlyList<ISkill> skills = registry.GetAllSkills();
        Assert.Equal(17, skills.Count);

        // Проверяем наличие всех ключевых навыков
        Assert.NotNull(registry.FindSkill("notes.create"));
        Assert.NotNull(registry.FindSkill("finance.add_expense"));
        Assert.NotNull(registry.FindSkill("reminders.create"));
        Assert.NotNull(registry.FindSkill("crm.contact_upsert"));

        // Вызов через ISkillInvoker
        ISkillInvoker invoker = registry;
        var request = new SkillInvocationRequest
        {
            SkillId = "notes.create",
            PayloadJson = "{\"title\":\"Invoker Note\",\"content\":\"From ISkillInvoker\"}",
            RunId = RunId.New(),
        };

        SkillInvocationResult result = await invoker.InvokeSkillAsync(request);
        Assert.True(result.IsSuccess);
        Assert.Contains("Invoker Note", result.OutputJson);

        // Вызов неизвестного навыка
        var unknownRequest = new SkillInvocationRequest
        {
            SkillId = "unknown.skill",
            PayloadJson = "{}",
            RunId = RunId.New(),
        };

        SkillInvocationResult unknownResult = await invoker.InvokeSkillAsync(unknownRequest);
        Assert.False(unknownResult.IsSuccess);
        Assert.Equal("PersonalSkill.NotFound", unknownResult.Error?.Code);
    }

    [Fact]
    public void PersonalSkillCatalog_ContainsAll17Descriptors()
    {
        Assert.Equal(17, PersonalSkillCatalog.AllDescriptors.Count);
        foreach (PersonalSkillDescriptor desc in PersonalSkillCatalog.AllDescriptors)
        {
            Assert.False(string.IsNullOrWhiteSpace(desc.SkillId));
            Assert.False(string.IsNullOrWhiteSpace(desc.Name));
            Assert.False(string.IsNullOrWhiteSpace(desc.Description));
            Assert.False(string.IsNullOrWhiteSpace(desc.Category));
            Assert.False(string.IsNullOrWhiteSpace(desc.InputSchemaJson));
        }
    }

    #endregion

    private sealed class ManualTestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTestTimeProvider(DateTimeOffset initialUtcNow)
        {
            _utcNow = initialUtcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
