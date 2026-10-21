using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CyberPipe.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    actor = table.Column<string>(type: "TEXT", nullable: false),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    entity_type = table.Column<string>(type: "TEXT", nullable: true),
                    entity_id = table.Column<string>(type: "TEXT", nullable: true),
                    details_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chain_hitl_approvals",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    run_id = table.Column<string>(type: "TEXT", nullable: false),
                    chain_id = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    preview_text = table.Column<string>(type: "TEXT", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    attach_files_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Pending"),
                    resolution_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chain_hitl_approvals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chains",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    definition_json = table.Column<string>(type: "TEXT", nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    requires_hitl = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chains", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "embeddings",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    @namespace = table.Column<string>(name: "namespace", type: "TEXT", nullable: false),
                    source_ref = table.Column<string>(type: "TEXT", nullable: false),
                    model_id = table.Column<string>(type: "TEXT", nullable: false),
                    dimension = table.Column<int>(type: "INTEGER", nullable: false),
                    embedding = table.Column<string>(type: "TEXT", nullable: false),
                    text_hash = table.Column<string>(type: "TEXT", nullable: false),
                    metadata = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embeddings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kv_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value_json = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kv_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "netrunner_packages",
                columns: table => new
                {
                    slug = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<string>(type: "TEXT", nullable: false),
                    manifest_hash = table.Column<string>(type: "TEXT", nullable: false),
                    package_dir = table.Column<string>(type: "TEXT", nullable: false),
                    manifest_json = table.Column<string>(type: "TEXT", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_netrunner_packages", x => new { x.slug, x.version });
                });

            migrationBuilder.CreateTable(
                name: "personal_contacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    channels = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'{}'"),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "personal_finance",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: false),
                    category = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_finance", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "personal_notes",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    tags = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'[]'"),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "personal_reminders",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    due_time = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    cron_expression = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_reminders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "semantic_cache",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    cache_key = table.Column<string>(type: "TEXT", nullable: false),
                    model_id = table.Column<string>(type: "TEXT", nullable: false),
                    embedding = table.Column<string>(type: "TEXT", nullable: true),
                    response_text = table.Column<string>(type: "TEXT", nullable: false),
                    metadata = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    expires_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_semantic_cache", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "token_budget_ledger",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    budget_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    model_id = table.Column<string>(type: "TEXT", nullable: false),
                    hosting_type = table.Column<string>(type: "TEXT", nullable: false),
                    entry_type = table.Column<string>(type: "TEXT", nullable: false),
                    request_id = table.Column<string>(type: "TEXT", nullable: true),
                    input_tokens = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    output_tokens = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    cost_usd = table.Column<decimal>(type: "TEXT", nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_token_budget_ledger", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chain_runs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    chain_id = table.Column<string>(type: "TEXT", nullable: false),
                    trigger_event_id = table.Column<string>(type: "TEXT", nullable: true),
                    idempotency_key = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    started_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    finished_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    error_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chain_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_chain_runs_chains_chain_id",
                        column: x => x.chain_id,
                        principalTable: "chains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "personal_interactions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    contact_id = table.Column<string>(type: "TEXT", nullable: false),
                    channel = table.Column<string>(type: "TEXT", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_interactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_personal_interactions_personal_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "personal_contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approvals",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    run_id = table.Column<string>(type: "TEXT", nullable: true),
                    chain_id = table.Column<string>(type: "TEXT", nullable: true),
                    step = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    decision = table.Column<string>(type: "TEXT", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approvals", x => x.id);
                    table.ForeignKey(
                        name: "FK_approvals_chain_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "chain_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_approvals_chains_chain_id",
                        column: x => x.chain_id,
                        principalTable: "chains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chain_checkpoints",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    run_id = table.Column<string>(type: "TEXT", nullable: false),
                    chain_id = table.Column<string>(type: "TEXT", nullable: false),
                    step = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    attempt_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    input_json = table.Column<string>(type: "TEXT", nullable: true),
                    output_json = table.Column<string>(type: "TEXT", nullable: true),
                    error_json = table.Column<string>(type: "TEXT", nullable: true),
                    checkpoint_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chain_checkpoints", x => x.id);
                    table.ForeignKey(
                        name: "FK_chain_checkpoints_chain_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "chain_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chain_checkpoints_chains_chain_id",
                        column: x => x.chain_id,
                        principalTable: "chains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approvals_chain_id",
                table: "approvals",
                column: "chain_id");

            migrationBuilder.CreateIndex(
                name: "ix_approvals_run_id",
                table: "approvals",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_approvals_status",
                table: "approvals",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_approvals_status_created",
                table: "approvals",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_action",
                table: "audit_log",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor",
                table: "audit_log",
                column: "actor");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_created_at",
                table: "audit_log",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chain_checkpoints_chain_id",
                table: "chain_checkpoints",
                column: "chain_id");

            migrationBuilder.CreateIndex(
                name: "ix_chain_checkpoints_run_id",
                table: "chain_checkpoints",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "uq_chain_checkpoints_run_step",
                table: "chain_checkpoints",
                columns: new[] { "run_id", "step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chain_hitl_approvals_run_id",
                table: "chain_hitl_approvals",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_chain_hitl_approvals_status_expires",
                table: "chain_hitl_approvals",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chain_runs_chain_id",
                table: "chain_runs",
                column: "chain_id");

            migrationBuilder.CreateIndex(
                name: "ix_chain_runs_chain_status",
                table: "chain_runs",
                columns: new[] { "chain_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_chain_runs_started_at",
                table: "chain_runs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_chain_runs_status",
                table: "chain_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "uq_chain_runs_chain_idempotency",
                table: "chain_runs",
                columns: new[] { "chain_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chains_is_enabled",
                table: "chains",
                column: "is_enabled");

            migrationBuilder.CreateIndex(
                name: "ix_chains_name",
                table: "chains",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_embeddings_model_id",
                table: "embeddings",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_embeddings_namespace",
                table: "embeddings",
                column: "namespace");

            migrationBuilder.CreateIndex(
                name: "ix_embeddings_namespace_model",
                table: "embeddings",
                columns: new[] { "namespace", "model_id" });

            migrationBuilder.CreateIndex(
                name: "ix_embeddings_text_hash",
                table: "embeddings",
                column: "text_hash");

            migrationBuilder.CreateIndex(
                name: "uq_embeddings_namespace_source_model",
                table: "embeddings",
                columns: new[] { "namespace", "source_ref", "model_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_personal_contacts_name",
                table: "personal_contacts",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_personal_finance_occurred",
                table: "personal_finance",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_personal_interactions_contact",
                table: "personal_interactions",
                columns: new[] { "contact_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_personal_notes_updated",
                table: "personal_notes",
                column: "updated_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_personal_reminders_status",
                table: "personal_reminders",
                columns: new[] { "status", "due_time" });

            migrationBuilder.CreateIndex(
                name: "ix_semantic_cache_expires_at",
                table: "semantic_cache",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_semantic_cache_model_id",
                table: "semantic_cache",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "uq_semantic_cache_cache_key",
                table: "semantic_cache",
                column: "cache_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_token_budget_ledger_budget_date",
                table: "token_budget_ledger",
                column: "budget_date");

            migrationBuilder.CreateIndex(
                name: "ix_token_budget_ledger_model_id",
                table: "token_budget_ledger",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_budget_ledger_request_id",
                table: "token_budget_ledger",
                column: "request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approvals");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "chain_checkpoints");

            migrationBuilder.DropTable(
                name: "chain_hitl_approvals");

            migrationBuilder.DropTable(
                name: "embeddings");

            migrationBuilder.DropTable(
                name: "kv_settings");

            migrationBuilder.DropTable(
                name: "netrunner_packages");

            migrationBuilder.DropTable(
                name: "personal_finance");

            migrationBuilder.DropTable(
                name: "personal_interactions");

            migrationBuilder.DropTable(
                name: "personal_notes");

            migrationBuilder.DropTable(
                name: "personal_reminders");

            migrationBuilder.DropTable(
                name: "semantic_cache");

            migrationBuilder.DropTable(
                name: "token_budget_ledger");

            migrationBuilder.DropTable(
                name: "chain_runs");

            migrationBuilder.DropTable(
                name: "personal_contacts");

            migrationBuilder.DropTable(
                name: "chains");
        }
    }
}
