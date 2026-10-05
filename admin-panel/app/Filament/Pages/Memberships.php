<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use App\Services\OperatorWork;
use Filament\Actions\Action;
use Filament\Notifications\Notification;
use Filament\Pages\Page;
use Filament\Schemas\Components\Section;
use Filament\Schemas\Components\Text;
use Filament\Schemas\Schema;
use Illuminate\Validation\ValidationException;

class Memberships extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Заявки';

    protected static ?string $title = 'Заявки';

    protected static ?string $slug = 'memberships';

    protected static ?int $navigationSort = 50;

    public function content(Schema $schema): Schema
    {
        $joins = app(OperatorWork::class)->pendingJoins();
        if ($joins === []) {
            return $schema->components([
                Text::make('Нет заявок'),
            ]);
        }

        $components = [];
        foreach ($joins as $row) {
            $requestId = $row['request_id'];
            $communityId = $row['community_id'];
            $key = str_replace('-', '', $requestId);
            $components[] = Section::make($row['username'])
                ->description($row['community_name'].' · '.$communityId)
                ->key('join-'.$key)
                ->headerActions([
                    Action::make('accept'.$key)
                        ->label('Принять')
                        ->action(fn () => $this->acceptJoin($requestId)),
                    Action::make('reject'.$key)
                        ->label('Отклонить')
                        ->action(fn () => $this->rejectJoin($requestId)),
                ]);
        }

        return $schema->components($components);
    }

    public function acceptJoin(string $requestId): void
    {
        $this->resolve($requestId, true);
        Notification::make()->title('Заявка принята')->success()->send();
    }

    public function rejectJoin(string $requestId): void
    {
        $this->resolve($requestId, false);
        Notification::make()->title('Заявка отклонена')->success()->send();
    }

    private function resolve(string $requestId, bool $accepted): void
    {
        $row = null;
        foreach (app(OperatorWork::class)->pendingJoins() as $candidate) {
            if ($candidate['request_id'] === $requestId) {
                $row = $candidate;
                break;
            }
        }
        if ($row === null) {
            throw ValidationException::withMessages(['data.request_id' => 'Объект не найден.']);
        }
        app(OperatorWork::class)->resolveJoin($this->actor(), $row['community_id'], $requestId, $accepted);
    }
}
