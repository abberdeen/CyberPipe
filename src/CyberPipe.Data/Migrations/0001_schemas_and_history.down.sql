-- Down for 0001: drops migration history and the empty reserved quartz schema.
-- app schema is dropped only when empty (earlier rollbacks remove app tables first).
DROP TABLE IF EXISTS app.schema_migrations;
DROP SCHEMA IF EXISTS quartz;
DROP SCHEMA IF EXISTS app;
