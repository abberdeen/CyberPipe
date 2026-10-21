-- reversible: true
-- TASK-011 migration 0002: embeddings metadata + vector and semantic cache structure.
-- Vector dimension is NOT fixed here: dimension column records the model dimension from TASK-012
-- configuration, and the embedding column intentionally has no fixed type. No ANN index here (TASK-012).
-- SQLite stores the vector as TEXT (JSON array); similarity is computed in C#.
CREATE TABLE IF NOT EXISTS embeddings (
    id text PRIMARY KEY,
    namespace text NOT NULL,
    source_ref text NOT NULL,
    model_id text NOT NULL,
    dimension integer NOT NULL CHECK (dimension > 0),
    embedding text NOT NULL,
    text_hash text NOT NULL,
    metadata text NOT NULL DEFAULT '{}',
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT uq_embeddings_namespace_source_model UNIQUE (namespace, source_ref, model_id)
);
CREATE INDEX IF NOT EXISTS ix_embeddings_namespace ON embeddings (namespace);
CREATE INDEX IF NOT EXISTS ix_embeddings_model_id ON embeddings (model_id);
CREATE INDEX IF NOT EXISTS ix_embeddings_text_hash ON embeddings (text_hash);

CREATE TABLE IF NOT EXISTS semantic_cache (
    id text PRIMARY KEY,
    cache_key text NOT NULL,
    model_id text NOT NULL,
    embedding text,
    response_text text NOT NULL,
    metadata text NOT NULL DEFAULT '{}',
    expires_at text,
    created_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_at text NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT uq_semantic_cache_cache_key UNIQUE (cache_key)
);
CREATE INDEX IF NOT EXISTS ix_semantic_cache_model_id ON semantic_cache (model_id);
CREATE INDEX IF NOT EXISTS ix_semantic_cache_expires_at ON semantic_cache (expires_at);
