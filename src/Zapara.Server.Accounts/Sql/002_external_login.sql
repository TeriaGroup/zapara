CREATE TABLE __SCHEMA__.external_identities (
    user_id uuid NOT NULL REFERENCES __SCHEMA__.users(user_id),
    provider text NOT NULL CHECK (provider IN ('vk','yandex')),
    subject text NOT NULL CHECK (char_length(subject) BETWEEN 1 AND 256),
    linked_at timestamptz NOT NULL,
    PRIMARY KEY (provider,subject),
    UNIQUE (user_id,provider)
);
CREATE TABLE __SCHEMA__.reauth_proofs (
    proof_hash bytea PRIMARY KEY CHECK (octet_length(proof_hash)=32),
    user_id uuid NOT NULL REFERENCES __SCHEMA__.users(user_id),
    family_id uuid NOT NULL REFERENCES __SCHEMA__.session_families(family_id),
    purpose text NOT NULL CHECK (purpose IN ('link:vk','link:yandex','unlink:vk','unlink:yandex','set_password')),
    security_version bigint NOT NULL CHECK (security_version>0),
    provider_verified boolean NOT NULL,
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz NULL,
    reservation uuid NULL
);
CREATE INDEX reauth_expiry ON __SCHEMA__.reauth_proofs(expires_at);
CREATE TABLE __SCHEMA__.oauth_transactions (
    transaction_id uuid PRIMARY KEY,
    owner_id uuid NOT NULL,
    purpose text NOT NULL CHECK (purpose IN ('login','link','reauth')),
    provider text NOT NULL CHECK (provider IN ('vk','yandex')),
    native_challenge bytea NOT NULL CHECK (octet_length(native_challenge)=32),
    state_hash bytea NOT NULL UNIQUE CHECK (octet_length(state_hash)=32),
    initiator_user_id uuid NULL REFERENCES __SCHEMA__.users(user_id),
    initiator_family_id uuid NULL REFERENCES __SCHEMA__.session_families(family_id),
    security_version bigint NULL,
    proof_hash bytea NULL REFERENCES __SCHEMA__.reauth_proofs(proof_hash),
    proof_purpose text NULL,
    status text NOT NULL CHECK (status IN ('pending','callbackClaimed','awaitingApp','completed','failed','expired')),
    expires_at timestamptz NOT NULL,
    resolved_user_id uuid NULL REFERENCES __SCHEMA__.users(user_id),
    consumed_at timestamptz NULL,
    subject text NULL CHECK (char_length(subject) BETWEEN 1 AND 256),
    display_name text NULL CHECK (char_length(display_name) BETWEEN 1 AND 80),
    handoff_hash bytea NULL CHECK (octet_length(handoff_hash)=32),
    handoff_expires_at timestamptz NULL CHECK (handoff_expires_at<=expires_at),
    return_kind text NOT NULL CHECK (return_kind IN ('windows','android')),
    return_port integer NULL CHECK (return_port BETWEEN 1024 AND 65535),
    device_id uuid NOT NULL,
    device_name text NOT NULL,
    platform text NOT NULL CHECK (platform IN ('windows','android')),
    CHECK ((purpose='login' AND initiator_user_id IS NULL AND initiator_family_id IS NULL AND security_version IS NULL)
        OR (purpose<>'login' AND initiator_user_id IS NOT NULL AND initiator_family_id IS NOT NULL AND security_version IS NOT NULL)),
    CHECK ((return_kind='windows' AND return_port IS NOT NULL) OR (return_kind='android' AND return_port IS NULL))
);
CREATE INDEX oauth_expiry ON __SCHEMA__.oauth_transactions(owner_id,expires_at);
