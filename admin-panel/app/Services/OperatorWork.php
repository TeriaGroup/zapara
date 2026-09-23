<?php

namespace App\Services;

use App\Models\AccountUser;
use App\Support\Zapara;
use Illuminate\Database\QueryException;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

final class OperatorWork
{
    public function __construct(private readonly AccountDirectory $accounts) {}

    public function createCommunity(AccountUser $actor, string $name, ?string $description): string
    {
        $name = $this->text($name, 80, false, 'name');
        $description = $description === null ? '' : $this->text($description, 2000, true, 'description');
        $id = (string) Str::uuid();
        $now = now();
        DB::transaction(function () use ($actor, $name, $description, $id, $now): void {
            $this->lockActor($actor);
            DB::table(Zapara::communities().'.communities')->insert([
                'community_id' => $id,
                'name' => $name,
                'description' => $description,
                'revision' => 1,
                'created_at' => $now,
                'updated_at' => $now,
            ]);
            $this->audit($actor->user_id, 'community_created', 'community', $id, $now);
        });

        return $id;
    }

    public function mapCatalog(AccountUser $actor, string $communityId, string $groupId, string $groupName): void
    {
        $communityId = $this->uuid($communityId, 'community_id');
        $groupId = $this->text($groupId, 64, false, 'group_id');
        $groupName = $this->text($groupName, 80, false, 'group_name');
        $mapId = (string) Str::uuid();
        $now = now();
        try {
            DB::transaction(function () use ($actor, $communityId, $groupId, $groupName, $mapId, $now): void {
                $this->lockActor($actor);
                $this->lockCommunity($communityId);
                DB::table(Zapara::communities().'.catalog_maps')->insert([
                    'map_id' => $mapId,
                    'community_id' => $communityId,
                    'group_id' => $groupId,
                    'group_name' => $groupName,
                    'created_at' => $now,
                ]);
                $this->communityAudit($actor->user_id, $communityId, 'catalog_mapped', 'catalog_map', $mapId, $now);
                $this->audit($actor->user_id, 'catalog_mapped', 'catalog_map', $mapId, $now);
            });
        } catch (QueryException $exception) {
            if ($this->unique($exception)) {
                throw ValidationException::withMessages(['data.group_id' => 'Код группы уже привязан.']);
            }
            throw $exception;
        }
    }

    public function assignStaff(AccountUser $actor, string $communityId, string $userId, string $role, ?string $password): void
    {
        $this->accounts->requirePassword($actor, $password);
        $communityId = $this->uuid($communityId, 'community_id');
        $userId = $this->uuid($userId, 'user_id');
        if (! in_array($role, ['headman', 'curator'], true)) {
            throw ValidationException::withMessages(['data.role' => 'Недопустимые данные.']);
        }
        $assignmentId = (string) Str::uuid();
        $now = now();
        DB::transaction(function () use ($actor, $communityId, $userId, $role, $assignmentId, $now): void {
            $this->lockActor($actor);
            $status = DB::table(Zapara::accounts().'.users')->where('user_id', $userId)->lockForUpdate()->value('status');
            if ($status !== 'active') {
                throw ValidationException::withMessages(['data.user_id' => 'Объект не найден.']);
            }
            $this->lockCommunity($communityId);
            $communities = Zapara::communities();
            DB::table($communities.'.memberships')->updateOrInsert(
                ['community_id' => $communityId, 'user_id' => $userId],
                ['role' => $role, 'status' => 'active', 'created_at' => $now, 'revoked_at' => null],
            );
            DB::table($communities.'.staff_assignments')
                ->where('community_id', $communityId)
                ->where('user_id', $userId)
                ->whereNull('revoked_at')
                ->update(['revoked_at' => $now]);
            DB::table($communities.'.staff_assignments')->insert([
                'assignment_id' => $assignmentId,
                'community_id' => $communityId,
                'user_id' => $userId,
                'role' => $role,
                'assigned_at' => $now,
                'revoked_at' => null,
            ]);
            $this->communityAudit($actor->user_id, $communityId, 'staff_assigned', 'staff_assignment', $assignmentId, $now);
            $this->audit($actor->user_id, 'staff_assigned', 'staff_assignment', $assignmentId, $now);
        });
    }

    public function resolveJoin(AccountUser $actor, string $communityId, string $requestId, bool $accepted): void
    {
        $communityId = $this->uuid($communityId, 'community_id');
        $requestId = $this->uuid($requestId, 'request_id');
        $now = now();
        DB::transaction(function () use ($actor, $communityId, $requestId, $accepted, $now): void {
            $this->lockActor($actor);
            $this->lockCommunity($communityId);
            $row = DB::table(Zapara::communities().'.join_requests')
                ->where('request_id', $requestId)
                ->where('community_id', $communityId)
                ->lockForUpdate()
                ->first();
            if ($row === null) {
                throw ValidationException::withMessages(['data.request_id' => 'Объект не найден.']);
            }
            if ($row->status !== 'pending') {
                throw ValidationException::withMessages(['data.request_id' => 'Конфликт данных.']);
            }
            DB::table(Zapara::communities().'.join_requests')->where('request_id', $requestId)->update([
                'status' => $accepted ? 'accepted' : 'rejected',
                'resolved_at' => $now,
                'resolved_by' => $actor->user_id,
            ]);
            if ($accepted) {
                $membership = DB::table(Zapara::communities().'.memberships')
                    ->where('community_id', $communityId)
                    ->where('user_id', $row->user_id)
                    ->first();
                if ($membership === null) {
                    DB::table(Zapara::communities().'.memberships')->insert([
                        'community_id' => $communityId,
                        'user_id' => $row->user_id,
                        'role' => 'member',
                        'status' => 'active',
                        'created_at' => $now,
                        'revoked_at' => null,
                    ]);
                } elseif ($membership->status === 'revoked') {
                    DB::table(Zapara::communities().'.memberships')
                        ->where('community_id', $communityId)
                        ->where('user_id', $row->user_id)
                        ->update(['role' => 'member', 'status' => 'active', 'revoked_at' => null]);
                }
            }
            $action = $accepted ? 'join_accepted' : 'join_rejected';
            $this->communityAudit($actor->user_id, $communityId, $action, 'join_request', $requestId, $now);
            $this->audit($actor->user_id, $action, 'join_request', $requestId, $now);
        });
    }

    public function publishAnnouncement(AccountUser $actor, string $communityId, string $title, string $body): string
    {
        $communityId = $this->uuid($communityId, 'community_id');
        $title = $this->text($title, 200, false, 'title');
        $body = str_replace("\r\n", "\n", $body);
        $body = str_replace("\r", "\n", $body);
        $body = $this->body($body);
        $id = (string) Str::uuid();
        $now = now();
        DB::transaction(function () use ($actor, $communityId, $title, $body, $id, $now): void {
            $this->lockActor($actor);
            $this->lockCommunity($communityId);
            DB::table(Zapara::communities().'.announcements')->insert([
                'announcement_id' => $id,
                'community_id' => $communityId,
                'title' => $title,
                'body' => $body,
                'revision' => 1,
                'created_by' => $actor->user_id,
                'created_at' => $now,
                'updated_at' => $now,
            ]);
            $this->communityAudit($actor->user_id, $communityId, 'announcement_published', 'announcement', $id, $now);
        });

        return $id;
    }

    public function moderate(AccountUser $actor, string $kind, string $objectId, string $communityId, ?string $password): void
    {
        $this->accounts->requirePassword($actor, $password);
        $objectId = $this->uuid($objectId, 'object_id');
        $communityId = $this->uuid($communityId, 'community_id');
        if (! in_array($kind, ['shared_homework', 'announcement', 'poll'], true)) {
            throw ValidationException::withMessages(['data.kind' => 'Недопустимые данные.']);
        }
        $now = now();
        DB::transaction(function () use ($actor, $kind, $objectId, $communityId, $now): void {
            $this->lockActor($actor);
            $this->lockCommunity($communityId);
            $communities = Zapara::communities();
            $deleted = match ($kind) {
                'shared_homework' => $this->deleteHomework($communities, $objectId, $communityId),
                'announcement' => DB::table($communities.'.announcements')->where('announcement_id', $objectId)->where('community_id', $communityId)->delete(),
                'poll' => $this->deletePoll($communities, $objectId, $communityId),
            };
            if ($deleted < 1) {
                throw ValidationException::withMessages(['data.object_id' => 'Объект не найден.']);
            }
            $this->audit($actor->user_id, 'content_moderated', $kind, $objectId, $now);
        });
    }

    /**
     * @return list<array{family_id: string, username: string, device_name: string}>
     */
    public function openSessions(): array
    {
        $accounts = Zapara::accounts();

        return DB::table($accounts.'.session_families as f')
            ->join($accounts.'.users as u', 'u.user_id', '=', 'f.user_id')
            ->whereNull('f.revoked_at')
            ->orderByDesc('f.last_seen_at')
            ->orderBy('f.family_id')
            ->limit(200)
            ->get(['f.family_id', 'u.username', 'f.device_name'])
            ->map(fn ($row): array => [
                'family_id' => (string) $row->family_id,
                'username' => (string) $row->username,
                'device_name' => (string) $row->device_name,
            ])
            ->all();
    }

    /**
     * @return array<string, string>
     */
    public function accountOptions(): array
    {
        return DB::table(Zapara::accounts().'.users')
            ->where('status', 'active')
            ->orderBy('username')
            ->pluck('username', 'user_id')
            ->all();
    }

    public function revokeFamily(AccountUser $actor, string $familyId, ?string $password): void
    {
        $this->accounts->requirePassword($actor, $password);
        $familyId = $this->uuid($familyId, 'family_id');
        $now = now();
        DB::transaction(function () use ($actor, $familyId, $now): void {
            $this->lockActor($actor);
            $revoked = DB::table(Zapara::accounts().'.session_families')->where('family_id', $familyId)->lockForUpdate()->value('revoked_at');
            $exists = DB::table(Zapara::accounts().'.session_families')->where('family_id', $familyId)->exists();
            if (! $exists) {
                throw ValidationException::withMessages(['data.family_id' => 'Объект не найден.']);
            }
            if ($revoked !== null) {
                throw ValidationException::withMessages(['data.family_id' => 'Конфликт данных.']);
            }
            $updated = DB::table(Zapara::accounts().'.session_families')
                ->where('family_id', $familyId)
                ->whereNull('revoked_at')
                ->update(['revoked_at' => $now, 'revocation_reason' => 'revoke']);
            if ($updated !== 1) {
                throw ValidationException::withMessages(['data.family_id' => 'Конфликт данных.']);
            }
            $this->audit($actor->user_id, 'session_revoked', 'session_family', $familyId, $now);
        });
    }

    /**
     * @return list<array{community_id: string, name: string, description: string, group_id: string|null}>
     */
    public function communities(): array
    {
        $communities = Zapara::communities();

        return DB::table($communities.'.communities as c')
            ->leftJoin($communities.'.catalog_maps as m', 'm.community_id', '=', 'c.community_id')
            ->orderBy('c.name')
            ->orderBy('c.community_id')
            ->orderBy('m.group_id')
            ->get(['c.community_id', 'c.name', 'c.description', 'm.group_id'])
            ->map(fn ($row): array => [
                'community_id' => (string) $row->community_id,
                'name' => (string) $row->name,
                'description' => (string) $row->description,
                'group_id' => $row->group_id === null ? null : (string) $row->group_id,
            ])
            ->all();
    }

    /**
     * @return array<string, string>
     */
    public function communityOptions(): array
    {
        $options = [];
        foreach ($this->communities() as $row) {
            $options[$row['community_id']] ??= $row['name'];
        }

        return $options;
    }

    /**
     * @return list<array{request_id: string, community_id: string, community_name: string, username: string}>
     */
    public function pendingJoins(): array
    {
        $communities = Zapara::communities();

        return DB::table($communities.'.join_requests as r')
            ->join(Zapara::accounts().'.users as u', 'u.user_id', '=', 'r.user_id')
            ->join($communities.'.communities as c', 'c.community_id', '=', 'r.community_id')
            ->where('r.status', 'pending')
            ->orderBy('r.created_at')
            ->orderBy('r.request_id')
            ->get(['r.request_id', 'r.community_id', 'c.name as community_name', 'u.username'])
            ->map(fn ($row): array => [
                'request_id' => (string) $row->request_id,
                'community_id' => (string) $row->community_id,
                'community_name' => (string) $row->community_name,
                'username' => (string) $row->username,
            ])
            ->all();
    }

    /**
     * @return list<array{kind: string, object_id: string, community_id: string, title: string, community_name: string}>
     */
    public function contentRows(): array
    {
        $communities = Zapara::communities();
        $homework = DB::table($communities.'.shared_homework')
            ->selectRaw("'shared_homework' as kind, homework_id as object_id, community_id, title, created_at");
        $announcements = DB::table($communities.'.announcements')
            ->selectRaw("'announcement' as kind, announcement_id as object_id, community_id, title, created_at");
        $polls = DB::table($communities.'.polls')
            ->selectRaw("'poll' as kind, poll_id as object_id, community_id, question as title, created_at");

        return DB::query()
            ->fromSub($homework->unionAll($announcements)->unionAll($polls), 'content')
            ->join($communities.'.communities as c', 'c.community_id', '=', 'content.community_id')
            ->orderByDesc('content.created_at')
            ->orderBy('content.object_id')
            ->limit(200)
            ->get(['content.kind', 'content.object_id', 'content.community_id', 'content.title', 'c.name as community_name'])
            ->map(fn ($row): array => [
                'kind' => (string) $row->kind,
                'object_id' => (string) $row->object_id,
                'community_id' => (string) $row->community_id,
                'title' => (string) $row->title,
                'community_name' => (string) $row->community_name,
            ])
            ->all();
    }

    /**
     * @return list<array{created_at: string, action: string, object_type: string, object_id: string, outcome: string}>
     */
    public function auditRows(): array
    {
        return DB::table(Zapara::admin().'.admin_audit')
            ->orderByDesc('created_at')
            ->orderByDesc('event_id')
            ->limit(200)
            ->get(['created_at', 'action', 'object_type', 'object_id', 'outcome'])
            ->map(fn ($row): array => [
                'created_at' => (string) $row->created_at,
                'action' => (string) $row->action,
                'object_type' => (string) $row->object_type,
                'object_id' => (string) $row->object_id,
                'outcome' => (string) $row->outcome,
            ])
            ->all();
    }

    private function deleteHomework(string $communities, string $objectId, string $communityId): int
    {
        DB::table($communities.'.shared_homework_completion')->where('homework_id', $objectId)->delete();

        return DB::table($communities.'.shared_homework')->where('homework_id', $objectId)->where('community_id', $communityId)->delete();
    }

    private function deletePoll(string $communities, string $objectId, string $communityId): int
    {
        DB::table($communities.'.votes')->where('poll_id', $objectId)->delete();

        return DB::table($communities.'.polls')->where('poll_id', $objectId)->where('community_id', $communityId)->delete();
    }

    private function lockActor(AccountUser $actor): void
    {
        $status = DB::table(Zapara::accounts().'.users')->where('user_id', $actor->user_id)->lockForUpdate()->value('status');
        $admin = DB::table(Zapara::admin().'.platform_admins')
            ->where('user_id', $actor->user_id)
            ->whereNull('revoked_at')
            ->lockForUpdate()
            ->exists();
        if ($status !== 'active' || ! $admin) {
            abort(403);
        }
    }

    private function lockCommunity(string $communityId): void
    {
        $found = DB::table(Zapara::communities().'.communities')->where('community_id', $communityId)->lockForUpdate()->exists();
        if (! $found) {
            throw ValidationException::withMessages(['data.community_id' => 'Объект не найден.']);
        }
    }

    private function audit(string $actorId, string $action, string $type, string $objectId, \DateTimeInterface $now): void
    {
        DB::table(Zapara::admin().'.admin_audit')->insert([
            'event_id' => (string) Str::uuid(),
            'actor_id' => $actorId,
            'action' => $action,
            'object_type' => $type,
            'object_id' => $objectId,
            'outcome' => 'success',
            'created_at' => $now,
        ]);
    }

    private function communityAudit(string $actorId, string $communityId, string $action, string $type, string $objectId, \DateTimeInterface $now): void
    {
        DB::table(Zapara::communities().'.community_audit')->insert([
            'event_id' => (string) Str::uuid(),
            'community_id' => $communityId,
            'actor_id' => $actorId,
            'action' => $action,
            'object_type' => $type,
            'object_id' => $objectId,
            'outcome' => 'success',
            'created_at' => $now,
        ]);
    }

    private function text(string $value, int $maximum, bool $allowEmpty, string $field): string
    {
        if (str_contains($value, "\0") || preg_match('/\p{C}/u', $value) || ! mb_check_encoding($value, 'UTF-8')) {
            throw ValidationException::withMessages(['data.'.$field => 'Недопустимые данные.']);
        }
        $count = mb_strlen($value, 'UTF-8');
        if ($count > $maximum || ($count === 0 && ! $allowEmpty)) {
            throw ValidationException::withMessages(['data.'.$field => 'Недопустимые данные.']);
        }

        return $value;
    }

    private function body(string $value): string
    {
        $stripped = str_replace(["\n", "\t"], '', $value);
        if (str_contains($value, "\0") || preg_match('/\p{C}/u', $stripped) || ! mb_check_encoding($value, 'UTF-8')) {
            throw ValidationException::withMessages(['data.body' => 'Недопустимые данные.']);
        }
        $count = mb_strlen($value, 'UTF-8');
        if ($count < 1 || $count > 8000) {
            throw ValidationException::withMessages(['data.body' => 'Недопустимые данные.']);
        }

        return $value;
    }

    private function uuid(string $value, string $field): string
    {
        if (! Str::isUuid($value) || $value === '00000000-0000-0000-0000-000000000000') {
            throw ValidationException::withMessages(['data.'.$field => 'Недопустимые данные.']);
        }

        return $value;
    }

    private function unique(QueryException $exception): bool
    {
        return $exception->getCode() === '23505' || str_contains($exception->getMessage(), '23505');
    }
}
