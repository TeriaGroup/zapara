<?php

namespace App\Models;

use App\Support\Zapara;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

/**
 * Назначение персонала в сообществе. Только чтение, запись через OperatorWork::assignStaff().
 */
class StaffAssignment extends Model
{
    public const ROLES = [
        'headman' => 'Староста',
        'curator' => 'Куратор',
    ];

    public $incrementing = false;

    public $timestamps = false;

    protected $primaryKey = 'assignment_id';

    protected $keyType = 'string';

    protected $guarded = ['*'];

    public function getTable(): string
    {
        return Zapara::communities().'.staff_assignments';
    }

    public function user(): BelongsTo
    {
        return $this->belongsTo(AccountUser::class, 'user_id', 'user_id');
    }

    public function community(): BelongsTo
    {
        return $this->belongsTo(Community::class, 'community_id', 'community_id');
    }
}
