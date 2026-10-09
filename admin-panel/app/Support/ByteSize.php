<?php

namespace App\Support;

/**
 * Размеры для квот: в базе — байты, в интерфейсе — число и единица (МБ / ГБ, двоичные: 1 ГБ = 1024 МБ).
 */
final class ByteSize
{
    public const UNITS = [
        'mb' => 1048576,
        'gb' => 1073741824,
    ];

    public const LABELS = [
        'mb' => 'МБ',
        'gb' => 'ГБ',
    ];

    /**
     * Байты → число и единица для формы. Целые гигабайты показываются в ГБ, остальное — в МБ.
     *
     * @return array{amount: string, unit: string}
     */
    public static function split(int $bytes): array
    {
        if ($bytes > 0 && $bytes % self::UNITS['gb'] === 0) {
            return ['amount' => (string) intdiv($bytes, self::UNITS['gb']), 'unit' => 'gb'];
        }

        return ['amount' => self::number($bytes / self::UNITS['mb']), 'unit' => 'mb'];
    }

    /**
     * Число и единица из формы → байты. Если значение не меняли, сохраняются исходные байты без округления.
     */
    public static function toBytes(mixed $amount, mixed $unit, ?int $original = null): int
    {
        $unit = is_string($unit) && isset(self::UNITS[$unit]) ? $unit : 'mb';
        $amount = str_replace(',', '.', trim((string) $amount));
        if ($original !== null && self::split($original) === ['amount' => self::number((float) $amount), 'unit' => $unit]) {
            return $original;
        }

        return (int) round((float) $amount * self::UNITS[$unit]);
    }

    /**
     * Байты → «1 ГБ», «500 МБ», «1,5 ГБ», «820 КБ».
     */
    public static function format(int $bytes): string
    {
        foreach (['ГБ' => self::UNITS['gb'], 'МБ' => self::UNITS['mb'], 'КБ' => 1024] as $label => $size) {
            if ($bytes >= $size) {
                return str_replace('.', ',', self::number($bytes / $size, 1)).' '.$label;
            }
        }

        return $bytes.' Б';
    }

    private static function number(float $value, int $precision = 3): string
    {
        $text = number_format($value, $precision, '.', '');

        return str_contains($text, '.') ? rtrim(rtrim($text, '0'), '.') : $text;
    }
}
