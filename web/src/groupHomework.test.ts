import assert from "node:assert/strict";
import { test } from "node:test";
import {
  completeGroupCopy,
  saveEditorHomework,
  shareFailedNote,
  shareLocalOnlyNote,
  shareSharedNote,
  shareSignInNote,
} from "./groupHomework.ts";

const subject = "лек ВЫСШ. МАТЕМАТ";
const text = "§5, задачи 1–12";

async function save(share: boolean, signedIn: boolean, communityId: string, failSend = false) {
  const local: { subject: string; text: string }[] = [];
  const sent: { subject: string; text: string }[] = [];
  const outcome = await saveEditorHomework(
    { subject, text, share, isNew: true },
    signedIn,
    communityId,
    (savedSubject, savedText) => { local.push({ subject: savedSubject, text: savedText }); },
    async (savedSubject, savedText) => {
      sent.push({ subject: savedSubject, text: savedText });
      if (failSend) throw new Error("down");
    },
  );
  return { outcome, local, sent };
}

test("four share starts keep the saved task and send only when allowed", async () => {
  const off = await save(false, true, "community");
  assert.deepEqual(off.local, [{ subject, text }]);
  assert.deepEqual(off.sent, []);
  assert.equal(off.outcome.stored, true);
  assert.equal(off.outcome.sent, false);
  assert.equal(off.outcome.note, "");

  const signedOut = await save(true, false, "community");
  assert.deepEqual(signedOut.local, [{ subject, text }]);
  assert.deepEqual(signedOut.sent, []);
  assert.equal(signedOut.outcome.note, shareSignInNote);

  const noCommunity = await save(true, true, "  ");
  assert.deepEqual(noCommunity.local, [{ subject, text }]);
  assert.deepEqual(noCommunity.sent, []);
  assert.equal(noCommunity.outcome.note, shareLocalOnlyNote);

  const failed = await save(true, true, "community", true);
  assert.deepEqual(failed.local, [{ subject, text }]);
  assert.deepEqual(failed.sent, [{ subject, text }]);
  assert.equal(failed.outcome.stored, true);
  assert.equal(failed.outcome.sent, false);
  assert.equal(failed.outcome.note, shareFailedNote);

  const shared = await save(true, true, "community");
  assert.deepEqual(shared.local, [{ subject, text }]);
  assert.deepEqual(shared.sent, [{ subject, text }]);
  assert.equal(shared.outcome.sent, true);
  assert.equal(shared.outcome.note, shareSharedNote);

  let sentOnEdit = false;
  const edited = await saveEditorHomework(
    { subject, text, share: true, isNew: false },
    true,
    "community",
    () => undefined,
    async () => { sentOnEdit = true; },
  );
  assert.equal(sentOnEdit, false);
  assert.equal(edited.sent, false);
  assert.equal(edited.note, "");
});

test("one member's done does not flip the other copy or the local task", () => {
  const local = [{ id: "mine", done: false }];
  const copies = [
    { homeworkId: "hw", memberId: "anna", completed: false },
    { homeworkId: "hw", memberId: "boris", completed: false },
    { homeworkId: "other", memberId: "anna", completed: true },
  ];
  const next = completeGroupCopy(local, copies, "anna", "hw", true);
  assert.equal(local[0].done, false);
  assert.equal(copies[0].completed, false);
  assert.equal(copies[1].completed, false);
  assert.equal(next.local[0].done, false);
  assert.equal(next.local[0].id, "mine");
  assert.equal(next.copies[0].completed, true);
  assert.equal(next.copies[1].completed, false);
  assert.equal(next.copies[2].completed, true);
});
