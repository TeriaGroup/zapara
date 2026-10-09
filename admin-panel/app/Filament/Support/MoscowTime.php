<?php

namespace App\Filament\Support;

use Illuminate\Support\Carbon;

final class MoscowTime
{
    public static function dateTime(string $value): string
    {
        return Carbon::parse($value)->timezone('Europe/Moscow')->locale('ru')->isoFormat('D MMMM, HH:mm').' МСК';
    }
}
