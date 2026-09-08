ALTER TABLE __SCHEMA__.reauth_proofs DROP CONSTRAINT reauth_proofs_purpose_check;
ALTER TABLE __SCHEMA__.reauth_proofs ADD CONSTRAINT reauth_proofs_purpose_check
    CHECK (purpose IN ('link:vk','link:yandex','unlink:vk','unlink:yandex','set_password','set_recovery_email','export','delete_account'));
ALTER TABLE __SCHEMA__.account_security_events DROP CONSTRAINT account_security_events_action_check;
ALTER TABLE __SCHEMA__.account_security_events ADD CONSTRAINT account_security_events_action_check
    CHECK (action IN ('register','login','logout','revoke','revoke_all','password_change','refresh_replay','password_reset','export','delete_account'));
CREATE TABLE __SCHEMA__.export_jobs (
    export_id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES __SCHEMA__.users(user_id),
    status text NOT NULL CHECK (status IN ('queued','running','ready','failed','expired')),
    created_at timestamptz NOT NULL,
    completed_at timestamptz NULL,
    expires_at timestamptz NULL,
    payload bytea NULL,
    byte_size integer NULL CHECK (byte_size IS NULL OR byte_size >= 0),
    CHECK (
        (status = 'ready' AND payload IS NOT NULL AND completed_at IS NOT NULL AND expires_at IS NOT NULL AND byte_size IS NOT NULL)
        OR (status <> 'ready' AND payload IS NULL AND byte_size IS NULL)
    )
);
CREATE INDEX export_jobs_user ON __SCHEMA__.export_jobs(user_id, created_at);
CREATE TABLE __SCHEMA__.deletion_jobs (
    job_id uuid PRIMARY KEY,
    user_id uuid NOT NULL UNIQUE,
    status text NOT NULL CHECK (status IN ('queued','running','completed','failed')),
    created_at timestamptz NOT NULL,
    completed_at timestamptz NULL,
    CHECK ((status = 'completed' AND completed_at IS NOT NULL) OR (status <> 'completed' AND completed_at IS NULL))
);
CREATE TABLE __SCHEMA__.deletion_manifests (
    user_id uuid PRIMARY KEY,
    normalized_username text NOT NULL CHECK (normalized_username ~ '^[a-z0-9_.-]{3,32}$'),
    deleted_at timestamptz NOT NULL
);
CREATE FUNCTION __SCHEMA__.reject_deleted_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF EXISTS (SELECT 1 FROM __SCHEMA__.deletion_manifests WHERE user_id = NEW.user_id) THEN
    RAISE EXCEPTION 'deleted identity' USING ERRCODE = '23514';
  END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER users_reject_deleted_identity BEFORE INSERT ON __SCHEMA__.users
FOR EACH ROW EXECUTE FUNCTION __SCHEMA__.reject_deleted_identity();
