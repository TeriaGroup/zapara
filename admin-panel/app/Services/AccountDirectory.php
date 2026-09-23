<?php

namespace App\Services;

use App\Auth\IdentityPassword;
use App\Models\AccountUser;
use App\Rules\AccountRules;
use App\Support\Zapara;
use Illuminate\Database\QueryException;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

final class AccountDirectory
{
    public function create(string $username, ?string $displayName, string $password): AccountUser
    {
        $this->reject(AccountRules::username($username), 'username');
        $this->reject(AccountRules::password($password, true), 'password');
        $displayName = $displayName === '' ? null : $displayName;
        $this->reject(AccountRules::displayName($displayName), 'display_name');
        $id = (string) Str::uuid();
        $now = now();
        try {
            DB::transaction(function () use ($id, $username, $displayName, $password, $now): void {
                DB::table(Zapara::accounts().'.users')->insert([
                    'user_id' => $id,
                    'username' => $username,
                    'normalized_username' => strtolower($username),
                    'display_name' => $displayName,
                    'created_at' => $now,
                    'status' => 'active',
                ]);
                DB::table(Zapara::accounts().'.password_credentials')->insert([
                    'user_id' => $id,
                    'password_hash' => IdentityPassword::hash($password),
                    'changed_at' => $now,
                ]);
            });
        } catch (QueryException $exception) {
            if ($this->unique($exception)) {
                throw ValidationException::withMessages([
                    'data.username' => 'Такое имя уже занято.',
                ]);
            }
            throw $exception;
        }

        return AccountUser::query()->findOrFail($id);
    }

    public function update(AccountUser $user, string $username, ?string $displayName, ?string $password): void
    {
        $this->reject(AccountRules::username($username), 'username');
        $displayName = $displayName === '' ? null : $displayName;
        $this->reject(AccountRules::displayName($displayName), 'display_name');
        $this->reject(AccountRules::password($password, false), 'password');
        $now = now();
        try {
            DB::transaction(function () use ($user, $username, $displayName, $password, $now): void {
                DB::table(Zapara::accounts().'.users')->where('user_id', $user->user_id)->update([
                    'username' => $username,
                    'normalized_username' => strtolower($username),
                    'display_name' => $displayName,
                ]);
                if (is_string($password) && $password !== '') {
                    DB::table(Zapara::accounts().'.users')->where('user_id', $user->user_id)->update([
                        'credential_version' => DB::raw('credential_version + 1'),
                    ]);
                    DB::table(Zapara::accounts().'.password_credentials')->where('user_id', $user->user_id)->update([
                        'password_hash' => IdentityPassword::hash($password),
                        'changed_at' => $now,
                        'failed_count' => 0,
                        'failure_window_started_at' => null,
                        'locked_until' => null,
                    ]);
                    DB::table(Zapara::accounts().'.session_families')
                        ->where('user_id', $user->user_id)
                        ->whereNull('revoked_at')
                        ->update([
                            'revoked_at' => $now,
                            'revocation_reason' => 'password_change',
                        ]);
                    $this->consumeTokens($user->user_id, $now);
                }
            });
        } catch (QueryException $exception) {
            if ($this->unique($exception)) {
                throw ValidationException::withMessages([
                    'data.username' => 'Такое имя уже занято.',
                ]);
            }
            throw $exception;
        }
    }

    public function disable(AccountUser $actor, AccountUser $target, ?string $password): void
    {
        $this->requirePassword($actor, $password);
        if ($actor->user_id === $target->user_id) {
            throw ValidationException::withMessages(['data.user_id' => 'Недопустимые данные.']);
        }
        $now = now();
        DB::transaction(function () use ($actor, $target, $now): void {
            $this->lockActor($actor);
            $status = DB::table(Zapara::accounts().'.users')->where('user_id', $target->user_id)->lockForUpdate()->value('status');
            if (! is_string($status)) {
                throw ValidationException::withMessages(['data.user_id' => 'Объект не найден.']);
            }
            $accounts = Zapara::accounts();
            DB::table($accounts.'.users')->where('user_id', $target->user_id)->update(['status' => 'disabled']);
            $this->consumeTokens($target->user_id, $now);
            DB::table($accounts.'.session_families')
                ->where('user_id', $target->user_id)
                ->whereNull('revoked_at')
                ->update(['revoked_at' => $now, 'revocation_reason' => 'disabled']);
            DB::table(Zapara::admin().'.admin_sessions')
                ->where('user_id', $target->user_id)
                ->whereNull('revoked_at')
                ->update(['revoked_at' => $now]);
            $this->audit($actor->user_id, 'account_disabled', 'account', $target->user_id, $now);
        });
    }

    public function remove(AccountUser $actor, AccountUser $target, ?string $password): void
    {
        $this->requirePassword($actor, $password);
        if ($actor->user_id === $target->user_id) {
            throw ValidationException::withMessages(['data.user_id' => 'Недопустимые данные.']);
        }
        $now = now();
        DB::transaction(function () use ($actor, $target, $now): void {
            $this->lockActor($actor);
            $status = DB::table(Zapara::accounts().'.users')->where('user_id', $target->user_id)->lockForUpdate()->value('status');
            if (! is_string($status)) {
                throw ValidationException::withMessages(['data.user_id' => 'Объект не найден.']);
            }
            DB::table(Zapara::accounts().'.users')->where('user_id', $target->user_id)->update(['status' => 'deleting']);
            $this->consumeTokens($target->user_id, $now);
            DB::table(Zapara::accounts().'.session_families')
                ->where('user_id', $target->user_id)
                ->whereNull('revoked_at')
                ->update(['revoked_at' => $now, 'revocation_reason' => 'deleting']);
            DB::table(Zapara::admin().'.admin_sessions')
                ->where('user_id', $target->user_id)
                ->whereNull('revoked_at')
                ->update(['revoked_at' => $now]);
        });
    }

    public function requirePassword(AccountUser $actor, ?string $password): void
    {
        if ($password === null || $password === '') {
            throw ValidationException::withMessages([
                'data.current_password' => 'Повторная аутентификация обязательна.',
            ]);
        }
        $hash = DB::table(Zapara::accounts().'.password_credentials')
            ->where('user_id', $actor->user_id)
            ->value('password_hash');
        if (! is_string($hash) || ! IdentityPassword::verify($hash, $password)) {
            throw ValidationException::withMessages([
                'data.current_password' => 'Неверные данные для входа.',
            ]);
        }
    }

    private function consumeTokens(string $userId, \DateTimeInterface $now): void
    {
        $accounts = Zapara::accounts();
        DB::table($accounts.'.recovery_email_tokens')->where('user_id', $userId)->whereNull('consumed_at')->update(['consumed_at' => $now]);
        DB::table($accounts.'.password_reset_tokens')->where('user_id', $userId)->whereNull('consumed_at')->update(['consumed_at' => $now]);
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

    private function reject(?string $message, string $field): void
    {
        if ($message !== null) {
            throw ValidationException::withMessages(['data.'.$field => $message]);
        }
    }

    private function unique(QueryException $exception): bool
    {
        return $exception->getCode() === '23505' || str_contains($exception->getMessage(), '23505');
    }
}
