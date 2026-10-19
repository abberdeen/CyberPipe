-- reversible: true
-- TASK-046: Human-in-the-loop approvals table for chains
CREATE TABLE IF NOT EXISTS app.chain_hitl_approvals (
    id uuid PRIMARY KEY,
    run_id uuid NOT NULL,
    chain_id uuid NOT NULL,
    title text NOT NULL,
    preview_text text NOT NULL,
    payload_json jsonb NOT NULL,
    attach_files_json jsonb NOT NULL DEFAULT '[]'::jsonb,
    status text NOT NULL DEFAULT 'Pending',
    resolution_json jsonb,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_chain_hitl_approvals_status_expires ON app.chain_hitl_approvals (status, expires_at);
CREATE INDEX IF NOT EXISTS ix_chain_hitl_approvals_run_id ON app.chain_hitl_approvals (run_id);
