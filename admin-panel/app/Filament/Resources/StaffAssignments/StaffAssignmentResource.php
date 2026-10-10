<?php

namespace App\Filament\Resources\StaffAssignments;

use App\Filament\Support\MoscowTime;
use App\Filament\Resources\Communities\CommunityResource;
use App\Filament\Resources\StaffAssignments\Pages\ManageStaffAssignments;
use App\Models\AccountUser;
use App\Models\StaffAssignment;
use Filament\Resources\Resource;
use Filament\Tables\Columns\TextColumn;
use Filament\Tables\Filters\SelectFilter;
use Filament\Tables\Table;
use Illuminate\Database\Eloquent\Builder;
use Illuminate\Database\Eloquent\Model;

class StaffAssignmentResource extends Resource
{
    protected static ?string $model = StaffAssignment::class;

    protected static ?string $navigationLabel = 'Персонал';

    protected static ?string $modelLabel = 'назначение';

    protected static ?string $pluralModelLabel = 'Персонал';

    protected static ?string $slug = 'staff';

    protected static ?int $navigationSort = 40;

    public static function canAccess(): bool
    {
        $user = auth()->user();

        return $user instanceof AccountUser && $user->isPlatformAdmin();
    }

    public static function canCreate(): bool
    {
        return false;
    }

    public static function canEdit(Model $record): bool
    {
        return false;
    }

    public static function canDelete(Model $record): bool
    {
        return false;
    }

    public static function getEloquentQuery(): Builder
    {
        return parent::getEloquentQuery()->whereNull('revoked_at')->with(['user', 'community']);
    }

    public static function table(Table $table): Table
    {
        return $table
            ->columns([
                TextColumn::make('user.username')
                    ->label('Пользователь')
                    ->weight('medium')
                    ->description(fn (StaffAssignment $record): ?string => $record->user?->display_name ?: null)
                    ->searchable(),
                TextColumn::make('community.name')
                    ->label('Сообщество')
                    ->url(fn (StaffAssignment $record): string => CommunityResource::getUrl('view', ['record' => $record->community_id]))
                    ->color('primary')
                    ->searchable(),
                TextColumn::make('role')
                    ->label('Роль')
                    ->badge()
                    ->formatStateUsing(fn (string $state): string => StaffAssignment::ROLES[$state] ?? $state)
                    ->color(fn (string $state): string => $state === 'headman' ? 'info' : 'success'),
                TextColumn::make('assigned_at')
                    ->label('Назначен')
                    ->formatStateUsing(fn ($state): string => MoscowTime::short($state)) // G-3: «9 окт., 18:49», год — если не текущий
                    ->sortable()
                    ->visibleFrom('md'),
            ])
            ->defaultSort('assigned_at', 'desc')
            ->stackedOnMobile()
            ->filters([
                SelectFilter::make('role')->label('Роль')->options(StaffAssignment::ROLES),
            ])
            ->emptyStateHeading('Персонал не назначен')
            ->emptyStateDescription('Назначьте старосту или куратора кнопкой «Назначить» или внутри сообщества.');
    }

    public static function getPages(): array
    {
        return [
            'index' => ManageStaffAssignments::route('/'),
        ];
    }
}
