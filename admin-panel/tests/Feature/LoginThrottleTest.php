<?php

namespace Tests\Feature;

use App\Auth\LoginThrottle;
use App\Filament\Pages\Login;
use Filament\Facades\Filament;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Route;
use Livewire\Livewire;
use PHPUnit\Framework\Attributes\DataProvider;
use Tests\TestCase;

class LoginThrottleTest extends TestCase
{
    public static function networks(): array
    {
        return [
            ['203.0.113.9', '4:203.0.113.9'],
            ['::ffff:203.0.113.9', '4:203.0.113.9'],
            ['2001:db8:1:2:aaaa:bbbb:cccc:dddd', '6:20010db800010002::/64'],
            ['2001:db8:1:2::1', '6:20010db800010002::/64'],
            ['2001:db8:1:3::1', '6:20010db800010003::/64'],
            [null, 'none'],
            ['not an ip', 'none'],
        ];
    }

    #[DataProvider('networks')]
    public function test_network_key_uses_full_ipv4_and_ipv6_slash_64(?string $ip, string $expected): void
    {
        $this->assertSame($expected, LoginThrottle::networkKey($ip));
    }

    public function test_pair_limit_blocks_only_that_network_for_that_account(): void
    {
        $throttle = new LoginThrottle;
        for ($i = 0; $i < LoginThrottle::PAIR_FAILURE_LIMIT; $i++) {
            $this->assertSame(0, $throttle->availableIn('platform.admin', '4:198.51.100.1'));
            $throttle->failed('platform.admin', '4:198.51.100.1');
        }
        $this->assertGreaterThan(0, $throttle->availableIn('platform.admin', '4:198.51.100.1'));
        $this->assertSame(0, $throttle->availableIn('platform.admin', '4:198.51.100.2'));
        $this->assertSame(0, $throttle->availableIn('someone.else', '4:198.51.100.1'));
        $this->travel(LoginThrottle::WINDOW_SECONDS + 1)->seconds();
        $this->assertSame(0, $throttle->availableIn('platform.admin', '4:198.51.100.1'));
    }

    public function test_network_limit_blocks_a_network_spraying_many_accounts(): void
    {
        $throttle = new LoginThrottle;
        for ($i = 0; $i < LoginThrottle::NETWORK_FAILURE_LIMIT; $i++) {
            $throttle->failed('user'.$i, '6:20010db8000000aa::/64');
        }
        $this->assertGreaterThan(0, $throttle->availableIn('fresh.user', '6:20010db8000000aa::/64'));
        $this->assertSame(0, $throttle->availableIn('fresh.user', '6:20010db8000000ab::/64'));
    }

    public function test_distributed_failures_delay_every_network_up_to_the_cap(): void
    {
        $throttle = new LoginThrottle;
        for ($i = 0; $i < 30; $i++) {
            $throttle->failed('platform.admin', '4:192.0.2.'.$i);
        }
        foreach (['4:192.0.2.1', '4:203.0.113.50'] as $network) {
            $wait = $throttle->availableIn('platform.admin', $network);
            $this->assertGreaterThan(0, $wait);
            $this->assertLessThanOrEqual(LoginThrottle::MAX_ACCOUNT_DELAY_SECONDS, $wait);
        }
        $this->travel(LoginThrottle::MAX_ACCOUNT_DELAY_SECONDS)->seconds();
        $this->assertSame(0, $throttle->availableIn('platform.admin', '4:203.0.113.50'));
    }

    public function test_success_clears_account_counters_but_not_the_network(): void
    {
        $throttle = new LoginThrottle;
        for ($i = 0; $i < LoginThrottle::NETWORK_FAILURE_LIMIT - 1; $i++) {
            $throttle->failed('other'.$i, '4:198.51.100.9');
        }
        $throttle->failed('mine', '4:198.51.100.9');
        $throttle->succeeded('mine', '4:198.51.100.9');
        $this->assertGreaterThan(0, $throttle->availableIn('mine', '4:198.51.100.9'));
    }

    public function test_account_delay_is_progressive_and_capped(): void
    {
        $this->assertSame(0, LoginThrottle::accountDelay(9));
        $this->assertSame(1, LoginThrottle::accountDelay(10));
        $this->assertSame(8, LoginThrottle::accountDelay(13));
        $this->assertSame(60, LoginThrottle::accountDelay(16));
        $this->assertSame(60, LoginThrottle::accountDelay(10_000));
    }

    public function test_throttled_login_stops_before_any_credential_lookup(): void
    {
        Filament::setCurrentPanel(Filament::getPanel('admin'));
        $network = LoginThrottle::networkKey('127.0.0.1');
        for ($i = 0; $i < LoginThrottle::PAIR_FAILURE_LIMIT; $i++) {
            app(LoginThrottle::class)->failed('platform.admin', $network);
        }
        $queries = 0;
        DB::listen(function () use (&$queries): void {
            $queries++;
        });

        Livewire::test(Login::class)
            ->set('data.username', 'Platform.Admin')
            ->set('data.password', 'whatever-password')
            ->call('authenticate')
            ->assertNotified()
            ->assertNoRedirect();

        $this->assertSame(0, $queries);
    }

    public function test_wide_key_is_the_ipv6_slash_48(): void
    {
        $this->assertSame('6:20010db80001::/48', LoginThrottle::wideNetworkKey('6:20010db8000100aa::/64'));
        $this->assertNull(LoginThrottle::wideNetworkKey('4:203.0.113.9'));
        $this->assertNull(LoginThrottle::wideNetworkKey('none'));
    }

    public function test_wide_tier_blocks_a_slash_48_rotating_slash_64_networks(): void
    {
        $throttle = new LoginThrottle;
        for ($i = 0; $i < LoginThrottle::WIDE_NETWORK_FAILURE_LIMIT; $i++) {
            $network = LoginThrottle::networkKey(sprintf('2001:db8:1:%x::1', $i));
            $this->assertSame(0, $throttle->availableIn('user'.$i, $network));
            $throttle->failed('user'.$i, $network);
        }
        $this->assertGreaterThan(0, $throttle->availableIn('fresh.user', LoginThrottle::networkKey('2001:db8:1:ffff::1')));
        $this->assertSame(0, $throttle->availableIn('fresh.user', LoginThrottle::networkKey('2001:db8:2::1')));
        $this->assertSame(0, $throttle->availableIn('fresh.user', '4:198.51.100.1'));
    }

    public function test_many_networks_cannot_lock_an_account(): void
    {
        $throttle = new LoginThrottle;
        $owner = LoginThrottle::networkKey('2001:db8:ffff::1');
        $start = now()->getTimestamp();
        $guesses = 0;
        for ($i = 0; $i < 1000 && now()->getTimestamp() < $start + 1800; $i++) {
            $network = LoginThrottle::networkKey(sprintf('2001:db8:%x::1', $i));
            $wait = $throttle->availableIn('platform.admin', $network);
            $this->assertLessThanOrEqual(LoginThrottle::MAX_ACCOUNT_DELAY_SECONDS, $wait);
            if ($wait > 0) {
                $this->travel($wait)->seconds();
            }
            $this->assertSame(0, $throttle->availableIn('platform.admin', $network));
            $throttle->failed('platform.admin', $network);
            $guesses++;
            $this->assertLessThanOrEqual(LoginThrottle::MAX_ACCOUNT_DELAY_SECONDS, $throttle->availableIn('platform.admin', $owner));
        }
        // The delay still spaces out guesses from fresh networks to about one per minute.
        $this->assertGreaterThanOrEqual(LoginThrottle::ACCOUNT_DELAY_THRESHOLD, $guesses);
        $this->assertLessThanOrEqual(60, $guesses);
        $this->assertSame(0, $throttle->availableIn('other.user', $owner));
        $this->travel(LoginThrottle::MAX_ACCOUNT_DELAY_SECONDS)->seconds();
        $this->assertSame(0, $throttle->availableIn('platform.admin', $owner));
    }

    public function test_forwarded_for_is_trusted_only_from_configured_proxies(): void
    {
        Route::get('/_test/ip', fn (Request $request) => $request->ip());

        // Default: private ranges (the Docker network) are trusted, public peers are not.
        $this->withServerVariables(['REMOTE_ADDR' => '172.18.0.3'])->withHeader('X-Forwarded-For', '203.0.113.7')
            ->get('/_test/ip')->assertSeeText('203.0.113.7');
        $this->withServerVariables(['REMOTE_ADDR' => '198.51.100.5'])->withHeader('X-Forwarded-For', '203.0.113.7')
            ->get('/_test/ip')->assertSeeText('198.51.100.5');

        // A narrower configured network excludes everything else.
        config(['trustedproxy.proxies' => ['172.18.0.0/16']]);
        $this->withServerVariables(['REMOTE_ADDR' => '10.0.0.2'])->withHeader('X-Forwarded-For', '203.0.113.7')
            ->get('/_test/ip')->assertSeeText('10.0.0.2');
        $this->withServerVariables(['REMOTE_ADDR' => '172.18.5.9'])->withHeader('X-Forwarded-For', '203.0.113.7')
            ->get('/_test/ip')->assertSeeText('203.0.113.7');
    }
}
