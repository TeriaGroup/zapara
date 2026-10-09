<?php

namespace App\Auth;

use Illuminate\Contracts\Hashing\Hasher;
use SensitiveParameter;

/**
 * Default hasher of the panel. Account passwords live in Zapara.Server's accounts schema as ASP.NET Identity V3
 * hashes; those are checked with {@see IdentityPassword}. Everything the panel hashes itself (for example MFA
 * recovery codes) still goes through the wrapped bcrypt hasher.
 *
 * Needed for Laravel's `current_password` rule, which Filament uses to confirm the password before setting up
 * or disabling two-factor authentication.
 */
final class ZaparaHasher implements Hasher
{
    public function __construct(private readonly Hasher $inner) {}

    public static function isIdentityHash(mixed $hashedValue): bool
    {
        if (! is_string($hashedValue) || $hashedValue === '' || str_starts_with($hashedValue, '$')) {
            return false;
        }
        $decoded = base64_decode($hashedValue, true);

        // 0x01: Identity v3; 0x00: Identity v2, recognised only so that it is rejected instead of reaching bcrypt.
        return $decoded !== false && strlen($decoded) >= 13 && in_array(ord($decoded[0]), [0x00, 0x01], true);
    }

    public function info($hashedValue): array
    {
        return self::isIdentityHash($hashedValue)
            ? ['algo' => 'aspnet-identity-v3', 'algoName' => 'aspnet-identity-v3', 'options' => []]
            : $this->inner->info($hashedValue);
    }

    public function make(#[SensitiveParameter] $value, array $options = []): string
    {
        return $this->inner->make($value, $options);
    }

    public function check(#[SensitiveParameter] $value, $hashedValue, array $options = []): bool
    {
        if (self::isIdentityHash($hashedValue)) {
            return is_string($value) && IdentityPassword::verify($hashedValue, $value);
        }

        return $this->inner->check($value, $hashedValue, $options);
    }

    public function needsRehash($hashedValue, array $options = []): bool
    {
        // Identity hashes belong to Zapara.Server; the panel never rewrites them.
        return self::isIdentityHash($hashedValue) ? false : $this->inner->needsRehash($hashedValue, $options);
    }
}
