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

    /** «9 окт., 18:31 МСК» — формат дат глоссария, с поясом. */
    public static function dateTime(string $value): string
    {
        return self::short($value).' МСК';
    }

    public static function time(string $value): string
    {
        return Carbon::parse($value)->timezone('Europe/Moscow')->format('H:i').' МСК';
    }

    /**
     * G-3: общий формат дат — «9 окт., 18:49»; с годом — «9 окт. 2026, 18:49»; без времени — «9 окт. 2026».
     */
    public static function short(DateTimeInterface|string|null $value, bool $year = false, bool $time = true): string
    {
        if ($value === null || $value === '') {
            return '';
        }
        $at = Carbon::parse($value)->timezone('Europe/Moscow');
        $text = $at->day.' '.self::MONTHS[$at->month - 1].($year ? ' '.$at->year : '');

        return $time ? $text.', '.$at->format('H:i') : $text;
    }
}
