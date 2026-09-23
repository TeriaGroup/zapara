<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use App\Services\OperatorWork;
use Filament\Pages\Page;
use Filament\Schemas\Components\Html;
use Filament\Schemas\Schema;
use Illuminate\Support\HtmlString;

class AuditLog extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Журнал';

    protected static ?string $title = 'Журнал';

    protected static ?string $slug = 'audit';

    protected static ?int $navigationSort = 80;

    /** @var list<array{created_at: string, action: string, object_type: string, object_id: string, outcome: string}> */
    public array $rows = [];

    public function mount(): void
    {
        $this->rows = app(OperatorWork::class)->auditRows();
    }

    public function content(Schema $schema): Schema
    {
        $html = '<table><thead><tr><th>Действие</th><th>Объект</th><th>Идентификатор</th><th>Исход</th></tr></thead><tbody>';
        foreach ($this->rows as $row) {
            $html .= '<tr><td>'.e($row['action']).'</td><td>'.e($row['object_type']).'</td><td>'.e($row['object_id']).'</td><td>'.e($row['outcome']).'</td></tr>';
        }
        $html .= '</tbody></table>';

        return $schema->components([
            Html::make(new HtmlString($html)),
        ]);
    }
}
