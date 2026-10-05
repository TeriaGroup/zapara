import assert from "node:assert/strict";
import test from "node:test";
import { supportAppend, supportDraft, supportFiles } from "./support.ts";

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
  const photo = new File([new Uint8Array([1])], "снимок.png", { type: "image/png" });
  const log = new File([new TextEncoder().encode("строка")], "отчёт.log", { type: "text/plain" });
  assert.equal(supportFiles([photo], [log]).error, undefined);
  assert.equal(supportFiles([photo, photo, photo, photo], []).error, "Можно приложить не больше трёх фотографий.");
  assert.equal(supportFiles([], [new File(["x"], "notes.exe")]).error, "Лог должен быть текстовым файлом .txt или .log.");
});
