<?php

namespace App\Filament\Pages;

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
