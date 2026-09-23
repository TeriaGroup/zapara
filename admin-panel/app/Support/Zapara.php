<?php

namespace App\Support;

final class Zapara
{
    public static function dsn(): ?string
    {
        $raw = getenv('ZAPARA_TEST_POSTGRES');
        if (! is_string($raw) || trim($raw) === '') {
            $raw = $_SERVER['ZAPARA_TEST_POSTGRES'] ?? $_ENV['ZAPARA_TEST_POSTGRES'] ?? null;
        }

        return is_string($raw) && trim($raw) !== '' ? $raw : null;
    }

    /**
     * @return array{host: string, port: int, database: string, username: string, password: string}
     */
    public static function parseDsn(string $raw): array
    {
        $map = [];
        foreach (explode(';', $raw) as $part) {
            if (! str_contains($part, '=')) {
                continue;
            }
            [$key, $value] = explode('=', $part, 2);
            $map[strtolower(trim($key))] = trim($value);
        }
        $host = $map['host'] ?? '';
        $database = $map['database'] ?? '';
        $port = (int) ($map['port'] ?? 0);
        if (! in_array($host, ['127.0.0.1', 'localhost'], true) || $port !== 56432 || $database !== 'zapara_test') {
            throw new \RuntimeException('Панель подключается только к локальной тестовой базе zapara_test.');
        }

        return [
            'host' => $host,
            'port' => $port,
            'database' => $database,
            'username' => $map['username'] ?? '',
            'password' => $map['password'] ?? '',
        ];
    }

    public static function schema(string $name): string
    {
        $value = (string) config('zapara.schemas.'.$name);
        if (! preg_match('/\A[a-z][a-z0-9_]{0,62}\z/', $value)) {
            throw new \RuntimeException('Недопустимое имя схемы.');
        }

        return $value;
    }

    public static function accounts(): string
    {
        return self::schema('accounts');
    }

    public static function admin(): string
    {
        return self::schema('admin');
    }

    public static function communities(): string
    {
        return self::schema('communities');
    }

    public static function settings(): string
    {
        return self::schema('settings');
    }
}
