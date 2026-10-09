<?php

namespace App\Auth;

/**
 * ASP.NET Core Identity password hashes, format v3 (the format Zapara.Server writes with PasswordHasher).
 *
 * Layout (base64): 0x01 marker, then big-endian uint32 PRF (0 = HMAC-SHA1, 1 = HMAC-SHA256, 2 = HMAC-SHA512),
 * uint32 iteration count, uint32 salt length, the salt, and the PBKDF2 subkey (all remaining bytes).
 * .NET 8 writes PRF 2 with 100 000 iterations; older Identity versions wrote PRF 1. The v2 format (0x00 marker,
 * HMAC-SHA1 with 1000 iterations) is not accepted: the server never writes it.
 */
final class IdentityPassword
{
    public const ITERATIONS = 100000;

    private const PRF = [0 => 'sha1', 1 => 'sha256', 2 => 'sha512'];

    /** Bounds that also apply in .NET (salt and subkey at least 128 bits) plus a ceiling against absurd work factors. */
    private const MIN_SALT = 16;

    private const MIN_SUBKEY = 16;

    private const MAX_ITERATIONS = 10_000_000;

    /** Same as the server writes: PBKDF2-HMAC-SHA512, 100 000 iterations, 128-bit salt, 256-bit subkey. */
    public static function hash(string $password): string
    {
        $salt = random_bytes(16);
        $subkey = hash_pbkdf2('sha512', $password, $salt, self::ITERATIONS, 32, true);

        return base64_encode(chr(0x01).pack('N', 2).pack('N', self::ITERATIONS).pack('N', 16).$salt.$subkey);
    }

    /** False for a wrong password and for anything that is not a well-formed v3 hash; never throws. */
    public static function verify(string $hashed, string $password): bool
    {
        $decoded = base64_decode($hashed, true);
        if ($decoded === false || strlen($decoded) < 13 || ord($decoded[0]) !== 0x01) {
            return false;
        }
        $header = unpack('Nprf/Niterations/Nsalt', substr($decoded, 1, 12));
        if ($header === false) {
            return false;
        }
        $algorithm = self::PRF[$header['prf']] ?? null;
        $iterations = $header['iterations'];
        $saltLength = $header['salt'];
        if ($algorithm === null || $iterations < 1 || $iterations > self::MAX_ITERATIONS || $saltLength < self::MIN_SALT) {
            return false;
        }
        $subkeyLength = strlen($decoded) - 13 - $saltLength;
        if ($subkeyLength < self::MIN_SUBKEY) {
            return false;
        }
        $salt = substr($decoded, 13, $saltLength);
        $expected = substr($decoded, 13 + $saltLength);
        $actual = hash_pbkdf2($algorithm, $password, $salt, $iterations, $subkeyLength, true);

        return hash_equals($expected, $actual);
    }
}
