<?php

namespace App\Providers\Filament;

use Filament\Http\Middleware\Authenticate;
use Filament\Http\Middleware\AuthenticateSession;
use Filament\Http\Middleware\DisableBladeIconComponents;
use Filament\Http\Middleware\DispatchServingFilamentEvent;
use App\Filament\Pages\Login;
use App\Filament\Pages\Security;
use App\Auth\ThrottledAppAuthentication;
use Filament\Pages\Dashboard;
use Filament\Panel;
use App\Filament\Navigation\AdminNavigation;
use Filament\Facades\Filament;
use Filament\Navigation\NavigationBuilder;
use Filament\View\PanelsRenderHook;
use Illuminate\Contracts\View\View;
use Filament\PanelProvider;
use Filament\Support\Colors\Color;
use Illuminate\Support\Facades\Route;
use Illuminate\Cookie\Middleware\AddQueuedCookiesToResponse;
use Illuminate\Cookie\Middleware\EncryptCookies;
use Illuminate\Foundation\Http\Middleware\PreventRequestForgery;
use Illuminate\Routing\Middleware\SubstituteBindings;
use Illuminate\Session\Middleware\StartSession;
use Illuminate\View\Middleware\ShareErrorsFromSession;

class AdminPanelProvider extends PanelProvider
{
    public function panel(Panel $panel): Panel
    {
        return $panel
            ->default()
            ->id('admin')
            ->path('admin')
            ->login(Login::class)
            ->profile(Security::class, isSimple: false)
            // TOTP (RFC 6238) with one-time recovery codes. Enforcement is a deploy switch, see admin-panel/README.md.
            // codeWindow(1): the current 30-second step and one on each side (Filament's default accepts ±4 minutes).
            // Wrong codes are limited per admin (App\Auth\MfaThrottle); an accepted code cannot be reused.
            ->multiFactorAuthentication(
                [
                    ThrottledAppAuthentication::make()
                        ->brandName('Расписание военмех')
                        ->codeWindow(1)
                        ->recoverable(),
                ],
                isRequired: fn (): bool => (bool) config('zapara.admin_mfa_required'),
            )
            ->brandName('Расписание военмех')
            // Цвета из общих дизайн-токенов (#11): config/design-tokens.php генерирует scripts/design/tokens.mjs.
            ->colors(array_map(
                // Оттенок 600 — фон кнопок и бейджей Filament: ставим ровно цвет токена, контраст которого проверен.
                fn (string|array $value): array => is_array($value) ? $value : array_replace(Color::hex($value), [600 => $value]),
                config('design-tokens.filament'),
            ))
            ->renderHook(\Filament\View\PanelsRenderHook::HEAD_END, fn (): \Illuminate\Contracts\View\View => view('filament.design-tokens'))
            ->discoverResources(in: app_path('Filament/Resources'), for: 'App\Filament\Resources')
            ->discoverPages(in: app_path('Filament/Pages'), for: 'App\Filament\Pages')
            ->pages([
                Dashboard::class,
            ])
            // Контраст полей форм (#25); полные имена классов, чтобы не трогать блок use (его меняют #4 и #26).
            ->renderHook(\Filament\View\PanelsRenderHook::HEAD_END, fn (): \Illuminate\Contracts\View\View => view('filament.admin-forms'))
            ->authenticatedRoutes(function (): void {
                Route::get('/support-files/{id}', [\App\Http\Controllers\SupportFileController::class, 'show'])
                    ->name('support-file');
            })
            ->discoverWidgets(in: app_path('Filament/Widgets'), for: 'App\Filament\Widgets')
            ->widgets([])
            ->navigation(fn (NavigationBuilder $builder): NavigationBuilder => AdminNavigation::build($builder, Filament::getCurrentOrDefaultPanel()))
            ->renderHook(PanelsRenderHook::HEAD_END, fn (): View => view('filament.admin-theme'))
            ->middleware([
                EncryptCookies::class,
                AddQueuedCookiesToResponse::class,
                StartSession::class,
                AuthenticateSession::class,
                ShareErrorsFromSession::class,
                PreventRequestForgery::class,
                SubstituteBindings::class,
                DisableBladeIconComponents::class,
                DispatchServingFilamentEvent::class,
            ])
            ->authMiddleware([
                Authenticate::class,
            ]);
    }
}
