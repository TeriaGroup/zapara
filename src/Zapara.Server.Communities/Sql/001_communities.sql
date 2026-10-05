CREATE TABLE __COM__.schema_migrations (
    version integer PRIMARY KEY CHECK (version > 0),
    checksum text NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'),
    applied_at timestamptz NOT NULL
);
CREATE TABLE __COM__.communities (
    community_id uuid PRIMARY KEY CHECK (community_id <> '00000000-0000-0000-0000-000000000000'),
    name text NOT NULL CHECK (char_length(name) BETWEEN 1 AND 80 AND name !~ '[[:cntrl:]]'),
    description text NOT NULL CHECK (char_length(description) <= 2000 AND description !~ '[[:cntrl:]]'),
    revision bigint NOT NULL CHECK (revision > 0),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
CREATE TABLE __COM__.catalog_maps (
    map_id uuid PRIMARY KEY CHECK (map_id <> '00000000-0000-0000-0000-000000000000'),
    community_id uuid NOT NULL REFERENCES __COM__.communities(community_id),
    group_id text NOT NULL UNIQUE CHECK (char_length(group_id) BETWEEN 1 AND 64 AND group_id !~ '[[:cntrl:]]'),
    group_name text NOT NULL CHECK (char_length(group_name) BETWEEN 1 AND 80 AND group_name !~ '[[:cntrl:]]'),
    created_at timestamptz NOT NULL
);
CREATE INDEX catalog_maps_community ON __COM__.catalog_maps(community_id);
CREATE TABLE __COM__.memberships (
    community_id uuid NOT NULL REFERENCES __COM__.communities(community_id),
    user_id uuid NOT NULL REFERENCES __ACCOUNTS__.users(user_id) ON DELETE CASCADE,
    role text NOT NULL CHECK (role IN ('member','headman','curator')),
    status text NOT NULL CHECK (status IN ('active','revoked')),
    created_at timestamptz NOT NULL,
    revoked_at timestamptz NULL,
    PRIMARY KEY (community_id, user_id),
    CHECK ((status = 'active' AND revoked_at IS NULL) OR (status = 'revoked' AND revoked_at IS NOT NULL))
);
CREATE INDEX memberships_user ON __COM__.memberships(user_id);
CREATE TABLE __COM__.join_requests (
    request_id uuid PRIMARY KEY CHECK (request_id <> '00000000-0000-0000-0000-000000000000'),
    community_id uuid NOT NULL REFERENCES __COM__.communities(community_id),
    user_id uuid NOT NULL REFERENCES __ACCOUNTS__.users(user_id) ON DELETE CASCADE,
    status text NOT NULL CHECK (status IN ('pending','accepted','rejected')),
    created_at timestamptz NOT NULL,
    resolved_at timestamptz NULL,
    resolved_by uuid NULL REFERENCES __ACCOUNTS__.users(user_id) ON DELETE SET NULL,
    CHECK (
        (status = 'pending' AND resolved_at IS NULL AND resolved_by IS NULL) OR
        (status IN ('accepted','rejected') AND resolved_at IS NOT NULL)
    )
);
CREATE UNIQUE INDEX join_requests_pending ON __COM__.join_requests(community_id, user_id) WHERE status = 'pending';
CREATE INDEX join_requests_community ON __COM__.join_requests(community_id, status);
CREATE TABLE __COM__.staff_assignments (
    assignment_id uuid PRIMARY KEY CHECK (assignment_id <> '00000000-0000-0000-0000-000000000000'),
    community_id uuid NOT NULL REFERENCES __COM__.communities(community_id),
    user_id uuid NOT NULL REFERENCES __ACCOUNTS__.users(user_id) ON DELETE CASCADE,
    role text NOT NULL CHECK (role IN ('headman','curator')),
    assigned_at timestamptz NOT NULL,
    revoked_at timestamptz NULL,
    CHECK (revoked_at IS NULL OR revoked_at >= assigned_at)
);
CREATE UNIQUE INDEX staff_assignments_active ON __COM__.staff_assignments(community_id, user_id) WHERE revoked_at IS NULL;
CREATE TABLE __COM__.shared_homework (
    homework_id uuid PRIMARY KEY CHECK (homework_id <> '00000000-0000-0000-0000-000000000000'),
    community_id uuid NOT NULL REFERENCES __COM__.communities(community_id),
    title text NOT NULL CHECK (char_length(title) BETWEEN 1 AND 200 AND title !~ '[[:cntrl:]]'),
    body text NOT NULL CHECK (char_length(body) BETWEEN 1 AND 8000 AND body !~ '[[:cntrl:]]'),
    revision bigint NOT NULL CHECK (revision > 0),
    created_by uuid NULL REFERENCES __ACCOUNTS__.users(user_id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
CREATE INDEX shared_homework_community ON __COM__.shared_homework(community_id);
CREATE TABLE __COM__.shared_homework_completion (
    homework_id uuid NOT NULL REFERENCES __COM__.shared_homework(homework_id),
    user_id uuid NOT NULL REFERENCES __ACCOUNTS__.users(user_id) ON DELETE CASCADE,
    completed boolean NOT NULL,
    revision bigint NOT NULL CHECK (revision > 0),
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (homework_id, user_id)
);
CREATE TABLE __COM__.announcements (
    announcement_id uuid PRIMARY KEY CHECK (announcement_id <> '00000000-0000-0000-0000-000000000000'),
    community_id uuid NOT NULL REFERENCES __COM__.communities(community_id),
    title text NOT NULL CHECK (char_length(title) BETWEEN 1 AND 200 AND title !~ '[[:cntrl:]]'),
    body text NOT NULL CHECK (char_length(body) BETWEEN 1 AND 8000 AND body !~ '[[:cntrl:]]'),
    revision bigint NOT NULL CHECK (revision > 0),
    created_by uuid NULL REFERENCES __ACCOUNTS__.users(user_id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
CREATE INDEX announcements_community ON __COM__.announcements(community_id);
CREATE TABLE __COM__.polls (
    poll_id uuid PRIMARY KEY CHECK (poll_id <> '00000000-0000-0000-0000-000000000000'),
    community_id uuid NOT NULL REFERENCES __COM__.communities(community_id),
    question text NOT NULL CHECK (char_length(question) BETWEEN 1 AND 400 AND question !~ '[[:cntrl:]]'),
    deadline_at timestamptz NOT NULL,
    revision bigint NOT NULL CHECK (revision > 0),
    created_by uuid NULL REFERENCES __ACCOUNTS__.users(user_id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
CREATE INDEX polls_community ON __COM__.polls(community_id);
CREATE TABLE __COM__.poll_options (
    option_id uuid PRIMARY KEY CHECK (option_id <> '00000000-0000-0000-0000-000000000000'),
    poll_id uuid NOT NULL REFERENCES __COM__.polls(poll_id) ON DELETE CASCADE,
    label text NOT NULL CHECK (char_length(label) BETWEEN 1 AND 80 AND label !~ '[[:cntrl:]]'),
    ordinal integer NOT NULL CHECK (ordinal > 0),
    UNIQUE (poll_id, ordinal),
    UNIQUE (poll_id, option_id)
);
CREATE TABLE __COM__.votes (
    poll_id uuid NOT NULL,
    user_id uuid NOT NULL REFERENCES __ACCOUNTS__.users(user_id) ON DELETE CASCADE,
    option_id uuid NOT NULL,
    created_at timestamptz NOT NULL,
    PRIMARY KEY (poll_id, user_id),
    FOREIGN KEY (poll_id, option_id) REFERENCES __COM__.poll_options(poll_id, option_id)
);
CREATE TABLE __COM__.community_audit (
    event_id uuid PRIMARY KEY CHECK (event_id <> '00000000-0000-0000-0000-000000000000'),
    community_id uuid NOT NULL REFERENCES __COM__.communities(community_id),
    actor_id uuid NULL REFERENCES __ACCOUNTS__.users(user_id) ON DELETE SET NULL,
    action text NOT NULL CHECK (action IN (
        'join_requested','join_accepted','join_rejected',
        'homework_published','homework_updated',
        'announcement_published','announcement_updated',
        'poll_published','staff_assigned','staff_revoked','catalog_mapped')),
    object_type text NOT NULL CHECK (object_type IN (
        'community','join_request','membership','staff_assignment',
        'shared_homework','announcement','poll','catalog_map')),
    object_id uuid NOT NULL CHECK (object_id <> '00000000-0000-0000-0000-000000000000'),
    outcome text NOT NULL CHECK (outcome IN ('success','denied','conflict','invalid')),
    created_at timestamptz NOT NULL
);
CREATE INDEX community_audit_community ON __COM__.community_audit(community_id, created_at);
CREATE FUNCTION __COM__.reject_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'community_audit is append-only';
END $$;
CREATE TRIGGER community_audit_no_update BEFORE UPDATE OR DELETE ON __COM__.community_audit
FOR EACH ROW EXECUTE FUNCTION __COM__.reject_audit_mutation();
