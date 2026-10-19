-- Down for 0005: drops only objects created by 0005 and its technical seed rows.
-- Rows in app.chains, app.chain_runs and other tables from earlier migrations are preserved.
DELETE FROM app.kv_settings WHERE key IN ('schema.version', 'schema.quartz_reserved');
DROP INDEX IF EXISTS app.ix_audit_log_actor;
DROP INDEX IF EXISTS app.ix_approvals_status_created;
DROP INDEX IF EXISTS app.ix_chain_runs_chain_status;
DROP INDEX IF EXISTS app.ix_embeddings_namespace_model;
