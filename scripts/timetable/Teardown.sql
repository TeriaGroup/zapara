-- Invoke only after Verify-Milestone.ps1 verifies its exclusive CREATE receipt.
\set ON_ERROR_STOP on
SELECT :'schema' ~ '^tt_m1_[0-9a-f]{32}$' AS safe \gset
\if :safe
DROP SCHEMA :"schema" CASCADE;
SELECT NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = :'schema') AS dropped \gset
\if :dropped
SELECT :'schema' AS dropped_owned_schema;
\else
SELECT 1/0;
\endif
\else
SELECT 1/0;
\endif
