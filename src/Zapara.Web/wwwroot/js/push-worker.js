// Imported by the published /app/ service worker.
self.addEventListener('push', event => {
    // The transport payload is deliberately not rendered: no account or study data on a lock screen.
    event.waitUntil(self.registration.showNotification('Запара', {
        body: 'Откройте приложение, чтобы проверить расписание и задания.',
        icon: '/app/icons/icon-512.png', tag: 'zapara-reminder',
        data: { url: '/app/' }
    }));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const client of windows) {
            const url = new URL(client.url);
            if (url.origin === self.location.origin && url.pathname.startsWith('/app/')) {
                await client.focus(); return;
            }
        }
        await self.clients.openWindow('/app/');
    })());
});
