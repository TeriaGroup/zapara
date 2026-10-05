export function download(bytes, filename) {
    if (!(bytes instanceof Uint8Array) || bytes.byteLength === 0 || bytes.byteLength > 16 * 1024 * 1024
        || !/^zapara-(transfer-[0-9]{8}-[0-9]{6}|backup-[0-9a-f-]{36})\.json$/.test(filename)) throw new Error('Некорректный файл переноса.');
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/json;charset=utf-8' }));
    const link = document.createElement('a');
    link.download = filename; link.href = url; link.hidden = true; document.body.appendChild(link);
    try { link.click(); } finally { link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
}
