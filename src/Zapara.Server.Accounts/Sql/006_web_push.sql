CREATE TABLE __SCHEMA__.web_push_subscriptions (
    subscription_id uuid PRIMARY KEY,
    family_id uuid NOT NULL UNIQUE REFERENCES __SCHEMA__.session_families(family_id) ON DELETE CASCADE,
    endpoint_hash bytea NOT NULL UNIQUE CHECK (octet_length(endpoint_hash)=32),
    protected_subscription text NOT NULL,
    enabled boolean NOT NULL,
    time_zone text NOT NULL CHECK (char_length(time_zone) BETWEEN 1 AND 80),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
CREATE TABLE __SCHEMA__.web_push_deliveries (
    subscription_id uuid NOT NULL REFERENCES __SCHEMA__.web_push_subscriptions(subscription_id) ON DELETE CASCADE,
    local_date date NOT NULL,
    slot smallint NOT NULL CHECK (slot BETWEEN 0 AND 2),
    scheduled_time text NOT NULL CHECK (char_length(scheduled_time) BETWEEN 1 AND 32),
    status text NOT NULL CHECK (status IN ('claimed','accepted','unavailable')),
    attempted_at timestamptz NOT NULL,
    PRIMARY KEY (subscription_id,local_date,slot,scheduled_time)
);
CREATE INDEX web_push_delivery_age ON __SCHEMA__.web_push_deliveries(attempted_at);
CREATE FUNCTION __SCHEMA__.web_push_revoke_family() RETURNS trigger LANGUAGE plpgsql AS $body$
BEGIN
    IF NEW.revoked_at IS NOT NULL THEN
        DELETE FROM __SCHEMA__.web_push_subscriptions WHERE family_id=NEW.family_id;
    END IF;
    RETURN NEW;
END;
$body$;
CREATE TRIGGER web_push_family_revoked AFTER UPDATE OF revoked_at ON __SCHEMA__.session_families
    FOR EACH ROW EXECUTE FUNCTION __SCHEMA__.web_push_revoke_family();
