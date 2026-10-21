-- reversible: true
-- FIX-03: Netrunner package registry and personal skills persistence (TASK-042, TASK-051).
CREATE TABLE IF NOT EXISTS netrunner_packages (
    slug text NOT NULL,
    version text NOT NULL,
    manifest_hash text NOT NULL,
    package_dir text NOT NULL,
    manifest_json text NOT NULL,
    registered_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    PRIMARY KEY (slug, version)
);

CREATE TABLE IF NOT EXISTS personal_notes (
    id text PRIMARY KEY,
    title text NOT NULL,
    content text NOT NULL,
    tags text NOT NULL DEFAULT '[]',
    is_deleted integer NOT NULL DEFAULT 0,
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP)
);
CREATE INDEX IF NOT EXISTS ix_personal_notes_updated ON personal_notes (updated_at DESC);

CREATE TABLE IF NOT EXISTS personal_finance (
    id text PRIMARY KEY,
    kind integer NOT NULL,
    amount real NOT NULL,
    currency text NOT NULL,
    category text NOT NULL,
    description text,
    occurred_at text NOT NULL,
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP)
);
CREATE INDEX IF NOT EXISTS ix_personal_finance_occurred ON personal_finance (occurred_at DESC);

CREATE TABLE IF NOT EXISTS personal_reminders (
    id text PRIMARY KEY,
    title text NOT NULL,
    due_time text,
    cron_expression text,
    status integer NOT NULL DEFAULT 0,
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    completed_at text
);
CREATE INDEX IF NOT EXISTS ix_personal_reminders_status ON personal_reminders (status, due_time);

CREATE TABLE IF NOT EXISTS personal_contacts (
    id text PRIMARY KEY,
    name text NOT NULL,
    channels text NOT NULL DEFAULT '{}',
    notes text,
    is_deleted integer NOT NULL DEFAULT 0,
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP)
);
CREATE INDEX IF NOT EXISTS ix_personal_contacts_name ON personal_contacts (lower(name));

CREATE TABLE IF NOT EXISTS personal_interactions (
    id text PRIMARY KEY,
    contact_id text NOT NULL REFERENCES personal_contacts (id) ON DELETE CASCADE,
    channel text NOT NULL,
    note text NOT NULL,
    occurred_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP)
);
CREATE INDEX IF NOT EXISTS ix_personal_interactions_contact ON personal_interactions (contact_id, occurred_at DESC);
