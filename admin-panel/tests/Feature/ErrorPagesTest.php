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
}
