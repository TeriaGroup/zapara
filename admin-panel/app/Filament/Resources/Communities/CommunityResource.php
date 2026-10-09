<?php

namespace App\Filament\Resources\Communities;

use App\Filament\Resources\Communities\Pages\ListCommunities;
use App\Filament\Resources\Communities\Pages\ViewCommunity;
use App\Filament\Resources\Communities\RelationManagers\GroupsRelationManager;
use App\Filament\Resources\Communities\RelationManagers\StaffRelationManager;
use App\Models\AccountUser;
use App\Models\Community;
use Filament\Infolists\Components\TextEntry;
use Filament\Resources\Resource;
use Filament\Schemas\Components\Section;
use Filament\Schemas\Schema;
use Filament\Tables\Columns\TextColumn;
use Filament\Tables\Table;
use Illuminate\Database\Eloquent\Model;

class CommunityResource extends Resource
{
    protected static ?string $model = Community::class;

    protected static ?string $navigationLabel = 'Сообщества';

    protected static ?string $modelLabel = 'сообщество';

    protected static ?string $pluralModelLabel = 'Сообщества';

    protected static ?string $slug = 'communities';

    protected static ?string $recordTitleAttribute = 'name';

    protected static ?int $navigationSort = 30;

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

    public static function canView(Model $record): bool
    {
        return static::canAccess();
    }

    public static function infolist(Schema $schema): Schema
    {
        return $schema->components([
            Section::make()
                ->columns(['default' => 1, 'md' => 3])
                ->columnSpanFull()
                ->schema([
                    TextEntry::make('name')->label('Название'),
                    TextEntry::make('description')->label('Описание')->placeholder('Нет описания'),
                    TextEntry::make('created_at')
                        ->label('Создано')
                        ->dateTime('j F Y, H:i', 'Europe/Moscow')
                        ->suffix(' МСК'),
                    TextEntry::make('community_id')
                        ->label('ID')
                        ->copyable()
                        ->copyMessage('Скопировано')
                        ->fontFamily('mono')
                        ->color('gray'),
                ]),
        ]);
    }

    public static function table(Table $table): Table
    {
        return $table
            ->columns([
                TextColumn::make('name')
                    ->label('Название')
                    ->searchable()
                    ->sortable()
                    ->weight('medium')
                    ->description(fn (Community $record): ?string => $record->description !== '' ? $record->description : null),
                TextColumn::make('groups.group_id')
                    ->label('Группы')
                    ->badge()
                    ->color('gray')
                    ->placeholder('Не привязаны'),
                TextColumn::make('staff_count')
                    ->label('Персонал')
                    ->counts('staff')
                    ->numeric(),
                TextColumn::make('created_at')
                    ->label('Создано')
                    ->dateTime('j M Y', 'Europe/Moscow')
                    ->sortable()
                    ->visibleFrom('md'),
            ])
            ->defaultSort('name')
            ->stackedOnMobile()
            ->recordUrl(fn (Community $record): string => static::getUrl('view', ['record' => $record]))
            ->emptyStateHeading('Сообществ пока нет')
            ->emptyStateDescription('Создайте сообщество, затем привяжите к нему группы и назначьте персонал.');
    }

    public static function getRelations(): array
    {
        return [
            GroupsRelationManager::class,
            StaffRelationManager::class,
        ];
    }

    public static function getPages(): array
    {
        return [
            'index' => ListCommunities::route('/'),
            'view' => ViewCommunity::route('/{record}'),
        ];
    }
}
