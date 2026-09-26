import assert from "node:assert/strict";
import { test } from "node:test";
import { canonicalUtc, syncSubjectKey } from "./utc.ts";
test("UTC writes match canonical server fractional format",()=>{assert.equal(canonicalUtc("2026-09-26T10:00:00.000Z"),"2026-09-26T10:00:00Z");assert.equal(canonicalUtc("2026-09-26T10:00:00.120Z"),"2026-09-26T10:00:00.12Z");assert.equal(canonicalUtc("2026-09-26T10:00:00.123Z"),"2026-09-26T10:00:00.123Z");assert.throws(()=>canonicalUtc("invalid"));});
test("private subject key collapses ASCII whitespace and preserves interior NBSP",()=>{assert.equal(syncSubjectKey("  Ёж\t Тест\r\nX  "),"еж тест x");assert.equal(syncSubjectKey("A\u00a0B"),"a\u00a0b");});
