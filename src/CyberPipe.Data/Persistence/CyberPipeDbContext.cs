using Microsoft.EntityFrameworkCore;

namespace CyberPipe.Data.Persistence;

/// <summary>
/// Code-first контекст единственной базы CyberPipe. Таблицы и колонки повторяют схему
/// SQL-миграций 0001–0007, внешние ключи и каскады сохранены. Миграции — EF (Migrations/).
/// </summary>
public sealed class CyberPipeDbContext : DbContext
{
    /// <summary>Создаёт контекст поверх готовых опций (connection string + WAL-перехватчик).</summary>
    public CyberPipeDbContext(DbContextOptions<CyberPipeDbContext> options)
        : base(options)
    {
    }

    internal DbSet<EmbeddingEntity> Embeddings => Set<EmbeddingEntity>();
    internal DbSet<SemanticCacheEntity> SemanticCache => Set<SemanticCacheEntity>();
    internal DbSet<ChainEntity> Chains => Set<ChainEntity>();
    internal DbSet<ChainRunEntity> ChainRuns => Set<ChainRunEntity>();
    internal DbSet<CheckpointEntity> Checkpoints => Set<CheckpointEntity>();
    internal DbSet<ApprovalEntity> Approvals => Set<ApprovalEntity>();
    internal DbSet<TokenBudgetEntity> TokenBudgets => Set<TokenBudgetEntity>();
    internal DbSet<SettingEntity> Settings => Set<SettingEntity>();
    internal DbSet<AuditEntity> AuditLog => Set<AuditEntity>();
    internal DbSet<HitlEntity> HitlApprovals => Set<HitlEntity>();
    internal DbSet<PackageEntity> Packages => Set<PackageEntity>();
    internal DbSet<NoteEntity> Notes => Set<NoteEntity>();
    internal DbSet<FinanceEntity> Finance => Set<FinanceEntity>();
    internal DbSet<ReminderEntity> Reminders => Set<ReminderEntity>();
    internal DbSet<ContactEntity> Contacts => Set<ContactEntity>();
    internal DbSet<InteractionEntity> Interactions => Set<InteractionEntity>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTimestampConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableUtcTimestampConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmbeddingEntity>(b =>
        {
            b.ToTable("embeddings");
            b.HasKey(e => e.Id).HasName("PK_embeddings");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Namespace).HasColumnName("namespace").IsRequired();
            b.Property(e => e.SourceRef).HasColumnName("source_ref").IsRequired();
            b.Property(e => e.ModelId).HasColumnName("model_id").IsRequired();
            b.Property(e => e.Dimension).HasColumnName("dimension");
            b.Property(e => e.Embedding).HasColumnName("embedding").IsRequired();
            b.Property(e => e.TextHash).HasColumnName("text_hash").IsRequired();
            b.Property(e => e.Metadata).HasColumnName("metadata").HasDefaultValue("{}");
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.HasIndex(e => e.Namespace).HasDatabaseName("ix_embeddings_namespace");
            b.HasIndex(e => e.ModelId).HasDatabaseName("ix_embeddings_model_id");
            b.HasIndex(e => e.TextHash).HasDatabaseName("ix_embeddings_text_hash");
            b.HasIndex(e => new { e.Namespace, e.ModelId }).HasDatabaseName("ix_embeddings_namespace_model");
            b.HasIndex(e => new { e.Namespace, e.SourceRef, e.ModelId })
                .IsUnique()
                .HasDatabaseName("uq_embeddings_namespace_source_model");
        });

        modelBuilder.Entity<SemanticCacheEntity>(b =>
        {
            b.ToTable("semantic_cache");
            b.HasKey(e => e.Id).HasName("PK_semantic_cache");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.CacheKey).HasColumnName("cache_key").IsRequired();
            b.Property(e => e.ModelId).HasColumnName("model_id").IsRequired();
            b.Property(e => e.Embedding).HasColumnName("embedding");
            b.Property(e => e.ResponseText).HasColumnName("response_text").IsRequired();
            b.Property(e => e.Metadata).HasColumnName("metadata").HasDefaultValue("{}");
            b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.HasIndex(e => e.ModelId).HasDatabaseName("ix_semantic_cache_model_id");
            b.HasIndex(e => e.ExpiresAt).HasDatabaseName("ix_semantic_cache_expires_at");
            b.HasIndex(e => e.CacheKey).IsUnique().HasDatabaseName("uq_semantic_cache_cache_key");
        });

        modelBuilder.Entity<ChainEntity>(b =>
        {
            b.ToTable("chains");
            b.HasKey(e => e.Id).HasName("PK_chains");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Name).HasColumnName("name").IsRequired();
            b.Property(e => e.DefinitionJson).HasColumnName("definition_json").IsRequired();
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);
            b.Property(e => e.RequiresHitl).HasColumnName("requires_hitl").HasDefaultValue(false);
            b.Property(e => e.Version).HasColumnName("version").HasDefaultValue(1);
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.HasIndex(e => e.Name).HasDatabaseName("ix_chains_name");
            b.HasIndex(e => e.IsEnabled).HasDatabaseName("ix_chains_is_enabled");
        });

        modelBuilder.Entity<ChainRunEntity>(b =>
        {
            b.ToTable("chain_runs");
            b.HasKey(e => e.Id).HasName("PK_chain_runs");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.ChainId).HasColumnName("chain_id").IsRequired();
            b.Property(e => e.TriggerEventId).HasColumnName("trigger_event_id");
            b.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").IsRequired();
            b.Property(e => e.Status).HasColumnName("status").IsRequired();
            b.Property(e => e.Source).HasColumnName("source").IsRequired();
            b.Property(e => e.PayloadJson).HasColumnName("payload_json").HasDefaultValue("{}");
            b.Property(e => e.StartedAt).HasColumnName("started_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.FinishedAt).HasColumnName("finished_at");
            b.Property(e => e.ErrorJson).HasColumnName("error_json");
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.HasIndex(e => e.ChainId).HasDatabaseName("ix_chain_runs_chain_id");
            b.HasIndex(e => e.Status).HasDatabaseName("ix_chain_runs_status");
            b.HasIndex(e => e.StartedAt).HasDatabaseName("ix_chain_runs_started_at");
            b.HasIndex(e => new { e.ChainId, e.Status }).HasDatabaseName("ix_chain_runs_chain_status");
            b.HasIndex(e => new { e.ChainId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("uq_chain_runs_chain_idempotency");
            b.HasOne<ChainEntity>().WithMany().HasForeignKey(e => e.ChainId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CheckpointEntity>(b =>
        {
            b.ToTable("chain_checkpoints");
            b.HasKey(e => e.Id).HasName("PK_chain_checkpoints");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.RunId).HasColumnName("run_id").IsRequired();
            b.Property(e => e.ChainId).HasColumnName("chain_id").IsRequired();
            b.Property(e => e.Step).HasColumnName("step").IsRequired();
            b.Property(e => e.State).HasColumnName("state").IsRequired();
            b.Property(e => e.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
            b.Property(e => e.InputJson).HasColumnName("input_json");
            b.Property(e => e.OutputJson).HasColumnName("output_json");
            b.Property(e => e.ErrorJson).HasColumnName("error_json");
            b.Property(e => e.CheckpointAt).HasColumnName("checkpoint_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.HasIndex(e => e.RunId).HasDatabaseName("ix_chain_checkpoints_run_id");
            b.HasIndex(e => e.ChainId).HasDatabaseName("ix_chain_checkpoints_chain_id");
            b.HasIndex(e => new { e.RunId, e.Step }).IsUnique().HasDatabaseName("uq_chain_checkpoints_run_step");
            b.HasOne<ChainRunEntity>().WithMany().HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ChainEntity>().WithMany().HasForeignKey(e => e.ChainId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApprovalEntity>(b =>
        {
            b.ToTable("approvals");
            b.HasKey(e => e.Id).HasName("PK_approvals");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.RunId).HasColumnName("run_id");
            b.Property(e => e.ChainId).HasColumnName("chain_id");
            b.Property(e => e.Step).HasColumnName("step").IsRequired();
            b.Property(e => e.Status).HasColumnName("status").HasDefaultValue("pending");
            b.Property(e => e.PayloadJson).HasColumnName("payload_json").HasDefaultValue("{}");
            b.Property(e => e.Decision).HasColumnName("decision");
            b.Property(e => e.DecidedAt).HasColumnName("decided_at");
            b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP")
                .IsConcurrencyToken();
            b.HasIndex(e => e.RunId).HasDatabaseName("ix_approvals_run_id");
            b.HasIndex(e => e.ChainId).HasDatabaseName("ix_approvals_chain_id");
            b.HasIndex(e => e.Status).HasDatabaseName("ix_approvals_status");
            b.HasIndex(e => new { e.Status, e.CreatedAt }).HasDatabaseName("ix_approvals_status_created");
            b.HasOne<ChainRunEntity>().WithMany().HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ChainEntity>().WithMany().HasForeignKey(e => e.ChainId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TokenBudgetEntity>(b =>
        {
            b.ToTable("token_budget_ledger");
            b.HasKey(e => e.Id).HasName("PK_token_budget_ledger");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.BudgetDate).HasColumnName("budget_date").IsRequired();
            b.Property(e => e.ModelId).HasColumnName("model_id").IsRequired();
            b.Property(e => e.HostingType).HasColumnName("hosting_type").IsRequired();
            b.Property(e => e.EntryType).HasColumnName("entry_type").IsRequired();
            b.Property(e => e.RequestId).HasColumnName("request_id");
            b.Property(e => e.InputTokens).HasColumnName("input_tokens").HasDefaultValue(0);
            b.Property(e => e.OutputTokens).HasColumnName("output_tokens").HasDefaultValue(0);
            b.Property(e => e.CostUsd).HasColumnName("cost_usd").HasDefaultValue(0m);
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP")
                .IsConcurrencyToken();
            b.HasIndex(e => e.BudgetDate).HasDatabaseName("ix_token_budget_ledger_budget_date");
            b.HasIndex(e => e.ModelId).HasDatabaseName("ix_token_budget_ledger_model_id");
            b.HasIndex(e => e.RequestId).HasDatabaseName("ix_token_budget_ledger_request_id");
        });

        modelBuilder.Entity<SettingEntity>(b =>
        {
            b.ToTable("kv_settings");
            b.HasKey(e => e.Key).HasName("PK_kv_settings");
            b.Property(e => e.Key).HasColumnName("key");
            b.Property(e => e.ValueJson).HasColumnName("value_json").IsRequired();
            b.Property(e => e.Description).HasColumnName("description");
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP")
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<AuditEntity>(b =>
        {
            b.ToTable("audit_log");
            b.HasKey(e => e.Id).HasName("PK_audit_log");
            b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(e => e.Actor).HasColumnName("actor").IsRequired();
            b.Property(e => e.Action).HasColumnName("action").IsRequired();
            b.Property(e => e.EntityType).HasColumnName("entity_type");
            b.Property(e => e.EntityId).HasColumnName("entity_id");
            b.Property(e => e.DetailsJson).HasColumnName("details_json").HasDefaultValue("{}");
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_audit_log_created_at");
            b.HasIndex(e => e.Action).HasDatabaseName("ix_audit_log_action");
            b.HasIndex(e => new { e.EntityType, e.EntityId }).HasDatabaseName("ix_audit_log_entity");
            b.HasIndex(e => e.Actor).HasDatabaseName("ix_audit_log_actor");
        });

        modelBuilder.Entity<HitlEntity>(b =>
        {
            b.ToTable("chain_hitl_approvals");
            b.HasKey(e => e.Id).HasName("PK_chain_hitl_approvals");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.RunId).HasColumnName("run_id").IsRequired();
            b.Property(e => e.ChainId).HasColumnName("chain_id").IsRequired();
            b.Property(e => e.Title).HasColumnName("title").IsRequired();
            b.Property(e => e.PreviewText).HasColumnName("preview_text").IsRequired();
            b.Property(e => e.PayloadJson).HasColumnName("payload_json").IsRequired();
            b.Property(e => e.AttachFilesJson).HasColumnName("attach_files_json").HasDefaultValue("[]");
            b.Property(e => e.Status).HasColumnName("status").HasDefaultValue("Pending");
            b.Property(e => e.ResolutionJson).HasColumnName("resolution_json");
            b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
            b.HasIndex(e => new { e.Status, e.ExpiresAt })
                .HasDatabaseName("ix_chain_hitl_approvals_status_expires");
            b.HasIndex(e => e.RunId).HasDatabaseName("ix_chain_hitl_approvals_run_id");
        });

        modelBuilder.Entity<PackageEntity>(b =>
        {
            b.ToTable("netrunner_packages");
            b.HasKey(e => new { e.Slug, e.Version }).HasName("PK_netrunner_packages");
            b.Property(e => e.Slug).HasColumnName("slug");
            b.Property(e => e.Version).HasColumnName("version");
            b.Property(e => e.ManifestHash).HasColumnName("manifest_hash").IsRequired();
            b.Property(e => e.PackageDir).HasColumnName("package_dir").IsRequired();
            b.Property(e => e.ManifestJson).HasColumnName("manifest_json").IsRequired();
            b.Property(e => e.RegisteredAt).HasColumnName("registered_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<NoteEntity>(b =>
        {
            b.ToTable("personal_notes");
            b.HasKey(e => e.Id).HasName("PK_personal_notes");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Title).HasColumnName("title").IsRequired();
            b.Property(e => e.Content).HasColumnName("content").IsRequired();
            b.Property(e => e.Tags).HasColumnName("tags").HasConversion(JsonColumnConversions.StringList)
                .HasDefaultValueSql("'[]'");
            b.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.HasIndex(e => e.UpdatedAt).IsDescending().HasDatabaseName("ix_personal_notes_updated");
        });

        modelBuilder.Entity<FinanceEntity>(b =>
        {
            b.ToTable("personal_finance");
            b.HasKey(e => e.Id).HasName("PK_personal_finance");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Kind).HasColumnName("kind");
            b.Property(e => e.Amount).HasColumnName("amount");
            b.Property(e => e.Currency).HasColumnName("currency").IsRequired();
            b.Property(e => e.Category).HasColumnName("category").IsRequired();
            b.Property(e => e.Description).HasColumnName("description");
            b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.HasIndex(e => e.OccurredAt).IsDescending().HasDatabaseName("ix_personal_finance_occurred");
        });

        modelBuilder.Entity<ReminderEntity>(b =>
        {
            b.ToTable("personal_reminders");
            b.HasKey(e => e.Id).HasName("PK_personal_reminders");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Title).HasColumnName("title").IsRequired();
            b.Property(e => e.DueTime).HasColumnName("due_time");
            b.Property(e => e.CronExpression).HasColumnName("cron_expression");
            b.Property(e => e.Status).HasColumnName("status").HasDefaultValue(0);
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.CompletedAt).HasColumnName("completed_at");
            b.HasIndex(e => new { e.Status, e.DueTime }).HasDatabaseName("ix_personal_reminders_status");
        });

        modelBuilder.Entity<ContactEntity>(b =>
        {
            b.ToTable("personal_contacts");
            b.HasKey(e => e.Id).HasName("PK_personal_contacts");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Name).HasColumnName("name").IsRequired();
            b.Property(e => e.Channels).HasColumnName("channels")
                .HasConversion(JsonColumnConversions.StringMap).HasDefaultValueSql("'{}'");
            b.Property(e => e.Notes).HasColumnName("notes");
            b.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            b.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.HasIndex(e => e.Name).HasDatabaseName("ix_personal_contacts_name");
        });

        modelBuilder.Entity<InteractionEntity>(b =>
        {
            b.ToTable("personal_interactions");
            b.HasKey(e => e.Id).HasName("PK_personal_interactions");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.ContactId).HasColumnName("contact_id").IsRequired();
            b.Property(e => e.Channel).HasColumnName("channel").IsRequired();
            b.Property(e => e.Note).HasColumnName("note").IsRequired();
            b.Property(e => e.OccurredAt).HasColumnName("occurred_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.HasIndex(e => new { e.ContactId, e.OccurredAt }).IsDescending(false, true)
                .HasDatabaseName("ix_personal_interactions_contact");
            b.HasOne<ContactEntity>().WithMany().HasForeignKey(e => e.ContactId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
