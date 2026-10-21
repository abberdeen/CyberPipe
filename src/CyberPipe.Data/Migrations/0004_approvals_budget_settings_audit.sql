-- reversible: true
-- TASK-011 migration 0004: approvals (TASK-046), token budget ledger (TASK-033), kv settings, audit log.
CREATE TABLE IF NOT EXISTS approvals (
    id text PRIMARY KEY,
    run_id text REFERENCES chain_runs (id) ON DELETE CASCADE,
    chain_id text REFERENCES chains (id) ON DELETE CASCADE,
    step text NOT NULL,
    status text NOT NULL DEFAULT 'pending',
    payload_json text NOT NULL DEFAULT '{}',
    decision text,
    decided_at text,
    expires_at text,
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP)
);
CREATE INDEX IF NOT EXISTS ix_approvals_run_id ON approvals (run_id);
CREATE INDEX IF NOT EXISTS ix_approvals_chain_id ON approvals (chain_id);
CREATE INDEX IF NOT EXISTS ix_approvals_status ON approvals (status);

CREATE TABLE IF NOT EXISTS token_budget_ledger (
    id text PRIMARY KEY,
    budget_date text NOT NULL,
    model_id text NOT NULL,
    hosting_type text NOT NULL,
    entry_type text NOT NULL,
    request_id text,
    input_tokens integer NOT NULL DEFAULT 0 CHECK (input_tokens >= 0),
    output_tokens integer NOT NULL DEFAULT 0 CHECK (output_tokens >= 0),
    cost_usd real NOT NULL DEFAULT 0 CHECK (cost_usd >= 0),
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP)
);
CREATE INDEX IF NOT EXISTS ix_token_budget_ledger_budget_date ON token_budget_ledger (budget_date);
CREATE INDEX IF NOT EXISTS ix_token_budget_ledger_model_id ON token_budget_ledger (model_id);
CREATE INDEX IF NOT EXISTS ix_token_budget_ledger_request_id ON token_budget_ledger (request_id);

CREATE TABLE IF NOT EXISTS kv_settings (
    key text PRIMARY KEY,
    value_json text NOT NULL,
    description text,
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP)
);

CREATE TABLE IF NOT EXISTS audit_log (
    id integer PRIMARY KEY AUTOINCREMENT,
    actor text NOT NULL,
    action text NOT NULL,
    entity_type text,
    entity_id text,
    details_json text NOT NULL DEFAULT '{}',
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP)
);
CREATE INDEX IF NOT EXISTS ix_audit_log_created_at ON audit_log (created_at);
CREATE INDEX IF NOT EXISTS ix_audit_log_action ON audit_log (action);
CREATE INDEX IF NOT EXISTS ix_audit_log_entity ON audit_log (entity_type, entity_id);
