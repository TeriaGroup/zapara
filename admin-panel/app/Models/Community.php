<?php

namespace App\Models;

use App\Support\Zapara;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\HasMany;

/**
 * Только чтение для таблиц панели. Изменения идут через OperatorWork (там аудит и блокировки).
 */
class Community extends Model
{
    public $incrementing = false;

    public $timestamps = false;

    protected $primaryKey = 'community_id';

    protected $keyType = 'string';

    protected $guarded = ['*'];

    public function getTable(): string
    {
        return Zapara::communities().'.communities';
    }

    public function groups(): HasMany
    {
        return $this->hasMany(CatalogMap::class, 'community_id', 'community_id');
    }

    public function staff(): HasMany
    {
        return $this->hasMany(StaffAssignment::class, 'community_id', 'community_id')->whereNull('revoked_at');
    }
}
