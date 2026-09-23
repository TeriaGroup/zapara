<?php

namespace App\Auth;

final class IdentityPassword
{
    public const ITERATIONS = 100000;

    public static function hash(string $password): string
    {
        $salt = random_bytes(16);
        $subkey = hash_pbkdf2('sha256', $password, $salt, self::ITERATIONS, 32, true);
        $bytes = chr(0x01)
            .pack('N', 1)
            .pack('N', self::ITERATIONS)
            .pack('N', 16)
            .$salt
            .$subkey;

        return base64_encode($bytes);
    }

    public static function verify(string $hashed, string $password): bool
    {
        $decoded = base64_decode($hashed, true);
        if ($decoded === false || strlen($decoded) < 14 || ord($decoded[0]) !== 0x01) {
            return false;
        }
        $prf = unpack('N', substr($decoded, 1, 4))[1];
        $iterations = unpack('N', substr($decoded, 5, 4))[1];
        $saltLength = unpack('N', substr($decoded, 9, 4))[1];
        if ($prf !== 1 || $iterations < 1 || $saltLength < 16) {
            return false;
        }
        if (strlen($decoded) < 13 + $saltLength + 16) {
            return false;
        }
        $salt = substr($decoded, 13, $saltLength);
        $expected = substr($decoded, 13 + $saltLength);
        $actual = hash_pbkdf2('sha256', $password, $salt, $iterations, strlen($expected), true);

        return hash_equals($expected, $actual);
    }
}
