namespace CyberPipe.Skills.Personal;

/// <summary>
/// Описание метаданных персонального навыка (TASK-051).
/// </summary>
public sealed record PersonalSkillDescriptor
{
    /// <summary>Идентификатор навыка (например, "notes.create", "finance.add_expense").</summary>
    public required string SkillId { get; init; }

    /// <summary>Человекочитаемое название навыка.</summary>
    public required string Name { get; init; }

    /// <summary>Описание для семантического роутера и подсказок LLM.</summary>
    public required string Description { get; init; }

    /// <summary>Категория навыка ("notes", "finance", "reminders", "crm").</summary>
    public required string Category { get; init; }

    /// <summary>JSON-схема входных параметров.</summary>
    public string InputSchemaJson { get; init; } = "{}";
}

/// <summary>
/// Каталог описаний персональных навыков владельца (TASK-051).
/// </summary>
public static class PersonalSkillCatalog
{
    /// <summary>Все стандартные дескрипторы персональных навыков.</summary>
    public static readonly IReadOnlyList<PersonalSkillDescriptor> AllDescriptors =
    [
        // Notes
        new()
        {
            SkillId = "notes.create",
            Name = "Create Note",
            Description = "Creates a new note with title, content, and optional tags.",
            Category = "notes",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"title\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"},\"tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}}",
        },
        new()
        {
            SkillId = "notes.search",
            Name = "Search Notes",
            Description = "Searches notes by text in title, content, or tags.",
            Category = "notes",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"limit\":{\"type\":\"integer\"}}}",
        },
        new()
        {
            SkillId = "notes.get",
            Name = "Get Note",
            Description = "Gets a single note by its GUID.",
            Category = "notes",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"noteId\":{\"type\":\"string\",\"format\":\"uuid\"}},\"required\":[\"noteId\"]}",
        },
        new()
        {
            SkillId = "notes.update",
            Name = "Update Note",
            Description = "Updates an existing note's title, content, or tags.",
            Category = "notes",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"noteId\":{\"type\":\"string\",\"format\":\"uuid\"},\"title\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"},\"tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"noteId\"]}",
        },
        new()
        {
            SkillId = "notes.delete",
            Name = "Delete Note",
            Description = "Deletes a note by its GUID.",
            Category = "notes",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"noteId\":{\"type\":\"string\",\"format\":\"uuid\"}},\"required\":[\"noteId\"]}",
        },

        // Finance
        new()
        {
            SkillId = "finance.add_expense",
            Name = "Add Expense",
            Description = "Records an expense with amount, currency (RUB/USD/EUR), category, and description.",
            Category = "finance",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"amount\":{\"type\":\"number\",\"minimum\":0.01},\"currency\":{\"type\":\"string\"},\"category\":{\"type\":\"string\"},\"description\":{\"type\":\"string\"}},\"required\":[\"amount\"]}",
        },
        new()
        {
            SkillId = "finance.add_income",
            Name = "Add Income",
            Description = "Records an income with amount, currency (RUB/USD/EUR), category, and description.",
            Category = "finance",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"amount\":{\"type\":\"number\",\"minimum\":0.01},\"currency\":{\"type\":\"string\"},\"category\":{\"type\":\"string\"},\"description\":{\"type\":\"string\"}},\"required\":[\"amount\"]}",
        },
        new()
        {
            SkillId = "finance.balance",
            Name = "Get Balance",
            Description = "Calculates total net balance for a specified currency.",
            Category = "finance",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"currency\":{\"type\":\"string\"}}}",
        },
        new()
        {
            SkillId = "finance.report",
            Name = "Finance Report",
            Description = "Generates a categorized spending and income report for a specified date range.",
            Category = "finance",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"from\":{\"type\":\"string\",\"format\":\"date-time\"},\"to\":{\"type\":\"string\",\"format\":\"date-time\"},\"currency\":{\"type\":\"string\"}}}",
        },

        // Reminders
        new()
        {
            SkillId = "reminders.create",
            Name = "Create Reminder",
            Description = "Creates a future reminder at a specified due date/time or Cron schedule.",
            Category = "reminders",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"title\":{\"type\":\"string\"},\"dueTime\":{\"type\":\"string\",\"format\":\"date-time\"},\"cron\":{\"type\":\"string\"}},\"required\":[\"title\"]}",
        },
        new()
        {
            SkillId = "reminders.list",
            Name = "List Reminders",
            Description = "Lists reminders with optional status filter (Pending, Completed, Cancelled).",
            Category = "reminders",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"status\":{\"type\":\"string\",\"enum\":[\"Pending\",\"Completed\",\"Cancelled\"]}}}",
        },
        new()
        {
            SkillId = "reminders.cancel",
            Name = "Cancel Reminder",
            Description = "Cancels a pending reminder by GUID.",
            Category = "reminders",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"reminderId\":{\"type\":\"string\",\"format\":\"uuid\"}},\"required\":[\"reminderId\"]}",
        },
        new()
        {
            SkillId = "reminders.complete",
            Name = "Complete Reminder",
            Description = "Marks a pending reminder as completed by GUID.",
            Category = "reminders",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"reminderId\":{\"type\":\"string\",\"format\":\"uuid\"}},\"required\":[\"reminderId\"]}",
        },

        // CRM
        new()
        {
            SkillId = "crm.contact_upsert",
            Name = "Upsert Contact",
            Description = "Creates or updates contact details, communication channels, and notes.",
            Category = "crm",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"channels\":{\"type\":\"object\"},\"notes\":{\"type\":\"string\"},\"contactId\":{\"type\":\"string\",\"format\":\"uuid\"}},\"required\":[\"name\"]}",
        },
        new()
        {
            SkillId = "crm.find",
            Name = "Find Contact",
            Description = "Finds contacts by name or communication channel.",
            Category = "crm",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}}}",
        },
        new()
        {
            SkillId = "crm.interaction_add",
            Name = "Add CRM Interaction",
            Description = "Records an interaction with a contact.",
            Category = "crm",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"contactId\":{\"type\":\"string\",\"format\":\"uuid\"},\"channel\":{\"type\":\"string\"},\"note\":{\"type\":\"string\"}},\"required\":[\"contactId\",\"note\"]}",
        },
        new()
        {
            SkillId = "crm.history",
            Name = "CRM History",
            Description = "Lists interaction history for a contact.",
            Category = "crm",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"contactId\":{\"type\":\"string\",\"format\":\"uuid\"},\"limit\":{\"type\":\"integer\"}},\"required\":[\"contactId\"]}",
        },
    ];
}
