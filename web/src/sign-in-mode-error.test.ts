import assert from "node:assert/strict";
import { test } from "node:test";
import { readFileSync } from "node:fs";

const pages = readFileSync(new URL("./pages.tsx", import.meta.url), "utf8");

test("#148 (R3-02): switching Вход / Регистрация clears the sign-in error", () => {
  const login = pages.match(/<button type="button" className=\{mode === "login" \? "active" : ""\} onClick=\{\(\) => \{([^}]*)\}\}>Вход<\/button>/);
  const register = pages.match(/<button type="button" className=\{mode === "register" \? "active" : ""\} onClick=\{\(\) => \{([^}]*)\}\}[^>]*>Регистрация<\/button>/);
  assert.ok(login && register, "both tabs");
  for (const [tab, body] of [["Вход", login[1]], ["Регистрация", register[1]]]) assert.match(body, /setError\(""\)/, tab);
});
