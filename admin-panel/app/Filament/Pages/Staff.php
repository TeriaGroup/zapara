<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use App\Filament\Support\OperatorTables;
use App\Services\OperatorWork;
use Filament\Actions\Action;
use Filament\Forms\Components\Select;
use Filament\Forms\Components\TextInput;
use Filament\Notifications\Notification;
use Filament\Pages\Page;
use Filament\Schemas\Components\Actions;
use Filament\Schemas\Components\EmbeddedSchema;
use Filament\Schemas\Components\Form;
use Filament\Schemas\Components\Html;
use Filament\Schemas\Schema;

class Staff extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Персонал';

    protected static ?string $title = 'Персонал';

    protected static ?string $slug = 'staff';

    protected static ?int $navigationSort = 40;

    /** @var array<string, mixed>|null */
    public ?array $data = [];

    public function mount(): void
    {
        $this->form->fill(['role' => 'headman']);
    }

    public function defaultForm(Schema $schema): Schema
    {
        return $schema->statePath('data');
    }

    public function form(Schema $schema): Schema
    {
        return $schema->components([
            Select::make('community_id')
                ->label('Сообщество')
                ->options(fn (): array => app(OperatorWork::class)->communityOptions()),
            Select::make('user_id')
                ->label('Пользователь')
                ->options(fn (): array => app(OperatorWork::class)->accountOptions()),
            Select::make('role')->label('Роль')->options([
                'headman' => 'Староста',
                'curator' => 'Куратор',
            ]),
            TextInput::make('current_password')->label('Подтверждение пароля')->password(),
        ]);
    }

    public function content(Schema $schema): Schema
    {
        return $schema->components([
            Html::make(fn (): \Illuminate\Support\HtmlString => OperatorTables::communities()),
            Form::make([EmbeddedSchema::make('form')])
                ->id('form')
                ->livewireSubmitHandler('assignStaff')
                ->footer([
                    Actions::make([
                        Action::make('assignStaff')->label('Назначить')->submit('assignStaff'),
                    ]),
                ]),
        ]);
    }

    public function assignStaff(): void
    {
        $state = $this->form->getState();
        app(OperatorWork::class)->assignStaff(
            $this->actor(),
            (string) ($state['community_id'] ?? ''),
            (string) ($state['user_id'] ?? ''),
            (string) ($state['role'] ?? ''),
            isset($state['current_password']) ? (string) $state['current_password'] : null,
        );
        Notification::make()->title('Роль назначена')->success()->send();
    }
}
