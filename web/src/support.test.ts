import assert from "node:assert/strict";
import test from "node:test";
import { supportAppend, supportDraft } from "./support.ts";

test("guest does not open a thread and a signed-in exchange stays one thread", () => {
  const guest = supportDraft(false, "Кнопка", "Не нажимается кнопка пары");
  assert.equal(guest.error, "Войдите в аккаунт, чтобы отправить сообщение и увидеть ответ.");
  assert.equal(guest.body, undefined);
  const opened = supportDraft(true, "Кнопка", "Не нажимается кнопка пары");
  assert.equal(opened.error, undefined);
  let messages = supportAppend([], "user", opened.body!);
  messages = supportAppend(messages, "operator", "Поправили переключатель.");
  messages = supportAppend(messages, "user", "Теперь нажимается.");
  assert.deepEqual(messages.map(item => item.author), ["user", "operator", "user"]);
  assert.equal(messages[0].body, "Не нажимается кнопка пары");
  assert.equal(messages[2].body, "Теперь нажимается.");
});
