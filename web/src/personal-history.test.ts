import test from "node:test";
import assert from "node:assert/strict";
import { personalCopyText, searchPersonalHistory } from "./personal-history.ts";
test("copy returns user text/caption, never deleted text, service URLs or card payload", () => {
  assert.equal(personalCopyText({body:"  Привет  ",kind:"text",deleted:false}),"Привет");
  assert.equal(personalCopyText({body:"Подпись",kind:"image",deleted:false}),"Подпись");
  assert.equal(personalCopyText({body:"https://example.test/web-api/media/1",kind:"image",deleted:false}),null);
  assert.equal(personalCopyText({body:"https://example.org",kind:"text",deleted:false}),"https://example.org");
  assert.equal(personalCopyText({body:"https://example.org/app/schedule",kind:"image",deleted:false}),"https://example.org/app/schedule");
  assert.equal(personalCopyText({body:"Смотрите https://example.org/app/schedule",kind:"text",deleted:false}),"Смотрите https://example.org/app/schedule");
  assert.equal(personalCopyText({body:"/web-api/example-typed-link",kind:"text",deleted:false}),"/web-api/example-typed-link");
  for (const message of [{body:"secret",kind:"text",deleted:true},{body:"/web-api/media/1",kind:"file",deleted:false},{body:"{id:1}",kind:"card",deleted:false}]) assert.equal(personalCopyText(message),null);
});
test("local history search includes filenames and excludes deleted bodies without mutating messages", () => {
  const rows = [{body:"Физика",kind:"text",deleted:false,fileName:null},{body:"Физика",kind:"text",deleted:true,fileName:null},{body:"",kind:"file",deleted:false,fileName:"Физика.pdf"}];
  assert.deepEqual(searchPersonalHistory(rows,"  ФИЗИКА "),[rows[0],rows[2]]);
  assert.equal(searchPersonalHistory(rows,""),rows);
  assert.equal(rows.length,3);
});
