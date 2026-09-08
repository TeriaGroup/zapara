namespace Zapara.Server.Admin;

internal static class AdminSql
{
    internal static string Baseline() => """
CREATE TABLE __ADM__.schema_migrations (
    version integer PRIMARY KEY CHECK (version > 0),
    checksum text NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'),
    applied_at timestamptz NOT NULL
);
CREATE TABLE __ADM__.platform_admins (
    user_id uuid PRIMARY KEY REFERENCES __ACCOUNTS__.users(user_id),
    granted_at timestamptz NOT NULL,
    revoked_at timestamptz NULL,
    CHECK (revoked_at IS NULL OR revoked_at >= granted_at)
);
CREATE TABLE __ADM__.admin_sessions (
    session_id uuid PRIMARY KEY CHECK (session_id <> '00000000-0000-0000-0000-000000000000'),
    token_hash bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    user_id uuid NOT NULL REFERENCES __ACCOUNTS__.users(user_id),
    created_at timestamptz NOT NULL,
    authenticated_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL CHECK (expires_at > created_at),
    revoked_at timestamptz NULL,
    credential_version bigint NOT NULL CHECK (credential_version > 0),
    reauth_until timestamptz NULL
);
CREATE INDEX admin_sessions_user ON __ADM__.admin_sessions(user_id);
CREATE TABLE __ADM__.admin_audit (
    event_id uuid PRIMARY KEY CHECK (event_id <> '00000000-0000-0000-0000-000000000000'),
    actor_id uuid NULL REFERENCES __ACCOUNTS__.users(user_id) ON DELETE SET NULL,
    action text NOT NULL CHECK (action IN (
        'bootstrap','login','logout','reauth',
        'community_created','catalog_mapped',
        'staff_assigned','staff_revoked',
        'join_accepted','join_rejected',
        'account_disabled','session_revoked',
        'content_moderated')),
    object_type text NOT NULL CHECK (object_type IN (
        'platform_admin','admin_session','community','catalog_map',
        'staff_assignment','join_request','account','session_family',
        'shared_homework','announcement','poll')),
    object_id text NOT NULL CHECK (char_length(object_id) BETWEEN 1 AND 80),
    outcome text NOT NULL CHECK (outcome IN ('success','denied','conflict','invalid')),
    created_at timestamptz NOT NULL
);
CREATE INDEX admin_audit_created ON __ADM__.admin_audit(created_at);
CREATE FUNCTION __ADM__.reject_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'admin_audit is append-only';
END $$;
CREATE TRIGGER admin_audit_no_update BEFORE UPDATE OR DELETE ON __ADM__.admin_audit
FOR EACH ROW EXECUTE FUNCTION __ADM__.reject_audit_mutation();
""";
}
