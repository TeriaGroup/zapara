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
  assert.equal(passwordRules("bad\u0007password!!", "", { password: true })[1].state, "bad");
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
