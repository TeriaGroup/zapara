if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => navigator.serviceWorker.register('/app/service-worker.js', { scope: '/app/', updateViaCache: 'none' }).catch(() => {
        window.dispatchEvent(new CustomEvent('zapara-offline-unavailable'));
    }));
}
