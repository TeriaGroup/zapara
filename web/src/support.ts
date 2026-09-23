export type SupportNote = { author: string; body: string };

export function supportDraft(signedIn: boolean, subject: string, body: string): { error?: string; subject?: string; body?: string } {
  if (!signedIn) return { error: "Войдите в аккаунт, чтобы отправить сообщение и увидеть ответ." };
  const theme = subject.trim();
  const text = body.trim();
  if (theme.length < 3 || text.length < 3) return { error: "Опишите тему и что случилось." };
  return { subject: theme, body: text };
}

export function supportAppend(messages: SupportNote[], author: string, body: string): SupportNote[] {
  return [...messages, { author, body: body.trim() }];
}
