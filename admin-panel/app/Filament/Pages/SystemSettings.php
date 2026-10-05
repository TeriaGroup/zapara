<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use App\Services\OperatorSettings;
use Filament\Actions\Action;
use Filament\Forms\Components\TextInput;
use Filament\Forms\Components\Toggle;
use Filament\Notifications\Notification;
use Filament\Pages\Page;
use Filament\Schemas\Components\Actions;
use Filament\Schemas\Components\EmbeddedSchema;
use Filament\Schemas\Components\Form;
use Filament\Schemas\Schema;
use Filament\Support\Exceptions\Halt;

class SystemSettings extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Настройки системы';

    protected static ?string $title = 'Настройки системы';

    protected static ?string $slug = 'settings';

    protected static ?int $navigationSort = 10;

    /**
     * @var array<string, mixed>|null
     */
    public ?array $data = [];

    public function mount(): void
    {
        $this->form->fill([
            'registration_enabled' => OperatorSettings::registrationEnabled() ?? false,
            'vk_enabled' => OperatorSettings::read('vk_enabled') === 'true',
            'vk_client_id' => OperatorSettings::read('vk_client_id') ?? '',
            'vk_callback' => OperatorSettings::read('vk_callback') ?? '',
            'vk_secret' => '',
            'yandex_enabled' => OperatorSettings::read('yandex_enabled') === 'true',
            'yandex_client_id' => OperatorSettings::read('yandex_client_id') ?? '',
            'yandex_callback' => OperatorSettings::read('yandex_callback') ?? '',
            'yandex_secret' => '',
            's3_endpoint' => OperatorSettings::read('s3_endpoint') ?? '',
            's3_region' => OperatorSettings::read('s3_region') ?? '',
            's3_bucket' => OperatorSettings::read('s3_bucket') ?? '',
            's3_access_key' => OperatorSettings::read('s3_access_key') ?? '',
            's3_secret' => '',
            'quota_group_bytes' => OperatorSettings::read('quota_group_bytes') ?? '1073741824',
            'quota_user_bytes' => OperatorSettings::read('quota_user_bytes') ?? '524288000',
        ]);
    }

    public function defaultForm(Schema $schema): Schema
    {
        return $schema->statePath('data');
    }

    public function form(Schema $schema): Schema
    {
        return $schema->components([
            Toggle::make('registration_enabled')
                ->label('Регистрация открыта')
                ->helperText('Сохранённое значение читает сервер «Расписание военмех» и по нему открывает или закрывает регистрацию.'),
            Toggle::make('vk_enabled')->label('VK ID включён'),
            TextInput::make('vk_client_id')->label('VK ID, идентификатор клиента'),
            TextInput::make('vk_callback')->label('VK ID, адрес возврата')->helperText('Точный адрес: https://voen.teriahost.ru/auth/vk/callback'),
            TextInput::make('vk_secret')->label('VK ID, секрет')->password()->helperText(OperatorSettings::publicValue('vk_secret') === 'configured' ? 'Секрет уже сохранён. Пустое поле его не заменяет.' : 'Секрет не показывается целиком.'),
            Toggle::make('yandex_enabled')->label('Яндекс ID включён'),
            TextInput::make('yandex_client_id')->label('Яндекс ID, идентификатор клиента'),
            TextInput::make('yandex_callback')->label('Яндекс ID, адрес возврата')->helperText('Точный адрес: https://voen.teriahost.ru/auth/yandex/callback'),
            TextInput::make('yandex_secret')->label('Яндекс ID, секрет')->password()->helperText(OperatorSettings::publicValue('yandex_secret') === 'configured' ? 'Секрет уже сохранён. Пустое поле его не заменяет.' : 'Секрет не показывается целиком.'),
            TextInput::make('s3_endpoint')->label('S3, адрес'),
            TextInput::make('s3_region')->label('S3, регион'),
            TextInput::make('s3_bucket')->label('S3, бакет'),
            TextInput::make('s3_access_key')->label('S3, ключ доступа'),
            TextInput::make('s3_secret')->label('S3, секрет')->password()->helperText(OperatorSettings::publicValue('s3_secret') === 'configured' ? 'Секрет уже сохранён. Пустое поле его не заменяет.' : 'Секрет не показывается целиком.'),
            TextInput::make('quota_group_bytes')->label('Лимит группы, байт')->numeric()->default('1073741824')->helperText('По умолчанию 1 ГиБ на всё, что сдают студенты группы.'),
            TextInput::make('quota_user_bytes')->label('Лимит студента, байт')->numeric()->default('524288000')->helperText('По умолчанию 500 МиБ на студента.'),
        ]);
    }

    public function content(Schema $schema): Schema
    {
        return $schema->components([
            Form::make([EmbeddedSchema::make('form')])
                ->id('form')
                ->livewireSubmitHandler('save')
                ->footer([
                    Actions::make([
                        Action::make('save')->label('Сохранить')->submit('save'),
                    ]),
                ]),
        ]);
    }

    public function save(): void
    {
        $state = $this->form->getState();
        $this->requireCallback($state, 'yandex_enabled', 'yandex_callback', 'Яндекс ID', 'https://voen.teriahost.ru/auth/yandex/callback');
        $this->requireCallback($state, 'vk_enabled', 'vk_callback', 'VK ID', 'https://voen.teriahost.ru/auth/vk/callback');
        OperatorSettings::saveRegistration((bool) ($state['registration_enabled'] ?? false));
        foreach ([
            'vk_enabled', 'vk_client_id', 'vk_callback', 'yandex_enabled', 'yandex_client_id', 'yandex_callback',
            's3_endpoint', 's3_region', 's3_bucket', 's3_access_key', 'quota_group_bytes', 'quota_user_bytes',
        ] as $key) {
            if (array_key_exists($key, $state)) {
                OperatorSettings::save($key, $key === 'vk_enabled' || $key === 'yandex_enabled' ? (((bool) $state[$key]) ? 'true' : 'false') : (string) ($state[$key] ?? ''));
            }
        }
        foreach (['vk_secret', 'yandex_secret', 's3_secret'] as $key) {
            OperatorSettings::save($key, (string) ($state[$key] ?? ''), true);
        }
        Notification::make()->title('Настройки сохранены')->success()->send();
    }

    /**
     * @param  array<string, mixed>  $state
     */
    private function requireCallback(array &$state, string $enabled, string $key, string $name, string $expected): void
    {
        if (! (bool) ($state[$enabled] ?? false)) {
            return;
        }
        $callback = trim((string) ($state[$key] ?? ''));
        if ($callback !== $expected) {
            Notification::make()->title($name.': адрес возврата должен быть '.$expected)->danger()->send();
            throw new Halt();
        }
        $state[$key] = $callback;
    }
}
