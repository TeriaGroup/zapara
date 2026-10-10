<?php

namespace Tests\Feature;

use Illuminate\Support\Facades\Route;
use Tests\TestCase;

/**
 * #149 (R3-01): страницы ошибок — в стиле панели, по-русски, с тёмной темой и путём назад,
 * без английских «Not Found» / «Server Error» и без подробностей исключения.
 */
class ErrorPagesTest extends TestCase
{
    protected function setUp(): void
    {
        parent::setUp();
        config(['app.debug' => false]);
        Route::get('/__zp-error/500', fn () => throw new \RuntimeException('секретная-деталь-исключения'));
        Route::post('/__zp-error/500', fn () => throw new \RuntimeException('секретная-деталь-исключения'));
        Route::get('/__zp-error/502', fn () => abort(502, 'detail-from-abort'));
        Route::get('/__zp-error/418', fn () => abort(418, 'detail-from-abort'));
        foreach ([403, 419, 503] as $code) {
            Route::get("/__zp-error/{$code}", fn () => abort($code, 'detail-from-abort'));
        }
    }

    private function assertThemedPage(string $html): void
    {
        $this->assertStringContainsString('<html lang="ru">', $html);
        $this->assertStringContainsString('--zp-surface-0', $html, 'design tokens');
        $this->assertStringContainsString('.dark {', $html, 'dark tokens');
        $this->assertStringContainsString("classList.add('dark')", $html, 'dark mode script');
        $this->assertStringContainsString("localStorage.getItem('theme')", $html, 'panel theme choice');
        $this->assertStringContainsString('prefers-color-scheme: dark', $html, 'system theme');
        $this->assertStringContainsString('Расписание военмех', $html);
        foreach (['Not Found', 'Server Error', 'Forbidden', 'Page Expired', 'Service Unavailable', 'Method Not Allowed', 'Bad Gateway'] as $english) {
            $this->assertStringNotContainsString($english, $html);
        }
    }

    public function test_unknown_admin_page_is_a_themed_russian_404_with_a_way_back(): void
    {
        $html = (string) $this->get('/admin/no-such-page')->assertNotFound()->getContent();
        $this->assertThemedPage($html);
        $this->assertStringContainsString('Страница не найдена', $html);
        $this->assertStringContainsString('<a class="zp-primary" href="'.url('/admin').'">На инфопанель</a>', $html);
    }

    public function test_server_error_is_themed_and_hides_the_exception(): void
    {
        $html = (string) $this->get('/__zp-error/500')->assertStatus(500)->getContent();
        $this->assertThemedPage($html);
        $this->assertStringContainsString('Что-то пошло не так', $html);
        $this->assertStringContainsString('href="'.url('/admin').'">На инфопанель</a>', $html);
        $this->assertStringNotContainsString('секретная-деталь-исключения', $html);
        $this->assertStringNotContainsString('RuntimeException', $html);
    }

    /** @return array<string, array{int, string, string}> */
    public static function otherCodes(): array
    {
        return [
            '403' => [403, 'Нет доступа', '/admin'],
            '419' => [419, 'Сессия истекла', '/admin/login'],
            '503' => [503, 'Панель временно недоступна', '/__zp-error/503'],
        ];
    }

    /** @dataProvider otherCodes */
    #[\PHPUnit\Framework\Attributes\DataProvider('otherCodes')]
    public function test_other_error_pages_are_themed_with_an_action(int $code, string $title, string $href): void
    {
        $html = (string) $this->get("/__zp-error/{$code}")->assertStatus($code)->getContent();
        $this->assertThemedPage($html);
        $this->assertStringContainsString($title, $html);
        $this->assertStringContainsString('<a class="zp-primary" href="'.url($href).'">', $html);
        $this->assertStringNotContainsString('detail-from-abort', $html);
    }

    public function test_actions_are_44px_targets(): void
    {
        $html = (string) $this->get('/admin/no-such-page')->getContent();
        $this->assertMatchesRegularExpression('/\.zp-error-actions a \{[^}]*min-height: 44px;/', $html);
    }

    public function test_get_on_the_logout_route_is_a_themed_405_not_the_english_default(): void
    {
        $response = $this->get('/admin/logout');
        $response->assertStatus(405);
        $html = (string) $response->getContent();
        $this->assertThemedPage($html);
        $this->assertStringContainsString('Ошибка 405', $html);
        $this->assertStringContainsString('Запрос не выполнен', $html);
        $this->assertStringContainsString('<a class="zp-primary" href="'.url('/admin').'">На инфопанель</a>', $html);
    }

    public function test_other_4xx_and_5xx_use_the_generic_fallbacks(): void
    {
        $teapot = (string) $this->get('/__zp-error/418')->assertStatus(418)->getContent();
        $this->assertThemedPage($teapot);
        $this->assertStringContainsString('Ошибка 418', $teapot);
        $this->assertStringNotContainsString('detail-from-abort', $teapot);

        $gateway = (string) $this->get('/__zp-error/502')->assertStatus(502)->getContent();
        $this->assertThemedPage($gateway);
        $this->assertStringContainsString('Ошибка 502', $gateway);
        $this->assertStringContainsString('Подробности записаны в журнал сервера.', $gateway);
        $this->assertStringNotContainsString('detail-from-abort', $gateway);
    }

    public function test_500_copy_and_reload_link_returns_to_the_page_a_post_came_from(): void
    {
        $from = url('/admin/users/create');
        $html = (string) $this->withHeader('Referer', $from)->post('/__zp-error/500')->assertStatus(500)->getContent();
        $this->assertStringContainsString('Подробности записаны в журнал сервера.', $html);
        $this->assertStringNotContainsString('Мы уже записали', $html);
        $this->assertStringContainsString('<a href="'.$from.'">Обновить страницу</a>', $html);
        $this->assertStringNotContainsString('href="'.url('/__zp-error/500').'"', $html);
    }

    public function test_403_says_account_not_uchetnaya_zapis(): void
    {
        $html = (string) $this->get('/__zp-error/403')->getContent();
        $this->assertStringContainsString('У вашего аккаунта нет прав', $html);
        $this->assertStringNotContainsString('учётной записи', $html);
    }

    public function test_env_example_ships_with_debug_off(): void
    {
        $env = (string) file_get_contents(base_path('.env.example'));
        $this->assertMatchesRegularExpression('/^APP_DEBUG=false$/m', $env);
        $this->assertDoesNotMatchRegularExpression('/^APP_DEBUG=true$/m', $env);
    }

    public function test_500_and_5xx_render_without_touching_the_session(): void
    {
        // StartSession упал (например, недоступен драйвер сессий) — страница ошибки не должна обращаться к сессии.
        // Раньше «Обновить страницу» брала url()->previous(), а это чтение сессии.
        $request = \Illuminate\Http\Request::create('/admin/users/1/edit', 'POST', server: ['HTTP_REFERER' => 'http://localhost/admin/users']);
        $this->app->instance('request', $request);
        $boom = fn () => throw new \RuntimeException('session store unavailable');
        app('url')->setRequest($request);
        app('url')->setSessionResolver($boom);
        $this->app->bind('session', $boom);
        $this->app->bind('session.store', $boom);

        foreach (['errors.500', 'errors.5xx', 'errors.503'] as $view) {
            $html = view($view, ['exception' => new \Symfony\Component\HttpKernel\Exception\HttpException(502)])->render();
            $this->assertStringContainsString('Расписание военмех', $html, $view);
        }
        $this->assertStringContainsString('<a href="http://localhost/admin/users">Обновить страницу</a>', view('errors.500')->render());

        // Без Referer url()->previous() шёл бы в сессию; теперь — на инфопанель, сессия не нужна.
        $bare = \Illuminate\Http\Request::create('/admin/users/1/edit', 'POST');
        $this->app->instance('request', $bare);
        app('url')->setRequest($bare);
        $this->assertStringContainsString('<a href="http://localhost/admin">Обновить страницу</a>', view('errors.500')->render());
        $this->assertStringContainsString('Обновить страницу', view('errors.5xx', ['exception' => new \Symfony\Component\HttpKernel\Exception\HttpException(502)])->render());
    }

    public function test_back_link_ignores_foreign_or_missing_referers(): void
    {
        foreach (['https://evil.example/phish', 'javascript:alert(1)', '//evil.example/x', ''] as $referer) {
            $request = $referer === '' ? $this : $this->withHeader('Referer', $referer);
            $html = (string) $request->post('/__zp-error/500')->assertStatus(500)->getContent();
            $this->assertStringContainsString('<a href="'.url('/admin').'">Обновить страницу</a>', $html, $referer);
            if ($referer !== '') {
                $this->assertStringNotContainsString($referer, $html);
            }
            $this->flushHeaders();
        }
    }

    public function test_503_retry_keeps_the_query_string(): void
    {
        $html = (string) $this->get('/__zp-error/503?tab=quotas&page=2')->assertStatus(503)->getContent();
        $this->assertStringContainsString('href="'.url('/__zp-error/503').'?page=2&amp;tab=quotas"', $html);
    }
}
