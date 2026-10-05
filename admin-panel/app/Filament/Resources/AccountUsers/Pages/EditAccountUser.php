<?php

namespace App\Filament\Resources\AccountUsers\Pages;

use App\Filament\Resources\AccountUsers\AccountUserResource;
use App\Models\AccountUser;
use App\Services\AccountDirectory;
use Filament\Resources\Pages\EditRecord;
use Illuminate\Database\Eloquent\Model;

class EditAccountUser extends EditRecord
{
    protected static string $resource = AccountUserResource::class;

    protected function handleRecordUpdate(Model $record, array $data): Model
    {
        /** @var AccountUser $record */
        app(AccountDirectory::class)->update(
            $record,
            (string) ($data['username'] ?? ''),
            isset($data['display_name']) ? (string) $data['display_name'] : null,
            isset($data['password']) ? (string) $data['password'] : null,
        );

        return $record->refresh();
    }
}
