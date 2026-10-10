{{--
    #149 (R3-01): страницы ошибок в стиле панели вместо стандартных Laravel «404 | Not Found».
    Самодостаточны: без базы, сессии и сборки Vite (страница 500 должна открываться, даже когда сломано остальное).
    Цвета — общие токены (filament.design-tokens), тёмная тема — как выбрано в панели (localStorage theme), иначе как в системе.
--}}
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="robots" content="noindex">
    <title>@yield('title') · Расписание военмех</title>
    <script>
        (() => {
            let theme = 'system';
            try { theme = localStorage.getItem('theme') ?? 'system'; } catch (e) {}
            if (theme === 'dark' || (theme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches)) {
                document.documentElement.classList.add('dark');
            }
        })();
    </script>
    <link rel="stylesheet" href="{{ asset('fonts/filament/filament/inter/index.css') }}">
    @include('filament.design-tokens')
    <style>
        *, *::before, *::after { box-sizing: border-box; }
        html { color-scheme: light; }
        html.dark { color-scheme: dark; }
        body {
            margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 24px 16px;
            background: var(--zp-surface-1); color: var(--zp-text-primary);
            font: 400 16px/1.5 Inter, ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
        }
        .zp-error { width: 100%; max-width: 28rem; }
        .zp-error-brand { margin: 0 0 16px; text-align: center; font-weight: 700; font-size: 18px; }
        .zp-error-card {
            background: var(--zp-surface-0); border: 1px solid var(--zp-border-subtle); border-radius: 12px; padding: 32px 24px;
            box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
        }
        .zp-error-code { margin: 0 0 4px; color: var(--zp-text-secondary); font-size: 14px; font-weight: 500; font-variant-numeric: tabular-nums; }
        .zp-error h1 { margin: 0 0 8px; font-size: 24px; line-height: 1.3; font-weight: 700; }
        .zp-error p.zp-error-text { margin: 0 0 24px; color: var(--zp-text-secondary); }
        .zp-error-actions { display: flex; flex-wrap: wrap; gap: 8px; }
        .zp-error-actions a {
            display: inline-flex; align-items: center; justify-content: center; min-height: 44px; padding: 0 16px; border-radius: 8px;
            font-weight: 600; font-size: 14px; text-decoration: none; border: 1px solid var(--zp-border-control); color: var(--zp-text-primary);
        }
        .zp-error-actions a.zp-primary { background: var(--zp-accent); color: var(--zp-on-accent); border-color: var(--zp-accent); }
        .zp-error-actions a:focus-visible { outline: 2px solid var(--zp-focus); outline-offset: 2px; }
        .zp-error-actions a:not(.zp-primary):hover { background: var(--zp-surface-pressed); }
        @media (max-width: 480px) { .zp-error-actions a { flex: 1 1 100%; } }
    </style>
</head>
<body>
    <main class="zp-error" aria-labelledby="zp-error-title">
        <p class="zp-error-brand">Расписание военмех</p>
        <section class="zp-error-card">
            <p class="zp-error-code">Ошибка @yield('code')</p>
            <h1 id="zp-error-title">@yield('title')</h1>
            <p class="zp-error-text">@yield('message')</p>
            <div class="zp-error-actions">
                @hasSection('actions')
                    @yield('actions')
                @else
                    <a class="zp-primary" href="{{ url('/admin') }}">На инфопанель</a>
                @endif
            </div>
        </section>
    </main>
</body>
</html>
