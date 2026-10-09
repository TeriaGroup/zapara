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
function block(selector: string) { const at = css.indexOf(selector + " {"); assert.ok(at >= 0, selector); return css.slice(at, css.indexOf("}", at)); }
function cssVar(scope: string, name: string) { const m = block(scope).match(new RegExp(`--${name}:\\s*(#[0-9a-f]{6})`)); assert.ok(m, `${scope} ${name}`); return m![1]; }
function fallback(scope: string, name: string) {
  const esc = scope.replace(/[[\]"().*]/g, "\\$&");
  const m = css.match(new RegExp(`(?:^|\\n)${esc} \\{[^}]*--${name}:\\s*var\\(--zp-[a-z-]+,\\s*(#[0-9a-f]{6})\\)`));
  assert.ok(m, `${scope} ${name}`); return m![1];
}

test("#16: field border ≥3:1 and placeholder ≥4.5:1 in both themes", () => {
  for (const scope of [":root", ':root[data-theme="light"]']) {
    const canvas = cssVar(scope, "canvas"), card = cssVar(scope, "card"), surface = cssVar(scope, "surface");
    const border = fallback(scope, "field-border"), placeholder = fallback(scope, "field-placeholder");
    for (const bg of [canvas, card, surface]) assert.ok(ratio(border, bg) >= 3, `${scope} border ${border} on ${bg}: ${ratio(border, bg).toFixed(2)}`);
    assert.ok(ratio(placeholder, canvas) >= 4.5, `${scope} placeholder ${placeholder} on ${canvas}`);
  }
  assert.match(css, /--field-fill: var\(--canvas\)/);
  assert.match(css, /\.field input:focus-visible[^{]*\{ outline: 2px solid var\(--field-focus\)/);
  assert.doesNotMatch(mobile, /border-color: transparent/, "mobile fields keep a visible border");
});
