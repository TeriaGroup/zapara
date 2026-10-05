<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
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
use Filament\Schemas\Components\Section;
use Filament\Schemas\Schema;

class Content extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Материалы';

    protected static ?string $title = 'Материалы';

    protected static ?string $slug = 'content';

    protected static ?int $navigationSort = 60;

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
            Select::make('community_id')
                ->label('Сообщество')
                ->options(fn (): array => app(OperatorWork::class)->communityOptions()),
            TextInput::make('title')->label('Заголовок'),
            Textarea::make('body')->label('Текст'),
            TextInput::make('current_password')->label('Подтверждение пароля')->password(),
        ]);
    }

    public function content(Schema $schema): Schema
    {
        $components = [
            Form::make([EmbeddedSchema::make('form')])
                ->id('form')
                ->livewireSubmitHandler('publish')
                ->footer([
                    Actions::make([
                        Action::make('publish')->label('Опубликовать')->submit('publish'),
                    ]),
                ]),
        ];
        foreach (app(OperatorWork::class)->contentRows() as $row) {
            $kind = $row['kind'];
            $objectId = $row['object_id'];
            $communityId = $row['community_id'];
            $key = str_replace('-', '', $objectId);
            $components[] = Section::make($row['title'])
                ->description($this->kindLabel($kind).' · '.$row['community_name'].' · '.$communityId)
                ->key('content-'.$key)
                ->headerActions([
                    Action::make('moderate'.$key)
                        ->label('Снять')
                        ->action(fn () => $this->moderate($kind, $objectId, $communityId)),
                ]);
        }

        return $schema->components($components);
    }

    public function publish(): void
    {
        $state = $this->form->getState();
        app(OperatorWork::class)->publishAnnouncement(
            $this->actor(),
            (string) ($state['community_id'] ?? ''),
            (string) ($state['title'] ?? ''),
            (string) ($state['body'] ?? ''),
        );
        Notification::make()->title('Материал опубликован')->success()->send();
    }

    public function moderate(string $kind, string $objectId, string $communityId): void
    {
        $password = $this->data['current_password'] ?? null;
        app(OperatorWork::class)->moderate(
            $this->actor(),
            $kind,
            $objectId,
            $communityId,
            is_string($password) ? $password : null,
        );
        Notification::make()->title('Материал снят')->success()->send();
    }

    private function kindLabel(string $kind): string
    {
        return match ($kind) {
            'shared_homework' => 'Домашнее задание',
            'announcement' => 'Объявление',
            'poll' => 'Голосование',
            default => $kind,
        };
    }
}
