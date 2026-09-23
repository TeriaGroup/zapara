<?php

namespace App\Services;

use App\Support\Zapara;
use Illuminate\Support\Facades\DB;

final class OperatorSettings
{
    public static function ensure(): void
    {
        $schema = Zapara::settings();
        DB::statement('CREATE SCHEMA IF NOT EXISTS '.$schema);
        DB::statement(<<<SQL
            CREATE TABLE IF NOT EXISTS {$schema}.system_settings (
                key text PRIMARY KEY,
                value text NOT NULL,
                updated_at timestamptz NOT NULL
            )
            SQL);
    }

    public static function registrationEnabled(): ?bool
    {
        self::ensure();
        $value = DB::table(Zapara::settings().'.system_settings')
            ->where('key', 'registration_enabled')
            ->value('value');
        if (! is_string($value)) {
            return null;
        }

        return filter_var($value, FILTER_VALIDATE_BOOLEAN, FILTER_NULL_ON_FAILURE) ?? false;
    }

    public static function saveRegistration(bool $enabled): void
    {
        self::ensure();
        $table = Zapara::settings().'.system_settings';
        $value = $enabled ? 'true' : 'false';
        $existing = DB::table($table)->where('key', 'registration_enabled')->exists();
        if ($existing) {
            DB::table($table)->where('key', 'registration_enabled')->update([
                'value' => $value,
                'updated_at' => now(),
            ]);

            return;
        }
        DB::table($table)->insert([
            'key' => 'registration_enabled',
            'value' => $value,
            'updated_at' => now(),
        ]);
    }

    public static function read(string $key): ?string
    {
        self::ensure();
        $value = DB::table(Zapara::settings().'.system_settings')->where('key', $key)->value('value');

        return is_string($value) ? $value : null;
    }

    public static function publicValue(string $key): string
    {
        $value = self::read($key) ?? '';
        if (in_array($key, ['vk_secret', 'yandex_secret', 's3_secret'], true)) {
            return $value === '' ? '' : 'configured';
        }

        return $value;
    }

    public static function save(string $key, ?string $value, bool $secret = false): void
    {
        if ($value === null) {
            return;
        }
        if ($secret && $value === '') {
            return;
        }
        self::ensure();
        $table = Zapara::settings().'.system_settings';
        if (DB::table($table)->where('key', $key)->exists()) {
            DB::table($table)->where('key', $key)->update(['value' => $value, 'updated_at' => now()]);

            return;
        }
        DB::table($table)->insert(['key' => $key, 'value' => $value, 'updated_at' => now()]);
    }

    /**
     * @return array{user: int, limit: int, group: int, groupLimit: int}
     */
    public static function usage(string $userId, string $groupId): array
    {
        self::ensure();
        $schema = Zapara::settings();
        DB::statement('CREATE TABLE IF NOT EXISTS '.$schema.'.quota_counters (scope text NOT NULL, scope_id text NOT NULL, bytes bigint NOT NULL, PRIMARY KEY (scope, scope_id))');
        $user = (int) (DB::table($schema.'.quota_counters')->where('scope', 'user')->where('scope_id', $userId)->value('bytes') ?? 0);
        $group = (int) (DB::table($schema.'.quota_counters')->where('scope', 'group')->where('scope_id', $groupId)->value('bytes') ?? 0);
        $userLimit = (int) (self::read('quota_user_bytes') ?: '524288000');
        $groupLimit = (int) (self::read('quota_group_bytes') ?: '1073741824');

        return ['user' => $user, 'limit' => $userLimit, 'group' => $group, 'groupLimit' => $groupLimit];
    }
}
