import assert from "node:assert/strict";
import { test } from "node:test";
import { PersonalComposerStore, personalText, personalTextCount, personalTextValid, sendOnEnter, emptyChatState } from "./personal-composer.ts";
import type { SocialMessage } from "./types.ts";

const message = (id: string, body = id) => ({ messageId: id, body } as SocialMessage);

test("conversation drafts survive consumer remounts and are erased on account changes", () => {
  const store = new PersonalComposerStore("a");
  store.text("one", "Первая"); store.reply("one", message("reply"));
  store.text("two", "Вторая");
  assert.equal(store.read("one").text, "Первая");
  assert.equal(store.read("one").reply?.messageId, "reply");
  assert.equal(store.read("two").text, "Вторая");
  const ticket = store.begin("one")!;
  store.scope("b"); store.text("one", "Другой аккаунт");
  store.finish(ticket, "Старый ответ", true);
  assert.equal(store.read("one").text, "Другой аккаунт");
  assert.equal(store.read("one").error, "");
  store.scope("a"); assert.equal(store.read("one").text, "");
});

test("reply preserves text; cancel and successful edit restore normal draft and reply", () => {
  const store = new PersonalComposerStore("a");
  store.text("c", "Обычный текст"); store.reply("c", message("reply"));
  store.edit("c", message("edited", "Старый текст"));
  store.text("c", "Правка"); store.cancel("c");
  assert.equal(store.read("c").text, "Обычный текст");
  assert.equal(store.read("c").reply?.messageId, "reply");
  store.edit("c", message("edited", "Старый текст"));
  store.text("c", "Правка"); store.finish(store.begin("c")!, "", true);
  assert.equal(store.read("c").text, "Обычный текст");
  assert.equal(store.read("c").reply?.messageId, "reply");
  assert.equal(store.read("c").editing, null);
});

test("late success clears only the sent raw text and revision, with no automatic retry", () => {
  const store = new PersonalComposerStore("a");
  store.text("c", "  Привет  "); store.reply("c", message("reply"));
  const first = store.begin("c")!; assert.equal(store.begin("c"), null);
  store.finish(first, "", true); assert.equal(store.read("c").text, "");
  assert.equal(store.read("c").reply, null);
  store.text("c", "Отправлено"); const next = store.begin("c")!;
  store.text("c", "Новое"); store.text("c", "Отправлено");
  store.reply("c", message("next-reply")); store.finish(next, "", true);
  assert.equal(store.read("c").text, "Отправлено");
  assert.equal(store.read("c").reply?.messageId, "next-reply");
  const failed = store.begin("c")!; store.finish(failed, "Ответ сервера не получен", false);
  assert.equal(store.read("c").busy, false); assert.equal(store.read("c").text, "Отправлено");
  assert.equal(store.read("c").error, "Ответ сервера не получен");
});

test("changed edit mode survives late edit acknowledgement and restores the saved draft on cancel", () => {
  const store = new PersonalComposerStore("a"); store.text("c", "Обычный");
  store.edit("c", message("one")); const sent = store.begin("c")!;
  store.edit("c", message("two")); store.finish(sent, "", true);
  assert.equal(store.read("c").editing?.messageId, "two");
  store.cancel("c"); assert.equal(store.read("c").text, "Обычный");
});

test("attachment acknowledgement preserves normal text and any later reply context", () => {
  const store = new PersonalComposerStore("a"); store.text("c", "Подпись позже");
  store.reply("c", message("one")); const ticket = store.begin("c", "attachment")!;
  store.finish(ticket, "", true); assert.equal(store.read("c").text, "Подпись позже");
  assert.equal(store.read("c").reply, null);
  store.reply("c", message("two")); const late = store.begin("c", "attachment")!;
  store.reply("c", message("three")); store.finish(late, "", true);
  assert.equal(store.read("c").reply?.messageId, "three");
});

test("server text length uses normalized Unicode characters without truncating pasted text", () => {
  assert.equal(personalTextValid(" "), false);
  assert.equal(personalTextValid("x".repeat(2000)), true);
  assert.equal(personalTextValid("x".repeat(2001)), false);
  assert.equal(personalTextValid("  " + "x".repeat(2000) + "  "), true);
  assert.equal(personalTextValid("😀".repeat(2000)), true);
  assert.equal(personalTextValid("😀".repeat(2001)), false);
  assert.equal(personalTextCount("😀\r\ntext"), 6);
  assert.equal(personalText("  a\r\nb  "), "a\nb");
  assert.equal(personalTextValid("x\u0000y"), false);
  assert.equal(personalTextValid("x\uD800y"), false);
  assert.equal(personalTextValid("x\ty\nz"), true);
  const store = new PersonalComposerStore("a"); store.text("c", "x".repeat(2100));
  assert.equal(store.read("c").text.length, 2100);
});

test("desktop Enter sends, while Shift, mobile Enter and IME keep the editor event", () => {
  assert.equal(sendOnEnter("Enter", false, false, false, true), true);
  assert.equal(sendOnEnter("Enter", true, false, false, true), false);
  assert.equal(sendOnEnter("Enter", false, true, false, true), false);
  assert.equal(sendOnEnter("Enter", false, false, true, true), false);
  assert.equal(sendOnEnter("Enter", false, false, false, false), false);
});

test("empty states distinguish loading, failed loading and successful empty history", () => {
  assert.equal(emptyChatState(true, "", 0), "loading");
  assert.equal(emptyChatState(false, "Ошибка", 0), "failed");
  assert.equal(emptyChatState(false, "", 0), "empty");
  assert.equal(emptyChatState(false, "Ошибка", 2), null);
});
