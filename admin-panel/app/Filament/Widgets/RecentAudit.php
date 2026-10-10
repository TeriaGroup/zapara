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

    /** r2: сколько последних действий на ПК и на телефоне; остальное — в журнале. */
    public const DESKTOP_LIMIT = 10;

    public const PHONE_LIMIT = 5;

    public const EXTRA_CLASS = 'zp-recent-audit-extra';

    public function table(Table $table): Table
    {
        $phone = AdminAudit::query()->orderByDesc('created_at')->orderByDesc('event_id')
            ->limit(self::PHONE_LIMIT)->pluck('event_id')->all();

        return AuditLog::configureTable($table)
            ->heading('Последние действия')
            ->query(AdminAudit::query()->with('actor')->limit(self::DESKTOP_LIMIT))
            ->paginated(false)
            // r2: на телефоне строки складываются в длинные карточки — там показываем только первые PHONE_LIMIT,
            // остальные помечены классом и скрыты CSS (admin-theme). Весь список — по ссылке «Весь журнал».
            ->recordClasses(fn (AdminAudit $record): array => in_array($record->getKey(), $phone, true) ? [] : [self::EXTRA_CLASS])
            ->headerActions([
                Action::make('all')->label('Весь журнал')->link()->url(AuditLog::getUrl()),
            ]);
    }
}
