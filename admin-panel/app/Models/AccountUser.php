<?php

namespace App\Models;

use App\Support\Zapara;
use Filament\Auth\MultiFactor\App\Contracts\HasAppAuthentication;
use Filament\Auth\MultiFactor\App\Contracts\HasAppAuthenticationRecovery;
use Filament\Models\Contracts\FilamentUser;
use Filament\Models\Contracts\HasName;
use Filament\Panel;
use Illuminate\Database\Eloquent\Relations\HasOne;
use Illuminate\Foundation\Auth\User as Authenticatable;
use Illuminate\Support\Facades\DB;
use SensitiveParameter;

class AccountUser extends Authenticatable implements FilamentUser, HasAppAuthentication, HasAppAuthenticationRecovery, HasName
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

    /**
     * The account's password hash (ASP.NET Identity, owned by Zapara.Server). Used by Laravel's `current_password`
     * rule through {@see \App\Auth\ZaparaHasher} and by AuthenticateSession, which ends panel sessions after a
     * password change.
     */
    public function getAuthPassword(): string
    {
        $hash = DB::table(Zapara::accounts().'.password_credentials')
            ->where('user_id', $this->user_id)
            ->value('password_hash');

        return is_string($hash) ? $hash : '';
    }

    /** TOTP data is kept in the panel's own table, keyed by user_id; accounts.users stays untouched. */
    public function mfaCredential(): HasOne
    {
        return $this->hasOne(AdminMfaCredential::class, 'user_id', 'user_id');
    }

    public function getAppAuthenticationSecret(): ?string
    {
        return $this->mfaCredential?->app_authentication_secret;
    }

    public function saveAppAuthenticationSecret(#[SensitiveParameter] ?string $secret): void
    {
        $credential = $this->mfaCredential ?? new AdminMfaCredential(['user_id' => $this->user_id]);
        $credential->app_authentication_secret = $secret;
        if ($secret === null) {
            $credential->app_authentication_recovery_codes = null;
        }
        $credential->save();
        $this->setRelation('mfaCredential', $credential);
    }

    public function getAppAuthenticationHolderName(): string
    {
        return (string) $this->username;
    }

    /** @return array<string>|null */
    public function getAppAuthenticationRecoveryCodes(): ?array
    {
        return $this->mfaCredential?->app_authentication_recovery_codes;
    }

    /** @param array<string>|null $codes */
    public function saveAppAuthenticationRecoveryCodes(#[SensitiveParameter] ?array $codes): void
    {
        $credential = $this->mfaCredential ?? new AdminMfaCredential(['user_id' => $this->user_id]);
        $credential->app_authentication_recovery_codes = $codes;
        $credential->save();
        $this->setRelation('mfaCredential', $credential);
    }
}
