<?php

namespace App\Models;

use App\Support\Zapara;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

/**
 * Привязка кода группы из каталога к сообществу. Только чтение, запись через OperatorWork::mapCatalog().
 */
class CatalogMap extends Model
{
    public $incrementing = false;

    public $timestamps = false;

    protected $primaryKey = 'map_id';

    protected $keyType = 'string';

    protected $guarded = ['*'];

    public function getTable(): string
    {
        return Zapara::communities().'.catalog_maps';
    }

    public function community(): BelongsTo
    {
        return $this->belongsTo(Community::class, 'community_id', 'community_id');
    }
}
