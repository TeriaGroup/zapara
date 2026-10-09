<?php

namespace App\Filament\Resources\Communities\RelationManagers;

use App\Filament\Resources\Communities\CommunityResource;
use App\Filament\Support\OperatorActions;
use App\Services\OperatorWork;
use Filament\Actions\Action;
use Filament\Forms\Components\TextInput;
use Filament\Notifications\Notification;
use Filament\Resources\RelationManagers\RelationManager;
use Filament\Tables\Columns\TextColumn;
use Filament\Tables\Table;
use Illuminate\Database\Eloquent\Model;

class GroupsRelationManager extends RelationManager
{
    protected static string $relationship = 'groups';

    protected static ?string $title = 'Группы';

    protected static ?string $modelLabel = 'группа';

    protected static ?string $pluralModelLabel = 'Группы';

    public static function canViewForRecord(Model $ownerRecord, string $pageClass): bool
    {
        return CommunityResource::canAccess();
    }

    public static function getBadge(Model $ownerRecord, string $pageClass): ?string
    {
        return (string) $ownerRecord->groups()->count();
    }

    public function isReadOnly(): bool
    {
        return false;
    }

    public function table(Table $table): Table
    {
        return $table
            ->recordTitleAttribute('group_id')
            ->columns([
                TextColumn::make('group_id')->label('Код группы')->weight('medium')->searchable(),
                TextColumn::make('group_name')->label('Название в каталоге'),
                TextColumn::make('created_at')
                    ->label('Привязана')
                    ->dateTime('j M Y, H:i', 'Europe/Moscow')
                    ->visibleFrom('md'),
            ])
            ->defaultSort('group_id')
            ->stackedOnMobile()
            ->headerActions([
                Action::make('mapCatalog')
                    ->label('Привязать группу')
                    ->modalHeading('Привязать группу')
                    ->modalDescription(fn (): string => 'Расписание группы будет связано с «'.$this->getOwnerRecord()->getAttribute('name').'».')
                    ->modalSubmitActionLabel('Привязать')
                    ->modalWidth('lg')
                    ->schema([
                        TextInput::make('group_id')->label('Код группы')->placeholder('Например, И831Б')->required()->maxLength(64),
                        TextInput::make('group_name')->label('Название группы')->placeholder('Как в каталоге расписания')->required()->maxLength(80),
                    ])
                    ->action(function (array $data): void {
                        OperatorActions::run($this, ['group_id', 'group_name'], fn () => app(OperatorWork::class)->mapCatalog(
                            OperatorActions::actor(),
                            (string) $this->getOwnerRecord()->getKey(),
                            (string) ($data['group_id'] ?? ''),
                            (string) ($data['group_name'] ?? ''),
                        ));
                        Notification::make()->title('Группа привязана')->success()->send();
                    }),
            ])
            ->emptyStateHeading('Группы не привязаны')
            ->emptyStateDescription('Привяжите код группы из каталога, чтобы участники видели её расписание.');
    }
}
