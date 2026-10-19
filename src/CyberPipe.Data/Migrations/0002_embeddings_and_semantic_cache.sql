-- reversible: true
-- TASK-011 migration 0002: embeddings metadata + vector and semantic cache structure.
-- Vector dimension is NOT fixed here: dimension column records the model dimension from TASK-012
-- configuration, and the vector column intentionally has no fixed dimension. No ANN index here (TASK-012).
CREATE TABLE IF NOT EXISTS app.embeddings (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    namespace text NOT NULL,
    source_ref text NOT NULL,
    model_id text NOT NULL,
    dimension integer NOT NULL CHECK (dimension > 0),
    embedding vector NOT NULL,
    text_hash text NOT NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_embeddings_namespace_source_model UNIQUE (namespace, source_ref, model_id)
);
CREATE INDEX IF NOT EXISTS ix_embeddings_namespace ON app.embeddings (namespace);
CREATE INDEX IF NOT EXISTS ix_embeddings_model_id ON app.embeddings (model_id);
CREATE INDEX IF NOT EXISTS ix_embeddings_text_hash ON app.embeddings (text_hash);

CREATE TABLE IF NOT EXISTS app.semantic_cache (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    cache_key text NOT NULL,
    model_id text NOT NULL,
    embedding vector,
    response_text text NOT NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    expires_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_semantic_cache_cache_key UNIQUE (cache_key)
);
CREATE INDEX IF NOT EXISTS ix_semantic_cache_model_id ON app.semantic_cache (model_id);
CREATE INDEX IF NOT EXISTS ix_semantic_cache_expires_at ON app.semantic_cache (expires_at);
