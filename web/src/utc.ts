export function canonicalUtc(value: Date | string): string {
    const date = value instanceof Date ? value : new Date(value);
    if (!Number.isFinite(date.getTime()))
        throw new Error("Некорректная дата");
    return date.toISOString().replace(/\.([0-9]*?)0+Z$/, (_, digits: string) => digits ? `.${digits}Z` : "Z");
}
export function localDateTimeInput(value: string | null | undefined): string {
    if (!value) return "";
    const date = new Date(value);
    if (!Number.isFinite(date.getTime())) return "";
    const pad = (part: number) => String(part).padStart(2, "0");
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
export function syncSubjectKey(raw: string): string { return raw.trim().toLowerCase().replaceAll("ё", "е").split(/[ \t\r\n]+/).filter(Boolean).join(" "); }
