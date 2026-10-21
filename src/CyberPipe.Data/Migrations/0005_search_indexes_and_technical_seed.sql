-- reversible: true
-- TASK-011 migration 0005: additional search indexes and technical seed only (no secrets, no user data).
CREATE INDEX IF NOT EXISTS ix_embeddings_namespace_model ON embeddings (namespace, model_id);
CREATE INDEX IF NOT EXISTS ix_chain_runs_chain_status ON chain_runs (chain_id, status);
CREATE INDEX IF NOT EXISTS ix_approvals_status_created ON approvals (status, created_at);
CREATE INDEX IF NOT EXISTS ix_audit_log_actor ON audit_log (actor);
INSERT INTO kv_settings (key, value_json, description)
VALUES
    ('schema.version', '"5"', 'Current schema migration version (TASK-011).'),
    ('schema.quartz_reserved', 'true', 'Reserved name from the PostgreSQL era; unused on SQLite.')
ON CONFLICT (key) DO NOTHING;
