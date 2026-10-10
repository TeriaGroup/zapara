<?php

namespace App\Filament\Pages;

use App\Auth\LoginThrottle;
use DanHarrin\LivewireRateLimiting\Exceptions\TooManyRequestsException;
use Filament\Auth\Http\Responses\Contracts\LoginResponse;
use Filament\Auth\Pages\Login as BaseLogin;
use Filament\Forms\Components\TextInput;
use Filament\Schemas\Components\Callout;
use Filament\Schemas\Components\Component;
use Filament\Schemas\Schema;
use Illuminate\Contracts\Support\Htmlable;
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
            'data.credentials' => __('filament-panels::auth/pages/login.messages.failed'),
        ]);
    }

    /**
     * Ошибка неверных данных — над формой, а не у одного поля: неизвестно, ошибся человек в имени или в пароле.
     */
    public function form(Schema $schema): Schema
    {
        return parent::form($schema)->components([
            Callout::make(fn (): ?string => $this->getErrorBag()->first('data.credentials'))
                ->danger()
                ->extraAttributes(['role' => 'alert'])
                ->visible(fn (): bool => $this->getErrorBag()->has('data.credentials')),
            $this->getEmailFormComponent(),
            $this->getPasswordFormComponent(),
            $this->getRememberFormComponent(),
        ]);
    }

    public function getSubheading(): string | Htmlable | null
    {
        // Шаг второго фактора (#4) оставляет подзаголовок Filament.
        if (filled($this->userUndertakingMultiFactorAuthentication)) {
            return parent::getSubheading();
        }

        return 'Только для администраторов платформы.';
    }
}
