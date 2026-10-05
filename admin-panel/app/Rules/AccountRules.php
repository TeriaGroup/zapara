<?php

namespace App\Rules;

final class AccountRules
{
    public static function username(mixed $value): ?string
    {
        if (! is_string($value) || ! preg_match('/\A[A-Za-z0-9_.-]{3,32}\z/', $value)) {
            return 'Имя пользователя должно содержать от 3 до 32 латинских букв, цифр или символов _ . -';
        }

        return null;
    }

    public static function password(mixed $value, bool $required): ?string
    {
        if ($value === null || $value === '') {
            return $required ? 'Пароль должен содержать от 12 до 128 символов.' : null;
        }
        if (! is_string($value) || self::scalars($value, 12, 128, false) === null) {
            return 'Пароль должен содержать от 12 до 128 символов.';
        }

        return null;
    }

    public static function displayName(mixed $value): ?string
    {
        if ($value === null || $value === '') {
            return null;
        }
        if (! is_string($value) || self::scalars($value, 1, 80, true) === null) {
            return 'Отображаемое имя должно содержать от 1 до 80 символов без управляющих знаков.';
        }

        return null;
    }

    public static function scalars(string $value, int $minimum, int $maximum, bool $rejectControls): ?string
    {
        if (str_contains($value, "\0")) {
            return null;
        }
        if ($rejectControls && preg_match('/\p{C}/u', $value)) {
            return null;
        }
        if (! mb_check_encoding($value, 'UTF-8')) {
            return null;
        }
        $count = mb_strlen($value, 'UTF-8');
        if ($count < $minimum || $count > $maximum) {
            return null;
        }

        return $value;
    }
}


