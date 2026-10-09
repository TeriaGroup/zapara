<?php

namespace App\Filament\Pages;

use App\Filament\Concerns\GuardsPlatformAdmin;
use App\Services\OperatorSettings;
use App\Support\ByteSize;
use Filament\Actions\Action;
use Filament\Forms\Components\Hidden;
use Filament\Forms\Components\TextInput;
use Filament\Forms\Components\Toggle;
use Filament\Forms\Components\ToggleButtons;
use Filament\Notifications\Notification;
use Filament\Pages\Page;
use Filament\Schemas\Components\Actions;
use Filament\Schemas\Components\Component;
use Filament\Schemas\Components\EmbeddedSchema;
use Filament\Schemas\Components\Flex;
use Filament\Schemas\Components\Form;
use Filament\Schemas\Components\Group;
use Filament\Schemas\Components\Tabs;
use Filament\Schemas\Components\Tabs\Tab;
use Filament\Schemas\Components\Utilities\Get;
use Filament\Schemas\Components\Utilities\Set;
use Filament\Schemas\Schema;
use Filament\Support\Exceptions\Halt;

class SystemSettings extends Page
{
    use GuardsPlatformAdmin;

    protected static ?string $navigationLabel = 'Настройки системы';

    protected static ?string $title = 'Настройки системы';

    protected static ?string $slug = 'settings';

    protected static ?int $navigationSort = 10;

    private const VK_CALLBACK = 'https://voen.teriahost.ru/auth/vk/callback';

    private const YANDEX_CALLBACK = 'https://voen.teriahost.ru/auth/yandex/callback';

    /**
     * @var array<string, mixed>|null
     */
    public ?array $data = [];

    /**
     * Ключи интеграций: значения не показываются, видно только, задан ли ключ.
     */
    private const SECRETS = ['vk_secret', 'yandex_secret', 's3_secret'];

    private const QUOTAS = [
        'quota_group' => ['quota_group_bytes', 1073741824],
        'quota_user' => ['quota_user_bytes', 524288000],
    ];

    /**
     * Квоты в байтах на момент открытия формы: без правок сохраняются как были.
     *
     * @var array<string, int>
     */
    public array $quotaBytes = [];

    public function mount(): void
    {
        $state = [
            'registration_enabled' => OperatorSettings::registrationEnabled() ?? false,
            'vk_enabled' => OperatorSettings::read('vk_enabled') === 'true',
            'vk_client_id' => OperatorSettings::read('vk_client_id') ?? '',
            'vk_callback' => OperatorSettings::read('vk_callback') ?? '',
            'yandex_enabled' => OperatorSettings::read('yandex_enabled') === 'true',
            'yandex_client_id' => OperatorSettings::read('yandex_client_id') ?? '',
            'yandex_callback' => OperatorSettings::read('yandex_callback') ?? '',
            's3_endpoint' => OperatorSettings::read('s3_endpoint') ?? '',
            's3_region' => OperatorSettings::read('s3_region') ?? '',
            's3_bucket' => OperatorSettings::read('s3_bucket') ?? '',
            's3_access_key' => OperatorSettings::read('s3_access_key') ?? '',
        ];
        foreach (self::SECRETS as $key) {
            $state[$key] = '';
            $state[$key.'_edit'] = false;
        }
        foreach (self::QUOTAS as $field => [$key, $default]) {
            $stored = OperatorSettings::read($key);
            $bytes = is_string($stored) && ctype_digit($stored) ? (int) $stored : $default;
            $this->quotaBytes[$field] = $bytes;
            ['amount' => $state[$field.'_amount'], 'unit' => $state[$field.'_unit']] = ByteSize::split($bytes);
        }
        $this->form->fill($state);
    }

    public function defaultForm(Schema $schema): Schema
    {
        return $schema->statePath('data');
    }

    public function form(Schema $schema): Schema
    {
        return $schema->components([
            Tabs::make('Разделы')
                ->persistTabInQueryString('tab')
                ->tabs([
                    Tab::make('Регистрация')->schema([
                        Toggle::make('registration_enabled')
                            ->label('Регистрация открыта')
                            ->helperText('Если выключено, новые пользователи не смогут зарегистрироваться.'),
                    ]),
                    Tab::make('Вход через VK')->schema($this->provider('vk', 'VK ID', self::VK_CALLBACK)),
                    Tab::make('Вход через Яндекс')->schema($this->provider('yandex', 'Яндекс ID', self::YANDEX_CALLBACK)),
                    Tab::make('Хранилище')->schema([
                        TextInput::make('s3_endpoint')->label('Адрес S3')->placeholder('https://storage.yandexcloud.net')->helperText('Нужны все пять полей. Пустое поле берётся из конфигурации сервера; пока хранилище не настроено, файлы лежат на диске сервера.'),
                        TextInput::make('s3_region')->label('Регион')->placeholder('ru-central1'),
                        TextInput::make('s3_bucket')->label('Бакет'),
                        TextInput::make('s3_access_key')->label('Ключ доступа'),
                        $this->secret('s3_secret', 'Секретный ключ'),
                    ]),
                    Tab::make('Квоты')->schema([
                        $this->quota('quota_group', 'Лимит группы', 'На всё, что сдают студенты группы. По умолчанию 1 ГБ.'),
                        $this->quota('quota_user', 'Лимит студента', 'По умолчанию 500 МБ. Один файл считается в оба лимита.'),
                    ]),
                ]),
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
                    ])->sticky(),
                ]),
        ]);
    }

    /**
     * @return list<Component>
     */
    private function provider(string $prefix, string $name, string $callback): array
    {
        return [
            Toggle::make($prefix.'_enabled')->label($name.' включён'),
            TextInput::make($prefix.'_client_id')->label('Идентификатор приложения (client ID)'),
            $this->secret($prefix.'_secret', 'Секрет приложения'),
            TextInput::make($prefix.'_callback')
                ->label('Адрес возврата')
                ->placeholder($callback)
                ->copyable(copyMessage: 'Адрес скопирован')
                ->helperText('Укажите этот адрес в настройках приложения '.$name.': '.$callback),
        ];
    }

    /**
     * Поле ключа: «Задан · Изменить». Пустое поле сохранённый ключ не заменяет.
     */
    private function secret(string $key, string $label): Component
    {
        $configured = OperatorSettings::publicValue($key) === 'configured';

        return Group::make([
            Hidden::make($key.'_edit')->dehydrated(false),
            TextInput::make($key)
                ->label($label)
                ->password()
                ->autocomplete('new-password')
                ->readOnly(fn (Get $get): bool => $configured && ! $get($key.'_edit'))
                ->placeholder(fn (Get $get): string => match (true) {
                    ! $configured => 'Не задан',
                    (bool) $get($key.'_edit') => 'Новое значение',
                    default => '••••••••',
                })
                ->hint($configured ? 'Задан' : 'Не задан')
                ->hintIcon($configured ? 'heroicon-m-check-circle' : null)
                ->hintColor($configured ? 'success' : 'gray')
                ->hintAction(
                    Action::make($key.'_change')
                        ->label('Изменить')
                        ->visible(fn (Get $get): bool => $configured && ! $get($key.'_edit'))
                        ->action(fn (Set $set) => $set($key.'_edit', true)),
                )
                ->helperText(fn (Get $get): ?string => $configured && $get($key.'_edit') ? 'Пустое поле оставит сохранённый ключ.' : null),
        ]);
    }

    private function quota(string $field, string $label, string $help): Component
    {
        return Flex::make([
            TextInput::make($field.'_amount')
                ->label($label)
                ->numeric()
                ->required()
                ->minValue(0)
                ->step('any')
                ->inputMode('decimal')
                ->helperText($help),
            ToggleButtons::make($field.'_unit')
                ->label('Единица')
                ->options(ByteSize::LABELS)
                ->inline()
                ->grouped()
                ->required()
                ->grow(false),
        ])->from('sm');
    }

    public function save(): void
    {
        $state = $this->form->getState();
        $this->requireCallback($state, 'yandex_enabled', 'yandex_callback', 'Яндекс ID', self::YANDEX_CALLBACK);
        $this->requireCallback($state, 'vk_enabled', 'vk_callback', 'VK ID', self::VK_CALLBACK);
        OperatorSettings::saveRegistration((bool) ($state['registration_enabled'] ?? false));
        foreach (self::QUOTAS as $field => [$key, $default]) {
            $state[$key] = (string) ByteSize::toBytes($state[$field.'_amount'] ?? '', $state[$field.'_unit'] ?? 'mb', $this->quotaBytes[$field] ?? null);
        }
        foreach ([
            'vk_enabled', 'vk_client_id', 'vk_callback', 'yandex_enabled', 'yandex_client_id', 'yandex_callback',
            's3_endpoint', 's3_region', 's3_bucket', 's3_access_key', 'quota_group_bytes', 'quota_user_bytes',
        ] as $key) {
            if (array_key_exists($key, $state)) {
                OperatorSettings::save($key, $key === 'vk_enabled' || $key === 'yandex_enabled' ? (((bool) $state[$key]) ? 'true' : 'false') : (string) ($state[$key] ?? ''));
            }
        }
        foreach (self::SECRETS as $key) {
            OperatorSettings::save($key, (string) ($state[$key] ?? ''), true);
        }
        foreach (self::QUOTAS as $field => [$key]) {
            $this->quotaBytes[$field] = (int) $state[$key];
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
