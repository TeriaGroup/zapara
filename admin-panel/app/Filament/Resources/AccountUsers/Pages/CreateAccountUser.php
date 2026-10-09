<?php

namespace App\Filament\Resources\AccountUsers\Pages;

use App\Filament\Resources\AccountUsers\AccountUserResource;
use App\Services\AccountDirectory;
use Filament\Resources\Pages\CreateRecord;
use Illuminate\Database\Eloquent\Model;

class CreateAccountUser extends CreateRecord
{
    protected static string $resource = AccountUserResource::class;

    protected static ?string $title = 'Создать пользователя';

    protected static ?string $breadcrumb = 'Создание';

    protected function handleRecordCreation(array $data): Model
    {
        return app(AccountDirectory::class)->create(
            (string) ($data['username'] ?? ''),
            isset($data['display_name']) ? (string) $data['display_name'] : null,
            (string) ($data['password'] ?? ''),
        );
    }

    protected function getCreatedNotificationTitle(): ?string
    {
        return 'Пользователь создан';
    }
}
