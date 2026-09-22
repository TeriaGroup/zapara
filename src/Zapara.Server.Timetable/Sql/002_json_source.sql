-- Add the university's fixed JSON metadata endpoint without weakening provenance.
ALTER TABLE {{schema}}.snapshots DROP CONSTRAINT snapshot_provenance;
ALTER TABLE {{schema}}.snapshots ADD CONSTRAINT snapshot_provenance CHECK (
    (source_kind='file' AND source_url IS NULL AND source_modified_at IS NULL) OR
    (source_kind='http' AND source_url IS NOT NULL AND source_url IN ({{xml_url}},{{json_url}})));
ALTER TABLE {{schema}}.schema_version DROP CONSTRAINT schema_version_one;
UPDATE {{schema}}.schema_version SET version=2;
ALTER TABLE {{schema}}.schema_version ADD CONSTRAINT schema_version_one CHECK (version=2);
