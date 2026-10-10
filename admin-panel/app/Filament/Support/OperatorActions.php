<?php

namespace App\Filament\Support;

use App\Models\AccountUser;
use App\Models\StaffAssignment;
use App\Services\OperatorWork;
use Filament\Forms\Components\Select;
use Filament\Forms\Components\TextInput;
use Filament\Notifications\Notification;
use Illuminate\Validation\ValidationException;
use Livewire\Component;

final class OperatorActions
{
    public static function actor(): AccountUser
    {
        $user = auth()->user();
        if (! $user instanceof AccountUser || ! $user->isPlatformAdmin()) {
            abort(403);
        }

        return $user;
    }

    /**
     * OperatorWork сообщает ошибки с ключами «data.<поле>». В модальном окне действия поля лежат
     * под «mountedActions.N.data.<поле>»: переносим ошибку к полю, а если такого поля нет, показываем уведомление.
     *
     * @param  list<string>  $fields
     */
    public static function run(Component $livewire, array $fields, callable $work): mixed
    {
        try {
            return $work();
        } catch (ValidationException $exception) {
            $index = max(0, count((array) ($livewire->mountedActions ?? [])) - 1);
            $mapped = [];
            $other = [];
            foreach ($exception->errors() as $key => $messages) {
                $field = str_starts_with($key, 'data.') ? substr($key, 5) : $key;
                if (in_array($field, $fields, true)) {
                    $mapped['mountedActions.'.$index.'.data.'.$field] = $messages;
                } else {
                    $other = array_merge($other, $messages);
                }
            }
            if ($other !== []) {
                Notification::make()->title(implode(' ', array_unique($other)))->danger()->send();
            }
            throw ValidationException::withMessages($mapped === [] ? ['mountedActions.'.$index.'.data' => $other] : $mapped);
        }
    }

    /**
     * Поля «Назначить». Роль заранее не выбрана.
     *
     * @return list<Select|TextInput>
     */
    public static function staffFields(bool $withCommunity): array
    {
        $fields = [];
        if ($withCommunity) {
            $fields[] = Select::make('community_id')
                ->label('Сообщество')
                ->placeholder('Выберите сообщество')
                ->options(fn (): array => app(OperatorWork::class)->communityOptions())
                ->searchable()
                ->required();
        }
        $fields[] = Select::make('user_id')
            ->label('Пользователь')
            ->placeholder('Выберите пользователя')
            ->options(fn (): array => app(OperatorWork::class)->accountOptions())
            ->searchable()
            ->required();
        $fields[] = Select::make('role')
            ->label('Роль')
            ->placeholder('Выберите роль')
            ->options(StaffAssignment::ROLES)
            ->required();
        $fields[] = TextInput::make('current_password')
            ->label('Подтверждение пароля')
            ->helperText('Введите свой пароль для подтверждения.')
            ->password()
            ->revealable();

        return $fields;
    }

    /**
     * @param  array<string, mixed>  $data
     */
    public static function assignStaff(Component $livewire, string $communityId, array $data): void
    {
        self::run($livewire, ['community_id', 'user_id', 'role', 'current_password'], fn () => app(OperatorWork::class)->assignStaff(
            self::actor(),
            $communityId,
            (string) ($data['user_id'] ?? ''),
            (string) ($data['role'] ?? ''),
            isset($data['current_password']) ? (string) $data['current_password'] : null,
        ));
        Notification::make()->title('Роль назначена')->success()->send();
    }
}
