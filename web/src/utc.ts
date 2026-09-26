export function canonicalUtc(value: Date | string): string {
    const date = value instanceof Date ? value : new Date(value);
    if (!Number.isFinite(date.getTime()))
        throw new Error("Некорректная дата");
    return date.toISOString().replace(/\.([0-9]*?)0+Z$/, (_, digits: string) => digits ? `.${digits}Z` : "Z");
}
export function syncSubjectKey(raw: string): string { return raw.trim().toLowerCase().replaceAll("ё", "е").split(/[ \t\r\n]+/).filter(Boolean).join(" "); }
