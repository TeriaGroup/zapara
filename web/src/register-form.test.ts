import { test } from "node:test";
import assert from "node:assert/strict";
import { loginMissing, missingText, passwordRules, registrationMissing, ruleMark } from "./register-form.ts";

test("#16: password rules stay neutral before the field is left", () => {
  const rules = passwordRules("", "", {});
  assert.deepEqual(rules.map(rule => rule.state), ["neutral", "neutral", "neutral"]);
  assert.deepEqual(passwordRules("short", "", {}).map(rule => rule.state), ["neutral", "neutral", "neutral"], "typing alone does not judge");
  assert.equal(ruleMark.neutral, "•");
});

test("#16: rules are judged after blur and on submit", () => {
  assert.deepEqual(passwordRules("short", "", { password: true }).map(rule => rule.state), ["bad", "ok", "neutral"]);
  assert.deepEqual(passwordRules("long enough pass", "long enough pass", { password: true, confirmation: true }).map(rule => rule.state), ["ok", "ok", "ok"]);
  assert.deepEqual(passwordRules("long enough pass", "other", { password: true, confirmation: true }).map(rule => rule.state), ["ok", "ok", "bad"]);
  assert.deepEqual(passwordRules("", "", { submitted: true }).map(rule => rule.state), ["bad", "bad", "bad"]);
  assert.equal(passwordRules("bad\u0000password!!", "", { password: true })[1].state, "bad");
});

test("#16: submit lists what is missing instead of a silent disabled button", () => {
  assert.deepEqual(registrationMissing({ username: "", password: "", confirmation: "", accepted: false }),
    ["логин", "пароль от 12 до 128 символов", "совпадающее подтверждение пароля", "согласие с документами"]);
  assert.deepEqual(registrationMissing({ username: "ivan", password: "long enough pass", confirmation: "long enough pass", accepted: true }), []);
  assert.equal(missingText(["логин", "согласие с документами"]), "Чтобы создать аккаунт, укажите: логин, согласие с документами.");
  assert.equal(missingText([]), "");
  assert.equal(loginMissing("", ""), "Чтобы войти, укажите логин и пароль.");
  assert.equal(loginMissing("ivan", ""), "Чтобы войти, укажите пароль.");
  assert.equal(loginMissing("ivan", "x"), "");
});

import { readFileSync } from "node:fs";

const css = readFileSync(new URL("./styles.css", import.meta.url), "utf8");
const mobile = readFileSync(new URL("./mobile-shell.css", import.meta.url), "utf8");
function lum(hex: string) { const c = hex.replace("#", "").match(/../g)!.map(v => parseInt(v, 16) / 255).map(v => v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4); return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]; }
function ratio(a: string, b: string) { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); }
// Значения CSS-переменных по каскаду: tokens.css (#50, если он есть), затем styles.css; последнее объявление выигрывает.
// Светлая тема наследует :root. var(--x, запасное) раскрывается через объявленный токен, иначе берётся запасное значение —
// так тест работает и до #50 (в styles.css шестнадцатеричные цвета), и после (--canvas: var(--zp-surface-0)).
const cssFiles = ["./tokens.css", "./styles.css"].flatMap(file => { try { return [readFileSync(new URL(file, import.meta.url), "utf8")]; } catch { return []; } });
function declarations(scope: string): Map<string, string> {
  const esc = scope.replace(/[[\]"().*]/g, "\\$&"), found = new Map<string, string>();
  for (const text of cssFiles) for (const m of text.matchAll(new RegExp(`(?:^|\\n|\\})\\s*${esc}\\s*\\{([^}]*)\\}`, "g")))
    for (const d of m[1].matchAll(/--([a-z0-9-]+)\s*:\s*([^;]+);?/g)) found.set(d[1], d[2].trim());
  return found;
}
function resolve(scope: string, value: string, depth = 0): string | null {
  assert.ok(depth < 10, `цикл в ${value}`);
  const hex = value.match(/^#[0-9a-f]{6}$/i); if (hex) return value.toLowerCase();
  const ref = value.match(/^var\(\s*--([a-z0-9-]+)\s*(?:,\s*(.+))?\)$/);
  if (!ref) return null;
  const own = lookup(scope, ref[1]);
  if (own !== undefined) return resolve(scope, own, depth + 1);
  return ref[2] ? resolve(scope, ref[2], depth + 1) : null;
}
function lookup(scope: string, name: string): string | undefined {
  return declarations(scope).get(name) ?? (scope === ":root" ? undefined : declarations(":root").get(name));
}
/** Действующий цвет переменной в теме (с раскрытием var()). */
function cssVar(scope: string, name: string) { const raw = lookup(scope, name); assert.ok(raw, `${scope} ${name}`); const v = resolve(scope, raw!); assert.ok(v, `${scope} ${name}: ${raw}`); return v!; }
/** Запасной цвет из var(--zp-…, #hex) — его видят до #50 и если токена нет. */
function fallback(scope: string, name: string) {
  const raw = declarations(scope).get(name); const m = raw?.match(/^var\(--zp-[a-z-]+,\s*(#[0-9a-f]{6})\)$/i);
  assert.ok(m, `${scope} ${name}: ${raw}`); return m![1];
}

test("#16: field border ≥3:1 and placeholder ≥4.5:1 in both themes", () => {
  for (const scope of [":root", ':root[data-theme="light"]']) {
    const canvas = cssVar(scope, "canvas"), card = cssVar(scope, "card"), surface = cssVar(scope, "surface");
    // И запасной цвет, и действующий (токен #50, если он есть) должны держать контраст.
    for (const border of new Set([fallback(scope, "field-border"), cssVar(scope, "field-border")]))
      for (const bg of [canvas, card, surface]) assert.ok(ratio(border, bg) >= 3, `${scope} border ${border} on ${bg}: ${ratio(border, bg).toFixed(2)}`);
    for (const placeholder of new Set([fallback(scope, "field-placeholder"), cssVar(scope, "field-placeholder")]))
      assert.ok(ratio(placeholder, canvas) >= 4.5, `${scope} placeholder ${placeholder} on ${canvas}`);
  }
  assert.match(css, /--field-fill: var\(--canvas\)/);
  assert.match(css, /\.field input:focus-visible[^{]*\{ outline: 2px solid var\(--field-focus\)/);
  assert.doesNotMatch(mobile, /border-color: transparent/, "mobile fields keep a visible border");
});
