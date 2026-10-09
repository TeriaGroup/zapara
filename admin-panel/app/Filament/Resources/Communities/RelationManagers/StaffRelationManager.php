<?php

namespace App\Filament\Resources\Communities\RelationManagers;

use App\Filament\Support\OperatorActions;
use App\Models\StaffAssignment;
use Filament\Actions\Action;
use Filament\Resources\RelationManagers\RelationManager;
use Filament\Tables\Columns\TextColumn;
use Filament\Tables\Table;
use Illuminate\Database\Eloquent\Model;

class StaffRelationManager extends RelationManager
{
    protected static string $relationship = 'staff';

    protected static ?string $title = 'Персонал';

    protected static ?string $modelLabel = 'назначение';

    protected static ?string $pluralModelLabel = 'Персонал';

    public static function canViewForRecord(Model $ownerRecord, string $pageClass): bool
    {
        return true;
    }

    public static function getBadge(Model $ownerRecord, string $pageClass): ?string
    {
        return (string) $ownerRecord->staff()->count();
    }

    public function isReadOnly(): bool
    {
        return false;
    }

    public function table(Table $table): Table
    {
        return $table
            ->modifyQueryUsing(fn ($query) => $query->with('user'))
            ->columns([
                TextColumn::make('user.username')
                    ->label('Пользователь')
                    ->weight('medium')
                    ->description(fn (StaffAssignment $record): ?string => $record->user?->display_name ?: null)
                    ->searchable(),
                TextColumn::make('role')
                    ->label('Роль')
                    ->badge()
                    ->formatStateUsing(fn (string $state): string => StaffAssignment::ROLES[$state] ?? $state)
                    ->color(fn (string $state): string => $state === 'headman' ? 'info' : 'success'),
                TextColumn::make('assigned_at')
                    ->label('Назначен')
                    ->dateTime('j M Y, H:i', 'Europe/Moscow')
                    ->visibleFrom('md'),
            ])
            ->defaultSort('assigned_at', 'desc')
            ->stackedOnMobile()
            ->headerActions([
                Action::make('assignStaff')
                    ->label('Назначить')
                    ->modalHeading(fn (): string => 'Назначить в «'.$this->getOwnerRecord()->getAttribute('name').'»')
                    ->modalSubmitActionLabel('Назначить')
                    ->modalWidth('lg')
                    ->schema(OperatorActions::staffFields(false))
                    ->action(fn (array $data) => OperatorActions::assignStaff($this, (string) $this->getOwnerRecord()->getKey(), $data)),
            ])
            ->emptyStateHeading('Персонал не назначен')
            ->emptyStateDescription('Назначьте старосту или куратора.');
    }
}
