-- reversible: true
-- TASK-011 migration 0005: additional search indexes and technical seed only (no secrets, no user data).
CREATE INDEX IF NOT EXISTS ix_embeddings_namespace_model ON app.embeddings (namespace, model_id);
CREATE INDEX IF NOT EXISTS ix_chain_runs_chain_status ON app.chain_runs (chain_id, status);
CREATE INDEX IF NOT EXISTS ix_approvals_status_created ON app.approvals (status, created_at);
CREATE INDEX IF NOT EXISTS ix_audit_log_actor ON app.audit_log (actor);
INSERT INTO app.kv_settings (key, value_json, description)
VALUES
    ('schema.version', '"5"'::jsonb, 'Current schema migration version (TASK-011).'),
    ('schema.quartz_reserved', 'true'::jsonb, 'Schema quartz is reserved empty for TASK-045.')
ON CONFLICT (key) DO NOTHING;
