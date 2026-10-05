<?php

namespace App\Filament\Resources\AccountUsers\Pages;

use App\Filament\Resources\AccountUsers\AccountUserResource;
use Filament\Actions\CreateAction;
use Filament\Resources\Pages\ListRecords;

class ListAccountUsers extends ListRecords
{
    protected static string $resource = AccountUserResource::class;

    protected function getHeaderActions(): array
    {
        return [
            CreateAction::make()->label('Создать'),
        ];
    }
}
