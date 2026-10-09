<?php

namespace App\Auth;

use Illuminate\Support\Facades\RateLimiter;

/**
 * Limits wrong two-factor codes per administrator, independent of the client address: whoever reaches the code
 * step already has the password, so the limit follows the admin, not the network. Wrong TOTP and recovery codes
 * share one counter. A correct code clears it.
 *
 * Kept separate from the password-step throttling (App\Auth\LoginThrottle in the login-throttle change); both can
 * be merged into one place once that change is in.
 */
final class MfaThrottle
{
    public const FAILURE_LIMIT = 5;

    public const WINDOW_SECONDS = 900;

    public function tooManyFailures(string $admin): bool
    {
        return RateLimiter::tooManyAttempts($this->key($admin), self::FAILURE_LIMIT);
    }

    public function availableIn(string $admin): int
    {
        return RateLimiter::availableIn($this->key($admin));
    }

    public function failed(string $admin): void
    {
        RateLimiter::hit($this->key($admin), self::WINDOW_SECONDS);
    }

    public function succeeded(string $admin): void
    {
        RateLimiter::clear($this->key($admin));
    }

    /** Admin ids are not stored in the cache in plain text. */
    private function key(string $admin): string
    {
        return 'admin-mfa:'.hash_hmac('sha256', $admin, (string) config('app.key'));
    }
}
