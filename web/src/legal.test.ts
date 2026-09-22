import assert from "node:assert/strict";
import { test } from "node:test";
import { createServer } from "vite";
import type { LegalDocument } from "./legal.ts";

const required = [
  "Расписание военмех",
  "неофициальное",
  "не является сервисом университета",
  "расписание, карты и локальные записи остаются на устройстве",
  "Аккаунт необязателен",
  "на сервере оператора в России хранятся только данные, которые нужны этому аккаунту и выбранной синхронизации",
  "Рекламных SDK нет",
  "Сторонней аналитики нет",
  "https://github.com/TeriaGroup/zapara",
];

test("the shipped documents are the two Russian texts the screens show", async () => {
  const server = await createServer({ server: { middlewareMode: true }, appType: "custom", logLevel: "error" });
  try {
    const mod = await server.ssrLoadModule("/src/legal.ts") as {
      legalDocument: (id: "agreement" | "policy") => LegalDocument;
      legalDocuments: () => LegalDocument[];
    };
    const docs = mod.legalDocuments();
    assert.deepEqual(docs.map(item => item.title), [
      "Пользовательское соглашение",
      "Политика обработки персональных данных",
    ]);
    assert.deepEqual(docs.map(item => item.id), ["agreement", "policy"]);
    for (const id of ["agreement", "policy"] as const) {
      const shown = mod.legalDocument(id);
      assert.equal(shown.title, docs.find(item => item.id === id)?.title);
      assert.equal(shown.body, docs.find(item => item.id === id)?.body);
      for (const line of required) assert.equal(shown.body.includes(line), true, line);
      assert.equal(shown.body.includes("ИНН"), false);
      assert.equal(shown.body.includes("почтовый адрес"), false);
      assert.equal(shown.body.includes("соблюдает закон"), false);
    }
  } finally {
    await server.close();
  }
});
