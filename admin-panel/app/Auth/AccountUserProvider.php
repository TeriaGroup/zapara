<?php

namespace App\Auth;

use App\Models\AccountUser;
use App\Support\Zapara;
use Illuminate\Auth\EloquentUserProvider;
use Illuminate\Contracts\Auth\Authenticatable;
use Illuminate\Support\Facades\DB;

class AccountUserProvider extends EloquentUserProvider
{
    public function retrieveByCredentials(array $credentials): ?Authenticatable
    {
        $username = $credentials['username'] ?? null;
        if (! is_string($username)) {
            return null;
        }

        return AccountUser::query()
            ->where('normalized_username', strtolower($username))
            ->first();
    }

    public function validateCredentials(Authenticatable $user, array $credentials): bool
    {
        $password = $credentials['password'] ?? '';
        if (! is_string($password) || ! $user instanceof AccountUser) {
            return false;
        }
        if ($user->status !== 'active') {
            return false;
        }
        $hash = DB::table(Zapara::accounts().'.password_credentials')
            ->where('user_id', $user->user_id)
            ->value('password_hash');
        if (! is_string($hash) || $hash === '') {
            return false;
        }

        return IdentityPassword::verify($hash, $password);
    }

    public function rehashPasswordIfRequired(Authenticatable $user, array $credentials, bool $force = false): void {}

    public function updateRememberToken(Authenticatable $user, $token): void {}
}
