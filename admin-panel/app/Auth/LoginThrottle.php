<?php

namespace App\Auth;

use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\RateLimiter;

/**
 * Failed-login throttling for the admin panel that never locks an account for everyone.
 *
 * Mirrors Zapara.Server.Accounts.LoginThrottle: failures are counted per client network
 * (IPv4 address, IPv6 /64), per wider IPv6 block (/48) and per (account, network) pair, so the network that
 * keeps failing is blocked. Failures against one account from many networks only add a progressive delay
 * (1 s doubling up to 60 s after the last failure) for every network; the account is never denied outright.
 */
final class LoginThrottle
{
    public const WINDOW_SECONDS = 900;

    public const NETWORK_FAILURE_LIMIT = 20;

    /** Failures one IPv6 /48 may make across all its /64 networks per window. */
    public const WIDE_NETWORK_FAILURE_LIMIT = 60;

    public const PAIR_FAILURE_LIMIT = 5;

    public const ACCOUNT_DELAY_THRESHOLD = 10;

    public const MAX_ACCOUNT_DELAY_SECONDS = 60;

    /** Full IPv4 address, or the /64 prefix of an IPv6 address. */
    public static function networkKey(?string $ip): string
    {
        $packed = is_string($ip) ? @inet_pton($ip) : false;
        if ($packed === false) {
            return 'none';
        }
        if (strlen($packed) === 16 && str_starts_with($packed, str_repeat("\0", 10)."\xff\xff")) {
            $packed = substr($packed, 12);
        }
        if (strlen($packed) === 4) {
            return '4:'.inet_ntop($packed);
        }

        return '6:'.bin2hex(substr($packed, 0, 8)).'::/64';
    }

    /** The IPv6 /48 containing a /64 network key, or null for IPv4 and unknown networks. */
    public static function wideNetworkKey(string $network): ?string
    {
        if (! str_starts_with($network, '6:') || strlen($network) < 14) {
            return null;
        }

        return '6:'.substr($network, 2, 12).'::/48';
    }

    public static function normalizeAccount(mixed $username): string
    {
        return is_string($username) ? strtolower(trim($username)) : '';
    }

    /** Seconds the client must wait before this attempt may be checked; 0 when it may proceed. */
    public function availableIn(string $account, string $network): int
    {
        $networkKey = $this->key('network', $network);
        if (RateLimiter::attempts($networkKey) >= self::NETWORK_FAILURE_LIMIT) {
            return max(1, RateLimiter::availableIn($networkKey));
        }
        $wide = self::wideNetworkKey($network);
        if ($wide !== null) {
            $wideKey = $this->key('wide', $wide);
            if (RateLimiter::attempts($wideKey) >= self::WIDE_NETWORK_FAILURE_LIMIT) {
                return max(1, RateLimiter::availableIn($wideKey));
            }
        }
        $pairKey = $this->key('pair', $account."\n".$network);
        if (RateLimiter::attempts($pairKey) >= self::PAIR_FAILURE_LIMIT) {
            return max(1, RateLimiter::availableIn($pairKey));
        }
        $failures = (int) RateLimiter::attempts($this->key('account', $account));
        $delay = self::accountDelay($failures);
        if ($delay === 0) {
            return 0;
        }
        $last = (int) Cache::get($this->key('account-last', $account), 0);
        $ready = $last + $delay;

        return $ready > now()->getTimestamp() ? $ready - now()->getTimestamp() : 0;
    }

    public function failed(string $account, string $network): void
    {
        RateLimiter::hit($this->key('network', $network), self::WINDOW_SECONDS);
        $wide = self::wideNetworkKey($network);
        if ($wide !== null) {
            RateLimiter::hit($this->key('wide', $wide), self::WINDOW_SECONDS);
        }
        RateLimiter::hit($this->key('pair', $account."\n".$network), self::WINDOW_SECONDS);
        RateLimiter::hit($this->key('account', $account), self::WINDOW_SECONDS);
        Cache::put($this->key('account-last', $account), now()->getTimestamp(), self::WINDOW_SECONDS);
    }

    /** The network counter stays: a valid login must not reset failures the network made against other accounts. */
    public function succeeded(string $account, string $network): void
    {
        RateLimiter::clear($this->key('pair', $account."\n".$network));
        RateLimiter::clear($this->key('account', $account));
        Cache::forget($this->key('account-last', $account));
    }

    /** 1 s at the threshold, doubling per further failure, capped. */
    public static function accountDelay(int $failures): int
    {
        if ($failures < self::ACCOUNT_DELAY_THRESHOLD) {
            return 0;
        }

        return min(1 << min($failures - self::ACCOUNT_DELAY_THRESHOLD, 16), self::MAX_ACCOUNT_DELAY_SECONDS);
    }

    /** Usernames and addresses are not stored in the cache in plain text. */
    private function key(string $kind, string $value): string
    {
        return 'admin-login:'.$kind.':'.hash_hmac('sha256', $value, (string) config('app.key'));
    }
}
