import assert from "node:assert/strict";
import test from "node:test";
import { packSupportLog, rememberLog, resetAppLog, supportLogFiles } from "./app-log.ts";

test("captured lines become a log file and a blank buffer adds nothing", async () => {
  resetAppLog();
  assert.deepEqual(supportLogFiles(), []);
  rememberLog("button failed");
  const files = supportLogFiles();
  assert.equal(files.length, 1);
  assert.equal(files[0].name, "web-1.log");
  assert.match(await files[0].text(), /button failed/);
  resetAppLog();
});

test("oversized text keeps the newest tail in at most three log files", async () => {
  const marker = "END-OF-LOG\n";
  const packed = packSupportLog("я".repeat(900_000) + "\n" + marker);
  assert.ok(packed.length >= 1 && packed.length <= 3);
  const decoder = new TextDecoder("utf-8", { fatal: true });
  let joined = "";
  for (const part of packed) {
    assert.ok(part.bytes.byteLength <= 512 * 1024);
    assert.match(part.name, /\.log$/);
    joined += decoder.decode(part.bytes);
  }
  assert.ok(joined.endsWith(marker));
  assert.equal(joined.includes("\0"), false);
  assert.equal(packSupportLog("").length, 0);
});
