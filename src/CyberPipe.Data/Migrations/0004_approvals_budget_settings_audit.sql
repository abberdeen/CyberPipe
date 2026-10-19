-- reversible: true
-- TASK-011 migration 0004: approvals (TASK-046), token budget ledger (TASK-033), kv settings, audit log.
CREATE TABLE IF NOT EXISTS app.approvals (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id uuid REFERENCES app.chain_runs (id) ON DELETE CASCADE,
    chain_id uuid REFERENCES app.chains (id) ON DELETE CASCADE,
    step text NOT NULL,
    status text NOT NULL DEFAULT 'pending',
    payload_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    decision text,
    decided_at timestamptz,
    expires_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_approvals_run_id ON app.approvals (run_id);
CREATE INDEX IF NOT EXISTS ix_approvals_chain_id ON app.approvals (chain_id);
CREATE INDEX IF NOT EXISTS ix_approvals_status ON app.approvals (status);

CREATE TABLE IF NOT EXISTS app.token_budget_ledger (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    budget_date date NOT NULL,
    model_id text NOT NULL,
    hosting_type text NOT NULL,
    entry_type text NOT NULL,
    request_id text,
    input_tokens integer NOT NULL DEFAULT 0 CHECK (input_tokens >= 0),
    output_tokens integer NOT NULL DEFAULT 0 CHECK (output_tokens >= 0),
    cost_usd numeric(12, 6) NOT NULL DEFAULT 0 CHECK (cost_usd >= 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_token_budget_ledger_budget_date ON app.token_budget_ledger (budget_date);
CREATE INDEX IF NOT EXISTS ix_token_budget_ledger_model_id ON app.token_budget_ledger (model_id);
CREATE INDEX IF NOT EXISTS ix_token_budget_ledger_request_id ON app.token_budget_ledger (request_id);

CREATE TABLE IF NOT EXISTS app.kv_settings (
    key text PRIMARY KEY,
    value_json jsonb NOT NULL,
    description text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS app.audit_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor text NOT NULL,
    action text NOT NULL,
    entity_type text,
    entity_id text,
    details_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_audit_log_created_at ON app.audit_log (created_at);
CREATE INDEX IF NOT EXISTS ix_audit_log_action ON app.audit_log (action);
CREATE INDEX IF NOT EXISTS ix_audit_log_entity ON app.audit_log (entity_type, entity_id);
