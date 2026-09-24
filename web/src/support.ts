export type SupportNote = { author: string; body: string };

const photoBytes = 4 * 1024 * 1024;
const logBytes = 512 * 1024;

export function supportDraft(signedIn: boolean, subject: string, body: string): { error?: string; subject?: string; body?: string } {
  if (!signedIn) return { error: "Войдите в аккаунт, чтобы отправить сообщение и увидеть ответ." };
  const theme = subject.trim();
  const text = body.trim();
  if (theme.length < 3 || text.length < 3) return { error: "Опишите тему и что случилось." };
  return { subject: theme, body: text };
}

export function supportFiles(photos: File[], logs: File[]): { error?: string } {
  if (photos.length > 3) return { error: "Можно приложить не больше трёх фотографий." };
  if (logs.length > 3) return { error: "Можно приложить не больше трёх логов." };
  for (const file of photos) {
    if (file.size <= 0) return { error: "Файл пустой." };
    if (file.size > photoBytes) return { error: "Фото больше 4 МиБ." };
    const type = file.type === "image/jpeg" || file.type === "image/png" || file.type === "image/webp";
    if (!type && !/\.(jpe?g|png|webp)$/i.test(file.name)) return { error: "Нужна фотография JPEG, PNG или WebP." };
  }
  for (const file of logs) {
    if (file.size <= 0) return { error: "Файл пустой." };
    if (file.size > logBytes) return { error: "Лог больше 512 КиБ." };
    if (!/\.(txt|log)$/i.test(file.name)) return { error: "Лог должен быть текстовым файлом .txt или .log." };
  }
  return {};
}

export function supportAppend(messages: SupportNote[], author: string, body: string): SupportNote[] {
  return [...messages, { author, body: body.trim() }];
}
