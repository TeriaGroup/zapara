<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use App\Filament\Support\OperatorTables;
use App\Services\OperatorWork;
use Filament\Actions\Action;
use Filament\Forms\Components\Select;
use Filament\Forms\Components\Textarea;
use Filament\Forms\Components\TextInput;
use Filament\Notifications\Notification;
use Filament\Pages\Page;
use Filament\Schemas\Components\Actions;
use Filament\Schemas\Components\EmbeddedSchema;
use Filament\Schemas\Components\Form;
use Filament\Schemas\Components\Html;
use Filament\Schemas\Schema;

class Communities extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Сообщества';

    protected static ?string $title = 'Сообщества';

    protected static ?string $slug = 'communities';

    protected static ?int $navigationSort = 30;

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
            TextInput::make('name')->label('Название'),
            Textarea::make('description')->label('Описание'),
            Select::make('community_id')
                ->label('Сообщество')
                ->options(fn (): array => app(OperatorWork::class)->communityOptions()),
            TextInput::make('group_id')->label('Код группы'),
            TextInput::make('group_name')->label('Имя группы'),
        ]);
    }

    public function content(Schema $schema): Schema
    {
        return $schema->components([
            Form::make([EmbeddedSchema::make('form')])
                ->id('form')
                ->livewireSubmitHandler('createCommunity')
                ->footer([
                    Actions::make([
                        Action::make('createCommunity')->label('Создать')->submit('createCommunity'),
                        Action::make('mapCatalog')->label('Привязать группу')->action('mapCatalog'),
                    ]),
                ]),
            Html::make(fn (): \Illuminate\Support\HtmlString => OperatorTables::communities()),
        ]);
    }

    public function createCommunity(): void
    {
        $state = $this->form->getState();
        $id = app(OperatorWork::class)->createCommunity($this->actor(), (string) ($state['name'] ?? ''), (string) ($state['description'] ?? ''));
        $this->data['community_id'] = $id;
        Notification::make()->title('Сообщество создано')->body($id)->success()->send();
    }

    public function mapCatalog(): void
    {
        $state = $this->form->getState();
        app(OperatorWork::class)->mapCatalog(
            $this->actor(),
            (string) ($state['community_id'] ?? ''),
            (string) ($state['group_id'] ?? ''),
            (string) ($state['group_name'] ?? ''),
        );
        Notification::make()->title('Группа привязана')->success()->send();
    }
}
