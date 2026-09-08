CREATE TABLE __SCHEMA__.schema_migrations (
    version integer PRIMARY KEY,
    checksum text NOT NULL,
    applied_at timestamptz NOT NULL
);
CREATE TABLE __SCHEMA__.users (
    user_id uuid PRIMARY KEY,
    username text NOT NULL CHECK (username ~ '^[A-Za-z0-9_.-]{3,32}$'),
    normalized_username text NOT NULL UNIQUE CHECK (normalized_username ~ '^[a-z0-9_.-]{3,32}$' AND normalized_username = lower(username COLLATE "C")),
    display_name text NULL CHECK (display_name IS NULL OR (char_length(display_name) BETWEEN 1 AND 80 AND display_name !~ '[[:cntrl:]]')),
    created_at timestamptz NOT NULL,
    status text NOT NULL CHECK (status IN ('active','disabled','deleting')),
    credential_version bigint NOT NULL DEFAULT 1 CHECK (credential_version > 0)
);
CREATE TABLE __SCHEMA__.password_credentials (
    user_id uuid PRIMARY KEY REFERENCES __SCHEMA__.users(user_id),
    password_hash text NOT NULL CHECK (length(btrim(password_hash)) > 0),
    changed_at timestamptz NOT NULL,
    failed_count integer NOT NULL DEFAULT 0 CHECK (failed_count >= 0),
    failure_window_started_at timestamptz NULL,
    locked_until timestamptz NULL
);
CREATE TABLE __SCHEMA__.session_families (
    family_id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES __SCHEMA__.users(user_id),
    device_id uuid NOT NULL,
    device_name text NOT NULL CHECK (char_length(device_name) BETWEEN 1 AND 80 AND device_name !~ '[[:cntrl:]]'),
    platform text NOT NULL CHECK (platform IN ('windows','android')),
    created_at timestamptz NOT NULL,
    authenticated_at timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL CHECK (expires_at > created_at),
    revoked_at timestamptz NULL,
    revocation_reason text NULL CHECK (revocation_reason IN ('logout','revoke','revoke_all','password_change','refresh_replay','disabled','deleting','expired'))
);
CREATE INDEX families_user ON __SCHEMA__.session_families(user_id);
CREATE INDEX families_expiry ON __SCHEMA__.session_families(expires_at);
CREATE TABLE __SCHEMA__.access_tokens (
    token_hash bytea PRIMARY KEY CHECK (octet_length(token_hash) = 32),
    family_id uuid NOT NULL UNIQUE REFERENCES __SCHEMA__.session_families(family_id),
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL CHECK (expires_at > created_at)
);
CREATE INDEX access_expiry ON __SCHEMA__.access_tokens(expires_at);
CREATE TABLE __SCHEMA__.refresh_tokens (
    token_hash bytea PRIMARY KEY CHECK (octet_length(token_hash) = 32),
    family_id uuid NOT NULL REFERENCES __SCHEMA__.session_families(family_id),
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL CHECK (expires_at > created_at),
    consumed_at timestamptz NULL,
    replacement_hash bytea NULL CHECK (replacement_hash IS NULL OR octet_length(replacement_hash) = 32),
    CHECK (replacement_hash IS NULL OR consumed_at IS NOT NULL)
);
CREATE UNIQUE INDEX refresh_active_family ON __SCHEMA__.refresh_tokens(family_id) WHERE consumed_at IS NULL;
CREATE INDEX refresh_family ON __SCHEMA__.refresh_tokens(family_id);
CREATE INDEX refresh_expiry ON __SCHEMA__.refresh_tokens(expires_at);
CREATE TABLE __SCHEMA__.account_security_events (
    event_id uuid PRIMARY KEY,
    user_id uuid NULL REFERENCES __SCHEMA__.users(user_id) ON DELETE SET NULL,
    family_id uuid NULL REFERENCES __SCHEMA__.session_families(family_id) ON DELETE SET NULL,
    action text NOT NULL CHECK (action IN ('register','login','logout','revoke','revoke_all','password_change','refresh_replay')),
    object_id text NULL CHECK (char_length(object_id) BETWEEN 1 AND 80),
    outcome text NOT NULL CHECK (outcome IN ('success','invalid_credentials','invalid_session','locked','disabled','denied')),
    created_at timestamptz NOT NULL
);
