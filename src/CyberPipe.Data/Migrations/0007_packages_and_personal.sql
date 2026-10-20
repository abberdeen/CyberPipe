-- reversible: true
-- FIX-03: Netrunner package registry and personal skills persistence (TASK-042, TASK-051).
CREATE TABLE IF NOT EXISTS app.netrunner_packages (
    slug text NOT NULL,
    version text NOT NULL,
    manifest_hash text NOT NULL,
    package_dir text NOT NULL,
    manifest_json jsonb NOT NULL,
    registered_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (slug, version)
);

CREATE TABLE IF NOT EXISTS app.personal_notes (
    id uuid PRIMARY KEY,
    title text NOT NULL,
    content text NOT NULL,
    tags text[] NOT NULL DEFAULT '{}',
    is_deleted boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_personal_notes_updated ON app.personal_notes (updated_at DESC);

CREATE TABLE IF NOT EXISTS app.personal_finance (
    id uuid PRIMARY KEY,
    kind integer NOT NULL,
    amount numeric(18,4) NOT NULL,
    currency text NOT NULL,
    category text NOT NULL,
    description text,
    occurred_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_personal_finance_occurred ON app.personal_finance (occurred_at DESC);

CREATE TABLE IF NOT EXISTS app.personal_reminders (
    id uuid PRIMARY KEY,
    title text NOT NULL,
    due_time timestamptz,
    cron_expression text,
    status integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz
);
CREATE INDEX IF NOT EXISTS ix_personal_reminders_status ON app.personal_reminders (status, due_time);

CREATE TABLE IF NOT EXISTS app.personal_contacts (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    channels jsonb NOT NULL DEFAULT '{}'::jsonb,
    notes text,
    is_deleted boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_personal_contacts_name ON app.personal_contacts (lower(name));

CREATE TABLE IF NOT EXISTS app.personal_interactions (
    id uuid PRIMARY KEY,
    contact_id uuid NOT NULL REFERENCES app.personal_contacts (id) ON DELETE CASCADE,
    channel text NOT NULL,
    note text NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_personal_interactions_contact ON app.personal_interactions (contact_id, occurred_at DESC);
