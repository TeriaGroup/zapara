<?php

namespace App\Models;

use App\Support\Zapara;
use Filament\Models\Contracts\FilamentUser;
use Filament\Models\Contracts\HasName;
use Filament\Panel;
use Illuminate\Foundation\Auth\User as Authenticatable;
use Illuminate\Support\Facades\DB;

class AccountUser extends Authenticatable implements FilamentUser, HasName
{
    public $incrementing = false;

    public $timestamps = false;

    protected $primaryKey = 'user_id';

    protected $keyType = 'string';

    protected $fillable = [
        'username',
        'display_name',
    ];

    public function getTable(): string
    {
        return Zapara::accounts().'.users';
    }

    public function getAuthIdentifierName(): string
    {
        return 'user_id';
    }

    public function getFilamentName(): string
    {
        $name = $this->display_name;

        return is_string($name) && $name !== '' ? $name : (string) $this->username;
    }

    public function canAccessPanel(Panel $panel): bool
    {
        return $this->isPlatformAdmin();
    }

    public function isPlatformAdmin(): bool
    {
        if ($this->status !== 'active') {
            return false;
        }

        return DB::table(Zapara::admin().'.platform_admins')
            ->where('user_id', $this->user_id)
            ->whereNull('revoked_at')
            ->exists();
    }
}
