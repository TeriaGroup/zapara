import assert from "node:assert/strict";
import { test } from "node:test";
import { readFileSync } from "node:fs";

const pages = readFileSync(new URL("./pages.tsx", import.meta.url), "utf8");
const tab = (label: string) => pages.match(new RegExp(`<button type="button" className=\\{mode === "(?:login|register)" \\? "active" : ""\\}([^\\n]*?)>${label}</button>`))?.[1] ?? "";

test("#148 (R3-02): switching Вход / Регистрация clears the sign-in error", () => {
  for (const label of ["Вход", "Регистрация"]) {
    const attrs = tab(label);
    assert.ok(attrs, label);
    assert.match(attrs, /onClick=\{\(\) => \{[^}]*setError\(""\)[^}]*\}\}/, label);
  }
});

test("#153: mode tabs are disabled while a sign-in request is pending, so a late failure cannot land in the other mode", () => {
  assert.match(tab("Вход"), /disabled=\{busy\}/);
  assert.match(tab("Регистрация"), /disabled=\{busy \|\| !app\.session\?\.capabilities\.registration\}/);
});
