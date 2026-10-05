CREATE TABLE __SYNC__.schema_migrations (
    version integer PRIMARY KEY CHECK (version > 0),
    checksum text NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'),
    applied_at timestamptz NOT NULL
);
CREATE TABLE __SYNC__.sync_state (
    user_id uuid PRIMARY KEY REFERENCES __ACCOUNTS__.users(user_id) ON DELETE CASCADE,
    epoch uuid NOT NULL CHECK (epoch <> '00000000-0000-0000-0000-000000000000'),
    sequence bigint NOT NULL CHECK (sequence >= 0),
    min_after_sequence bigint NOT NULL CHECK (min_after_sequence >= 0 AND min_after_sequence <= sequence),
    last_maintenance_at timestamptz NOT NULL
);
CREATE TABLE __SYNC__.sync_records (
    user_id uuid NOT NULL REFERENCES __SYNC__.sync_state(user_id) ON DELETE CASCADE,
    entity_type text NOT NULL CHECK (entity_type IN ('homework','completion','override','friend','settings')),
    entity_id uuid NOT NULL CHECK (entity_id <> '00000000-0000-0000-0000-000000000000'),
    revision bigint NOT NULL CHECK (revision > 0),
    tombstone boolean NOT NULL,
    changed_at timestamptz NOT NULL,
    payload jsonb,
    PRIMARY KEY (user_id, entity_type, entity_id),
    CHECK (entity_type <> 'settings' OR entity_id = '00000000-0000-0000-0000-000000000001'),
    CHECK ((tombstone AND payload IS NULL) OR (NOT tombstone AND payload IS NOT NULL AND jsonb_typeof(payload) = 'object')),
    CHECK (payload IS NULL OR octet_length(payload::text) <= 36864)
);
CREATE INDEX sync_records_revision ON __SYNC__.sync_records(user_id, revision);
CREATE INDEX sync_records_retention ON __SYNC__.sync_records(user_id, changed_at) WHERE tombstone;
CREATE TABLE __SYNC__.sync_receipts (
    user_id uuid NOT NULL REFERENCES __SYNC__.sync_state(user_id) ON DELETE CASCADE,
    op_id uuid NOT NULL CHECK (op_id <> '00000000-0000-0000-0000-000000000000'),
    request_digest bytea NOT NULL CHECK (octet_length(request_digest) = 32),
    status integer NOT NULL CHECK (status IN (200,409)),
    body bytea NOT NULL CHECK (octet_length(body) BETWEEN 2 AND 65536),
    created_at timestamptz NOT NULL,
    PRIMARY KEY (user_id, op_id)
);
CREATE INDEX sync_receipts_retention ON __SYNC__.sync_receipts(user_id, created_at);
CREATE TABLE __SYNC__.sync_changes (
    user_id uuid NOT NULL REFERENCES __SYNC__.sync_state(user_id) ON DELETE CASCADE,
    sequence bigint NOT NULL CHECK (sequence > 0),
    record jsonb NOT NULL CHECK (jsonb_typeof(record) = 'object' AND octet_length(record::text) <= 40960),
    op_id uuid NOT NULL CHECK (op_id <> '00000000-0000-0000-0000-000000000000'),
    created_at timestamptz NOT NULL,
    PRIMARY KEY (user_id, sequence)
);
CREATE INDEX sync_changes_retention ON __SYNC__.sync_changes(user_id, created_at);
CREATE TABLE __SYNC__.sync_manifests (
    id uuid PRIMARY KEY CHECK (id <> '00000000-0000-0000-0000-000000000000'),
    user_id uuid NOT NULL REFERENCES __SYNC__.sync_state(user_id) ON DELETE CASCADE,
    epoch uuid NOT NULL CHECK (epoch <> '00000000-0000-0000-0000-000000000000'),
    high_water bigint NOT NULL CHECK (high_water >= 0),
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL CHECK (expires_at = created_at + interval '10 minutes'),
    item_count bigint NOT NULL CHECK (item_count >= 0)
);
CREATE INDEX sync_manifests_owner ON __SYNC__.sync_manifests(user_id, epoch);
CREATE INDEX sync_manifests_retention ON __SYNC__.sync_manifests(user_id, expires_at);
CREATE TABLE __SYNC__.sync_manifest_items (
    manifest_id uuid NOT NULL REFERENCES __SYNC__.sync_manifests(id) ON DELETE CASCADE,
    ordinal bigint NOT NULL CHECK (ordinal > 0),
    record jsonb NOT NULL CHECK (jsonb_typeof(record) = 'object' AND octet_length(record::text) <= 40960),
    PRIMARY KEY (manifest_id, ordinal)
);
