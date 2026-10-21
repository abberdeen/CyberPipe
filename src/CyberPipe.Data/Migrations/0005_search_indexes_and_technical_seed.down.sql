DELETE FROM kv_settings WHERE key IN ('schema.version', 'schema.quartz_reserved');
DROP INDEX IF EXISTS ix_audit_log_actor;
DROP INDEX IF EXISTS ix_approvals_status_created;
DROP INDEX IF EXISTS ix_chain_runs_chain_status;
DROP INDEX IF EXISTS ix_embeddings_namespace_model;
