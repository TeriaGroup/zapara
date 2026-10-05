-- Canonical bodies allow LF and tabs; CRLF is normalized by the application.
-- Other control characters and the existing 8000-codepoint limit stay forbidden.
ALTER TABLE __COM__.shared_homework DROP CONSTRAINT shared_homework_body_check;
ALTER TABLE __COM__.shared_homework ADD CONSTRAINT shared_homework_body_check
    CHECK (char_length(body) BETWEEN 1 AND 8000 AND translate(body, E'\n\t', '') !~ '[[:cntrl:]]');
ALTER TABLE __COM__.announcements DROP CONSTRAINT announcements_body_check;
ALTER TABLE __COM__.announcements ADD CONSTRAINT announcements_body_check
    CHECK (char_length(body) BETWEEN 1 AND 8000 AND translate(body, E'\n\t', '') !~ '[[:cntrl:]]');
