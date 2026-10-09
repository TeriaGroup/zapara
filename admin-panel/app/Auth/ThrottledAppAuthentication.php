<?php

namespace App\Auth;

use Filament\Auth\MultiFactor\App\AppAuthentication;
use Filament\Auth\MultiFactor\App\Contracts\HasAppAuthentication;
use Filament\Auth\MultiFactor\App\Contracts\HasAppAuthenticationRecovery;
use Filament\Facades\Filament;
use Illuminate\Contracts\Auth\Authenticatable;
use SensitiveParameter;

/**
 * Filament's TOTP provider with a per-administrator limit on wrong codes ({@see MfaThrottle}).
 * While the limit is reached every code is refused, including a correct one, until the window ends.
 */
class ThrottledAppAuthentication extends AppAuthentication
{
    public function verifyCode(#[SensitiveParameter] string $code, #[SensitiveParameter] ?string $secret = null, bool $shouldPreventCodeReuse = false): bool
    {
        if ($secret === null) {
            /** @var HasAppAuthentication $user */
            $user = Filament::auth()->user();
            $secret = $this->getSecret($user);
        }
        // During login the admin is not authenticated yet; the secret is unique per admin, so it names the admin.
        $admin = self::adminForSecret($secret);
        $throttle = app(MfaThrottle::class);
        if ($throttle->tooManyFailures($admin)) {
            return false;
        }
        $valid = parent::verifyCode($code, $secret, $shouldPreventCodeReuse);
        $valid ? $throttle->succeeded($admin) : $throttle->failed($admin);

        return $valid;
    }

    public function verifyRecoveryCode(#[SensitiveParameter] string $recoveryCode, ?HasAppAuthenticationRecovery $user = null): bool
    {
        $user ??= Filament::auth()->user();
        $admin = $user instanceof HasAppAuthentication && filled($user->getAppAuthenticationSecret())
            ? self::adminForSecret($this->getSecret($user))
            : 'user:'.($user instanceof Authenticatable ? $user->getAuthIdentifier() : spl_object_id($user));
        $throttle = app(MfaThrottle::class);
        if ($throttle->tooManyFailures($admin)) {
            return false;
        }
        $valid = parent::verifyRecoveryCode($recoveryCode, $user);
        $valid ? $throttle->succeeded($admin) : $throttle->failed($admin);

        return $valid;
    }

    /** Same management actions as Filament's, with a stable key (used by tests to reach the actions). */
    public function getManagementSchemaComponents(): array
    {
        $components = parent::getManagementSchemaComponents();
        if (isset($components[0]) && method_exists($components[0], 'key')) {
            $components[0]->key('mfa-app-actions');
        }

        return $components;
    }

    public static function adminForSecret(#[SensitiveParameter] string $secret): string
    {
        return 'totp:'.hash('sha256', $secret);
    }
}
