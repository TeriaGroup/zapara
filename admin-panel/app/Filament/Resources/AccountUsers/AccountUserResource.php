<?php

namespace App\Filament\Resources\AccountUsers;

use App\Filament\Resources\AccountUsers\Pages\CreateAccountUser;
use App\Filament\Resources\AccountUsers\Pages\EditAccountUser;
use App\Filament\Resources\AccountUsers\Pages\ListAccountUsers;
use App\Models\AccountUser;
use App\Rules\DisplayNameRule;
use App\Rules\PasswordRule;
use App\Rules\UsernameRule;
use App\Services\AccountDirectory;
use Filament\Actions\Action;
use Filament\Forms\Components\TextInput;
use Filament\Resources\Resource;
use Filament\Schemas\Schema;
use Filament\Tables\Columns\TextColumn;
use Filament\Tables\Table;

class AccountUserResource extends Resource
{
    protected static ?string $model = AccountUser::class;

    protected static ?string $navigationLabel = 'Пользователи';

    protected static ?string $modelLabel = 'пользователь';

    protected static ?string $pluralModelLabel = 'Пользователи';

    protected static ?string $slug = 'users';

    protected static ?string $recordTitleAttribute = 'username';

    protected static ?int $navigationSort = 20;

    public static function form(Schema $schema): Schema
    {
        return $schema->components([
            TextInput::make('username')
                ->label('Имя пользователя')
                ->required()
                ->rule(new UsernameRule),
            TextInput::make('display_name')
                ->label('Отображаемое имя')
                ->rule(new DisplayNameRule),
            TextInput::make('password')
                ->label('Пароль')
                ->password()
                ->revealable()
                ->required(fn (string $operation): bool => $operation === 'create')
                ->dehydrated(fn (?string $state): bool => filled($state))
                ->rule(fn (string $operation): PasswordRule => new PasswordRule($operation === 'create')),
        ]);
    }

    public static function table(Table $table): Table
    {
        return $table
            ->columns([
                TextColumn::make('username')->label('Имя пользователя')->searchable(),
                TextColumn::make('display_name')->label('Отображаемое имя')->searchable(),
                TextColumn::make('status')
                    ->label('Статус')
                    ->formatStateUsing(fn (?string $state): string => match ($state) {
                        'active' => 'активен',
                        'disabled' => 'отключён',
                        'deleting' => 'удаляется',
                        default => (string) $state,
                    }),
            ])
            ->defaultSort('created_at', 'desc')
            ->recordActions([
                Action::make('edit')->label('Изменить')->url(fn (AccountUser $record): string => static::getUrl('edit', ['record' => $record])),
                Action::make('disable')
                    ->label('Отключить')
                    ->schema([
                        TextInput::make('current_password')->label('Подтверждение пароля')->password(),
                    ])
                    ->action(function (AccountUser $record, array $data): void {
                        $actor = auth()->user();
                        if (! $actor instanceof AccountUser) {
                            abort(403);
                        }
                        app(AccountDirectory::class)->disable($actor, $record, $data['current_password'] ?? null);
                    }),
                Action::make('remove')
                    ->label('Удалить')
                    ->schema([
                        TextInput::make('current_password')->label('Подтверждение пароля')->password(),
                    ])
                    ->action(function (AccountUser $record, array $data): void {
                        $actor = auth()->user();
                        if (! $actor instanceof AccountUser) {
                            abort(403);
                        }
                        app(AccountDirectory::class)->remove($actor, $record, $data['current_password'] ?? null);
                    }),
            ]);
    }

    public static function getPages(): array
    {
        return [
            'index' => ListAccountUsers::route('/'),
            'create' => CreateAccountUser::route('/create'),
            'edit' => EditAccountUser::route('/{record}/edit'),
        ];
    }
}
