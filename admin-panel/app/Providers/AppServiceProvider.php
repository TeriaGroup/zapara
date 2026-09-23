<?php

namespace App\Providers;

use App\Auth\AccountUserProvider;
use App\Support\Zapara;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\ServiceProvider;

class AppServiceProvider extends ServiceProvider
{
    public function register(): void
    {
        $raw = Zapara::dsn();
        if ($raw === null) {
            return;
        }
        $parsed = Zapara::parseDsn($raw);
        config([
            'database.default' => 'pgsql',
            'database.connections.pgsql.host' => $parsed['host'],
            'database.connections.pgsql.port' => (string) $parsed['port'],
            'database.connections.pgsql.database' => $parsed['database'],
            'database.connections.pgsql.username' => $parsed['username'],
            'database.connections.pgsql.password' => $parsed['password'],
            'database.connections.pgsql.search_path' => 'public',
        ]);
    }

    public function boot(): void
    {
        $this->app->setLocale('ru');
        Auth::provider('accounts', function ($app, array $config) {
            return new AccountUserProvider($app['hash'], $config['model']);
        });
    }
}
