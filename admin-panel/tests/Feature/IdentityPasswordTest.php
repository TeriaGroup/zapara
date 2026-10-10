<?php

namespace Tests\Feature;

use App\Auth\IdentityPassword;
use App\Auth\ZaparaHasher;
use Illuminate\Support\Facades\Hash;
use PHPUnit\Framework\Attributes\DataProvider;
use Tests\TestCase;

/**
 * Fixtures are real hashes produced by .NET (Microsoft.AspNetCore.Identity PasswordHasher) and verified there:
 * - server_sha512: PasswordHasher with the server's options (IdentityV3, IterationCount = 100000), .NET 8;
 * - default_sha512: PasswordHasher with default options, .NET 8;
 * - identity6_sha256: PasswordHasher with default options from Microsoft.Extensions.Identity.Core 6.0 (HMAC-SHA256,
 *   10 000 iterations), the format older accounts may still carry;
 * - v3_sha1: v3 layout with PRF 0 (HMAC-SHA1, 10 000 iterations) built with KeyDerivation.Pbkdf2; PasswordHasher
 *   accepts it but has no option to write it;
 * - v2: PasswordHasher in IdentityV2 compatibility mode; the server never writes it and the panel rejects it.
 */
class IdentityPasswordTest extends TestCase
{
    private const SERVER_SHA512 = 'AQAAAAIAAYagAAAAEBkyvG7mmN2TaCX5yCLhJgNNDZsRKhucia+issyLYRxKWR9F5r5HNV3GZ209T8MJIw==';

    private const SERVER_SHA512_PASSWORD = 'Panel-password-1';

    public static function dotnetHashes(): array
    {
        return [
            'server_sha512' => [self::SERVER_SHA512, self::SERVER_SHA512_PASSWORD],
            'default_sha512' => ['AQAAAAIAAYagAAAAEDwJ5GM401hg5FNoDYEc2ydbU8eX4qhk5Myq2Dgn3OEAsAw8FgovUE1U5Hev2+tQug==', 'пароль-Ünïcode 1'],
            'identity6_sha256' => ['AQAAAAEAACcQAAAAEIMxbln1QGEcXrqob45DxXUayZ1IgXtUbigpyCnoWwXBRYTrfr+upvJr0XhLemu4Qw==', 'Identity6-pass-3'],
            'v3_sha1' => ['AQAAAAAAACcQAAAAEFr2TDFapIV2GMf37dDCJUrqAZ/8lw6Mrzov75LWiY903txyt4XN8sNq/Pv/iqE9Zw==', 'Sha1-v3-pass'],
        ];
    }

    #[DataProvider('dotnetHashes')]
    public function test_dotnet_hashes_verify_with_the_right_password_only(string $hash, string $password): void
    {
        $this->assertTrue(IdentityPassword::verify($hash, $password));
        $this->assertFalse(IdentityPassword::verify($hash, $password.'x'));
        $this->assertFalse(IdentityPassword::verify($hash, ''));
        $this->assertFalse(IdentityPassword::verify($hash, strtoupper($password)));
    }

    public function test_v2_hash_is_rejected(): void
    {
        $this->assertFalse(IdentityPassword::verify('ALcYqOmD+IfRqOuJyOLSH8pTAZVxKDZDo5xabcvGPUHVXloJHPvASliZpTfQfJjFDg==', 'Legacy-pass-2'));
        $this->assertFalse(Hash::check('Legacy-pass-2', 'ALcYqOmD+IfRqOuJyOLSH8pTAZVxKDZDo5xabcvGPUHVXloJHPvASliZpTfQfJjFDg=='));
    }

    public static function malformed(): array
    {
        $raw = base64_decode(self::SERVER_SHA512, true);
        $header = fn (int $prf, int $iterations, int $salt) => chr(1).pack('N', $prf).pack('N', $iterations).pack('N', $salt);

        return [
            'empty' => [''],
            'not base64' => ['%%% not base64 %%%'],
            'garbage' => [base64_encode(random_bytes(61))."\0"],
            'marker only' => [base64_encode(chr(1))],
            'truncated header' => [base64_encode(substr($raw, 0, 10))],
            'header without salt' => [base64_encode(substr($raw, 0, 13))],
            'truncated salt' => [base64_encode(substr($raw, 0, 20))],
            'truncated subkey' => [base64_encode(substr($raw, 0, 13 + 16 + 8))],
            'extra byte' => [base64_encode($raw."\0")],
            'unknown prf' => [base64_encode($header(3, 100000, 16).substr($raw, 13))],
            'zero iterations' => [base64_encode($header(2, 0, 16).substr($raw, 13))],
            'absurd iterations' => [base64_encode($header(2, 0xFFFFFFFF, 16).substr($raw, 13))],
            'short salt' => [base64_encode($header(2, 100000, 8).substr($raw, 13))],
            'salt longer than input' => [base64_encode($header(2, 100000, 0xFFFFFFFF).substr($raw, 13))],
            'unknown marker' => [base64_encode(chr(2).substr($raw, 1))],
            'bcrypt' => ['$2y$04$abcdefghijklmnopqrstuuJ4n1bW2ZzWm3vXg5tq4dG7bQy8l9Y2a'],
        ];
    }

    #[DataProvider('malformed')]
    public function test_malformed_hashes_are_rejected_without_exceptions(string $hash): void
    {
        $this->assertFalse(IdentityPassword::verify($hash, self::SERVER_SHA512_PASSWORD));
    }

    public function test_panel_hashes_use_the_server_format(): void
    {
        $hash = IdentityPassword::hash('New-panel-pass-4');
        $raw = base64_decode($hash, true);
        $this->assertSame(61, strlen($raw));
        $this->assertSame(['prf' => 2, 'iterations' => 100000, 'salt' => 16], unpack('Nprf/Niterations/Nsalt', substr($raw, 1, 12)));
        $this->assertTrue(IdentityPassword::verify($hash, 'New-panel-pass-4'));
        $this->assertFalse(IdentityPassword::verify($hash, 'New-panel-pass-5'));
    }

    public function test_zapara_hasher_confirms_server_written_passwords(): void
    {
        // Laravel's current_password rule (Filament MFA setup/disable) goes through the default hasher.
        $this->assertInstanceOf(ZaparaHasher::class, Hash::driver());
        $this->assertTrue(Hash::check(self::SERVER_SHA512_PASSWORD, self::SERVER_SHA512));
        $this->assertFalse(Hash::check('wrong', self::SERVER_SHA512));
        $this->assertFalse(Hash::needsRehash(self::SERVER_SHA512));
    }
}
