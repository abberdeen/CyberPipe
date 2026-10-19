-- reversible: true
-- TASK-011 migration 0003: chain definitions, runs and checkpoints structure only (logic: TASK-044).
CREATE TABLE IF NOT EXISTS app.chains (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    definition_json jsonb NOT NULL,
    is_enabled boolean NOT NULL DEFAULT true,
    requires_hitl boolean NOT NULL DEFAULT false,
    version integer NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_chains_name ON app.chains (name);
CREATE INDEX IF NOT EXISTS ix_chains_is_enabled ON app.chains (is_enabled);

CREATE TABLE IF NOT EXISTS app.chain_runs (
    id uuid PRIMARY KEY,
    chain_id uuid NOT NULL REFERENCES app.chains (id) ON DELETE CASCADE,
    trigger_event_id uuid,
    idempotency_key text NOT NULL,
    status text NOT NULL,
    source text NOT NULL,
    payload_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    started_at timestamptz NOT NULL DEFAULT now(),
    finished_at timestamptz,
    error_json jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_chain_runs_chain_idempotency UNIQUE (chain_id, idempotency_key)
);
CREATE INDEX IF NOT EXISTS ix_chain_runs_chain_id ON app.chain_runs (chain_id);
CREATE INDEX IF NOT EXISTS ix_chain_runs_status ON app.chain_runs (status);
CREATE INDEX IF NOT EXISTS ix_chain_runs_started_at ON app.chain_runs (started_at);

CREATE TABLE IF NOT EXISTS app.chain_checkpoints (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id uuid NOT NULL REFERENCES app.chain_runs (id) ON DELETE CASCADE,
    chain_id uuid NOT NULL REFERENCES app.chains (id) ON DELETE CASCADE,
    step text NOT NULL,
    state text NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    input_json jsonb,
    output_json jsonb,
    error_json jsonb,
    checkpoint_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_chain_checkpoints_run_step UNIQUE (run_id, step)
);
CREATE INDEX IF NOT EXISTS ix_chain_checkpoints_run_id ON app.chain_checkpoints (run_id);
CREATE INDEX IF NOT EXISTS ix_chain_checkpoints_chain_id ON app.chain_checkpoints (chain_id);
