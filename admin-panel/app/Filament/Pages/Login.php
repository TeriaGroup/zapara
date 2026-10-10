<?php

namespace App\Filament\Pages;

use App\Auth\LoginThrottle;
use DanHarrin\LivewireRateLimiting\Exceptions\TooManyRequestsException;
use Filament\Auth\Http\Responses\Contracts\LoginResponse;
use Filament\Auth\Pages\Login as BaseLogin;
use Filament\Forms\Components\TextInput;
use Filament\Schemas\Components\Component;
use Illuminate\Validation\ValidationException;
use SensitiveParameter;

class Login extends BaseLogin
{
    protected static bool $isDiscovered = false;

    public function authenticate(): ?LoginResponse
    {
        $account = LoginThrottle::normalizeAccount($this->data['username'] ?? null);
        $network = LoginThrottle::networkKey(request()->ip());
        // Checked before the password, so a throttled attempt never learns whether the password was right.
        $wait = app(LoginThrottle::class)->availableIn($account, $network);
        if ($wait > 0) {
            $this->getRateLimitedNotification(new TooManyRequestsException(static::class, 'authenticate', (string) request()->ip(), $wait))?->send();

            return null;
        }

        $response = parent::authenticate();
        if ($response !== null) {
            app(LoginThrottle::class)->succeeded($account, $network);
        }

        return $response;
    }

    /** Filament's own per-minute limit, keyed by the same network grouping (IPv6 by /64). */
    protected function getRateLimitKey($method, $component = null)
    {
        $method ??= 'authenticate';
        $component ??= static::class;

        return 'livewire-rate-limiter:'.sha1($component.'|'.$method.'|'.LoginThrottle::networkKey(request()->ip()));
    }

    protected function getEmailFormComponent(): Component
    {
        return TextInput::make('username')
            ->label('Имя пользователя')
            ->required()
            ->autocomplete('username')
            ->autofocus();
    }

    protected function getCredentialsFromFormData(#[SensitiveParameter] array $data): array
    {
        return [
            'username' => $data['username'],
            'password' => $data['password'],
        ];
    }

    protected function throwFailureValidationException(): never
    {
        app(LoginThrottle::class)->failed(
            LoginThrottle::normalizeAccount($this->data['username'] ?? null),
            LoginThrottle::networkKey(request()->ip()),
        );

        throw ValidationException::withMessages([
            'data.username' => 'Неверные данные для входа.',
        ]);
    }
}
