<?php

namespace App\Filament\Widgets;

use App\Filament\Pages\AuditLog;
use App\Models\AdminAudit;
use Filament\Actions\Action;
use Filament\Tables\Table;
use Filament\Widgets\TableWidget;

class RecentAudit extends TableWidget
{
    protected static ?int $sort = 2;

    protected static bool $isLazy = false;

    protected int|string|array $columnSpan = 'full';

    protected static ?string $heading = 'Последние действия';

    public function table(Table $table): Table
    {
        return AuditLog::configureTable($table)
            ->heading('Последние действия')
            ->query(AdminAudit::query()->with('actor')->limit(8))
            ->paginated(false)
            ->headerActions([
                Action::make('all')->label('Весь журнал')->link()->url(AuditLog::getUrl()),
            ]);
    }
}
