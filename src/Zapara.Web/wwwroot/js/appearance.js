(() => {
    const system = matchMedia('(prefers-color-scheme: dark)');
    function apply() {
        let theme = 'system', motion = 'on';
        try { theme = localStorage.getItem('zapara.theme') || theme; motion = localStorage.getItem('zapara.motion') || motion; } catch { }
        document.documentElement.dataset.theme = theme === 'system' ? (system.matches ? 'dark' : 'light') : theme;
        document.documentElement.dataset.motion = motion;
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', document.documentElement.dataset.theme === 'dark' ? '#111111' : '#f7f7f7');
    }
    system.addEventListener('change', apply);
    window.addEventListener('zapara-appearance', apply);
    window.addEventListener('storage', apply);
    apply();
})();
