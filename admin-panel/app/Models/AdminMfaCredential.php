<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

/**
 * @property string $user_id
 * @property string|null $app_authentication_secret
 * @property array<string>|null $app_authentication_recovery_codes
 */
class AdminMfaCredential extends Model
{
    public $incrementing = false;

    protected $table = 'admin_mfa_credentials';

    protected $primaryKey = 'user_id';

    protected $keyType = 'string';

    protected $fillable = ['user_id'];

    protected $hidden = ['app_authentication_secret', 'app_authentication_recovery_codes'];

    protected function casts(): array
    {
        return [
            'app_authentication_secret' => 'encrypted',
            'app_authentication_recovery_codes' => 'encrypted:array',
        ];
    }
}
