import agreement from "../../legal/user-agreement.txt?raw";
import policy from "../../legal/privacy-policy.txt?raw";

export const legalContact = "https://github.com/TeriaGroup/zapara";

export type LegalId = "agreement" | "policy";

export type LegalDocument = { id: LegalId; title: string; body: string };

const titles: Record<LegalId, string> = {
  agreement: "Пользовательское соглашение",
  policy: "Политика обработки персональных данных",
};

function bodyOf(text: string) {
  return text.replace(/^\uFEFF/, "").trim();
}

/** The documents the settings screen and the account form show. */
export function legalDocument(id: LegalId): LegalDocument {
  return { id, title: titles[id], body: bodyOf(id === "agreement" ? agreement : policy) };
}

export function legalDocuments(): LegalDocument[] {
  return [legalDocument("agreement"), legalDocument("policy")];
}
