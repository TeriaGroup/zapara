<?php

namespace App\Filament\Support;

use App\Services\OperatorWork;
use Illuminate\Support\HtmlString;

final class OperatorTables
{
    public static function communities(): HtmlString
    {
        $html = '<table><thead><tr><th>Название</th><th>Описание</th><th>Группа</th><th>Идентификатор</th></tr></thead><tbody>';
        foreach (app(OperatorWork::class)->communities() as $row) {
            $html .= '<tr><td>'.e($row['name']).'</td><td>'.e($row['description']).'</td><td>'.e($row['group_id'] ?? '—').'</td><td>'.e($row['community_id']).'</td></tr>';
        }

        return new HtmlString($html.'</tbody></table>');
    }
}
