<?php

namespace App\Filament\Concerns;

use App\Models\AccountUser;

trait GuardsPlatformAdmin
{
    public static function canAccess(): bool
    {
        $user = auth()->user();

        return $user instanceof AccountUser && $user->isPlatformAdmin();
    }

    protected function actor(): AccountUser
    {
        $user = auth()->user();
        if (! $user instanceof AccountUser || ! $user->isPlatformAdmin()) {
            abort(403);
        }

        return $user;
    }
}
