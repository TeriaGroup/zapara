<?php

namespace App\Filament\Support;

use DateTimeInterface;
use Illuminate\Support\Carbon;

final class MoscowTime
{
    /**
     * G-3: краткие месяцы — как на web и desktop (ICU ru): «9 окт.», «9 сент.», «9 мая».
     * У Carbon ru они без точки («окт», «сен»).
     */
    private const MONTHS = ['янв.', 'февр.', 'мар.', 'апр.', 'мая', 'июн.', 'июл.', 'авг.', 'сент.', 'окт.', 'нояб.', 'дек.'];

    /** «9 окт., 18:31 МСК» — формат дат глоссария, с поясом (год — только если не текущий). */
    public static function dateTime(DateTimeInterface|string|null $value): string
    {
        if ($value === null || $value === '') {
            return '';
        }

        return self::short($value).' МСК';
    }

    public static function time(string $value): string
    {
        return Carbon::parse($value)->timezone('Europe/Moscow')->format('H:i').' МСК';
    }

    /**
     * G-3: общий формат дат админки — «9 окт., 18:49»; без времени — «9 окт.». Год пишется, только если он не текущий
     * (по Москве): «30 дек. 2025, 18:49», «30 дек. 2025». Все даты админки идут через этот метод.
     */
    public static function short(DateTimeInterface|string|null $value, bool $time = true): string
    {
        if ($value === null || $value === '') {
            return '';
        }
        $at = Carbon::parse($value)->timezone('Europe/Moscow');
        $year = $at->year !== now('Europe/Moscow')->year;
        $text = $at->day.' '.self::MONTHS[$at->month - 1].($year ? ' '.$at->year : '');

        return $time ? $text.', '.$at->format('H:i') : $text;
    }
}
