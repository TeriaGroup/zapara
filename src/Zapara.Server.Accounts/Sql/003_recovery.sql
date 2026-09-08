ALTER TABLE __SCHEMA__.reauth_proofs DROP CONSTRAINT reauth_proofs_purpose_check;
ALTER TABLE __SCHEMA__.reauth_proofs ADD CONSTRAINT reauth_proofs_purpose_check
    CHECK (purpose IN ('link:vk','link:yandex','unlink:vk','unlink:yandex','set_password','set_recovery_email'));
ALTER TABLE __SCHEMA__.account_security_events DROP CONSTRAINT account_security_events_action_check;
ALTER TABLE __SCHEMA__.account_security_events ADD CONSTRAINT account_security_events_action_check
    CHECK (action IN ('register','login','logout','revoke','revoke_all','password_change','refresh_replay','password_reset'));
CREATE TABLE __SCHEMA__.recovery_addresses (
    user_id uuid PRIMARY KEY REFERENCES __SCHEMA__.users(user_id),
    email text NOT NULL CHECK (char_length(email) BETWEEN 6 AND 254 AND email = lower(email COLLATE "C") AND email ~ '^[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,24}$'),
    verified_at timestamptz NOT NULL
);
CREATE TABLE __SCHEMA__.recovery_email_tokens (
    token_hash bytea PRIMARY KEY CHECK (octet_length(token_hash) = 32),
    user_id uuid NOT NULL REFERENCES __SCHEMA__.users(user_id),
    email text NOT NULL CHECK (char_length(email) BETWEEN 6 AND 254 AND email = lower(email COLLATE "C") AND email ~ '^[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,24}$'),
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz NULL
);
CREATE INDEX recovery_email_tokens_user ON __SCHEMA__.recovery_email_tokens(user_id);
CREATE INDEX recovery_email_tokens_expiry ON __SCHEMA__.recovery_email_tokens(expires_at);
CREATE TABLE __SCHEMA__.password_reset_tokens (
    token_hash bytea PRIMARY KEY CHECK (octet_length(token_hash) = 32),
    user_id uuid NOT NULL REFERENCES __SCHEMA__.users(user_id),
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz NULL
);
CREATE INDEX password_reset_tokens_user ON __SCHEMA__.password_reset_tokens(user_id);
CREATE INDEX password_reset_tokens_expiry ON __SCHEMA__.password_reset_tokens(expires_at);
