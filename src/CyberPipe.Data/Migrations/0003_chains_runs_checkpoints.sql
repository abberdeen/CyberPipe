-- reversible: true
-- TASK-011 migration 0003: chain definitions, runs and checkpoints structure only (logic: TASK-044).
CREATE TABLE IF NOT EXISTS chains (
    id text PRIMARY KEY,
    name text NOT NULL,
    definition_json text NOT NULL,
    is_enabled integer NOT NULL DEFAULT 1,
    requires_hitl integer NOT NULL DEFAULT 0,
    version integer NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP)
);
CREATE INDEX IF NOT EXISTS ix_chains_name ON chains (name);
CREATE INDEX IF NOT EXISTS ix_chains_is_enabled ON chains (is_enabled);

CREATE TABLE IF NOT EXISTS chain_runs (
    id text PRIMARY KEY,
    chain_id text NOT NULL REFERENCES chains (id) ON DELETE CASCADE,
    trigger_event_id text,
    idempotency_key text NOT NULL,
    status text NOT NULL,
    source text NOT NULL,
    payload_json text NOT NULL DEFAULT '{}',
    started_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    finished_at text,
    error_json text,
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT uq_chain_runs_chain_idempotency UNIQUE (chain_id, idempotency_key)
);
CREATE INDEX IF NOT EXISTS ix_chain_runs_chain_id ON chain_runs (chain_id);
CREATE INDEX IF NOT EXISTS ix_chain_runs_status ON chain_runs (status);
CREATE INDEX IF NOT EXISTS ix_chain_runs_started_at ON chain_runs (started_at);

CREATE TABLE IF NOT EXISTS chain_checkpoints (
    id text PRIMARY KEY,
    run_id text NOT NULL REFERENCES chain_runs (id) ON DELETE CASCADE,
    chain_id text NOT NULL REFERENCES chains (id) ON DELETE CASCADE,
    step text NOT NULL,
    state text NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    input_json text,
    output_json text,
    error_json text,
    checkpoint_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT uq_chain_checkpoints_run_step UNIQUE (run_id, step)
);
CREATE INDEX IF NOT EXISTS ix_chain_checkpoints_run_id ON chain_checkpoints (run_id);
CREATE INDEX IF NOT EXISTS ix_chain_checkpoints_chain_id ON chain_checkpoints (chain_id);
