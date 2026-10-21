-- reversible: true
-- TASK-011 migration 0001: migration history (SQLite: single main schema, no extensions).
CREATE TABLE IF NOT EXISTS schema_migrations (
    version integer PRIMARY KEY,
    applied_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    checksum text NOT NULL
);
