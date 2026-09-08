-- Tokens are replaced only by a validated quoted identifier and an escaped fixed URL literal.
CREATE TABLE {{schema}}.schema_version (
    version integer PRIMARY KEY CONSTRAINT schema_version_one CHECK (version = 1),
    applied_at timestamptz NOT NULL
);
CREATE TABLE {{schema}}.refresh_attempts (
    attempt_id uuid PRIMARY KEY,
    sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    started_at timestamptz NOT NULL,
    finished_at timestamptz NULL,
    status text NOT NULL CONSTRAINT attempt_status CHECK (status IN ('running','success','failed','abandoned')),
    error_code text NULL CONSTRAINT attempt_error CHECK (error_code IN
        ('snapshot_malformed','source_rejected','source_timeout','db_unavailable','publication_unknown','cancelled','abandoned')),
    CONSTRAINT attempt_terminal CHECK (
        (status='running' AND finished_at IS NULL AND error_code IS NULL) OR
        (status='success' AND finished_at IS NOT NULL AND error_code IS NULL) OR
        (status IN ('failed','abandoned') AND finished_at IS NOT NULL AND error_code IS NOT NULL)),
    CONSTRAINT attempt_abandoned CHECK (status <> 'abandoned' OR error_code = 'abandoned')
);
CREATE TABLE {{schema}}.snapshots (
    snapshot_id uuid PRIMARY KEY,
    attempt_id uuid UNIQUE NOT NULL REFERENCES {{schema}}.refresh_attempts(attempt_id),
    payload jsonb NOT NULL CONSTRAINT snapshot_payload CHECK (jsonb_typeof(payload)='object'),
    original_xml bytea NOT NULL CONSTRAINT snapshot_bytes CHECK (octet_length(original_xml) BETWEEN 1 AND 16777216),
    source_kind text NOT NULL CONSTRAINT snapshot_kind CHECK (source_kind IN ('file','http')),
    source_url text NULL,
    source_sha256 text NOT NULL CONSTRAINT snapshot_hash CHECK (source_sha256 ~ '^[0-9a-f]{64}$'),
    fetched_at timestamptz NOT NULL,
    published_at timestamptz NOT NULL,
    source_modified_at timestamptz NULL,
    CONSTRAINT snapshot_provenance CHECK (
        (source_kind='file' AND source_url IS NULL AND source_modified_at IS NULL) OR
        (source_kind='http' AND source_url IS NOT NULL AND source_url={{url}}))
);
CREATE TABLE {{schema}}.state (
    singleton boolean PRIMARY KEY CONSTRAINT state_singleton CHECK (singleton),
    current_snapshot_id uuid NULL REFERENCES {{schema}}.snapshots(snapshot_id)
);
INSERT INTO {{schema}}.schema_version(version,applied_at) VALUES(1, CURRENT_TIMESTAMP);
INSERT INTO {{schema}}.state(singleton,current_snapshot_id) VALUES(true,NULL);
