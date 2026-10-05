const name = 'zapara-web-v1';
let opening;
function open() {
    return opening ||= new Promise((resolve, reject) => {
        const request = indexedDB.open(name, 1);
        request.onupgradeneeded = () => {
            request.result.createObjectStore('public');
            request.result.createObjectStore('profiles');
            request.result.createObjectStore('preferences');
        };
        request.onsuccess = () => {
            const db = request.result;
            db.onversionchange = () => { db.close(); opening = undefined; };
            resolve(db);
        };
        request.onerror = () => { opening = undefined; reject(new Error('Не удалось открыть хранилище устройства.')); };
        request.onblocked = () => { opening = undefined; reject(new Error('Закройте другие вкладки «Расписание военмех» для обновления хранилища.')); };
    });
}
export async function dropHeavyLocalCopies() {
    const db = await open();
    return new Promise((resolve, reject) => {
        const tx = db.transaction('public', 'readwrite');
        const records = tx.objectStore('public');
        const cursor = records.openCursor();
        cursor.onerror = () => reject(new Error('Не удалось облегчить сохранённые данные.'));
        cursor.onsuccess = () => {
            const current = cursor.result;
            if (!current) return;
            const key = String(current.key);
            if (key === 'lecturers:xml') current.delete();
            else if (key.startsWith('schedule') && typeof current.value === 'string' && current.value.length > 1500000) {
                try {
                    const parsed = JSON.parse(current.value);
                    const lessons = parsed?.snapshot?.lessons;
                    if (Array.isArray(lessons) && lessons.length > 400) {
                        parsed.snapshot.lessons = [];
                        parsed.fromBundle = false;
                        parsed.loadedGroupIds = [];
                        current.update(JSON.stringify(parsed));
                    }
                } catch { /* Оставляем прежнюю копию, если её нельзя разобрать. */ }
            }
            current.continue();
        };
        tx.oncomplete = () => resolve();
        tx.onabort = tx.onerror = () => reject(new Error('Не удалось облегчить сохранённые данные.'));
    });
}
export async function read(store, key) {
    const db = await open();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(store, 'readonly');
        const request = tx.objectStore(store).get(key);
        request.onsuccess = () => resolve(request.result ?? null);
        request.onerror = () => reject(new Error('Не удалось прочитать сохранённые данные.'));
    });
}
export async function write(store, key, value) {
    const db = await open();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(store, 'readwrite');
        tx.objectStore(store).put(value, key);
        tx.oncomplete = () => resolve();
        tx.onabort = tx.onerror = () => reject(new Error('Не удалось сохранить данные. Проверьте свободное место на устройстве.'));
    });
}
export function appearance(theme, animations) {
    try { localStorage.setItem('zapara.theme', theme); localStorage.setItem('zapara.motion', animations ? 'on' : 'off'); } catch { }
    window.dispatchEvent(new Event('zapara-appearance'));
}
export async function commitSnapshot(profileKey, profile, cache) {
    const db = await open();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(['profiles', 'public'], 'readwrite');
        tx.objectStore('profiles').put(profile, profileKey);
        tx.objectStore('public').put(cache, 'schedule');
        tx.oncomplete = () => resolve();
        tx.onabort = tx.onerror = () => reject(new Error('Обновление не сохранено. Предыдущее расписание осталось на устройстве.'));
    });
}

export function compareExchangeProfile(owner, expectedRevision, profile) {
    return compareProfile(owner, expectedRevision, profile);
}
export function compareExchangeProfileSnapshot(owner, expectedRevision, profile, cache) {
    return compareProfile(owner, expectedRevision, profile, cache);
}

function integerField(value, camel, pascal) {
    const present = name => Object.prototype.hasOwnProperty.call(value, name) && value[name] != null;
    const read = name => {
        const revision = value[name];
        if (!Number.isSafeInteger(revision) || revision < 0) throw new Error('Некорректная версия сохранённого профиля. Данные не изменены.');
        return revision;
    };
    const fromCamel = present(camel) ? read(camel) : null;
    const fromPascal = present(pascal) ? read(pascal) : null;
    if (fromCamel != null && fromPascal != null && fromCamel !== fromPascal) throw new Error('Некорректная версия сохранённого профиля. Данные не изменены.');
    return fromCamel ?? fromPascal ?? 0;
}
function parsedProfile(raw, owner) {
    let value;
    try { value = JSON.parse(raw); } catch { throw new Error('Не удалось прочитать сохранённый профиль. Данные не изменены.'); }
    if (!value || typeof value !== 'object' || Array.isArray(value))
        throw new Error('Владелец сохранённого профиля не совпадает. Данные не изменены.');
    const storedOwner = value.owner ?? value.Owner;
    if (!storedOwner || storedOwner !== owner || (value.owner != null && value.Owner != null && value.owner !== value.Owner))
        throw new Error('Владелец сохранённого профиля не совпадает. Данные не изменены.');
    const revision = integerField(value, 'storageRevision', 'StorageRevision');
    const resetEpoch = integerField(value, 'resetEpoch', 'ResetEpoch');
    if (!Number.isSafeInteger(revision) || revision < 0 || !Number.isSafeInteger(resetEpoch) || resetEpoch < 0)
        throw new Error('Некорректная версия сохранённого профиля. Данные не изменены.');
    return { value, revision, resetEpoch };
}

async function compareProfile(owner, expectedRevision, profile, cache) {
    if (typeof owner !== 'string' || !owner || owner.length > 2048 || typeof profile !== 'string'
        || !Number.isSafeInteger(expectedRevision) || expectedRevision < 0 || expectedRevision >= Number.MAX_SAFE_INTEGER)
        throw new Error('Некорректный запрос сохранения профиля.');
    const candidate = parsedProfile(profile, owner);
    if (candidate.revision !== expectedRevision + 1)
        throw new Error('Новая версия профиля должна следовать за предыдущей.');
    if (cache !== undefined) {
        let snapshot;
        try { snapshot = typeof cache === 'string' ? JSON.parse(cache) : null; } catch { snapshot = null; }
        if (!snapshot || typeof snapshot !== 'object' || Array.isArray(snapshot)) throw new Error('Некорректный снимок расписания.');
    }
    const db = await open();
    return new Promise((resolve, reject) => {
        const stores = cache === undefined ? ['profiles'] : ['profiles', 'public'];
        const tx = db.transaction(stores, 'readwrite');
        const profiles = tx.objectStore('profiles');
        let accepted = false, failure;
        tx.oncomplete = () => resolve(accepted);
        tx.onerror = () => { failure ||= new Error('Не удалось сохранить данные. Проверьте свободное место на устройстве.'); };
        tx.onabort = () => reject(failure || new Error('Сохранение отменено. Предыдущие данные не изменены.'));
        const read = profiles.get(owner);
        read.onerror = () => { failure = new Error('Не удалось прочитать сохранённый профиль.'); };
        read.onsuccess = () => {
            try {
                const current = read.result === undefined || read.result === null ? { revision: 0, resetEpoch: 0 } : parsedProfile(read.result, owner);
                if (current.resetEpoch !== candidate.resetEpoch) throw new Error('Этот локальный профиль очищен. Старое действие отменено.');
                // Never write a stale body over a revision this transaction did not read.
                if (current.revision !== expectedRevision || candidate.revision !== current.revision + 1) return;
                profiles.put(profile, owner);
                if (cache !== undefined) {
                    tx.objectStore('public').put(cache, 'schedule');
                    tx.objectStore('public').put(cache, 'schedule:' + owner);
                }
                accepted = true;
            } catch (error) {
                failure = error instanceof Error ? error : new Error('Сохранение отклонено.');
                tx.abort();
            }
        };
    });
}

const escapedOwner = owner => encodeURIComponent(owner).replace(/[!'()*]/g, c => '%' + c.charCodeAt(0).toString(16).toUpperCase());
export async function writeProfilePreference(owner, expectedResetEpoch, key, raw) {
    if (typeof owner !== 'string' || !owner || !Number.isSafeInteger(expectedResetEpoch) || expectedResetEpoch < 0
        || typeof key !== 'string' || !(key.startsWith('draft:' + escapedOwner(owner) + ':') || key.startsWith('community-cache:' + escapedOwner(owner) + ':'))
        || typeof raw !== 'string') throw new Error('Некорректный запрос сохранения данных профиля.');
    JSON.parse(raw);
    const db = await open();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(['profiles', 'preferences'], 'readwrite');
        let failure, accepted = false;
        tx.oncomplete = () => resolve(accepted);
        tx.onerror = () => { failure ||= new Error('Не удалось сохранить данные профиля. Проверьте свободное место.'); };
        tx.onabort = () => reject(failure || new Error('Сохранение отменено. Предыдущие данные не изменены.'));
        const request = tx.objectStore('profiles').get(owner);
        request.onsuccess = () => {
            try {
                const epoch = request.result == null ? 0 : parsedProfile(request.result, owner).resetEpoch;
                if (epoch !== expectedResetEpoch) return;
                tx.objectStore('preferences').put(raw, key);
                accepted = true;
            } catch (error) { failure = error; tx.abort(); }
        };
    });
}
