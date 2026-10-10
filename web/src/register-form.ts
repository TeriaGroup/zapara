import { scalarInput } from "./scalar-input.ts";

/** #16: правило пароля. До ухода из поля — нейтральное, после blur или попытки отправки — ✓/✗. */
export type RuleState = "neutral" | "ok" | "bad";
export type PasswordRule = { key: "length" | "chars" | "match"; label: string; state: RuleState };
export type Touched = { password?: boolean; confirmation?: boolean; submitted?: boolean };

const lengthOk = (password: string) => { const n = [...password].length; return n >= 12 && n <= 128; };

export function passwordRules(password: string, confirmation: string, touched: Touched): PasswordRule[] {
  const shown = (field: "password" | "confirmation") => !!touched.submitted || !!touched[field];
  const state = (ok: boolean, visible: boolean): RuleState => !visible ? "neutral" : ok ? "ok" : "bad";
  return [
    { key: "length", label: "От 12 до 128 символов", state: state(lengthOk(password), shown("password") && (password !== "" || !!touched.submitted)) },
    { key: "chars", label: "Без недопустимых символов", state: state(password !== "" && scalarInput(password, 0, 100000, false), shown("password") && (password !== "" || !!touched.submitted)) },
    { key: "match", label: "Подтверждение совпадает", state: state(password !== "" && password === confirmation, shown("confirmation") && (confirmation !== "" || !!touched.submitted)) },
  ];
}

export const ruleMark: Record<RuleState, string> = { neutral: "•", ok: "✓", bad: "✗" };

export type RegisterFields = { username: string; password: string; confirmation: string; accepted: boolean };

/** Что не хватает для регистрации — показывается под кнопкой вместо «немой» неактивной кнопки. */
export function registrationMissing(fields: RegisterFields): string[] {
  const missing: string[] = [];
  if (!fields.username.trim()) missing.push("логин");
  if (!lengthOk(fields.password)) missing.push("пароль от 12 до 128 символов");
  else if (!scalarInput(fields.password, 12, 128, false)) missing.push("пароль без недопустимых символов");
  if (fields.password === "" || fields.password !== fields.confirmation) missing.push("совпадающее подтверждение пароля");
  if (!fields.accepted) missing.push("согласие с документами");
  return missing;
}

export function missingText(missing: string[]): string {
  return missing.length ? `Чтобы создать аккаунт, укажите: ${missing.join(", ")}.` : "";
}

export function loginMissing(username: string, password: string): string {
  const missing = [!username.trim() && "логин", !password && "пароль"].filter(Boolean) as string[];
  return missing.length ? `Чтобы войти, укажите ${missing.join(" и ")}.` : "";
}
