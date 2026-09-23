<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use App\Services\OperatorWork;
use Filament\Actions\Action;
use Filament\Forms\Components\TextInput;
use Filament\Notifications\Notification;
use Filament\Pages\Page;
use Filament\Schemas\Components\EmbeddedSchema;
use Filament\Schemas\Components\Form;
use Filament\Schemas\Components\Section;
use Filament\Schemas\Components\Text;
use Filament\Schemas\Schema;
use Illuminate\Validation\ValidationException;

class Sessions extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Сессии';

    protected static ?string $title = 'Сессии';

    protected static ?string $slug = 'sessions';

    protected static ?int $navigationSort = 70;

    /** @var array<string, mixed>|null */
    public ?array $data = [];

    public function mount(): void
    {
        $this->form->fill();
    }

    public function defaultForm(Schema $schema): Schema
    {
        return $schema->statePath('data');
    }

    public function form(Schema $schema): Schema
    {
        return $schema->components([
            TextInput::make('current_password')->label('Подтверждение пароля')->password(),
        ]);
    }

    public function content(Schema $schema): Schema
    {
        $sessions = app(OperatorWork::class)->openSessions();
        if ($sessions === []) {
            return $schema->components([
                Text::make('Нет открытых сессий'),
            ]);
        }

        $components = [
            Form::make([EmbeddedSchema::make('form')])->id('form'),
        ];
        foreach ($sessions as $row) {
            $familyId = $row['family_id'];
            $key = str_replace('-', '', $familyId);
            $components[] = Section::make($row['username'])
                ->description($row['device_name'])
                ->key('session-'.$key)
                ->headerActions([
                    Action::make('revoke'.$key)
                        ->label('Отозвать сессию')
                        ->action(fn () => $this->revokeFamily($familyId)),
                ]);
        }

        return $schema->components($components);
    }

    public function revokeFamily(string $familyId): void
    {
        $listed = false;
        foreach (app(OperatorWork::class)->openSessions() as $row) {
            if ($row['family_id'] === $familyId) {
                $listed = true;
                break;
            }
        }
        if (! $listed) {
            throw ValidationException::withMessages(['data.family_id' => 'Объект не найден.']);
        }
        $password = $this->data['current_password'] ?? null;
        app(OperatorWork::class)->revokeFamily(
            $this->actor(),
            $familyId,
            is_string($password) ? $password : null,
        );
        Notification::make()->title('Сессия отозвана')->success()->send();
    }
}
