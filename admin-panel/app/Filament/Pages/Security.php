<?php

namespace App\Filament\Pages;

use Filament\Auth\Pages\EditProfile;
use Filament\Schemas\Schema;
use Illuminate\Contracts\Support\Htmlable;
use Illuminate\Support\Arr;

/**
 * Profile page of the panel, reduced to two-factor management.
 * Name and password stay with Zapara.Server (accounts schema); the panel does not edit them.
 */
class Security extends EditProfile
{
    protected static bool $isDiscovered = false;

    protected static ?string $slug = 'security';

    public static function getLabel(): string
    {
        return 'Двухфакторная защита';
    }

    public function getTitle(): string|Htmlable
    {
        return 'Двухфакторная защита';
    }

    public function mount(): void {}

    public function form(Schema $schema): Schema
    {
        return $schema->components([]);
    }

    public function save(): void {}

    public function content(Schema $schema): Schema
    {
        return $schema->components(Arr::wrap($this->getMultiFactorAuthenticationContentComponent()));
    }
}
