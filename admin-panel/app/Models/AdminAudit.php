<?php

namespace App\Models;

use App\Support\Zapara;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

/**
 * Журнал администратора. Таблица только на чтение (триггер в БД запрещает UPDATE/DELETE).
 */
class AdminAudit extends Model
{
    public $incrementing = false;

    public $timestamps = false;

    protected $primaryKey = 'event_id';

    protected $keyType = 'string';

    protected $guarded = ['*'];

    protected $casts = [
        'created_at' => 'datetime',
    ];

    public function getTable(): string
    {
        return Zapara::admin().'.admin_audit';
    }

    public function actor(): BelongsTo
    {
        return $this->belongsTo(AccountUser::class, 'actor_id', 'user_id');
    }
}
