<?php

namespace App\Policies;

use App\Models\AccountUser;

class AccountUserPolicy
{
    public function viewAny(AccountUser $actor): bool
    {
        return $actor->isPlatformAdmin();
    }

    public function view(AccountUser $actor, AccountUser $user): bool
    {
        return $actor->isPlatformAdmin();
    }

    public function create(AccountUser $actor): bool
    {
        return $actor->isPlatformAdmin();
    }

    public function update(AccountUser $actor, AccountUser $user): bool
    {
        return $actor->isPlatformAdmin();
    }

    public function delete(AccountUser $actor, AccountUser $user): bool
    {
        return false;
    }
}
