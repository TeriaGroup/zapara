export function normalizeHtmlLf(html: string): string { return html.replace(/\r+\n/g, "\n").replace(/\r/g, "\n"); }
