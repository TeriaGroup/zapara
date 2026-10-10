<?php

namespace App\Filament\Resources\Communities\Pages;

use App\Filament\Resources\Communities\CommunityResource;
use App\Filament\Support\OperatorActions;
use App\Services\OperatorWork;
use Filament\Actions\Action;
use Filament\Forms\Components\Textarea;
use Filament\Forms\Components\TextInput;
use Filament\Notifications\Notification;
use Filament\Resources\Pages\ListRecords;

class ListCommunities extends ListRecords
{
    protected static string $resource = CommunityResource::class;

    protected function getHeaderActions(): array
    {
        return [
            Action::make('createCommunity')
                ->label('Создать сообщество')
                ->modalHeading('Новое сообщество')
                ->modalSubmitActionLabel('Создать')
                ->modalWidth('lg')
                ->schema([
                    TextInput::make('name')->label('Название')->placeholder('Например, Группа И831Б')->required()->maxLength(80),
                    Textarea::make('description')->label('Описание')->placeholder('Необязательно')->maxLength(2000)->rows(3),
                ])
                ->action(function (array $data): void {
                    $id = OperatorActions::run($this, ['name', 'description'], fn (): string => app(OperatorWork::class)->createCommunity(
                        OperatorActions::actor(),
                        (string) ($data['name'] ?? ''),
                        (string) ($data['description'] ?? ''),
                    ));
                    Notification::make()->title('Сообщество создано')->success()->send();
                    $this->redirect(CommunityResource::getUrl('view', ['record' => $id]));
                }),
        ];
    }
}
