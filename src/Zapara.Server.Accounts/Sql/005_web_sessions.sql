ALTER TABLE __SCHEMA__.session_families DROP CONSTRAINT session_families_platform_check;
ALTER TABLE __SCHEMA__.session_families ADD CONSTRAINT session_families_platform_check CHECK (platform IN ('windows','android','web'));
ALTER TABLE __SCHEMA__.oauth_transactions DROP CONSTRAINT oauth_transactions_platform_check;
ALTER TABLE __SCHEMA__.oauth_transactions ADD CONSTRAINT oauth_transactions_platform_check CHECK (platform IN ('windows','android','web'));
ALTER TABLE __SCHEMA__.oauth_transactions DROP CONSTRAINT oauth_transactions_return_kind_check;
ALTER TABLE __SCHEMA__.oauth_transactions ADD CONSTRAINT oauth_transactions_return_kind_check CHECK (return_kind IN ('windows','android','web'));
ALTER TABLE __SCHEMA__.oauth_transactions DROP CONSTRAINT oauth_transactions_check2;
ALTER TABLE __SCHEMA__.oauth_transactions ADD CONSTRAINT oauth_transactions_check2 CHECK ((return_kind='windows' AND return_port IS NOT NULL) OR (return_kind IN ('android','web') AND return_port IS NULL));
CREATE TABLE __SCHEMA__.web_sessions (
    session_hash bytea PRIMARY KEY CHECK (octet_length(session_hash)=32),
    family_id uuid NOT NULL UNIQUE REFERENCES __SCHEMA__.session_families(family_id) ON DELETE CASCADE,
    protected_tokens text NOT NULL,
    csrf_hash bytea NOT NULL CHECK (octet_length(csrf_hash)=32),
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL CHECK (expires_at>created_at)
);
CREATE INDEX web_sessions_expiry ON __SCHEMA__.web_sessions(expires_at);
CREATE TABLE __SCHEMA__.web_oauth_flows (
    transaction_id uuid PRIMARY KEY REFERENCES __SCHEMA__.oauth_transactions(transaction_id) ON DELETE CASCADE,
    browser_hash bytea NOT NULL CHECK (octet_length(browser_hash)=32),
    protected_verifier text NOT NULL,
    protected_result text NULL,
    expires_at timestamptz NOT NULL
);
CREATE INDEX web_oauth_expiry ON __SCHEMA__.web_oauth_flows(expires_at);
