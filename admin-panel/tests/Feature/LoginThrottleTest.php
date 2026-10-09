<?php

namespace Tests\Feature;

use App\Auth\LoginThrottle;
use App\Filament\Pages\Login;
use Filament\Facades\Filament;
use Illuminate\Support\Facades\DB;
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

    public function test_distributed_failures_delay_failing_networks_but_not_clean_ones(): void
    {
        $throttle = new LoginThrottle;
        for ($i = 0; $i < 30; $i++) {
            $throttle->failed('platform.admin', '4:192.0.2.'.$i);
        }
        $wait = $throttle->availableIn('platform.admin', '4:192.0.2.1');
        $this->assertGreaterThan(0, $wait);
        $this->assertLessThanOrEqual(LoginThrottle::MAX_ACCOUNT_DELAY_SECONDS, $wait);
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
}
