-- reversible: true
-- TASK-046: Human-in-the-loop approvals table for chains
CREATE TABLE IF NOT EXISTS chain_hitl_approvals (
    id text PRIMARY KEY,
    run_id text NOT NULL,
    chain_id text NOT NULL,
    title text NOT NULL,
    preview_text text NOT NULL,
    payload_json text NOT NULL,
    attach_files_json text NOT NULL DEFAULT '[]',
    status text NOT NULL DEFAULT 'Pending',
    resolution_json text,
    created_at text NOT NULL,
    expires_at text NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_chain_hitl_approvals_status_expires ON chain_hitl_approvals (status, expires_at);
CREATE INDEX IF NOT EXISTS ix_chain_hitl_approvals_run_id ON chain_hitl_approvals (run_id);
