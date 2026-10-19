-- reversible: true
-- TASK-011 migration 0001: schemas app/quartz and migration history.
-- quartz is created EMPTY here and reserved for Quartz.NET tables in TASK-045; no quartz tables in TASK-011.
CREATE SCHEMA IF NOT EXISTS app;
CREATE SCHEMA IF NOT EXISTS quartz;
COMMENT ON SCHEMA app IS 'CyberPipe application data (TASK-011).';
COMMENT ON SCHEMA quartz IS 'Reserved for Quartz.NET tables, created by TASK-045. Created empty by TASK-011.';
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE IF NOT EXISTS app.schema_migrations (
    version integer PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now(),
    checksum text NOT NULL
);
