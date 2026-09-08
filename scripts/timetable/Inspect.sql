\set ON_ERROR_STOP on
SELECT version, applied_at FROM :"schema".schema_version;
SELECT singleton, current_snapshot_id FROM :"schema".state;
SELECT count(*) AS snapshot_count FROM :"schema".snapshots;
SELECT snapshot_id, attempt_id, source_kind, source_sha256, fetched_at, published_at,
       source_modified_at, jsonb_array_length(payload->'groups') AS groups,
       jsonb_array_length(payload->'lessons') AS lessons
FROM :"schema".snapshots ORDER BY published_at, snapshot_id;
SELECT attempt_id, sequence, status, error_code, started_at, finished_at
FROM :"schema".refresh_attempts ORDER BY sequence;
