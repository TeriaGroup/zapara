<?php

namespace App\Filament\Resources\StaffAssignments\Pages;

use App\Filament\Resources\StaffAssignments\StaffAssignmentResource;
use App\Filament\Support\OperatorActions;
use Filament\Actions\Action;
use Filament\Resources\Pages\ManageRecords;

class ManageStaffAssignments extends ManageRecords
{
    protected static string $resource = StaffAssignmentResource::class;

    protected function getHeaderActions(): array
    {
        return [
            Action::make('assignStaff')
                ->label('Назначить')
                ->modalHeading('Назначить персонал')
                ->modalSubmitActionLabel('Назначить')
                ->modalWidth('lg')
                ->schema(OperatorActions::staffFields(true))
                ->action(fn (array $data) => OperatorActions::assignStaff($this, (string) ($data['community_id'] ?? ''), $data)),
        ];
    }
}
